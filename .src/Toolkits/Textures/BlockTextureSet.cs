using System;
using System.Collections.Generic;
using System.IO;
using Projection3D.Toolkits.Model;
using SkiaSharp;

namespace Projection3D.Toolkits.Textures;

/// <summary>
/// 一个方块六面贴图的运行时缓存。按需解码，释放时统一清理。
/// </summary>
public sealed class BlockTextureSet : IDisposable
{
    private readonly Dictionary<FaceKey, SKImage> _images = new();
    private readonly Dictionary<FaceKey, string> _paths = new();

    /// <summary>贴图边长（Minecraft 方块贴图通常 16）。</summary>
    public float TextureSize { get; private set; } = 16f;

    public static BlockTextureSet Load(BlockDefinition def)
    {
        var set = new BlockTextureSet();

        // 被雪覆盖的草方块：顶面 snow、侧面 dirt + 雪裙边、不染色
        if (def.SnowCovered)
        {
            LoadSnowCoveredGrass(def, set);
            if (!set.IsEmpty) return set;
        }

        // 草方块：走专门的染色合成流程
        //   top    = grass_block_top          × 群系色
        //   side   = grass_block_side         叠加 (grass_block_side_overlay × 群系色)
        //   bottom = dirt
        if (def.TintRgb is uint tint)
        {
            LoadTintedGrass(def, set, tint);
            if (!set.IsEmpty) return set;
            // 合成失败就退回到普通加载
        }

        // 精灵类方块（花/草/树苗）的贴图四周有大片透明区，
        // 若整张映射到平面，图案只占中间一小块，渲染出来会小得看不清。
        // 这类形状先按 alpha 包围盒裁剪，再按完整 UV 贴图。
        bool trimAlpha = def.Shape == BlockShapeKind.Cross;

        foreach (FaceKey key in Enum.GetValues<FaceKey>())
        {
            string path = def.GetPath(key);
            if (string.IsNullOrEmpty(path) || !TextureResources.Exists(path)) continue;

            var img = LoadImage(path, trimAlpha);
            if (img == null) continue;

            // 土径侧面：把透明的顶行用相邻不透明行填平，避免顶面边缘透光
            if (def.FillTransparentTopEdge)
            {
                img = FillTransparentTopEdge(img);
                if (img == null) continue;
            }

            set._images[key] = img;
            set._paths[key] = path;
            set.TextureSize = img.Width;
        }

        return set;
    }

    /// <summary>
    /// 把图片顶部完全透明的行，用第一条不透明行填充。
    /// 用于 dirt_path_side 这类"顶部一行故意透明（在游戏里被顶面遮住）"的贴图。
    /// </summary>
    private static SKImage FillTransparentTopEdge(SKImage source)
    {
        int w = source.Width;
        int h = source.Height;

        using var bmp = SKBitmap.FromImage(source);
        if (bmp == null) return source;

        // 找第一条有内容的行
        int firstOpaque = -1;
        for (int y = 0; y < h && firstOpaque < 0; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (bmp.GetPixel(x, y).Alpha > 0) { firstOpaque = y; break; }
            }
        }

        if (firstOpaque <= 0) return source;   // 全透明或首行就有内容，不用处理

        var result = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 前 firstOpaque 行用第一条不透明行的像素填充
                var c = bmp.GetPixel(x, y < firstOpaque ? firstOpaque : y);
                result.SetPixel(x, y, c);
            }
        }

        var image = SKImage.FromPixelCopy(result.Info, result.GetPixels(), result.RowBytes);
        result.Dispose();
        return image;
    }

    /// <summary>
    /// 被雪覆盖的草方块（grass_block_snow）。
    ///
    /// 与普通草的区别：
    ///   · 顶面不是草顶，而是 snow.png
    ///   · 侧面 = dirt（主体） + grass_block_snow.png（雪裙边）
    ///   · 不使用 grass_block_side_overlay，也不做群系染色
    /// </summary>
    private static void LoadSnowCoveredGrass(BlockDefinition def, BlockTextureSet set)
    {
        string sample = !string.IsNullOrEmpty(def.AnyPath)
            ? def.AnyPath
            : def.GetPath(FaceKey.Front);

        if (string.IsNullOrEmpty(sample)) return;

        string dir = TextureResources.GetDirectory(sample);

        string dirtPath = Path.Combine(dir, "dirt.png");
        string sideBasePath = Path.Combine(dir, "grass_block_side.png");
        string snowEdgePath = Path.Combine(dir, "grass_block_snow.png");
        string snowTopPath = Path.Combine(dir, "snow.png");

        // ---- 顶面：snow ----
        if (TextureResources.Exists(snowTopPath))
        {
            var snowTop = LoadImage(snowTopPath, trimAlpha: false);
            if (snowTop != null) set._images[FaceKey.Top] = snowTop;
        }

        // ---- 侧面：dirt（主体）叠加 雪裙边 ----
        //   主体优先用 dirt；没有 dirt 就退回 grass_block_side
        string basePath = TextureResources.Exists(dirtPath) ? dirtPath : sideBasePath;

        if (TextureResources.Exists(basePath))
        {
            using var baseBmp = DecodeBitmap(basePath);
            if (baseBmp != null)
            {
                SKImage? snowEdge = null;
                if (TextureResources.Exists(snowEdgePath)) snowEdge = LoadImage(snowEdgePath, trimAlpha: false);

                var sideImg = ComposeBitmap(baseBmp, snowEdge);
                snowEdge?.Dispose();

                if (sideImg != null)
                {
                    set._images[FaceKey.Front] = sideImg;
                    set._images[FaceKey.Back] = sideImg;
                    set._images[FaceKey.Left] = sideImg;
                    set._images[FaceKey.Right] = sideImg;
                    set.TextureSize = sideImg.Width;
                }
            }
        }

        // ---- 底面：泥土 ----
        if (TextureResources.Exists(dirtPath))
        {
            var dirtImg = LoadImage(dirtPath, trimAlpha: false);
            if (dirtImg != null && !set._images.ContainsKey(FaceKey.Bottom))
                set._images[FaceKey.Bottom] = dirtImg;
            else
                dirtImg?.Dispose();
        }
    }

    /// <summary>
    /// 草方块染色合成。
    /// 灰度贴图 × 群系颜色 = 真正的草色；裙边 overlay 再叠加到 dirt 侧面上。
    /// </summary>
    private static void LoadTintedGrass(BlockDefinition def, BlockTextureSet set, uint tint)
    {
        string topPath = def.GetPath(FaceKey.Top);
        string sidePath = def.GetPath(FaceKey.Front);

        if (string.IsNullOrEmpty(topPath) || !TextureResources.Exists(topPath)) return;
        if (string.IsNullOrEmpty(sidePath) || !TextureResources.Exists(sidePath)) return;

        string dir = TextureResources.GetDirectory(topPath);
        string overlayPath = Path.Combine(dir, "grass_block_side_overlay.png");
        string dirtPath = Path.Combine(dir, "dirt.png");

        // ---- 顶面：灰草顶 × 群系色 ----
        using (var topBmp = DecodeBitmap(topPath))
        {
            if (topBmp == null) return;

            var tintedTop = TintBitmap(topBmp, tint);
            set._images[FaceKey.Top] = tintedTop;
            set.TextureSize = tintedTop.Width;
        }

        // ---- 侧面：dirt/草边 叠加 染色裙边 ----
        using (var sideBmp = DecodeBitmap(sidePath))
        {
            if (sideBmp != null)
            {
                SKImage? tintedOverlay = null;

                if (TextureResources.Exists(overlayPath))
                {
                    using var overlayBmp = DecodeBitmap(overlayPath);
                    if (overlayBmp != null) tintedOverlay = TintBitmap(overlayBmp, tint);
                }

                var sideImg = ComposeBitmap(sideBmp, tintedOverlay);
                tintedOverlay?.Dispose();

                if (sideImg != null)
                {
                    set._images[FaceKey.Front] = sideImg;
                    set._images[FaceKey.Back] = sideImg;
                    set._images[FaceKey.Left] = sideImg;
                    set._images[FaceKey.Right] = sideImg;
                }
            }
        }

        // ---- 底面：泥土 ----
        if (TextureResources.Exists(dirtPath))
        {
            var dirtImg = LoadImage(dirtPath, trimAlpha: false);
            if (dirtImg != null) set._images[FaceKey.Bottom] = dirtImg;
        }
    }

    private static SKBitmap? DecodeBitmap(string path)
    {
        try
        {
            return TextureResources.DecodeBitmap(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把灰度图乘以群系颜色（RGB 相乘，保留 alpha）。
    ///
    /// ★ 必须用 SKImage.FromPixelCopy：FromBitmap 只引用 bitmap 内存不复制，
    ///   局部 bitmap 一旦被 GC/Dipose，纹理就变成未初始化的垃圾数据（表现为整面发灰）。
    /// </summary>
    private static SKImage TintBitmap(SKBitmap source, uint rgb)
    {
        byte tr = (byte)((rgb >> 16) & 0xFF);
        byte tg = (byte)((rgb >> 8) & 0xFF);
        byte tb = (byte)(rgb & 0xFF);

        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                byte r = (byte)(c.Red * tr / 255);
                byte g = (byte)(c.Green * tg / 255);
                byte b = (byte)(c.Blue * tb / 255);
                result.SetPixel(x, y, new SKColor(r, g, b, c.Alpha));
            }
        }

        var image = SKImage.FromPixelCopy(result.Info, result.GetPixels(), result.RowBytes);
        result.Dispose();
        return image;
    }

    /// <summary>把 overlay 叠加到底图上（overlay 透明处不覆盖）。</summary>
    private static SKImage? ComposeBitmap(SKBitmap baseBmp, SKImage? overlay)
    {
        var result = new SKBitmap(baseBmp.Width, baseBmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(baseBmp, 0, 0);

            if (overlay != null)
            {
                using var paint = new SKPaint
                {
                    IsAntialias = false,
                    BlendMode = SKBlendMode.SrcOver
                };
                canvas.DrawImage(overlay, 0, 0, paint);
            }
        }

        var image = SKImage.FromPixelCopy(result.Info, result.GetPixels(), result.RowBytes);
        result.Dispose();
        return image;
    }

    public SKImage? Get(FaceKey key)
    {
        return _images.TryGetValue(key, out var img) ? img : null;
    }

    public bool IsEmpty => _images.Count == 0;

    private static SKImage? LoadImage(string path, bool trimAlpha)
    {
        try
        {
            using var bitmap = TextureResources.DecodeBitmap(path);
            if (bitmap == null) return null;

            // Minecraft 方块贴图可能是"动画帧竖直排列"（如命令方块侧面是 16×64 的 4 帧）。
            // 导出静态图标时只取第一帧，否则一个面会重复显示多帧。
            if (bitmap.Height > bitmap.Width && bitmap.Height % bitmap.Width == 0)
            {
                using var frame = ExtractFirstFrame(bitmap);
                return trimAlpha ? TrimTransparent(frame) : SKImage.FromBitmap(frame);
            }

            return trimAlpha ? TrimTransparent(bitmap) : SKImage.FromBitmap(bitmap);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从动画贴图中裁出第一帧（顶部 width×width 区域）。</summary>
    private static SKBitmap ExtractFirstFrame(SKBitmap source)
    {
        int w = source.Width;
        var frame = new SKBitmap(w, w, source.ColorType, source.AlphaType);
        using (var canvas = new SKCanvas(frame))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                source,
                new SKRect(0, 0, w, w),        // 源：第一帧
                new SKRect(0, 0, w, w),        // 目标：整帧
                new SKPaint { IsAntialias = false });
        }
        return frame;
    }

    /// <summary>
    /// 按 alpha 包围盒裁掉四周透明区。
    /// 用于花/草这类"图案只占贴图中间一小块"的精灵方块。
    /// </summary>
    private static SKImage TrimTransparent(SKBitmap source)
    {
        int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (source.GetPixel(x, y).Alpha > 8)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        // 没有内容或已经占满 → 原样返回
        if (maxX < 0 || (minX == 0 && minY == 0 && maxX == source.Width - 1 && maxY == source.Height - 1))
        {
            return SKImage.FromBitmap(source);
        }

        int w = maxX - minX + 1;
        int h = maxY - minY + 1;

        var cropped = new SKBitmap(w, h, source.ColorType, source.AlphaType);
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                source,
                new SKRect(minX, minY, maxX + 1, maxY + 1),
                new SKRect(0, 0, w, h),
                new SKPaint { IsAntialias = false });
        }
        return SKImage.FromBitmap(cropped);
    }

    public void Dispose()
    {
        foreach (var img in _images.Values) img.Dispose();
        _images.Clear();
        _paths.Clear();
    }
}
