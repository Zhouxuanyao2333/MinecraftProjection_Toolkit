// ============================================================
//  贴图归组：{ "stone.png" → stone, "grass_block_top.png" → grass_block,
//              "white_bed_head_north.png" → white_bed, ... }
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Projection3D.Toolkits.Model;

namespace Projection3D.Toolkits.Textures;

/// <summary>一个方块的六面贴图路径与几何模型。</summary>
public sealed class BlockDefinition
{
    public required string Name { get; init; }

    /// <summary>几何类型（完整立方体 / 十字交叉 / 薄板 / 扁平层…）。归组完成后可能会被修正。</summary>
    public BlockShapeKind Shape { get; set; } = BlockShapeKind.Cube;

    /// <summary>
    /// 精确像素包围盒（非标准方块用）。
    /// 例如末地传送门框架 13 像素高、铁链 2×2 截面、告示牌由"立柱+板"两段组成。
    /// 非 null 时优先于 Shape 使用。
    /// </summary>
    public PixelBoxList? CustomBoxes { get; set; }

    /// <summary>
    /// 生物群系染色（0xRRGGBB）。仅草方块类使用。
    ///
    /// Minecraft 的 grass_block_top 与 grass_block_side_overlay 是**灰度图**，
    /// 真正的绿色由生物群系 tint 相乘得到——这就是为什么直接渲染出来是灰的。
    /// </summary>
    public uint? TintRgb { get; set; }

    /// <summary>
    /// 被雪覆盖的草方块（grass_block_snow）。
    /// 渲染规则与普通草不同：顶面用 snow，侧面用 dirt + 雪裙边，且**不染色**。
    /// </summary>
    public bool SnowCovered { get; set; }

    /// <summary>
    /// 用相邻不透明行填充顶部透明的边。
    /// dirt_path 的侧面贴图第 0 行是**完全透明**的（Minecraft 里那行被顶面遮住），
    /// 但我们的盒子侧面顶端正好露在这行上，不透填就会在顶面边缘透出背景形成白缝。
    /// </summary>
    public bool FillTransparentTopEdge { get; set; }

    /// <summary>面 → PNG 绝对路径。保证至少有一项。</summary>
    public Dictionary<FaceKey, string> Faces { get; } = new();

    /// <summary>用某个面作为 6 面回退来源（优先 Front/Top/Side）。</summary>
    public string? AnyPath { get; set; }

    public string GetPath(FaceKey key)
    {
        if (Faces.TryGetValue(key, out var p)) return p;
        return AnyPath ?? string.Empty;
    }

    /// <summary>构建该方块的几何面。</summary>
    public List<Face> BuildFaces()
    {
        if (CustomBoxes != null) return ModelBuilder.BuildPixelBoxes(CustomBoxes);
        return ModelBuilder.Build(Shape);
    }
}

/// <summary>
/// 扫描 textures\block 目录，把散落的贴图按命名规则归组为"方块"。
///
/// 命名规则：
///   stone.png                  → 方块 stone，6 面同图
///   grass_block_top.png        → 方块 grass_block 的顶面
///   grass_block_side.png       → 方块 grass_block 的侧面
///   command_block_front.png    → 方块 command_block 的正面
///   oak_log.png + oak_log_top  → 方块 oak_log（侧面用 oak_log，顶面用 oak_log_top）
/// 其余后缀（_overlay / _on / _conditional / _saw ...）视为附属贴图，忽略。
/// </summary>
public static class BlockCatalog
{
    private static readonly Dictionary<string, FaceKey> FaceSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["_top"] = FaceKey.Top,
        ["_bottom"] = FaceKey.Bottom,
        ["_front"] = FaceKey.Front,
        ["_back"] = FaceKey.Back,
        ["_left"] = FaceKey.Left,
        ["_right"] = FaceKey.Right,
        ["_side"] = FaceKey.Front   // 通用侧面：先当作正面候选
    };

    /// <summary>
    /// 只保留这些类型的方块：按方块名的**最后一段**匹配。
    ///
    /// 例：
    ///   grass_block          → 尾缀 block
    ///   deepslate_diamond_ore→ 尾缀 ore
    ///   white_terracotta     → 尾缀 terracotta
    ///   red_concrete_powder  → 尾缀 powder
    ///   white_wool           → 尾缀 wool
    ///   glass                → 尾缀 glass
    ///   shulker_box          → 尾缀 box
    ///   crafting_table       → 尾缀 table
    ///   sulfur_block         → 尾缀 block
    ///   furnace              → 尾缀 furnace
    ///   bee_nest             → 尾缀 nest
    ///   honey_block          → 尾缀 block
    ///   bamboo_mosaic        → 尾缀 mosaic
    ///   oak_planks           → 尾缀 planks
    ///   ancient_debris       → 尾缀 debris
    /// </summary>
    private static readonly HashSet<string> AllowedSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "block",
        "terracotta",
        "powder",
        "wool",
        "glass",
        "box",
        "table",
        "sulfur",
        "furnace",
        "nest",
        "honey",
        "mosaic",
        "planks",
        "debris",
        "ore",
        // ---- 没有 block 后缀、但本身就是完整方块的类型 ----
        "dirt",        // dirt / coarse_dirt / rooted_dirt
        "path",        // dirt_path（15/16 高，顶面下沉 1 像素）
        "sand",        // sand / red_sand / suspicious_sand / soul_sand
        "sandstone",   // sandstone 系列
        "mud",         // mud / packed_mud
        "bricks",      // mud_bricks / stone_bricks / deepslate_bricks …
        "soil"         // soul_soil
    };

    /// <summary>扫描目录，返回按名称排序的方块列表（只保留目标类型）。</summary>
    public static List<BlockDefinition> Scan(string textureBlockDir, bool onlyAllowedTypes = true)
    {
        var all = ScanAll(textureBlockDir);

        if (!onlyAllowedTypes) return all;

        return all.Where(b => IsAllowedType(b.Name)).ToList();
    }

    /// <summary>按方块名的最后一段判断是否属于保留类型。</summary>
    public static bool IsAllowedType(string blockName)
    {
        if (string.IsNullOrEmpty(blockName)) return false;

        // 草方块的生物群系变体（grass_block_plains / grass_block_jungle …）全部放行
        // grass_block_snow 是被雪覆盖的草变体，同样保留
        if (blockName.StartsWith("grass_block_", StringComparison.OrdinalIgnoreCase))
            return true;

        int idx = blockName.LastIndexOf('_');
        string last = idx >= 0 ? blockName[(idx + 1)..] : blockName;

        return AllowedSuffixes.Contains(last);
    }

    private static List<BlockDefinition> ScanAll(string textureBlockDir)
    {
        var result = new List<BlockDefinition>();
        if (string.IsNullOrEmpty(textureBlockDir)) return result;

        var blocks = new Dictionary<string, BlockDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in TextureResources.EnumeratePngFiles(textureBlockDir))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(name)) continue;

            // ① 多部件方块先归一化到主名（床 / 钟乳石）
            string? multiPartBase = NormalizeMultiPartName(name);
            if (multiPartBase != null)
            {
                var mp = GetOrCreate(blocks, multiPartBase);
                mp.AnyPath ??= file;
                continue;
            }

            // ② 找到名字末尾的面后缀
            FaceKey? face = null;
            string baseName = name;

            foreach (var kv in FaceSuffixes)
            {
                if (name.EndsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    face = kv.Value;
                    baseName = name[..^kv.Key.Length];
                    break;
                }
            }

            // 没有面后缀 → 视为"六面同图"的方块本体
            if (face == null)
            {
                // 过滤掉明显不是方块本体的附属贴图
                if (IsAccessory(name)) continue;

                var def = GetOrCreate(blocks, name);
                def.AnyPath ??= file;
                continue;
            }

            if (string.IsNullOrEmpty(baseName)) continue;
            if (IsAccessory(baseName)) continue;

            var target = GetOrCreate(blocks, baseName);

            // _side 只作为 "没有专门正面时" 的候选，避免覆盖真正的 _front
            if (face == FaceKey.Front && name.EndsWith("_side", StringComparison.OrdinalIgnoreCase))
            {
                target.Faces.TryAdd(FaceKey.Right, file);
                target.Faces.TryAdd(FaceKey.Left, file);
                target.Faces.TryAdd(FaceKey.Back, file);
                target.Faces.TryAdd(FaceKey.Front, file);
            }
            else
            {
                target.Faces[face.Value] = file;
            }
        }

        // 规整：补齐缺失的面，并生成 AnyPath
        foreach (var def in blocks.Values)
        {
            def.AnyPath ??= def.Faces.Values.FirstOrDefault();
            if (def.AnyPath == null) continue;

            // ★ 用"是否有分面纹理"修正误判：
            //   同时有顶面 + 侧面纹理的，一定是完整立方体
            //   （例如 grass_block 名字里有 "grass" 会被初判成十字交叉，实际是立方体）
            bool hasTop = def.Faces.ContainsKey(FaceKey.Top);
            bool hasSide = def.Faces.Keys.Any(k =>
                k is FaceKey.Front or FaceKey.Back or FaceKey.Left or FaceKey.Right);

            if (hasTop && hasSide)
            {
                def.Shape = BlockShapeKind.Cube;
            }

            // ★ 应用精确模型表：覆盖"比标准方块矮/窄/细/多段"的方块
            //   （土径 15 高、末地传送门框架 13 高、铁链 2×2 截面、告示牌为两段…）
            var special = SpecialModels.TryGet(def.Name);
            if (special != null)
            {
                def.CustomBoxes = special;
            }

            // ★ 被雪覆盖的草方块：渲染规则特殊（顶面 snow、侧面 dirt+雪裙边、不染色）
            if (def.Name.Equals("grass_block_snow", StringComparison.OrdinalIgnoreCase))
            {
                def.SnowCovered = true;
                def.Shape = BlockShapeKind.Cube;
            }

            // ★ 土径：侧面贴图顶部一行是透明的，需要填平
            if (def.Name.Equals("dirt_path", StringComparison.OrdinalIgnoreCase))
            {
                def.FillTransparentTopEdge = true;
            }

            result.Add(def);
        }

        // ============================================================
        // 草方块的特殊处理：生成生物群系变体
        //
        // Minecraft 的 grass_block_top / grass_block_side_overlay 是灰度图，
        // 真正的颜色 = 灰度 × 生物群系 grass_color。所以同一个 grass_block
        // 在不同群系里长得不一样——这里把它拆成多个"变体方块"。
        // ============================================================
        var grassBase = result.FirstOrDefault(b =>
            b.Name.Equals("grass_block", StringComparison.OrdinalIgnoreCase));

        if (grassBase != null)
        {
            var variants = new List<BlockDefinition>();

            foreach (var (biome, rgb) in GrassBiomeColors)
            {
                if (biome == "plains")
                {
                    // 默认的 grass_block 就是平原色
                    grassBase.TintRgb = rgb;
                    continue;
                }

                var variant = new BlockDefinition
                {
                    Name = "grass_block_" + biome,
                    Shape = BlockShapeKind.Cube,
                    AnyPath = grassBase.AnyPath,
                    TintRgb = rgb
                };

                foreach (var kv in grassBase.Faces) variant.Faces[kv.Key] = kv.Value;
                variants.Add(variant);
            }

            result.AddRange(variants);
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// 各生物群系的草色（Minecraft 原版 biome 定义里的 grass_color）。
    /// 数据来自原版数据包 data/minecraft/worldgen/biome/*.json 的 effects.grass_color。
    /// </summary>
    private static readonly (string Biome, uint Rgb)[] GrassBiomeColors =
    {
        ("plains",          0x91BD59),   // 平原 / 向日葵平原
        ("forest",          0x79C05A),   // 森林 / 繁花森林
        ("birch_forest",    0x88BB67),   // 桦木森林
        ("dark_forest",     0x507A32),   // 黑森林
        ("jungle",          0x59C93C),   // 丛林 / 竹林
        ("sparse_jungle",   0x64C73F),   // 稀疏丛林
        ("swamp",           0x6A7039),   // 沼泽 / 红树林沼泽
        ("savanna",         0xBFB755),   // 热带草原
        ("desert",          0xBFB755),   // 沙漠
        ("badlands",        0x90814D),   // 恶地
        ("wooded_badlands", 0x9E814D),   // 疏林恶地
        ("taiga",           0x86B783),   // 针叶林
        ("old_growth_taiga",0x86B87F),   // 原始松林
        ("snowy",           0x80B497),   // 雪原 / 雪林 / 冰刺
        ("cherry_grove",    0xB6DB61),   // 樱花树林
        ("meadow",          0x83BB6D),   // 草甸
        ("mushroom_fields", 0x55C93F),   // 蘑菇岛
        ("ocean",           0x8EB971),   // 海洋 / 河流 / 沙滩
        ("lush_caves",      0x8AB689),   // 繁茂洞穴 / 石岸
        ("stony_peaks",     0x9ABE4B),   // 裸岩山峰
        ("pale_garden",     0x778272)    // 苍白之园
    };

    /// <summary>
    /// 多部件方块归一化：把这些变体归到同一个方块名下。
    ///   白色床的 8 个部件 white_bed_head_north / white_bed_foot_east … → white_bed
    ///   钟乳石 pointed_dripstone_up_tip / _down_base …              → pointed_dripstone
    /// 返回 null 表示不需要归一化。
    /// </summary>
    private static string? NormalizeMultiPartName(string name)
    {
        // 床：<color>_bed_head_<dir> / <color>_bed_foot_<dir>
        // dir 有 4 个水平朝向 + "up"（枕头朝上的平放状态）
        var m = Regex.Match(name,
            @"^(.+_bed)_(?:head|foot)_(?:north|south|east|west|up)$",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // 钟乳石：pointed_dripstone_<up|down>_<tip|base|middle|frustum|tip_merge>
        m = Regex.Match(name,
            @"^(pointed_dripstone)_(?:up|down)_(?:tip|base|middle|frustum|tip_merge)$",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        return null;
    }

    private static BlockDefinition GetOrCreate(
        Dictionary<string, BlockDefinition> blocks, string name)
    {
        if (!blocks.TryGetValue(name, out var def))
        {
            def = new BlockDefinition
            {
                Name = name,
                Shape = BlockShapeClassifier.Classify(name)
            };
            blocks[name] = def;
        }
        return def;
    }

    /// <summary>明显不是独立方块的附属贴图。</summary>
    private static bool IsAccessory(string name)
    {
        // 这些后缀说明它是某个方块的变体/叠加层，不是方块本体
        string[] accessoryMarkers =
        {
            "_overlay", "_on", "_lit", "_conditional", "_saw", "_pane", "_eye",
            "_stem", "_inner", "_outer", "_edge", "_corner", "_detail",
            "_destroy_stage", "_debug", "_stage",
            "_particle", "_side_small", "_side_tall", "_top_overlay", "_bottom_overlay",
            "_front_honey"   // beehive_front_honey / bee_nest_front_honey 是蜂巢的纹理变体
        };

        foreach (var m in accessoryMarkers)
        {
            if (name.EndsWith(m, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
