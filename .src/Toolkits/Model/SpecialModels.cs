using System;

namespace Projection3D.Toolkits.Model;

/// <summary>
/// 用 Minecraft 像素（0~16）描述的包围盒。
/// 这是定义"非标准方块"模型的基础：末地传送门框架只有 13 像素高、铁链只有 2×2 截面……
/// </summary>
public readonly record struct PixelBox(
    float X0, float Y0, float Z0,
    float X1, float Y1, float Z1)
{
    public float Width => X1 - X0;
    public float Height => Y1 - Y0;
    public float Depth => Z1 - Z0;
}

/// <summary>
/// 特殊方块的精确模型表。
///
/// 背景：Minecraft 里大量方块的模型并不是 16×16×16 的完整立方体。
/// 如果统一按完整立方体渲染，会出现"该矮的偏高、该细的偏粗、该空的地方填实"。
/// 这里按方块名给出真实的像素包围盒。
///
/// 数据来源：Minecraft 原版 assets/minecraft/models/block/*.json 的 elements 定义。
/// </summary>
public static class SpecialModels
{
    /// <summary>
    /// 查询方块的精确模型。
    /// 返回 null 表示这是普通方块，交给形状分类器处理。
    /// </summary>
    public static PixelBoxList? TryGet(string blockName)
    {
        if (string.IsNullOrEmpty(blockName)) return null;

        string n = blockName.ToLowerInvariant();

        // ============================================================
        // 一、比标准方块矮 / 窄的"部分方块"
        // ============================================================

        // 土径 / 耕地：15 像素高（顶面凹下 1 像素）
        if (n == "dirt_path" || n == "farmland" || n == "farmland_moist")
            return Box(0, 0, 0, 16, 15, 16);

        // 末地传送门框架：13 像素高（顶部凹槽放末影之眼）
        if (n == "end_portal_frame" || n.StartsWith("end_portal_frame_"))
            return Box(0, 0, 0, 16, 13, 16);

        // 附魔台：12 像素高的底座（原版顶部另有书本模型，这里按主体渲染）
        if (n == "enchanting_table")
            return Box(0, 0, 0, 16, 12, 16);

        // 箱子：14×14×14，居中不贴边
        if (IsChest(n))
            return Box(1, 0, 1, 15, 14, 15);

        // 仙人掌：15×16×15（四面各缩进 1 像素，避免相邻方块穿插）
        if (n == "cactus" || n == "cactus_flower")
            return Box(1, 0, 1, 15, 16, 15);

        // 雪层 / 泥巴层按不到满高处理
        if (n == "snow")
            return Box(0, 0, 0, 16, 8, 16);

        // ============================================================
        // 二、细柱类：2×2 像素截面、贯穿全高
        // ============================================================

        // 铁链 / 铜链：必须以 _chain 结尾，且不能是"链式命令方块"
        // （chain_command_block 是完整立方体，不能当细柱）
        if (n.EndsWith("_chain", StringComparison.Ordinal) && !n.Contains("command_block"))
            return Box(7, 0, 7, 9, 16, 9);

        // 铁栏杆 / 铜栏杆 / 金栏杆：2×2 截面居中
        if (IsBars(n))
            return Box(7, 0, 7, 9, 16, 9);

        // 末地烛 / 避雷针：比铁链略粗（4×4）
        if (n == "end_rod" || n.Contains("lightning_rod"))
            return Box(6, 0, 6, 10, 16, 10);

        // 竹子：只有 bamboo_stalk 是细杆，bamboo_block / bamboo_planks 是完整方块
        if (n == "bamboo_stalk")
            return Box(7, 0, 7, 9, 16, 9);

        // ============================================================
        // 三、告示牌：板 + 支柱 两段
        // ============================================================

        if (IsSign(n))
        {
            // 站立告示牌：立柱 2×8×2 + 板 16×8×2
            return new PixelBoxList(new[]
            {
                new PixelBox(7, 0, 7, 9, 8, 9),      // 立柱
                new PixelBox(0, 8, 7, 16, 16, 9)     // 板
            });
        }

        if (IsHangingSign(n))
        {
            // 悬挂告示牌：吊链 + 板
            return new PixelBoxList(new[]
            {
                new PixelBox(7, 12, 7, 9, 16, 9),    // 吊链
                new PixelBox(0, 4, 7, 16, 12, 9)     // 板
            });
        }

        return null;
    }

    // ============================================================
    // 辅助判断
    // ============================================================

    private static bool IsChest(string n)
        => n == "chest" || n.EndsWith("_chest") ||
           n.Contains("trapped_chest") || n.Contains("ender_chest");

    private static bool IsBars(string n)
        => n.Contains("iron_bars") || n.Contains("copper_bars") || n.Contains("gold_bars");

    private static bool IsSign(string n)
        => n.EndsWith("_sign") && !n.Contains("hanging");

    private static bool IsHangingSign(string n)
        => n.Contains("hanging_sign");

    private static PixelBoxList Box(float x0, float y0, float z0, float x1, float y1, float z1)
        => new(new[] { new PixelBox(x0, y0, z0, x1, y1, z1) });
}

/// <summary>一个方块由若干个像素包围盒组成（告示牌 = 立柱 + 板）。</summary>
public sealed class PixelBoxList
{
    public PixelBox[] Boxes { get; }

    public PixelBoxList(PixelBox[] boxes)
    {
        Boxes = boxes;
    }
}
