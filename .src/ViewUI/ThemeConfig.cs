using Avalonia.Media;

namespace Projection3D.ViewUI;

/// <summary>
/// 全局外观配置。所有美化参数集中在这里，后期做设置页时接上 UI 就能实时调整。
/// </summary>
public static class ThemeConfig
{
    // ============================================================
    // 亚克力强度
    // ============================================================

    /// <summary>背景渐变层的整体不透明度（0 = 全透明，1 = 完全不透明）。</summary>
    public static double GradientOpacity { get; set; } = 0.82;

    /// <summary>内容卡片的不透明度（越大越"实"，越小越透）。</summary>
    public static double CardOpacity { get; set; } = 0.55;

    /// <summary>侧边栏卡片不透明度。</summary>
    public static double SidebarOpacity { get; set; } = 0.62;

    // ============================================================
    // 渐变配色（白 + 粉）
    // ============================================================

    /// <summary>背景渐变起点（左上）。</summary>
    public static Color GradientStart { get; set; } = Color.Parse("#FFFFFF");

    /// <summary>背景渐变中间色。</summary>
    public static Color GradientMid { get; set; } = Color.Parse("#FFF0F6");

    /// <summary>背景渐变终点（右下）。</summary>
    public static Color GradientEnd { get; set; } = Color.Parse("#FFD6E6");

    /// <summary>强调色（主按钮、进度条）。</summary>
    public static Color Accent { get; set; } = Color.Parse("#FF7EB3");

    /// <summary>强调色浅版（次按钮、选中态）。</summary>
    public static Color AccentSoft { get; set; } = Color.Parse("#FFB8D2");

    /// <summary>正文文字色。</summary>
    public static Color TextPrimary { get; set; } = Color.Parse("#3B3340");

    /// <summary>次要文字色。</summary>
    public static Color TextSecondary { get; set; } = Color.Parse("#8A8090");

    /// <summary>渲染区背景（浅色，让透明方块也能看清）。</summary>
    public static Color ViewportBg { get; set; } = Color.Parse("#FBF7FA");

    /// <summary>标题栏纯色背景（白色标题文字压在上面）。</summary>
    public static Color TitleBar { get; set; } = Color.Parse("#FF8FB8");

    // ============================================================
    // 圆角
    // ============================================================
    public const double RadiusLarge = 18;
    public const double RadiusMedium = 12;
    public const double RadiusSmall = 9;

    // ============================================================
    // 动画时长（毫秒）—— 线性优雅、收尾带一点弹性
    // ============================================================

    /// <summary>窗口启动动画时长（上移 + 摆正 + 淡入）。</summary>
    public const double WindowIntroMs = 760;

    /// <summary>欢迎弹窗出现时长（果冻感）。</summary>
    public const double ToastIntroMs = 460;

    /// <summary>欢迎弹窗滑出时长（向右）。</summary>
    public const double ToastExitMs = 420;

    /// <summary>欢迎弹窗自动消失前的停留时间。</summary>
    public const double ToastLifetimeMs = 6000;
}
