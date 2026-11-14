using System;
using System.Collections.Generic;
using Projection3D.Toolkits.Maths;
using Projection3D.Toolkits.Model;

namespace Projection3D.Toolkits.Render;

/// <summary>相机参数。yaw 绕 Y 轴水平旋转，pitch 绕 X 轴俯仰。</summary>
public sealed class Camera
{
    public float YawDeg { get; set; }
    public float PitchDeg { get; set; }

    /// <summary>相机到模型中心的距离（模型空间单位）。正交投影下不参与缩放，仅保留字段兼容。</summary>
    public float Distance { get; set; } = 6f;

    /// <summary>模型在视口中的占比（0~1）。</summary>
    public float FillFactor { get; set; } = 0.78f;

    /// <summary>
    /// 标准等轴测视角：yaw 45°、pitch 30°。
    /// pitch=30° 时顶面菱形正好是 2:1（Minecraft 社区方块图标的经典比例）。
    /// </summary>
    public static Camera Standard() => new() { YawDeg = 45f, PitchDeg = 30f };
}

/// <summary>投影到屏幕空间后的一个四边形面。</summary>
public sealed class ProjectedFace
{
    public FaceKey Key { get; init; }

    public float X0, Y0, X1, Y1, X2, Y2, X3, Y3;

    public Vec2 T0, T1, T2, T3;

    /// <summary>平均深度，用于画家算法排序（越大越远）。</summary>
    public float Depth { get; init; }

    /// <summary>光照系数（1.0 = 全亮）。</summary>
    public float Shade { get; init; }
}

/// <summary>
/// 软件 3D 投影管线：旋转 → 透视 → 背面剔除 → 光照 → 按深度排序。
/// 全部手写，无任何 3D 库依赖。
/// </summary>
public static class ProjectionRenderer
{
    public static List<ProjectedFace> Render(
        IReadOnlyList<Face> faces,
        Camera camera,
        float viewW,
        float viewH)
    {
        var result = new List<ProjectedFace>(faces.Count);
        if (faces.Count == 0 || viewW <= 0 || viewH <= 0) return result;

        float yaw = camera.YawDeg * MathF.PI / 180f;
        float pitch = camera.PitchDeg * MathF.PI / 180f;
        float cosY = MathF.Cos(yaw), sinY = MathF.Sin(yaw);
        float cosP = MathF.Cos(pitch), sinP = MathF.Sin(pitch);

        // ------------------------------------------------------------
        // 第一遍：求模型空间的包围盒与包围球
        //
        // ★ 关键：缩放和居中必须基于**模型空间**（旋转不变）的量。
        //   之前用"当前投影后的包围盒"自适应缩放，导致方块转一圈时
        //   投影宽度在 √2 与 1 之间变化，画面就会一会儿放大一会儿缩小
        //   （看起来像方块在前后走、还带点扭曲）。用包围球半径就恒定不变了。
        // ------------------------------------------------------------
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            Accumulate(f.V0);
            Accumulate(f.V1);
            Accumulate(f.V2);
            Accumulate(f.V3);

            void Accumulate(Vec3 v)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
                if (v.Z < minZ) minZ = v.Z;
                if (v.Z > maxZ) maxZ = v.Z;
            }
        }

        var center = new Vec3(
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f,
            (minZ + maxZ) * 0.5f);

        // 包围球半径（任何旋转角度下投影跨度都不会超过 2r）
        float radius = 0f;
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            radius = MathF.Max(radius, Dist(f.V0, center));
            radius = MathF.Max(radius, Dist(f.V1, center));
            radius = MathF.Max(radius, Dist(f.V2, center));
            radius = MathF.Max(radius, Dist(f.V3, center));
        }
        if (radius < 1e-4f) radius = 1e-4f;

        float scale = MathF.Min(viewW, viewH) * camera.FillFactor / (radius * 2f);

        float cx = viewW * 0.5f;
        float cy = viewH * 0.5f;

        // ------------------------------------------------------------
        // 第二遍：投影 + 背面剔除
        // ------------------------------------------------------------
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];

            var p0 = Project(f.V0, center, cosY, sinY, cosP, sinP, scale, cx, cy);
            var p1 = Project(f.V1, center, cosY, sinY, cosP, sinP, scale, cx, cy);
            var p2 = Project(f.V2, center, cosY, sinY, cosP, sinP, scale, cx, cy);
            var p3 = Project(f.V3, center, cosY, sinY, cosP, sinP, scale, cx, cy);

            // 背面剔除：屏幕空间叉乘，逆时针为正面
            float cross = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
            if (cross <= 0f) continue;

            result.Add(new ProjectedFace
            {
                Key = f.Key,
                X0 = p0.X, Y0 = p0.Y,
                X1 = p1.X, Y1 = p1.Y,
                X2 = p2.X, Y2 = p2.Y,
                X3 = p3.X, Y3 = p3.Y,
                T0 = f.T0, T1 = f.T1, T2 = f.T2, T3 = f.T3,
                Depth = -(p0.Z + p1.Z + p2.Z + p3.Z) * 0.25f,
                Shade = EstimateShade(f.Normal, cosY, sinY, cosP, sinP)
            });
        }

        // 画家算法：远的先画
        result.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        return result;
    }

    private static float Dist(Vec3 a, Vec3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>正交投影：平移到包围盒中心 → 旋转 → 屏幕坐标。Z 分量是相机空间深度。</summary>
    private static Vec3 Project(
        Vec3 v, Vec3 center,
        float cosY, float sinY,
        float cosP, float sinP,
        float scale, float cx, float cy)
    {
        float x = v.X - center.X;
        float y = v.Y - center.Y;
        float z = v.Z - center.Z;

        // Yaw（绕 Y）
        float x1 = x * cosY + z * sinY;
        float z1 = -x * sinY + z * cosY;

        // Pitch（绕 X）
        float y1 = y * cosP - z1 * sinP;
        float z2 = y * sinP + z1 * cosP;

        float sx = cx + x1 * scale;
        float sy = cy - y1 * scale;   // 屏幕 Y 向下

        return new Vec3(sx, sy, z2);
    }

    /// <summary>
    /// Lambert 光照。光方向定义在相机空间（略偏左上），
    /// 再逆变换回模型空间，效果是"光跟着相机走"，任何角度都不会死黑。
    /// </summary>
    private static float EstimateShade(
        Vec3 normal,
        float cosY, float sinY,
        float cosP, float sinP)
    {
        // 相机空间光方向
        float lx = -0.35f, ly = 0.55f, lz = 0.75f;
        float len = MathF.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= len; ly /= len; lz /= len;

        // 逆 Pitch
        float ly1 = ly * cosP + lz * sinP;
        float lz1 = -ly * sinP + lz * cosP;
        // 逆 Yaw
        float lxm = lx * cosY - lz1 * sinY;
        float lzm = lx * sinY + lz1 * cosY;

        float dot = normal.X * lxm + normal.Y * ly1 + normal.Z * lzm;

        // 环境光 0.55 + 漫反射 0.45
        return 0.55f + 0.45f * MathF.Max(0f, dot);
    }
}
