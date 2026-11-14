using System;
using System.Collections.Generic;
using Projection3D.Toolkits.Maths;

namespace Projection3D.Toolkits.Model;

/// <summary>立方体的六个面。用于从方块贴图集里挑对应的图像。</summary>
public enum FaceKey
{
    Top,
    Bottom,
    Front,
    Back,
    Left,
    Right
}

/// <summary>
/// 一个四边形面：4 个模型空间顶点 + 4 个归一化 UV。
/// 顶点绕向为"从外部看逆时针"，背面剔除依赖这个约定。
/// </summary>
public sealed class Face
{
    public required FaceKey Key { get; init; }

    public required Vec3 V0 { get; init; }
    public required Vec3 V1 { get; init; }
    public required Vec3 V2 { get; init; }
    public required Vec3 V3 { get; init; }

    public required Vec2 T0 { get; init; }
    public required Vec2 T1 { get; init; }
    public required Vec2 T2 { get; init; }
    public required Vec2 T3 { get; init; }

    /// <summary>外法线（模型空间），用于光照。</summary>
    public required Vec3 Normal { get; init; }
}

/// <summary>单位立方体模型：6 个面，尺寸 1×1×1，中心在原点。</summary>
public static class CubeModel
{
    /// <summary>
    /// 构建立方体。size 为边长（默认 1）。
    /// 每个面 UV 都是完整 0..1，贴图会铺满该面。
    /// </summary>
    public static List<Face> Build(float size = 1f)
    {
        float h = size * 0.5f;
        float x0 = -h, y0 = -h, z0 = -h;
        float x1 = h, y1 = h, z1 = h;

        var faces = new List<Face>(6);

        // ---------- +Y 顶面 ----------
        faces.Add(NewFace(FaceKey.Top,
            new Vec3(x0, y1, z0),
            new Vec3(x1, y1, z0),
            new Vec3(x1, y1, z1),
            new Vec3(x0, y1, z1),
            new Vec3(0, 1, 0)));

        // ---------- -Y 底面 ----------
        faces.Add(NewFace(FaceKey.Bottom,
            new Vec3(x1, y0, z0),
            new Vec3(x0, y0, z0),
            new Vec3(x0, y0, z1),
            new Vec3(x1, y0, z1),
            new Vec3(0, -1, 0)));

        // ---------- +Z 正面 ----------
        faces.Add(NewFace(FaceKey.Front,
            new Vec3(x0, y1, z1),
            new Vec3(x1, y1, z1),
            new Vec3(x1, y0, z1),
            new Vec3(x0, y0, z1),
            new Vec3(0, 0, 1)));

        // ---------- -Z 背面 ----------
        faces.Add(NewFace(FaceKey.Back,
            new Vec3(x1, y1, z0),
            new Vec3(x0, y1, z0),
            new Vec3(x0, y0, z0),
            new Vec3(x1, y0, z0),
            new Vec3(0, 0, -1)));

        // ---------- +X 右面 ----------
        faces.Add(NewFace(FaceKey.Right,
            new Vec3(x1, y1, z1),
            new Vec3(x1, y1, z0),
            new Vec3(x1, y0, z0),
            new Vec3(x1, y0, z1),
            new Vec3(1, 0, 0)));

        // ---------- -X 左面 ----------
        faces.Add(NewFace(FaceKey.Left,
            new Vec3(x0, y1, z0),
            new Vec3(x0, y1, z1),
            new Vec3(x0, y0, z1),
            new Vec3(x0, y0, z0),
            new Vec3(-1, 0, 0)));

        return faces;
    }

    private static Face NewFace(
        FaceKey key,
        Vec3 v0, Vec3 v1, Vec3 v2, Vec3 v3,
        Vec3 normal)
    {
        return new Face
        {
            Key = key,
            V0 = v0,
            V1 = v1,
            V2 = v2,
            V3 = v3,
            // 左上 → 右上 → 右下 → 左下（与顶点顺序对应）
            T0 = new Vec2(0f, 0f),
            T1 = new Vec2(1f, 0f),
            T2 = new Vec2(1f, 1f),
            T3 = new Vec2(0f, 1f),
            Normal = normal
        };
    }
}
