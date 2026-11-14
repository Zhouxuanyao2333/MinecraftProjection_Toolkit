using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using SkiaSharp;

namespace Projection3D.Toolkits.Textures;

/// <summary>
/// 贴图资源访问层。
///
/// 支持两种来源，调用方无需区分：
///   ① 磁盘目录（开发时 / 想换贴图时）——原行为不变；
///   ② 程序集内嵌资源（发布模式，tag 为 <c>embedded</c>）——
///      把 textures/block 下的贴图编进程序集，一个 exe 就能独立运行。
///
/// 内嵌资源的逻辑名形如：<c>Projection3D.textures.block.stone.png</c>，
/// 对外暴露的"路径"仍是 <c>Projection3D.textures.block\stone.png</c>，
/// 因此 <see cref="BlockDefinition"/> 里存的路径、以及 Path.GetDirectoryName
/// 之类的运算全部保持不变。
/// </summary>
public static class TextureResources
{
    /// <summary>内嵌资源时使用的虚拟根目录（仅作占位，不会真的访问磁盘）。</summary>
    public const string EmbeddedRoot = "Projection3D.textures.block";

    private static readonly Assembly AssetAssembly = typeof(TextureResources).Assembly;

    private static Dictionary<string, string>? _embeddedIndex;

    /// <summary>是否以内嵌资源模式运行（找不到磁盘贴图目录时自动开启）。</summary>
    public static bool Embedded { get; private set; }

    /// <summary>
    /// 定位贴图目录。优先磁盘，其次内嵌；都不存在返回空串。
    /// </summary>
    public static string ResolveBlockDir()
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, "textures", "block");
        if (Directory.Exists(candidate)) return Activate(candidate, embedded: false);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            candidate = Path.Combine(dir.FullName, "textures", "block");
            if (Directory.Exists(candidate)) return Activate(candidate, embedded: false);
            dir = dir.Parent;
        }

        if (HasEmbeddedTextures()) return Activate(EmbeddedRoot, embedded: true);

        return string.Empty;
    }

    private static string Activate(string dir, bool embedded)
    {
        Embedded = embedded;
        return dir;
    }

    /// <summary>当前资源根目录下的所有 PNG（磁盘取文件名，内嵌取索引键）。</summary>
    public static IEnumerable<string> EnumeratePngFiles(string blockDir)
    {
        if (!Embedded)
        {
            if (!Directory.Exists(blockDir)) return Array.Empty<string>();
            return Directory.EnumerateFiles(blockDir, "*.png");
        }

        var index = EnsureEmbeddedIndex();
        return index
            .Where(kv => kv.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)   // 返回"路径形态"的逻辑名，Path.GetFileNameWithoutExtension 才能取到方块名
            .ToList();
    }

    /// <summary>资源是否存在（磁盘文件或内嵌条目）。</summary>
    public static bool Exists(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!Embedded) return File.Exists(path);

        return EnsureEmbeddedIndex().ContainsKey(NormalizeLogicalName(path));
    }

    /// <summary>读取资源原始字节（PNG 编码数据，内嵌模式下自动解压）。</summary>
    public static byte[]? ReadAllBytes(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        if (!Embedded)
        {
            try { return File.ReadAllBytes(path); }
            catch { return null; }
        }

        var index = EnsureEmbeddedIndex();
        if (!index.TryGetValue(NormalizeLogicalName(path), out var resourceName)) return null;

        using var stream = AssetAssembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;

        using var ms = new MemoryStream((int)stream.Length);
        stream.CopyTo(ms);
        return Inflate(ms.ToArray());
    }

    /// <summary>
    /// 内嵌贴图在构建期用 deflate(zlib) 压缩过：PNG 本身已压缩，
    /// 再压一层能削掉重复的 zlib 头/块结构。解压失败则按原始 PNG 处理。
    /// </summary>
    private static byte[] Inflate(byte[] compressed)
    {
        try
        {
            using var input = new MemoryStream(compressed);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(compressed.Length * 3);
            z.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return compressed;
        }
    }

    /// <summary>解码为位图（磁盘与内嵌统一入口）。</summary>
    public static SKBitmap? DecodeBitmap(string path)
    {
        try
        {
            byte[]? bytes = ReadAllBytes(path);
            if (bytes == null || bytes.Length == 0) return null;

            using var data = SKData.CreateCopy(bytes);
            return SKBitmap.Decode(data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>取所在目录（内嵌模式返回虚拟根，供 Path.Combine 使用）。</summary>
    public static string GetDirectory(string path)
    {
        if (!Embedded) return Path.GetDirectoryName(path) ?? string.Empty;
        return EmbeddedRoot;
    }

    // ------------------------------------------------------------
    // 内嵌索引
    // ------------------------------------------------------------

    private static bool HasEmbeddedTextures()
    {
        return AssetAssembly
            .GetManifestResourceNames()
            .Any(n => n.StartsWith(EmbeddedRoot + ".", StringComparison.Ordinal)
                      && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> EnsureEmbeddedIndex()
    {
        if (_embeddedIndex != null) return _embeddedIndex;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in AssetAssembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(EmbeddedRoot + ".", StringComparison.Ordinal)) continue;

            // Projection3D.textures.block.stone.png
            //   → 去掉根前缀后：stone.png（文件名里不会有 .，所以直接去掉前缀即可）
            string fileName = name[(EmbeddedRoot.Length + 1)..];
            string logical = EmbeddedRoot + "\\" + fileName;

            map[logical] = name;
        }

        _embeddedIndex = map;
        return map;
    }

    /// <summary>
    /// 把外部传入的路径统一成内嵌索引的键：
    /// 允许 Projection3D.textures.block\stone.png / 大小写不同 / 正反斜杠混用。
    /// </summary>
    private static string NormalizeLogicalName(string path)
    {
        string name = Path.GetFileName(path.Replace('/', '\\'));
        return EmbeddedRoot + "\\" + name;
    }
}
