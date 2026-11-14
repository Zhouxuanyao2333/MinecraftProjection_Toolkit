using System;
using System.Collections.Generic;
using Projection3D.Toolkits.Maths;

namespace Projection3D.Toolkits.Model;

/// <summary>
/// 按几何类型构建方块模型。
///
/// 坐标系：单位方块 = 1×1×1，中心在原点（y 从 -0.5 到 +0.5）。
/// Minecraft 的 1 像素 = 1/16 单位。
/// </summary>
public static class ModelBuilder
{
    private const float P = 1f / 16f;   // 1 像素

    public static List<Face> Build(BlockShapeKind shape)
    {
        return shape switch
        {
            BlockShapeKind.Cross => BuildCross(),
            BlockShapeKind.Flat => BuildFlat(),
            BlockShapeKind.Slab => BuildSlab(),
            BlockShapeKind.Thin => BuildThin(),
            BlockShapeKind.Column => BuildColumn(),
            BlockShapeKind.WallPlane => BuildWallPlane(),
            BlockShapeKind.SmallCube => BuildSmallCube(),
            BlockShapeKind.Pointed => BuildPointed(),
            BlockShapeKind.Bed => BuildBed(),
            _ => CubeModel.Build(1f)
        };
    }

    /// <summary>
    /// 按精确像素包围盒构建（一个方块可以由多段组成，例如告示牌 = 立柱 + 板）。
    /// 像素坐标 0~16 → 模型空间 -0.5~+0.5。
    /// </summary>
    public static List<Face> BuildPixelBoxes(PixelBoxList list)
    {
        var faces = new List<Face>();

        foreach (var b in list.Boxes)
        {
            // 跳过退化盒
            if (b.Width <= 0f || b.Height <= 0f || b.Depth <= 0f) continue;

            faces.AddRange(BuildBox(
                x0: b.X0 * P - 0.5f,
                y0: b.Y0 * P - 0.5f,
                z0: b.Z0 * P - 0.5f,
                x1: b.X1 * P - 0.5f,
                y1: b.Y1 * P - 0.5f,
                z1: b.Z1 * P - 0.5f,
                topKey: FaceKey.Top,
                sideKey: FaceKey.Front));
        }

        return faces;
    }

    // ============================================================
    // 十字交叉模型：两个互相垂直的竖直平面（花 / 草 / 树苗）
    // 每个平面要正反两面都画，否则从背面看会消失。
    // ============================================================
    private static List<Face> BuildCross()
    {
        var faces = new List<Face>(8);

        // 平面 A：沿 xz 对角线 1（左下 → 右上）
        AddQuadDoubleSided(faces, FaceKey.Front,
            new Vec3(-0.5f, -0.5f, -0.5f),
            new Vec3(0.5f, -0.5f, 0.5f),
            new Vec3(0.5f, 0.5f, 0.5f),
            new Vec3(-0.5f, 0.5f, -0.5f));

        // 平面 B：沿 xz 对角线 2（右下 → 左上）
        AddQuadDoubleSided(faces, FaceKey.Front,
            new Vec3(0.5f, -0.5f, -0.5f),
            new Vec3(-0.5f, -0.5f, 0.5f),
            new Vec3(-0.5f, 0.5f, 0.5f),
            new Vec3(0.5f, 0.5f, -0.5f));

        return faces;
    }

    // ============================================================
    // 扁平层：y 从 -0.5 起，高 1 像素（地毯 / 铁轨 / 红石线）
    // ============================================================
    private static List<Face> BuildFlat()
    {
        return BuildBox(
            x0: -0.5f, y0: -0.5f, z0: -0.5f,
            x1: 0.5f, y1: -0.5f + P, z1: 0.5f,
            topKey: FaceKey.Top,
            sideKey: FaceKey.Front);
    }

    // ============================================================
    // 半砖：下半部分
    // ============================================================
    private static List<Face> BuildSlab()
    {
        return BuildBox(
            x0: -0.5f, y0: -0.5f, z0: -0.5f,
            x1: 0.5f, y1: 0f, z1: 0.5f,
            topKey: FaceKey.Top,
            sideKey: FaceKey.Front);
    }

    // ============================================================
    // 薄板：门 / 活板门 / 玻璃板
    // 门：厚度 3 像素，竖直贴在方块一侧
    // ============================================================
    private static List<Face> BuildThin()
    {
        float t = 3f * P;   // 厚度 3 像素

        return BuildBox(
            x0: -0.5f, y0: -0.5f, z0: 0.5f - t,
            x1: 0.5f, y1: 0.5f, z1: 0.5f,
            topKey: FaceKey.Top,
            sideKey: FaceKey.Front);
    }

    // ============================================================
    // 细柱：火把 / 蜡烛
    // 火把：2 像素宽、10 像素高，立在方块底面中央。
    //
    // ★ UV 要点：火把贴图是 16×16，火把图案只画在中间窄条 (7,6)-(9,16)。
    //   如果把整张贴图拉到一个 2×10 的盒子上，图案会被压成一条线几乎看不见。
    //   所以这里用定制 UV，只取图案所在的那一块。
    // ============================================================
    private static List<Face> BuildColumn()
    {
        float half = 1f * P;       // 半宽 1 像素 → 宽 2 像素
        float height = 10f * P;    // 高 10 像素

        float x0 = -half, x1 = half;
        float y0 = -0.5f, y1 = -0.5f + height;
        float z0 = -half, z1 = half;

        // 贴图图案区域（归一化）
        float u0 = 7f / 16f, u1 = 9f / 16f;
        float v0 = 6f / 16f, v1 = 1f;
        float vTop = 6f / 16f, vTopEnd = 8f / 16f;   // 顶端那一小段

        var faces = new List<Face>(6);

        // 四个侧面：v 从图案顶部到底部
        faces.Add(QuadUv(FaceKey.Front,
            new Vec3(x0, y1, z1), new Vec3(x1, y1, z1),
            new Vec3(x1, y0, z1), new Vec3(x0, y0, z1),
            new Vec3(0, 0, 1), u0, v0, u1, v1));

        faces.Add(QuadUv(FaceKey.Front,
            new Vec3(x1, y1, z0), new Vec3(x0, y1, z0),
            new Vec3(x0, y0, z0), new Vec3(x1, y0, z0),
            new Vec3(0, 0, -1), u0, v0, u1, v1));

        faces.Add(QuadUv(FaceKey.Front,
            new Vec3(x1, y1, z1), new Vec3(x1, y1, z0),
            new Vec3(x1, y0, z0), new Vec3(x1, y0, z1),
            new Vec3(1, 0, 0), u0, v0, u1, v1));

        faces.Add(QuadUv(FaceKey.Front,
            new Vec3(x0, y1, z0), new Vec3(x0, y1, z1),
            new Vec3(x0, y0, z1), new Vec3(x0, y0, z0),
            new Vec3(-1, 0, 0), u0, v0, u1, v1));

        // 顶面：火把顶端
        faces.Add(QuadUv(FaceKey.Top,
            new Vec3(x0, y1, z0), new Vec3(x1, y1, z0),
            new Vec3(x1, y1, z1), new Vec3(x0, y1, z1),
            new Vec3(0, 1, 0), u0, vTop, u1, vTopEnd));

        // 底面
        faces.Add(QuadUv(FaceKey.Bottom,
            new Vec3(x1, y0, z0), new Vec3(x0, y0, z0),
            new Vec3(x0, y0, z1), new Vec3(x1, y0, z1),
            new Vec3(0, -1, 0), u0, v1 - (v1 - v0) * 0.3f, u1, v1));

        return faces;
    }

    // ============================================================
    // 贴墙平面：梯子 / 藤蔓
    // 贴在 z = 0.5 的墙面上
    // ============================================================
    private static List<Face> BuildWallPlane()
    {
        float t = 0.5f * P;

        return BuildBox(
            x0: -0.5f, y0: -0.5f, z0: 0.5f - t,
            x1: 0.5f, y1: 0.5f, z1: 0.5f,
            topKey: FaceKey.Front,
            sideKey: FaceKey.Front);
    }

    // ============================================================
    // 小方块：海龟蛋（约 8×8×8 像素，坐在底面）
    // ============================================================
    private static List<Face> BuildSmallCube()
    {
        float h = 4f * P;   // 半边长 4 像素 → 8 像素立方

        return BuildBox(
            x0: -h, y0: -0.5f, z0: -h,
            x1: h, y1: -0.5f + h * 2f, z1: h,
            topKey: FaceKey.Top,
            sideKey: FaceKey.Front);
    }

    // ============================================================
    // 锥形：钟乳石 / 滴水石锥（底面 4 像素，向上收尖）
    // 用 4 个三角形近似
    // ============================================================
    private static List<Face> BuildPointed()
    {
        var faces = new List<Face>(4);
        float b = 2f * P;        // 底半宽 2 像素
        float topY = 0.5f;       // 尖顶
        float bottomY = -0.5f;

        var apex = new Vec3(0f, topY, 0f);
        var c0 = new Vec3(-b, bottomY, -b);
        var c1 = new Vec3(b, bottomY, -b);
        var c2 = new Vec3(b, bottomY, b);
        var c3 = new Vec3(-b, bottomY, b);

        AddTriangle(faces, FaceKey.Front, c0, c1, apex);
        AddTriangle(faces, FaceKey.Front, c1, c2, apex);
        AddTriangle(faces, FaceKey.Front, c2, c3, apex);
        AddTriangle(faces, FaceKey.Front, c3, c0, apex);

        return faces;
    }

    // ============================================================
    // 床：扁平底座 + 两端稍高（简化模型）
    // ============================================================
    private static List<Face> BuildBed()
    {
        // 底座：16×9×16 像素，离地 3 像素
        return BuildBox(
            x0: -0.5f, y0: -0.5f + 3f * P, z0: -0.5f,
            x1: 0.5f, y1: -0.5f + 9f * P, z1: 0.5f,
            topKey: FaceKey.Top,
            sideKey: FaceKey.Front);
    }

    // ============================================================
    // 通用盒子构建（六个面，UV 铺满）
    // ============================================================
    public static List<Face> BuildBox(
        float x0, float y0, float z0,
        float x1, float y1, float z1,
        FaceKey topKey = FaceKey.Top,
        FaceKey sideKey = FaceKey.Front)
    {
        var faces = new List<Face>(6);

        faces.Add(Quad(FaceKey.Top,
            new Vec3(x0, y1, z0), new Vec3(x1, y1, z0),
            new Vec3(x1, y1, z1), new Vec3(x0, y1, z1),
            new Vec3(0, 1, 0)));

        faces.Add(Quad(FaceKey.Bottom,
            new Vec3(x1, y0, z0), new Vec3(x0, y0, z0),
            new Vec3(x0, y0, z1), new Vec3(x1, y0, z1),
            new Vec3(0, -1, 0)));

        faces.Add(Quad(sideKey,
            new Vec3(x0, y1, z1), new Vec3(x1, y1, z1),
            new Vec3(x1, y0, z1), new Vec3(x0, y0, z1),
            new Vec3(0, 0, 1)));

        faces.Add(Quad(sideKey,
            new Vec3(x1, y1, z0), new Vec3(x0, y1, z0),
            new Vec3(x0, y0, z0), new Vec3(x1, y0, z0),
            new Vec3(0, 0, -1)));

        faces.Add(Quad(sideKey,
            new Vec3(x1, y1, z1), new Vec3(x1, y1, z0),
            new Vec3(x1, y0, z0), new Vec3(x1, y0, z1),
            new Vec3(1, 0, 0)));

        faces.Add(Quad(sideKey,
            new Vec3(x0, y1, z0), new Vec3(x0, y1, z1),
            new Vec3(x0, y0, z1), new Vec3(x0, y0, z0),
            new Vec3(-1, 0, 0)));

        return faces;
    }

    // ============================================================
    // 工具
    // ============================================================

    private static Face Quad(FaceKey key, Vec3 v0, Vec3 v1, Vec3 v2, Vec3 v3, Vec3 normal)
    {
        return new Face
        {
            Key = key,
            V0 = v0, V1 = v1, V2 = v2, V3 = v3,
            T0 = new Vec2(0f, 0f),
            T1 = new Vec2(1f, 0f),
            T2 = new Vec2(1f, 1f),
            T3 = new Vec2(0f, 1f),
            Normal = normal
        };
    }

    /// <summary>带自定义 UV 范围的四边形（u0,v0)=左上，(u1,v1)=右下。</summary>
    private static Face QuadUv(
        FaceKey key, Vec3 v0, Vec3 v1, Vec3 v2, Vec3 v3, Vec3 normal,
        float u0, float v0u, float u1, float v1u)
    {
        return new Face
        {
            Key = key,
            V0 = v0, V1 = v1, V2 = v2, V3 = v3,
            T0 = new Vec2(u0, v0u),
            T1 = new Vec2(u1, v0u),
            T2 = new Vec2(u1, v1u),
            T3 = new Vec2(u0, v1u),
            Normal = normal
        };
    }

    /// <summary>加一个平面（正反两面各一个 Face），用于 cross 模型。</summary>
    private static void AddQuadDoubleSided(
        List<Face> faces, FaceKey key,
        Vec3 v0, Vec3 v1, Vec3 v2, Vec3 v3)
    {
        // 法线用边的叉乘算
        var a = Sub(v1, v0);
        var b = Sub(v2, v0);
        var n = Cross(a, b);
        n = Normalize(n);

        faces.Add(Quad(key, v0, v1, v2, v3, n));

        // 反面：顶点顺序反转，法线取反
        faces.Add(Quad(key, v3, v2, v1, v0, new Vec3(-n.X, -n.Y, -n.Z)));
    }

    private static void AddTriangle(List<Face> faces, FaceKey key, Vec3 a, Vec3 b, Vec3 c)
    {
        // 用退化的四边形表示三角形（第 4 点重复 c），保持数据结构统一
        var n = Normalize(Cross(Sub(b, a), Sub(c, a)));
        faces.Add(Quad(key, a, b, c, c, n));
    }

    private static Vec3 Sub(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    private static Vec3 Normalize(Vec3 v)
    {
        float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        if (len < 1e-6f) return new Vec3(0, 1, 0);
        return new Vec3(v.X / len, v.Y / len, v.Z / len);
    }
}
