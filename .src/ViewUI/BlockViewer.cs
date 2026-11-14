using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Projection3D.Toolkits.Model;
using Projection3D.Toolkits.Render;
using Projection3D.Toolkits.Textures;
using SkiaSharp;

namespace Projection3D.ViewUI;

/// <summary>
/// 方块 3D 预览控件：鼠标左键拖拽旋转。
/// 渲染管线完全自研（Toolkits.Render），这里只负责接入 Avalonia 的 Skia 后端。
/// </summary>
public sealed class BlockViewer : Control
{
    public static readonly StyledProperty<BlockDefinition?> BlockProperty =
        AvaloniaProperty.Register<BlockViewer, BlockDefinition?>(nameof(Block));

    public static readonly StyledProperty<BlockTextureSet?> TexturesProperty =
        AvaloniaProperty.Register<BlockViewer, BlockTextureSet?>(nameof(Textures));

    public BlockDefinition? Block
    {
        get => GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    public BlockTextureSet? Textures
    {
        get => GetValue(TexturesProperty);
        set => SetValue(TexturesProperty, value);
    }

    private readonly List<Face> _faces;
    private float _yawDeg = 45f;
    private float _pitchDeg = 30f;

    private bool _dragging;
    private Point _dragStart;
    private float _dragStartYaw;
    private float _dragStartPitch;

    // ---------- 旋转动画 ----------
    private DispatcherTimer? _animTimer;
    private float _animStartYaw, _animStartPitch;
    private float _animTargetYaw, _animTargetPitch;
    private DateTime _animStartTime;
    private const double AnimDurationMs = 280;

    /// <summary>当前水平角（供导出时使用）。</summary>
    public float YawDeg => _yawDeg;

    /// <summary>当前俯仰角（供导出时使用）。</summary>
    public float PitchDeg => _pitchDeg;

    /// <summary>是否正在播放旋转动画。</summary>
    public bool IsRotating => _animTimer?.IsEnabled == true;

    /// <summary>角度变化时触发（拖拽或动画），供 UI 更新角度提示。</summary>
    public event EventHandler? AnglesChanged;

    private void NotifyAngles() => AnglesChanged?.Invoke(this, EventArgs.Empty);

    public BlockViewer()
    {
        _faces = CubeModel.Build(1f);
        Focusable = true;
        ClipToBounds = true;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BlockProperty)
        {
            // 换方块时按几何类型（或精确模型表）重建模型
            var def = change.GetNewValue<BlockDefinition?>();
            _faces.Clear();
            _faces.AddRange(def == null
                ? CubeModel.Build(1f)
                : def.BuildFaces());

            InvalidateVisual();
        }
        else if (change.Property == TexturesProperty)
        {
            InvalidateVisual();
        }
    }

    /// <summary>当前方块的几何类型（供 UI 显示）。</summary>
    public BlockShapeKind Shape => Block?.Shape ?? BlockShapeKind.Cube;

    /// <summary>把视角重置为标准等轴测（45°, 30°），带过渡动画。</summary>
    public void ResetView()
    {
        AnimateTo(45f, 30f);
    }

    /// <summary>
    /// 相对当前角度旋转，带过渡动画。
    /// 用于方向按钮（每次转 90°）。
    /// </summary>
    public void RotateBy(float yawDelta, float pitchDelta)
    {
        AnimateTo(_yawDeg + yawDelta, _pitchDeg + pitchDelta);
    }

    /// <summary>平滑过渡到目标角度（EaseOutCubic，约 280ms）。</summary>
    private void AnimateTo(float targetYaw, float targetPitch)
    {
        StopAnimation();

        // yaw 走最短路径，避免为了转 90° 而绕一大圈
        float deltaYaw = targetYaw - _yawDeg;
        deltaYaw %= 360f;
        if (deltaYaw > 180f) deltaYaw -= 360f;
        if (deltaYaw < -180f) deltaYaw += 360f;

        _animStartYaw = _yawDeg;
        _animStartPitch = _pitchDeg;
        _animTargetYaw = _yawDeg + deltaYaw;
        _animTargetPitch = Math.Clamp(targetPitch, -89f, 89f);
        _animStartTime = DateTime.UtcNow;

        if (_animTimer == null)
        {
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _animTimer.Tick += OnAnimationTick;
        }
        _animTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _animStartTime).TotalMilliseconds;
        float t = (float)Math.Clamp(elapsed / AnimDurationMs, 0.0, 1.0);

        // EaseOutCubic：起步快、收尾稳
        float eased = 1f - MathF.Pow(1f - t, 3f);

        _yawDeg = _animStartYaw + (_animTargetYaw - _animStartYaw) * eased;
        _pitchDeg = _animStartPitch + (_animTargetPitch - _animStartPitch) * eased;
        NotifyAngles();
        InvalidateVisual();

        if (t >= 1f)
        {
            _yawDeg = _animTargetYaw;
            _pitchDeg = _animTargetPitch;
            NotifyAngles();
            StopAnimation();
        }
    }

    private void StopAnimation()
    {
        if (_animTimer == null) return;
        _animTimer.Stop();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 手动拖拽优先：立刻打断正在播放的旋转动画
        StopAnimation();

        _dragging = true;
        _dragStart = e.GetPosition(this);
        _dragStartYaw = _yawDeg;
        _dragStartPitch = _pitchDeg;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        var cur = e.GetPosition(this);
        float dx = (float)(cur.X - _dragStart.X);
        float dy = (float)(cur.Y - _dragStart.Y);

        _yawDeg = _dragStartYaw + dx * 0.6f;

        // 屏幕 Y 向下增大。
        //   鼠标向上拖（dy < 0）→ pitch 减小 → 视角转向方块底部（看到底面）
        //   鼠标向下拖（dy > 0）→ pitch 增大 → 视角转向方块顶部（看到顶面）
        // 即"上下方向与鼠标移动方向相反"，符合拖拽模型的直觉。
        _pitchDeg = Math.Clamp(_dragStartPitch + dy * 0.6f, -89f, 89f);
        NotifyAngles();
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragging = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var b = Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        // ★ 用透明色填满控件：Avalonia 只对"有绘制内容"的区域做命中测试，
        //   不填这一下的话鼠标拖拽收不到事件（表现为"拖不动"）。
        context.FillRectangle(Brushes.Transparent, new Rect(b.Size));

        var textures = Textures;
        if (Block == null || textures == null || textures.IsEmpty) return;

        var camera = new Camera
        {
            YawDeg = _yawDeg,
            PitchDeg = _pitchDeg,
            FillFactor = 0.72f
        };

        var projected = ProjectionRenderer.Render(
            _faces, camera, (float)b.Width, (float)b.Height);

        context.Custom(new BlockDrawOperation(
            new Rect(b.Size), projected, textures));
    }

    private sealed class BlockDrawOperation : ICustomDrawOperation
    {
        private readonly IReadOnlyList<ProjectedFace> _faces;
        private readonly BlockTextureSet _textures;

        public Rect Bounds { get; }

        public BlockDrawOperation(Rect bounds, IReadOnlyList<ProjectedFace> faces, BlockTextureSet textures)
        {
            Bounds = bounds;
            _faces = faces;
            _textures = textures;
        }

        public void Dispose() { }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease == null) return;

            using var l = lease.Lease();
            SKCanvas canvas = l.SkCanvas;

            SkiaFacePainter.Draw(canvas, _faces, _textures.Get);
        }
    }
}
