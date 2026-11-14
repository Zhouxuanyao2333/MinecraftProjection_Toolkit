using System;
using System.Collections.Generic;
using System.IO;
using Projection3D.Toolkits.Model;
using SkiaSharp;

namespace Projection3D.Toolkits.Render;

/// <summary>
/// 离屏渲染：把方块按指定相机参数渲染成位图并导出 PNG。
/// 用于"保存标准三面图"（固定等轴测视角，与用户在预览里转到哪无关）。
/// </summary>
public static class OffscreenRenderer
{
    /// <summary>渲染成透明背景的位图。</summary>
    public static SKBitmap Render(
        IReadOnlyList<Face> faces,
        Camera camera,
        int width,
        int height,
        Func<FaceKey, SKImage?> getTexture)
    {
        if (width <= 0) width = 1;
        if (height <= 0) height = 1;

        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            var projected = ProjectionRenderer.Render(faces, camera, width, height);
            SkiaFacePainter.Draw(canvas, projected, getTexture);
            canvas.Flush();
        }
        return bitmap;
    }

    /// <summary>渲染并写入 PNG 文件。</summary>
    public static void SavePng(
        string outputPath,
        IReadOnlyList<Face> faces,
        Camera camera,
        int width,
        int height,
        Func<FaceKey, SKImage?> getTexture)
    {
        using var bitmap = Render(faces, camera, width, height, getTexture);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
    }
}
