using System;

namespace Projection3D.Toolkits.Model;

/// <summary>
/// 方块的几何类型。Minecraft 里并非所有方块都是完整立方体：
/// 花/草是十字交叉平面，门/活板门是薄板，地毯/铁轨是 1 像素扁平层，火把是细柱……
/// </summary>
public enum BlockShapeKind
{
    /// <summary>完整立方体（石頭、泥土、木板…）</summary>
    Cube,

    /// <summary>十字交叉平面（花、草、树苗、海草、作物）</summary>
    Cross,

    /// <summary>1/16 高的扁平层（地毯、铁轨、红石线、压力板、睡莲）</summary>
    Flat,

    /// <summary>下半砖（台阶）</summary>
    Slab,

    /// <summary>薄板：门 / 活板门 / 玻璃板</summary>
    Thin,

    /// <summary>细柱：火把 / 蜡烛</summary>
    Column,

    /// <summary>贴墙平面：梯子 / 藤蔓</summary>
    WallPlane,

    /// <summary>小方块：海龟蛋</summary>
    SmallCube,

    /// <summary>锥形：钟乳石 / 滴水石锥</summary>
    Pointed,

    /// <summary>床（组合模型）</summary>
    Bed
}

/// <summary>
/// 根据方块名推断几何类型。
/// 规则来自 Minecraft 原版 block model 的实际分类（cross / flat / door / torch …）。
/// </summary>
public static class BlockShapeClassifier
{
    public static BlockShapeKind Classify(string blockName)
    {
        if (string.IsNullOrEmpty(blockName)) return BlockShapeKind.Cube;

        string n = blockName.ToLowerInvariant();

        // ============================================================
        // ★ 最高优先级：以 "_block" 结尾的一律是完整立方体
        //   （red_mushroom_block / nether_wart_block / dripstone_block / grass_block …）
        //   这些名字里含 mushroom / wart / dripstone 等关键字，
        //   如果不先拦下来，会被后面的十字/锥形规则误判，渲染成交叉平面或尖锥。
        // ============================================================
        if (n.EndsWith("_block", StringComparison.Ordinal) ||
            n.EndsWith("_bricks", StringComparison.Ordinal))
            return BlockShapeKind.Cube;

        // ---------- 床 ----------
        if (n.Contains("bed") && !n.Contains("bedrock")) return BlockShapeKind.Bed;

        // ---------- 锥形 ----------
        if (n.Contains("pointed_dripstone") || n.Contains("dripstone"))
            return BlockShapeKind.Pointed;

        // ---------- 小方块 ----------
        if (n.Contains("turtle_egg")) return BlockShapeKind.SmallCube;

        // ---------- 贴墙平面 ----------
        if (n.Contains("ladder") || n.Contains("vine"))
            return BlockShapeKind.WallPlane;

        // ---------- 细柱 ----------
        // 注意：torchflower 是花（cross），不是火把
        if (n.Contains("torchflower")) return BlockShapeKind.Cross;
        if (n.Contains("torch") || n.Contains("candle"))
            return BlockShapeKind.Column;

        // ---------- 扁平层 ----------
        // 注意：farmland / dirt_path 是完整方块（16 高），不在这里
        if (n.Contains("carpet") ||
            n.Contains("pressure_plate") ||
            n.Contains("redstone_wire") ||
            n.Contains("tripwire") ||
            n.Contains("lily_pad") ||
            n.EndsWith("_rail") || n == "rail")
            return BlockShapeKind.Flat;

        // ---------- 十字交叉（花 / 草 / 作物 / 树苗）----------
        if (IsCrossModel(n)) return BlockShapeKind.Cross;

        // ---------- 薄板 ----------
        // 注意：chain_command_block 是完整立方体，不能因为名字里有 chain 就判成薄板
        if (n.Contains("trapdoor") ||
            n.Contains("_door") || n.EndsWith("door") ||
            n.Contains("_pane") ||
            (n.EndsWith("_chain") && !n.Contains("command_block")) ||
            n.Contains("iron_bars") || n.Contains("copper_bars") || n.Contains("gold_bars"))
            return BlockShapeKind.Thin;

        // ---------- 半砖 ----------
        if (n.EndsWith("_slab") || n.Contains("_slab_"))
            return BlockShapeKind.Slab;

        return BlockShapeKind.Cube;
    }

    /// <summary>Minecraft 里使用 cross 模型的方块。</summary>
    private static bool IsCrossModel(string n)
    {
        string[] keys =
        {
            "sapling", "flower", "tulip", "dandelion", "poppy", "orchid",
            "allium", "bluet", "daisy", "cornflower", "lily_of_the_valley",
            "wither_rose", "torchflower", "pitcher", "sunflower", "lilac",
            "rose_bush", "peony", "grass", "fern", "seagrass", "kelp",
            "wheat", "carrots", "potatoes", "beetroots", "sugar_cane",
            "bush", "dead_bush", "sweet_berry", "nether_wart", "crimson_roots",
            "warped_roots", "nether_sprouts", "crimson_fungus", "warped_fungus",
            "mushroom", "brown_mushroom", "red_mushroom", "azalea",
            "mangrove_propagule", "dripleaf", "big_dripleaf", "spore_blossom",
            "cactus_flower", "short_grass", "tall_grass", "rail"
        };

        foreach (var k in keys)
        {
            if (n.Contains(k)) return true;
        }
        return false;
    }
}
