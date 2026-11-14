using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Projection3D.Toolkits.Model;
using Projection3D.Toolkits.Render;
using Projection3D.Toolkits.Textures;

namespace Projection3D.ViewUI;

public partial class MainWindow : Window
{
    // ============================================================
    // 窗口圆角裁切（Windows Region）
    //
    // WindowDecorations=None 时，窗口的系统背景（亚克力/模糊）不会被 Avalonia
    // 的 Border.CornerRadius 裁切 —— 那是 DWM 在窗口背后画的直角矩形。
    // 必须用 SetWindowRgn 把整个窗口（含亚克力）裁成圆角。
    // ============================================================

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
        int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    private const int WindowCornerRadius = 16;

    // ============================================================
    // 窗口边缘拖动调整大小
    // ============================================================

    private const double ResizeBorderThickness = 6;

    /// <summary>导出尺寸（与市面常见方块图标一致）。</summary>
    private const int ExportSize = 300;

    /// <summary>弹窗进度条可填充的最大宽度（弹窗宽 326 − 左右内边距 18×2）。</summary>
    private const double ToastProgressMaxWidth = 290;

    private readonly List<BlockDefinition> _allBlocks = new();
    private readonly List<Face> _faces = CubeModel.Build(1f);

    private BlockTextureSet? _currentTextures;
    private BlockDefinition? _currentBlock;

    // ---------- 欢迎弹窗动画状态 ----------
    private enum ToastPhase { Hidden, Intro, Progress, Exit }

    private ToastPhase _toastPhase = ToastPhase.Hidden;
    private DispatcherTimer? _toastTimer;
    private DateTime _toastPhaseStart;

    // 弹窗的变换对象（XAML 里 TransformGroup 内的命名元素不会生成字段，只能在代码里建）
    private readonly ScaleTransform _toastScale = new(0.88, 0.88);
    private readonly TranslateTransform _toastTranslate = new(0, 34);

    // 教程动画用的变换对象
    private readonly ScaleTransform _tutorialCardScale = new(0.92, 0.92);
    private readonly TranslateTransform _tutorialCardTranslate = new(0, 26);
    private bool _tutorialAnimating;

    public MainWindow()
    {
        InitializeComponent();

        // ---------- 启动动画：初始态 ----------
        // 向下偏移 64px、轻微逆时针歪斜、完全透明；
        // 窗口显示后（Opened）切换成正常状态，由 XAML 里的 Transitions 负责补间。
        Opacity = 0;
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        var introTransform = new TransformGroup();
        introTransform.Children.Add(new TranslateTransform(0, 64));
        introTransform.Children.Add(new RotateTransform(-4.5));
        RenderTransform = introTransform;

        SearchBox.TextChanged += (_, _) => ApplyFilter();
        SaveButton.IsEnabled = false;

        Viewer.AnglesChanged += (_, _) => UpdateAngleText();

        WelcomeToast.RenderTransform = new TransformGroup
        {
            Children = { _toastScale, _toastTranslate }
        };

        // ---------- 教程动画的 Transitions ----------
        // 卡片：缩放 + 位移 + 透明度（果冻感弹出）
        TutorialCard.RenderTransform = new TransformGroup
        {
            Children = { _tutorialCardScale, _tutorialCardTranslate }
        };
        TutorialCard.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(320),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut()
            },
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(380),
                Easing = new Avalonia.Animation.Easings.BackEaseOut()
            }
        };

        // 内容区：步骤切换时淡入淡出
        TutorialContent.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut()
            }
        };

        Opened += async (_, _) =>
        {
            // ---------- 触发启动动画：回到正位、完全不透明 ----------
            RenderTransform = null;
            Opacity = 1;

            await LoadCatalogAsync();
            UpdateAngleText();

            // 给启动动画一点时间，再弹欢迎窗，避免两个动画打架
            await Task.Delay(420);
            ShowWelcomeToast();
        };

        Closed += (_, _) =>
        {
            _toastTimer?.Stop();
            _currentTextures?.Dispose();
        };

        // ---------- 窗口圆角裁切 + 边缘拖动调整大小 ----------
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel);

        Opened += (_, _) => ApplyRoundedWindowRegion();
        SizeChanged += (_, _) => ApplyRoundedWindowRegion();
    }

    // ============================================================
    // 加载方块目录
    // ============================================================

    private async Task LoadCatalogAsync()
    {
        string blockDir = ResolveTextureBlockDir();

        if (string.IsNullOrEmpty(blockDir))
        {
            StatusText.Text = "未找到 textures\\block 目录";
            CountText.Text = "0 个方块";
            return;
        }

        StatusText.Text = "正在扫描贴图…";

        var blocks = await Task.Run(() => BlockCatalog.Scan(blockDir));

        _allBlocks.Clear();
        _allBlocks.AddRange(blocks);

        ApplyFilter();

        // 默认选中命令方块（演示用），没有就选第一个
        var initial = _allBlocks.FirstOrDefault(b =>
                          b.Name.Equals("command_block", StringComparison.OrdinalIgnoreCase))
                      ?? _allBlocks.FirstOrDefault();

        if (initial != null)
        {
            BlockList.SelectedItem = initial.Name;
        }
        else
        {
            StatusText.Text = "该目录下没有可用方块";
        }
    }

    /// <summary>
    /// 找到贴图目录。优先输出目录，其次从可执行目录逐级向上找。
    /// </summary>
    private static string ResolveTextureBlockDir()
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, "textures", "block");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            candidate = Path.Combine(dir.FullName, "textures", "block");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return string.Empty;
    }

    // ============================================================
    // 列表筛选
    // ============================================================

    private void ApplyFilter()
    {
        string keyword = (SearchBox.Text ?? string.Empty).Trim();

        IEnumerable<BlockDefinition> query = _allBlocks;
        if (keyword.Length > 0)
        {
            query = _allBlocks.Where(b =>
                b.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var list = query.Select(b => b.Name).ToList();

        string? keep = BlockList.SelectedItem as string;

        BlockList.ItemsSource = list;
        CountText.Text = $"{list.Count} 个方块";

        if (keep != null && list.Contains(keep))
        {
            BlockList.SelectedItem = keep;
        }
    }

    // ============================================================
    // 选择方块 → 加载纹理
    // ============================================================

    private void OnBlockSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BlockList.SelectedItem is not string name) return;

        var def = _allBlocks.FirstOrDefault(b =>
            b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (def == null) return;

        // 释放旧纹理
        _currentTextures?.Dispose();
        _currentTextures = null;

        var set = BlockTextureSet.Load(def);
        if (set.IsEmpty)
        {
            StatusText.Text = $"{name}：贴图加载失败";
            SaveButton.IsEnabled = false;
            return;
        }

        _currentTextures = set;
        _currentBlock = def;

        // 按几何类型（或精确模型表）重建模型，导出时也用这一份
        _faces.Clear();
        _faces.AddRange(def.BuildFaces());

        Viewer.Block = def;
        Viewer.Textures = set;
        Viewer.ResetView();

        SaveButton.IsEnabled = true;
        StatusText.Text = $"{name} · {ShapeLabel(def.Shape)}";
        UpdateAngleText();
    }

    private static string ShapeLabel(BlockShapeKind shape) => shape switch
    {
        BlockShapeKind.Cube => "完整立方体",
        BlockShapeKind.Cross => "十字交叉",
        BlockShapeKind.Flat => "扁平层",
        BlockShapeKind.Slab => "半砖",
        BlockShapeKind.Thin => "薄板",
        BlockShapeKind.Column => "细柱",
        BlockShapeKind.WallPlane => "贴墙平面",
        BlockShapeKind.SmallCube => "小方块",
        BlockShapeKind.Pointed => "锥形",
        BlockShapeKind.Bed => "床",
        _ => shape.ToString()
    };

    // ============================================================
    // 角度提示
    // ============================================================

    private void UpdateAngleText()
    {
        if (AngleText == null) return;
        AngleText.Text = $"yaw {Viewer.YawDeg:0}° · pitch {Viewer.PitchDeg:0}°";
    }

    // ============================================================
    // 配色方案切换（樱花粉 / 薰衣草 / 薄荷青）
    // ============================================================

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        ApplyPreset(combo.SelectedIndex);
    }

    private void ApplyPreset(int index)
    {
        if (Application.Current == null) return;

        (Color start, Color mid, Color end, Color accent, Color accentSoft) = index switch
        {
            1 => (Color.Parse("#FFFFFF"), Color.Parse("#F4F1FF"), Color.Parse("#DED6F7"),
                  Color.Parse("#A78BFA"), Color.Parse("#D8CDFB")),
            2 => (Color.Parse("#FFFFFF"), Color.Parse("#EEFBF6"), Color.Parse("#CBEEE4"),
                  Color.Parse("#5EC9A8"), Color.Parse("#BCEBDC")),
            _ => (Color.Parse("#FFFFFF"), Color.Parse("#FFF1F7"), Color.Parse("#FFD8E8"),
                  Color.Parse("#FF7EB3"), Color.Parse("#FFC6DB"))
        };

        // 标题栏纯色（浅粉 / 浅紫 / 浅青），白色文字压在上面
        Color titleBar = index switch
        {
            1 => Color.Parse("#A78BFA"),
            2 => Color.Parse("#5FCAB0"),
            _ => Color.Parse("#FF8FB8")
        };

        var res = Application.Current.Resources;

        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };
        gradient.GradientStops.Add(new GradientStop(start, 0));
        gradient.GradientStops.Add(new GradientStop(mid, 0.52));
        gradient.GradientStops.Add(new GradientStop(end, 1));

        // 主按钮渐变：用主题色做浅→深，保证与当前配色一致
        var primaryGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };
        primaryGradient.GradientStops.Add(new GradientStop(accentSoft, 0));
        primaryGradient.GradientStops.Add(new GradientStop(accent, 1));

        res["BgGradient"] = gradient;
        res["AccentBrush"] = new SolidColorBrush(accent);
        res["AccentSoftBrush"] = new SolidColorBrush(accentSoft);
        res["AccentGlowBrush"] = new SolidColorBrush(accent, 0.20);
        res["TitleBarBrush"] = new SolidColorBrush(titleBar);
        res["PrimaryButtonBrush"] = primaryGradient;

        ThemeConfig.GradientStart = start;
        ThemeConfig.GradientMid = mid;
        ThemeConfig.GradientEnd = end;
        ThemeConfig.Accent = accent;
        ThemeConfig.AccentSoft = accentSoft;
        ThemeConfig.TitleBar = titleBar;
    }

    // ============================================================
    // 方向按钮：每次转 90°（带过渡动画）
    // ============================================================

    private void OnRotateLeftClicked(object? sender, RoutedEventArgs e)
        => Viewer.RotateBy(-90f, 0f);

    private void OnRotateRightClicked(object? sender, RoutedEventArgs e)
        => Viewer.RotateBy(90f, 0f);

    private void OnRotateUpClicked(object? sender, RoutedEventArgs e)
        => Viewer.RotateBy(0f, -90f);

    private void OnRotateDownClicked(object? sender, RoutedEventArgs e)
        => Viewer.RotateBy(0f, 90f);

    // ============================================================
    // 重置视角
    // ============================================================

    private void OnResetClicked(object? sender, RoutedEventArgs e)
    {
        Viewer.ResetView();
    }

    // ============================================================
    // 保存：导出**当前视角**的画面
    // ============================================================

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentBlock == null || _currentTextures == null)
        {
            StatusText.Text = "请先选择方块";
            return;
        }

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存当前视角",
                SuggestedFileName = _currentBlock.Name + ".png",
                DefaultExtension = "png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG 图片")
                    {
                        Patterns = new[] { "*.png" }
                    }
                }
            });

            if (file == null)
            {
                StatusText.Text = "已取消保存";
                return;
            }

            string outputPath = file.Path.LocalPath;

            var camera = new Camera
            {
                YawDeg = Viewer.YawDeg,
                PitchDeg = Viewer.PitchDeg,
                FillFactor = 0.82f
            };

            var textures = _currentTextures;
            string angleInfo = $"yaw {Viewer.YawDeg:0}° / pitch {Viewer.PitchDeg:0}°";

            await Task.Run(() =>
            {
                OffscreenRenderer.SavePng(
                    outputPath,
                    _faces,
                    camera,
                    ExportSize,
                    ExportSize,
                    textures.Get);
            });

            StatusText.Text = $"已保存 {Path.GetFileName(outputPath)}（{angleInfo}）";
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
    }

    // ============================================================
    // 右下角欢迎弹窗
    //
    //   出现：从小放大 + 从下往上 + 淡入（带一点回弹的果冻感）
    //   停留：底部进度条缓缓填满
    //   消失：向右滑出并淡出
    // ============================================================

    private void ShowWelcomeToast()
    {
        _toastPhase = ToastPhase.Intro;
        _toastPhaseStart = DateTime.UtcNow;

        WelcomeToast.IsVisible = true;
        WelcomeToast.Opacity = 0;
        _toastScale.ScaleX = 0.88;
        _toastScale.ScaleY = 0.88;
        _toastTranslate.X = 0;
        _toastTranslate.Y = 34;
        ToastProgressFill.Width = 0;

        _toastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _toastTimer.Tick -= OnToastTick;
        _toastTimer.Tick += OnToastTick;
        _toastTimer.Start();
    }

    private void OnToastTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _toastPhaseStart).TotalMilliseconds;

        switch (_toastPhase)
        {
            case ToastPhase.Intro:
            {
                double t = Math.Clamp(elapsed / ThemeConfig.ToastIntroMs, 0, 1);
                double eased = EaseOutBack(t);          // 轻微过冲 → 果冻感

                double scale = 0.88 + 0.12 * eased;
                _toastScale.ScaleX = scale;
                _toastScale.ScaleY = scale;
                _toastTranslate.Y = 34 * (1 - eased);
                WelcomeToast.Opacity = Math.Clamp(t * 1.5, 0, 1);

                if (t >= 1)
                {
                    _toastPhase = ToastPhase.Progress;
                    _toastPhaseStart = DateTime.UtcNow;
                }
                break;
            }

            case ToastPhase.Progress:
            {
                double p = Math.Clamp(elapsed / ThemeConfig.ToastLifetimeMs, 0, 1);
                ToastProgressFill.Width = ToastProgressMaxWidth * p;

                if (p >= 1)
                {
                    _toastPhase = ToastPhase.Exit;
                    _toastPhaseStart = DateTime.UtcNow;
                }
                break;
            }

            case ToastPhase.Exit:
            {
                double t = Math.Clamp(elapsed / ThemeConfig.ToastExitMs, 0, 1);
                double eased = t * t;                   // 加速滑出

                _toastTranslate.X = 350 * eased;
                WelcomeToast.Opacity = 1 - eased;

                if (t >= 1)
                {
                    _toastTimer?.Stop();
                    WelcomeToast.IsVisible = false;
                    _toastPhase = ToastPhase.Hidden;
                }
                break;
            }
        }
    }

    /// <summary>EaseOutBack：末尾轻微过冲再回落，就是那种"果冻感"。</summary>
    private static double EaseOutBack(double t)
    {
        const double s = 1.35;
        double u = t - 1.0;
        return u * u * ((s + 1) * u + s) + 1.0;
    }

    private void OnToastYesClicked(object? sender, RoutedEventArgs e)
    {
        DismissToast();

        // 等弹窗滑出后再打开教程，避免两个动画打架
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(420);
            StartTutorial();
        });
    }

    private void OnToastNoClicked(object? sender, RoutedEventArgs e)
    {
        DismissToast();
    }

    /// <summary>立即开始滑出。</summary>
    private void DismissToast()
    {
        if (_toastPhase is ToastPhase.Hidden or ToastPhase.Exit) return;

        _toastPhase = ToastPhase.Exit;
        _toastPhaseStart = DateTime.UtcNow;

        _toastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _toastTimer.Tick -= OnToastTick;
        _toastTimer.Tick += OnToastTick;
        _toastTimer.Start();
    }

    // ============================================================
    // 应用内自定义标题栏（系统标题栏已隐藏）
    // ============================================================

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 双击：最大化 / 还原
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        // 单键拖动窗口
        BeginMoveDrag(e);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
        => Close();

    // ============================================================
    // 新手教程
    // ============================================================

    private static readonly (string Title, string Body)[] TutorialSteps =
    {
        ("① 选择方块",
         "左侧列表收录了 Minecraft 原版方块贴图，共 250+ 种。\n" +
         "在搜索框输入名称可以快速筛选，点击任意一项即可在右侧 3D 预览区显示。"),

        ("② 旋转查看",
         "在预览区按住鼠标左键拖拽，可以自由旋转方块、观察各个角度。\n" +
         "也可以用下方方向按钮精确旋转：「◀ 左转 / 右转 ▶」转水平方向，" +
         "「▲ 上转 / ▼ 下转」转俯仰方向，每次 90°，带平滑过渡动画。"),

        ("③ 更换配色",
         "左下角「配色方案」提供 樱花粉 / 薰衣草 / 薄荷青 三套预设，\n" +
         "切换后整个界面的渐变背景与强调色会实时变化。"),

        ("④ 导出图片",
         "把方块转到满意的角度后，点击「保存当前视角」——\n" +
         "会把此刻的画面导出为 300×300 的透明背景 PNG，方便直接用作图标素材。")
    };

    private int _tutorialStep;

    private void StartTutorial()
    {
        _tutorialStep = 0;
        TutorialOverlay.IsVisible = true;

        // ---------- 卡片初始态：缩小 + 下移 + 透明 ----------
        _tutorialCardScale.ScaleX = 0.92;
        _tutorialCardScale.ScaleY = 0.92;
        _tutorialCardTranslate.Y = 26;
        TutorialCard.Opacity = 0;

        TutorialContent.Opacity = 1;
        RenderTutorialStepContent();

        // ---------- 播放弹出动画（BackEaseOut = 果冻感） ----------
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(30);
            TutorialCard.Opacity = 1;
            _tutorialCardScale.ScaleX = 1;
            _tutorialCardScale.ScaleY = 1;
            _tutorialCardTranslate.Y = 0;
        });
    }

    /// <summary>只更新教程内容，不涉及动画。</summary>
    private void RenderTutorialStepContent()
    {
        if (_tutorialStep < 0 || _tutorialStep >= TutorialSteps.Length) return;

        var (title, body) = TutorialSteps[_tutorialStep];
        TutorialTitle.Text = title;
        TutorialBody.Text = body;
        TutorialStepText.Text = $"第 {_tutorialStep + 1} 步 / 共 {TutorialSteps.Length} 步";
        TutorialNextButton.Content = _tutorialStep == TutorialSteps.Length - 1 ? "开始使用" : "下一步";

        // 进度点：当前步骤拉长成胶囊
        TutorialDots.Children.Clear();

        var accent = Application.Current?.Resources["AccentBrush"] as IBrush
                     ?? new SolidColorBrush(Color.Parse("#FF7EB3"));
        var idle = new SolidColorBrush(Color.Parse("#40FF7EB3"));

        for (int i = 0; i < TutorialSteps.Length; i++)
        {
            TutorialDots.Children.Add(new Border
            {
                Width = i == _tutorialStep ? 20 : 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = i == _tutorialStep ? accent : idle,
                Transitions = new Transitions
                {
                    new DoubleTransition
                    {
                        Property = WidthProperty,
                        Duration = TimeSpan.FromMilliseconds(260),
                        Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                    },
                    new BrushTransition
                    {
                        Property = BackgroundProperty,
                        Duration = TimeSpan.FromMilliseconds(260)
                    }
                }
            });
        }
    }

    private async void OnTutorialNextClicked(object? sender, RoutedEventArgs e)
    {
        // 动画进行中忽略连点，避免内容错乱
        if (_tutorialAnimating) return;

        if (_tutorialStep >= TutorialSteps.Length - 1)
        {
            CloseTutorial();
            return;
        }

        _tutorialAnimating = true;
        TutorialNextButton.IsEnabled = false;

        // 1) 旧内容淡出
        TutorialContent.Opacity = 0;
        await Task.Delay(210);

        // 2) 换内容（此时不可见）
        _tutorialStep++;
        RenderTutorialStepContent();

        // 3) 新内容淡入
        TutorialContent.Opacity = 1;
        await Task.Delay(210);

        TutorialNextButton.IsEnabled = true;
        _tutorialAnimating = false;
    }

    private void OnTutorialSkipClicked(object? sender, RoutedEventArgs e)
    {
        CloseTutorial();
    }

    private void CloseTutorial()
    {
        TutorialOverlay.IsVisible = false;

        // 重置卡片状态，保证下次打开还能正常播弹出动画
        _tutorialCardScale.ScaleX = 0.92;
        _tutorialCardScale.ScaleY = 0.92;
        _tutorialCardTranslate.Y = 26;
        TutorialCard.Opacity = 0;
        TutorialContent.Opacity = 1;

        StatusText.Text = "教程结束，开始制作你的方块图标吧";
    }

    // ============================================================
    // 窗口圆角裁切
    // ============================================================

    private void ApplyRoundedWindowRegion()
    {
        if (!OperatingSystem.IsWindows()) return;

        var handle = TryGetPlatformHandle();
        if (handle == null) return;

        double scale = RenderScaling;
        int w = (int)Math.Round(Bounds.Width * scale);
        int h = (int)Math.Round(Bounds.Height * scale);
        if (w <= 0 || h <= 0) return;

        int radius = (int)Math.Round(WindowCornerRadius * scale);

        // 椭圆宽高 = 2 × 半径，得到标准圆角矩形
        IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, radius * 2, radius * 2);
        if (rgn == IntPtr.Zero) return;

        // SetWindowRgn 成功后由系统接管该 region，不要自己删除
        SetWindowRgn(handle.Handle, rgn, true);
    }

    // ============================================================
    // 窗口边缘拖动调整大小（WindowDecorations=None 后没有系统边框）
    // ============================================================

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 有弹出层（如下拉列表）展开时，禁止边缘调整大小，
        // 否则靠近窗口边缘的选项会被窗口抢走点击。
        if (IsAnyPopupOpen()) return;
        if (IsInsidePopup(e.Source)) return;

        var edge = GetResizeEdge(e.GetPosition(this));
        if (edge == null) return;

        e.Handled = true;
        BeginResizeDrag(edge.Value, e);
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        // 下拉展开时保持普通光标，避免边缘光标盖住下拉选项
        if (IsAnyPopupOpen() || IsInsidePopup(e.Source))
        {
            Cursor = null;
            return;
        }

        UpdateResizeCursor(GetResizeEdge(e.GetPosition(this)));
    }

    /// <summary>当前是否有下拉框展开。</summary>
    private bool IsAnyPopupOpen()
    {
        return ThemeBox?.IsDropDownOpen == true;
    }

    /// <summary>事件源是否来自弹出层（Avalonia 的 OverlayPopupHost 会把事件冒泡到窗口）。</summary>
    private static bool IsInsidePopup(object? source)
    {
        if (source is not Visual v) return false;

        for (Visual? cur = v; cur != null; cur = cur.GetVisualParent())
        {
            string typeName = cur.GetType().Name;
            if (typeName.Contains("Popup", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>判断鼠标是否落在窗口边缘的调整区域，返回对应方向。</summary>
    private WindowEdge? GetResizeEdge(Point p)
    {
        var size = Bounds.Size;
        if (size.Width <= ResizeBorderThickness * 2 || size.Height <= ResizeBorderThickness * 2)
            return null;

        bool left = p.X <= ResizeBorderThickness;
        bool right = p.X >= size.Width - ResizeBorderThickness;
        bool top = p.Y <= ResizeBorderThickness;
        bool bottom = p.Y >= size.Height - ResizeBorderThickness;

        if (top && left) return WindowEdge.NorthWest;
        if (top && right) return WindowEdge.NorthEast;
        if (bottom && left) return WindowEdge.SouthWest;
        if (bottom && right) return WindowEdge.SouthEast;
        if (left) return WindowEdge.West;
        if (right) return WindowEdge.East;
        if (top) return WindowEdge.North;
        if (bottom) return WindowEdge.South;
        return null;
    }

    private void UpdateResizeCursor(WindowEdge? edge)
    {
        if (edge == null)
        {
            // 离开边缘：交还给默认（子控件自己的光标恢复生效）
            Cursor = null;
            return;
        }

        Cursor = edge switch
        {
            WindowEdge.North or WindowEdge.South
                => new Cursor(StandardCursorType.SizeNorthSouth),
            WindowEdge.West or WindowEdge.East
                => new Cursor(StandardCursorType.SizeWestEast),
            WindowEdge.NorthWest or WindowEdge.SouthEast
                => new Cursor(StandardCursorType.TopLeftCorner),
            _ => new Cursor(StandardCursorType.TopRightCorner)
        };
    }
}
