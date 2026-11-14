using System;

namespace Projection3D.Toolkits.Maths;

/// <summary>纹理坐标 / 屏幕点（归一化 UV 或像素坐标）。</summary>
public struct Vec2
{
    public float U;
    public float V;

    public Vec2(float u, float v)
    {
        U = u;
        V = v;
    }
}

/// <summary>模型空间顶点。Y 轴向上，方块中心在原点。</summary>
public struct Vec3
{
    public float X;
    public float Y;
    public float Z;

    public Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}
