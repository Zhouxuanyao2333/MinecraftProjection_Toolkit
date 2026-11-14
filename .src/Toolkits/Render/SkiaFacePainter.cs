using System;
using System.Collections.Generic;
using Projection3D.Toolkits.Model;
using SkiaSharp;

namespace Projection3D.Toolkits.Render;

/// <summary>
/// 把投影后的面绘制到 Skia 画布上。
/// 屏幕渲染和离屏导出共用这一套逻辑，保证"预览"和"保存的图"一致。
/// </summary>
public static class SkiaFacePainter
{
    public static void Draw(
        SKCanvas canvas,
        IReadOnlyList<ProjectedFace> faces,
        Func<FaceKey, SKImage?> getTexture)
    {
        if (faces.Count == 0) return;

        foreach (var f in faces)
        {
            var tex = getTexture(f.Key);
            if (tex == null) continue;

            float tw = tex.Width;
            float th = tex.Height;

            // 四边形拆成两个三角形
            var positions = new SKPoint[]
            {
                new(f.X0, f.Y0),
                new(f.X1, f.Y1),
                new(f.X2, f.Y2),
                new(f.X0, f.Y0),
                new(f.X2, f.Y2),
                new(f.X3, f.Y3),
            };

            var texCoords = new SKPoint[]
            {
                new(f.T0.U * tw, f.T0.V * th),
                new(f.T1.U * tw, f.T1.V * th),
                new(f.T2.U * tw, f.T2.V * th),
                new(f.T0.U * tw, f.T0.V * th),
                new(f.T2.U * tw, f.T2.V * th),
                new(f.T3.U * tw, f.T3.V * th),
            };

            byte shade = (byte)Math.Clamp(f.Shade * 255f, 0f, 255f);
            var colors = new SKColor[]
            {
                new(shade, shade, shade, 255),
                new(shade, shade, shade, 255),
                new(shade, shade, shade, 255),
                new(shade, shade, shade, 255),
                new(shade, shade, shade, 255),
                new(shade, shade, shade, 255),
            };

            // ★ 像素风的关键：最近邻采样。
            //   SkiaSharp 3.x 里 SKPaint.FilterQuality 已废弃且不再生效，
            //   采样模式必须在创建 shader 时通过 SKSamplingOptions 指定。
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            using var shader = SKShader.CreateImage(
                tex, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);

            using var paint = new SKPaint
            {
                IsAntialias = false,
                Shader = shader
            };

            canvas.DrawVertices(SKVertexMode.Triangles, positions, texCoords, colors, paint);
        }
    }
}
