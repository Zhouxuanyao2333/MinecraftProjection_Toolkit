# 渲染管线接手文档（RENDER_PIPELINE）

> **读者**：接手 3DProjection 的 AI 开发者
> **关联文档**：同目录 `HANDOVER.md`（项目接手总纲）
> **本文档的唯一目的**：把主项目现有的自研 3D 渲染管线**完整搬进本工具**，搬完之后**主项目不再自己渲染**，改为调用本工具。
> **生成时间**：2026-11-14

---

## 0. 一句话

主项目 `MainSrc\Tools\Player3DConfig\` 下有 **760 行**已经跑通的自研 3D 渲染代码。它要从主项目**迁走**，成为本工具的通用渲染核心；主项目删掉自己的实现，改用本工具的 API。

**本文档完整内嵌了这 760 行源码**（见 §2），并给出迁移方案（§4-§6）。接手方不需要去主项目里翻代码。

---

## 1. 现状：渲染代码在哪

### 1.1 三个文件

| 文件 | 行数 | 职责 | 依赖 |
|---|---|---|---|
| `MinecraftSkinModel.cs` | 284 | 模型数据：12 个盒子、72 个面、六面 UV 映射、Classic/Slim | 纯 C# |
| `Pivot3DRenderer.cs` | 136 | 纯 CPU 3D 管线：变换 / 投影 / 剔除 / 排序 / 光照 | 纯 C#（仅 `System.Numerics`） |
| `Pivot3DControl.cs` | 340 | Avalonia 控件 + SkiaSharp 光栅化 + 交互 | Avalonia + SkiaSharp |

位置：

```
ProjectLaunch\MainSrc\Tools\Player3DConfig\
├── MinecraftSkinModel.cs
├── Pivot3DRenderer.cs
└── Pivot3DControl.cs
```

### 1.2 边界：哪些渲染**不**归本文档管

主项目里还有两类渲染代码，**不在迁移范围**：

| 内容 | 位置 | 为什么不迁 |
|---|---|---|
| 液态玻璃效果 | `MainSrc\Tools\LiquidGlassAvaloniaUI\`（13 个文件） | **vendored 第三方库**（MIT，作者 KaranocaVe），独立 csproj、独立编译。与 3D 方块投影无关，不要动它 |
| 头像裁剪 | `MainSrc\Tools\JavaConfig\SkinCropper.cs` | 用 `RenderTargetBitmap` 做 2D 图像裁剪，不是 3D 管线 |
| 头像显示 | `MainSrc\Views\MainWindow.cs:1295-1299`、`SetupWindow.axaml.cs:738-743` | 同上，`RenderTargetBitmap` 2D 绘制 |

**判断标准**：只有 `Player3DConfig\` 下的三个文件是"自研 3D 管线"。

### 1.3 贴图库：`.src\textures\`（本工具的数据源）

项目里已经放进了一套**完整的 Minecraft 原版贴图**，共 **4004 个文件 / 约 2.0 MB**：

| 目录 | 文件数 | 说明 |
|---|---|---|
| `textures\block\` | 1405 | **方块贴图（本工具的主要数据源）** |
| `textures\item\` | 831 | 物品贴图 |
| `textures\entity\` | 759 | 实体贴图 |
| `textures\gui\` | 616 | GUI 贴图 |
| `textures\particle\` | 301 | 粒子贴图 |
| `textures\painting\` | 52 | 画 |
| `textures\mob_effect\` | 40 | 状态效果图标 |

- `.png` **3829 个**，标准尺寸 **16×16**
- `.mcmeta` **175 个**，元数据（如 `"mipmap_strategy": "strict_cutout"`）。生成静态图标时通常可以忽略

### 1.3.1 贴图 → 立方体 6 面的映射规则（核心业务逻辑）

这是"渲染方块"必须解决的问题，**不属于通用管线**，但要在这里写清楚，否则接手方会以为"一张贴图贴 6 面"就够了：

| 方块类型 | 映射方式 | 例子 |
|---|---|---|
| 六面相同 | 同一张贴图贴 6 面 | `stone.png`、`dirt.png`、`oak_planks.png` |
| 顶/侧/底不同 | 3 张不同贴图 | `grass_block_top.png`（顶）+ `grass_block_side.png`（侧）+ `dirt.png`（底） |
| 原木类 | 顶底相同、侧面相同 | `oak_log_top.png`（顶+底）+ `oak_log.png`（侧） |
| 带朝向 | 前后左右各不相同 | `furnace_front.png`、`furnace_side.png`、`furnace_top.png` |
| 半透明 | 需要 alpha 混合 | 玻璃、树叶、冰 |

### 1.3.2 贴图加载注意点

- 16×16 像素风 → 采样**必须最近邻**（`SKFilterQuality.None` + `IsAntialias = false`）
- 带 alpha 的贴图（玻璃/树叶）要保留透明通道
- `.mcmeta` 里可能有动画帧定义（水/岩浆/火），做静态图标时取第 1 帧即可
- **不要一次性加载 3829 张图**，按需加载 + 内存缓存
- 贴图为 16×16，立方体渲染时 UV 坐标系与玩家皮肤（64×64）不同，**模型层的 UV 要用归一化坐标**（0~1），渲染层再按实际贴图尺寸换算

### 1.4 分层结构

```
┌──────────────────────────────────────────────────────────┐
│  Pivot3DControl.cs                        340 行          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ Avalonia Control 子类                               │  │
│  │  · Render(DrawingContext) → context.Custom(...)     │  │
│  │  · Pivot3DDrawOperation : ICustomDrawOperation      │  │
│  │  · ISkiaSharpApiLeaseFeature → SKCanvas             │  │
│  │  · SKCanvas.DrawVertices(三角形 + 纹理 + 明暗)        │  │
│  │  · 交互：拖拽旋转 / 2 秒后自动旋转                     │  │
│  │  · 皮肤类型检测（Classic / Slim）                     │  │
│  └────────────────────────────────────────────────────┘  │
│                           ▲                               │
│                           │ List<ProjectedFace>           │
├──────────────────────────────────────────────────────────┤
│  Pivot3DRenderer.cs                       136 行          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ 纯函数静态类：Render(faces, yaw, pitch, w, h)        │  │
│  │  · Yaw（绕 Y）/ Pitch（绕 X）旋转                     │  │
│  │  · 透视投影 persp = cameraDist / depth               │  │
│  │  · 背面剔除（屏幕空间叉乘）                            │  │
│  │  · 画家算法排序（平均深度降序）                        │  │
│  │  · Lambert 光照（面法线 · 光向）                       │  │
│  └────────────────────────────────────────────────────┘  │
│                           ▲                               │
│                           │ List<SkinFace>                │
├──────────────────────────────────────────────────────────┤
│  MinecraftSkinModel.cs                    284 行          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ 数据构造：Build(SkinModelType) → List<SkinFace>     │  │
│  │  · Vec3 / Vec2 / SkinFace 结构                      │  │
│  │  · 12 个盒子（头/帽/身/身外层/左右臂/左右腿 + 外层）    │  │
│  │  · 每盒 6 面 × 4 顶点 = 72 个面                      │  │
│  │  · UV 直接按 64×64 皮肤贴图坐标硬编码                 │  │
│  │  · Classic（4 宽臂）/ Slim（3 宽臂）两套分支           │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
```

### 1.5 数据流（一次渲染的完整链路）

```
MinecraftSkinModel.Build(type)
        │  产出 List<SkinFace>（模型空间，Y 向上，单位≈MC 像素）
        ▼
Pivot3DRenderer.Render(faces, yaw, pitch, viewW, viewH, scale)
        │  逐面：
        │    ① Transform 四个顶点 → 模型空间 → 旋转 → 透视 → 屏幕空间
        │    ② 屏幕空间叉乘判背面，cross <= 0 丢弃
        │    ③ EstimateShade 算法线 + Lambert → 明暗系数
        │  最后按平均深度降序排序（画家算法）
        ▼
List<ProjectedFace>（屏幕空间四边形 + UV + 深度 + 明暗）
        │
        ▼
Pivot3DDrawOperation.Render(ImmediateDrawingContext)
        │  取 SKCanvas
        │  逐面：四边形拆两个三角形
        │        UV → 贴图像素坐标
        │        明暗系数 → SKColor 灰度
        │        canvas.DrawVertices(...)
        ▼
屏幕
```

---

## 2. 完整源码（2026-11-14 磁盘状态）

> 以下三份源码是**原文照搬**，未经删改。行号与主项目磁盘文件一致。
> 迁移时以本节为准；如果主项目后续又改了这三个文件，请重新同步本节。

### 2.1 `MinecraftSkinModel.cs`（284 行）

```csharp
using System.Collections.Generic;

namespace Project.Launch.Tools.Player3DConfig
{
    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    public struct Vec2
    {
        public float U, V;
        public Vec2(float u, float v) { U = u; V = v; }
    }

    /// <summary>模型类型：Classic（4 宽手臂） / Slim（3 宽手臂）</summary>
    public enum SkinModelType
    {
        Classic,
        Slim
    }

    public class SkinFace
    {
        public Vec3 V0, V1, V2, V3;
        public Vec2 T0, T1, T2, T3;
        public string Part = "";
        public bool Overlay;

        public SkinFace(Vec3 v0, Vec3 v1, Vec3 v2, Vec3 v3,
                        Vec2 t0, Vec2 t1, Vec2 t2, Vec2 t3,
                        string part, bool overlay)
        {
            V0 = v0; V1 = v1; V2 = v2; V3 = v3;
            T0 = t0; T1 = t1; T2 = t2; T3 = t3;
            Part = part;
            Overlay = overlay;
        }
    }

    public static class MinecraftSkinModel
    {
        private const float TEX_W = 64f;
        private const float TEX_H = 64f;

        public static List<SkinFace> Build(SkinModelType type = SkinModelType.Classic)
        {
            var faces = new List<SkinFace>();

            // ============ 头部 8×8×8 ============
            AddBox(faces, "head", false,
                x0: -4f, y0: 24f, z0: -4f, x1: 4f, y1: 32f, z1: 4f,
                front:  (8, 8, 8, 8),
                back:   (24, 8, 8, 8),
                right:  (0, 8, 8, 8),
                left:   (16, 8, 8, 8),
                top:    (8, 0, 8, 8),
                bottom: (16, 0, 8, 8));

            AddBox(faces, "hat", true,
                x0: -4.25f, y0: 23.75f, z0: -4.25f, x1: 4.25f, y1: 32.25f, z1: 4.25f,
                front:  (40, 8, 8, 8),
                back:   (56, 8, 8, 8),
                right:  (32, 8, 8, 8),
                left:   (48, 8, 8, 8),
                top:    (40, 0, 8, 8),
                bottom: (48, 0, 8, 8));

            // ============ 身体 8×12×4 ============
            AddBox(faces, "body", false,
                x0: -4f, y0: 12f, z0: -2f, x1: 4f, y1: 24f, z1: 2f,
                front:  (20, 20, 8, 12),
                back:   (32, 20, 8, 12),
                right:  (16, 20, 4, 12),
                left:   (28, 20, 4, 12),
                top:    (20, 16, 8, 4),
                bottom: (28, 16, 8, 4));

            AddBox(faces, "bodyOverlay", true,
                x0: -4.25f, y0: 11.75f, z0: -2.25f, x1: 4.25f, y1: 24.25f, z1: 2.25f,
                front:  (20, 36, 8, 12),
                back:   (32, 36, 8, 12),
                right:  (16, 36, 4, 12),
                left:   (28, 36, 4, 12),
                top:    (20, 32, 8, 4),
                bottom: (28, 32, 8, 4));

            // ============ 手臂（Classic / Slim 分支）============
            if (type == SkinModelType.Slim)
            {
                // 右臂 3×12×4，中心 (-5.5, 18, 0)，X 范围 [-7, -4]
                AddBox(faces, "rightArm", false,
                    x0: -7f, y0: 12f, z0: -2f, x1: -4f, y1: 24f, z1: 2f,
                    front:  (44, 20, 3, 12),
                    back:   (51, 20, 3, 12),
                    right:  (40, 20, 4, 12),
                    left:   (47, 20, 4, 12),
                    top:    (44, 16, 3, 4),
                    bottom: (47, 16, 3, 4));

                AddBox(faces, "rightArmOverlay", true,
                    x0: -7.25f, y0: 11.75f, z0: -2.25f, x1: -3.75f, y1: 24.25f, z1: 2.25f,
                    front:  (44, 36, 3, 12),
                    back:   (51, 36, 3, 12),
                    right:  (40, 36, 4, 12),
                    left:   (47, 36, 4, 12),
                    top:    (44, 32, 3, 4),
                    bottom: (47, 32, 3, 4));

                // 左臂 3×12×4，中心 (5.5, 18, 0)，X 范围 [4, 7]
                AddBox(faces, "leftArm", false,
                    x0: 4f, y0: 12f, z0: -2f, x1: 7f, y1: 24f, z1: 2f,
                    front:  (36, 52, 3, 12),
                    back:   (43, 52, 3, 12),
                    right:  (32, 52, 4, 12),
                    left:   (39, 52, 4, 12),
                    top:    (36, 48, 3, 4),
                    bottom: (39, 48, 3, 4));

                AddBox(faces, "leftArmOverlay", true,
                    x0: 3.75f, y0: 11.75f, z0: -2.25f, x1: 7.25f, y1: 24.25f, z1: 2.25f,
                    front:  (52, 52, 3, 12),
                    back:   (59, 52, 3, 12),
                    right:  (48, 52, 4, 12),
                    left:   (55, 52, 4, 12),
                    top:    (52, 48, 3, 4),
                    bottom: (55, 48, 3, 4));
            }
            else
            {
                // Classic 4×12×4
                AddBox(faces, "rightArm", false,
                    x0: -8f, y0: 12f, z0: -2f, x1: -4f, y1: 24f, z1: 2f,
                    front:  (44, 20, 4, 12),
                    back:   (52, 20, 4, 12),
                    right:  (40, 20, 4, 12),
                    left:   (48, 20, 4, 12),
                    top:    (44, 16, 4, 4),
                    bottom: (48, 16, 4, 4));

                AddBox(faces, "rightArmOverlay", true,
                    x0: -8.25f, y0: 11.75f, z0: -2.25f, x1: -3.75f, y1: 24.25f, z1: 2.25f,
                    front:  (44, 36, 4, 12),
                    back:   (52, 36, 4, 12),
                    right:  (40, 36, 4, 12),
                    left:   (48, 36, 4, 12),
                    top:    (44, 32, 4, 4),
                    bottom: (48, 32, 4, 4));

                AddBox(faces, "leftArm", false,
                    x0: 4f, y0: 12f, z0: -2f, x1: 8f, y1: 24f, z1: 2f,
                    front:  (36, 52, 4, 12),
                    back:   (44, 52, 4, 12),
                    right:  (32, 52, 4, 12),
                    left:   (40, 52, 4, 12),
                    top:    (36, 48, 4, 4),
                    bottom: (40, 48, 4, 4));

                AddBox(faces, "leftArmOverlay", true,
                    x0: 3.75f, y0: 11.75f, z0: -2.25f, x1: 8.25f, y1: 24.25f, z1: 2.25f,
                    front:  (52, 52, 4, 12),
                    back:   (60, 52, 4, 12),
                    right:  (48, 52, 4, 12),
                    left:   (56, 52, 4, 12),
                    top:    (52, 48, 4, 4),
                    bottom: (56, 48, 4, 4));
            }

            // ============ 腿（Classic / Slim 一致）============
            AddBox(faces, "rightLeg", false,
                x0: -4f, y0: 0f, z0: -2f, x1: 0f, y1: 12f, z1: 2f,
                front:  (4, 20, 4, 12),
                back:   (12, 20, 4, 12),
                right:  (0, 20, 4, 12),
                left:   (8, 20, 4, 12),
                top:    (4, 16, 4, 4),
                bottom: (8, 16, 4, 4));

            AddBox(faces, "rightLegOverlay", true,
                x0: -4.25f, y0: -0.25f, z0: -2.25f, x1: 0.25f, y1: 12.25f, z1: 2.25f,
                front:  (4, 36, 4, 12),
                back:   (12, 36, 4, 12),
                right:  (0, 36, 4, 12),
                left:   (8, 36, 4, 12),
                top:    (4, 32, 4, 4),
                bottom: (8, 32, 4, 4));

            AddBox(faces, "leftLeg", false,
                x0: 0f, y0: 0f, z0: -2f, x1: 4f, y1: 12f, z1: 2f,
                front:  (20, 52, 4, 12),
                back:   (28, 52, 4, 12),
                right:  (16, 52, 4, 12),
                left:   (24, 52, 4, 12),
                top:    (20, 48, 4, 4),
                bottom: (24, 48, 4, 4));

            AddBox(faces, "leftLegOverlay", true,
                x0: -0.25f, y0: -0.25f, z0: -2.25f, x1: 4.25f, y1: 12.25f, z1: 2.25f,
                front:  (4, 52, 4, 12),
                back:   (12, 52, 4, 12),
                right:  (0, 52, 4, 12),
                left:   (8, 52, 4, 12),
                top:    (4, 48, 4, 4),
                bottom: (8, 48, 4, 4));

            return faces;
        }

        private static void AddBox(
            List<SkinFace> faces, string part, bool overlay,
            float x0, float y0, float z0, float x1, float y1, float z1,
            (float u, float v, float w, float h) front,
            (float u, float v, float w, float h) back,
            (float u, float v, float w, float h) right,
            (float u, float v, float w, float h) left,
            (float u, float v, float w, float h) top,
            (float u, float v, float w, float h) bottom)
        {
            AddFace(faces, part, overlay,
                new Vec3(x0, y1, z1),
                new Vec3(x1, y1, z1),
                new Vec3(x1, y0, z1),
                new Vec3(x0, y0, z1),
                front);

            AddFace(faces, part, overlay,
                new Vec3(x1, y1, z0),
                new Vec3(x0, y1, z0),
                new Vec3(x0, y0, z0),
                new Vec3(x1, y0, z0),
                back);

            AddFace(faces, part, overlay,
                new Vec3(x0, y1, z0),
                new Vec3(x0, y1, z1),
                new Vec3(x0, y0, z1),
                new Vec3(x0, y0, z0),
                right);

            AddFace(faces, part, overlay,
                new Vec3(x1, y1, z1),
                new Vec3(x1, y1, z0),
                new Vec3(x1, y0, z0),
                new Vec3(x1, y0, z1),
                left);

            AddFace(faces, part, overlay,
                new Vec3(x0, y1, z0),
                new Vec3(x1, y1, z0),
                new Vec3(x1, y1, z1),
                new Vec3(x0, y1, z1),
                top);

            AddFace(faces, part, overlay,
                new Vec3(x1, y0, z0),
                new Vec3(x0, y0, z0),
                new Vec3(x0, y0, z1),
                new Vec3(x1, y0, z1),
                bottom);
        }

        private static void AddFace(
            List<SkinFace> faces, string part, bool overlay,
            Vec3 v0, Vec3 v1, Vec3 v2, Vec3 v3,
            (float u, float v, float w, float h) uv)
        {
            const float half = 0f;

            float uLeft = (uv.u + half) / TEX_W;
            float vTop = (uv.v + half) / TEX_H;
            float uRight = (uv.u + uv.w - half) / TEX_W;
            float vBottom = (uv.v + uv.h - half) / TEX_H;

            faces.Add(new SkinFace(
                v0, v1, v2, v3,
                new Vec2(uLeft, vTop),
                new Vec2(uRight, vTop),
                new Vec2(uRight, vBottom),
                new Vec2(uLeft, vBottom),
                part, overlay));
        }
    }
}
```

### 2.2 `Pivot3DRenderer.cs`（136 行）

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Project.Launch.Tools.Player3DConfig
{
    public class ProjectedFace
    {
        public float X0, Y0, X1, Y1, X2, Y2, X3, Y3;
        public Vec2 T0, T1, T2, T3;
        public float Depth;
        public float Shade;
        public bool Overlay;
    }

    public static class Pivot3DRenderer
    {
        public static List<ProjectedFace> Render(
            List<SkinFace> faces,
            float yawDeg, float pitchDeg,
            float viewW, float viewH,
            float scaleFactor = 0.75f)
        {
            var result = new List<ProjectedFace>(faces.Count);
            if (faces.Count == 0 || viewW <= 0 || viewH <= 0) return result;

            var center = new Vector3(0f, 16f, 0f);
            const float cameraDist = 60f;
            float scale = viewH * scaleFactor / 32f;
            float cx = viewW / 2f;
            float cy = viewH / 2f;

            float yawRad = yawDeg * MathF.PI / 180f;
            float pitchRad = pitchDeg * MathF.PI / 180f;
            float cosY = MathF.Cos(yawRad), sinY = MathF.Sin(yawRad);
            float cosP = MathF.Cos(pitchRad), sinP = MathF.Sin(pitchRad);

            foreach (var f in faces)
            {
                var p0 = Transform(f.V0, center, cosY, sinY, cosP, sinP, cameraDist, scale, cx, cy);
                var p1 = Transform(f.V1, center, cosY, sinY, cosP, sinP, cameraDist, scale, cx, cy);
                var p2 = Transform(f.V2, center, cosY, sinY, cosP, sinP, cameraDist, scale, cx, cy);
                var p3 = Transform(f.V3, center, cosY, sinY, cosP, sinP, cameraDist, scale, cx, cy);

                float avgZ = (p0.Z + p1.Z + p2.Z + p3.Z) * 0.25f;

                // 背面剔除（逆时针为正面）
                float cross = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                if (cross <= 0f) continue;

                float shade = EstimateShade(f, cosY, sinY, cosP, sinP);

                result.Add(new ProjectedFace
                {
                    X0 = p0.X, Y0 = p0.Y,
                    X1 = p1.X, Y1 = p1.Y,
                    X2 = p2.X, Y2 = p2.Y,
                    X3 = p3.X, Y3 = p3.Y,
                    T0 = f.T0, T1 = f.T1, T2 = f.T2, T3 = f.T3,
                    Depth = avgZ,
                    Shade = shade,
                    Overlay = f.Overlay
                });
            }

            // 画家算法：远的先画，近的覆盖远的
            result.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            return result;
        }

        private static Vector3 Transform(
            Vec3 v, Vector3 center,
            float cosY, float sinY, float cosP, float sinP,
            float cameraDist, float scale, float cx, float cy)
        {
            float x = v.X - center.X;
            float y = v.Y - center.Y;
            float z = v.Z - center.Z;

            // Yaw（绕 Y 轴）
            float x1 = x * cosY + z * sinY;
            float z1 = -x * sinY + z * cosY;

            // Pitch（绕 X 轴）
            float y1 = y * cosP - z1 * sinP;
            float z2 = y * sinP + z1 * cosP;

            // ★ 关键修正：相机在 +Z 方向看向 -Z，+Z 面（正脸）离相机更近
            float depth = cameraDist - z2;
            if (depth < 1f) depth = 1f;
            float persp = cameraDist / depth;

            float sx = cx + x1 * scale * persp;
            float sy = cy - y1 * scale * persp;

            return new Vector3(sx, sy, depth);
        }

        private static float EstimateShade(
            SkinFace f, float cosY, float sinY, float cosP, float sinP)
        {
            // 面法线（叉乘两条边）
            float ax = f.V1.X - f.V0.X, ay = f.V1.Y - f.V0.Y, az = f.V1.Z - f.V0.Z;
            float bx = f.V2.X - f.V0.X, by = f.V2.Y - f.V0.Y, bz = f.V2.Z - f.V0.Z;
            float nx = ay * bz - az * by;
            float ny = az * bx - ax * bz;
            float nz = ax * by - ay * bx;

            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 0.0001f) return 1.0f;

            // ★ 修复：模型面片缠绕方向给出的法线朝内，取反得到真正朝外的几何法线
            nx = -nx / len;
            ny = -ny / len;
            nz = -nz / len;

            // 光照方向（世界空间 / 相机空间）：从相机方向略偏左上
            float lx = 0.3f, ly = 0.6f, lz = 0.7f;
            float llen = MathF.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= llen; ly /= llen; lz /= llen;

            // ★ 修复：把光照方向从相机空间变换回模型空间（逆 Pitch 再逆 Yaw），
            //   让"光源跟着相机走"——人物转到哪个角度，朝向相机的一面始终被照亮，
            //   不再出现转到某个角度就死黑一片的情况
            float ly1 = ly * cosP + lz * sinP;
            float lz1 = -ly * sinP + lz * cosP;
            float lxm = lx * cosY - lz1 * sinY;
            float lzm = lx * sinY + lz1 * cosY;

            float dot = nx * lxm + ny * ly1 + nz * lzm;

            // 环境光 0.6 + 漫反射
            return 0.6f + 0.4f * MathF.Max(0f, dot);
        }
    }
}
```

### 2.3 `Pivot3DControl.cs`（340 行）

```csharp
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Project.Launch.Tools;
using SkiaSharp;


using Project.Launch.Tools.JavaConfig;
using Project.Launch.Tools.LogsConfig;
namespace Project.Launch.Tools.Player3DConfig
{
    public class Pivot3DControl : Control
    {
        public static readonly StyledProperty<Bitmap?> SkinProperty =
            AvaloniaProperty.Register<Pivot3DControl, Bitmap?>(nameof(Skin));

        public Bitmap? Skin
        {
            get => GetValue(SkinProperty);
            set => SetValue(SkinProperty, value);
        }

        private List<SkinFace> _faces;
        private SkinModelType _currentModelType = SkinModelType.Classic;
        private SKImage? _skinImage;
        private float _yaw = 0f;
        private float _pitch = 0f;

        private bool _isDragging;
        private Point _dragStart;
        private float _dragStartYaw;
        private float _dragStartPitch;
        private DateTime _lastInteraction = DateTime.MinValue;

        private DispatcherTimer? _autoRotateTimer;

        public Pivot3DControl()
        {
            _faces = MinecraftSkinModel.Build(SkinModelType.Classic);
            Focusable = true;
            ClipToBounds = true;

            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _lastInteraction = DateTime.Now;
            _autoRotateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _autoRotateTimer.Tick += OnAutoRotateTick;
            _autoRotateTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_autoRotateTimer != null)
            {
                _autoRotateTimer.Stop();
                _autoRotateTimer.Tick -= OnAutoRotateTick;
                _autoRotateTimer = null;
            }
            _skinImage?.Dispose();
            _skinImage = null;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SkinProperty)
            {
                _skinImage?.Dispose();
                _skinImage = BitmapToSkImage(Skin);

                if (Skin != null)
                {
                    var detected = DetectSkinType(Skin);
                    if (detected != _currentModelType)
                    {
                        _currentModelType = detected;
                        _faces = MinecraftSkinModel.Build(detected);

                        LogHelper.Write(LauncherPaths.AppLog,
                            $"皮肤检测: {detected}",
                            $"Skin detected: {detected}");
                    }
                }

                InvalidateVisual();
            }
        }

        // ============================================================
        // 检测皮肤类型（Classic / Slim）
        //
        // 方案：
        //   1. 看皮肤宽度决定是普通（64）还是 HD（128）
        //   2. 按比例缩放检测坐标
        //   3. 检测该列是否有颜色：
        //        - 有颜色 → Steve (Classic)
        //        - 全透明 → Alex (Slim)
        //
        // 基准：0-based (54, 20)，长度 12 行（对应 64×64）
        // ============================================================
        private static SkinModelType DetectSkinType(Bitmap bitmap)
        {
            try
            {
                int w = bitmap.PixelSize.Width;
                int h = bitmap.PixelSize.Height;

                // 老版 64×32 皮肤一律按 Classic
                if (w < 64 || h < 32) return SkinModelType.Classic;

                // 计算比例：64 → 1，128 → 2，256 → 4
                int ratio = w / 64;

                // 缩放检测坐标和长度
                int checkX = 54 * ratio;
                int checkY = 20 * ratio;
                int checkH = 12 * ratio;

                // 边界保护
                if (checkX >= w || checkY + checkH > h)
                    return SkinModelType.Classic;

                var rect = new PixelRect(checkX, checkY, 1, checkH);
                byte[] pixels = new byte[checkH * 4];
                unsafe
                {
                    fixed (byte* p = pixels)
                    {
                        bitmap.CopyPixels(rect, (IntPtr)p, pixels.Length, 4);
                    }
                }

                // 有任一不透明像素 → Classic
                for (int i = 0; i < checkH; i++)
                {
                    if (pixels[i * 4 + 3] != 0)
                    {
                        LogHelper.Write(LauncherPaths.AppLog,
                            $"检测: 尺寸={w}x{h}, ratio={ratio}, 位置=({checkX},{checkY}), 结果=Classic",
                            $"Detect: size={w}x{h}, ratio={ratio}, pos=({checkX},{checkY}), result=Classic");
                        return SkinModelType.Classic;
                    }
                }

                LogHelper.Write(LauncherPaths.AppLog,
                    $"检测: 尺寸={w}x{h}, ratio={ratio}, 位置=({checkX},{checkY}), 结果=Slim",
                    $"Detect: size={w}x{h}, ratio={ratio}, pos=({checkX},{checkY}), result=Slim");
                return SkinModelType.Slim;
            }
            catch (Exception ex)
            {
                LogHelper.Write(LauncherPaths.AppLog,
                    $"检测异常: {ex.Message}",
                    $"Detect exception: {ex.Message}");
                return SkinModelType.Classic;
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _isDragging = true;
            _dragStart = e.GetPosition(this);
            _dragStartYaw = _yaw;
            _dragStartPitch = _pitch;
            _lastInteraction = DateTime.Now;
            e.Pointer.Capture(this);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging) return;
            var cur = e.GetPosition(this);
            float dx = (float)(cur.X - _dragStart.X);
            float dy = (float)(cur.Y - _dragStart.Y);

            if (InteractionConfig.PivotInvertX) dx = -dx;
            if (InteractionConfig.PivotInvertY) dy = -dy;

            float sensitivity = InteractionConfig.PivotSensitivity;
            _yaw = _dragStartYaw + dx * sensitivity;
            _pitch = Math.Clamp(_dragStartPitch + dy * sensitivity, -30f, 30f);
            _lastInteraction = DateTime.Now;
            InvalidateVisual();
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isDragging = false;
            _lastInteraction = DateTime.Now;
            e.Pointer.Capture(null);
        }

        private void OnAutoRotateTick(object? sender, EventArgs e)
        {
            if (_isDragging) return;
            if ((DateTime.Now - _lastInteraction).TotalSeconds < 2.0) return;
            _yaw += 0.4f;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var b = Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;

            context.FillRectangle(Brushes.Transparent, new Rect(b.Size));

            var projected = Pivot3DRenderer.Render(
                _faces, _yaw, _pitch,
                (float)b.Width, (float)b.Height,
                0.78f);

            context.Custom(new Pivot3DDrawOperation(
                new Rect(b.Size), projected, _skinImage));
        }

        private static SKImage? BitmapToSkImage(Bitmap? bmp)
        {
            if (bmp == null) return null;
            try
            {
                int w = bmp.PixelSize.Width;
                int h = bmp.PixelSize.Height;
                if (w <= 0 || h <= 0) return null;

                var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                byte[] pixels = new byte[w * h * 4];
                unsafe
                {
                    fixed (byte* p = pixels)
                    {
                        bmp.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, pixels.Length, w * 4);
                    }
                }
                return SKImage.FromPixelCopy(info, pixels, w * 4);
            }
            catch { return null; }
        }

        private class Pivot3DDrawOperation : ICustomDrawOperation
        {
            private readonly List<ProjectedFace> _faces;
            private readonly SKImage? _skin;

            public Rect Bounds { get; }

            public Pivot3DDrawOperation(Rect bounds, List<ProjectedFace> faces, SKImage? skin)
            {
                Bounds = bounds;
                _faces = faces;
                _skin = skin;
            }

            public void Dispose() { }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;

            public void Render(ImmediateDrawingContext context)
            {
                var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature == null) return;

                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;

                if (_skin == null || _faces.Count == 0) return;

                float texW = _skin.Width;
                float texH = _skin.Height;

                foreach (var f in _faces)
                {
                    var positions = new SKPoint[]
                    {
                        new SKPoint(f.X0, f.Y0),
                        new SKPoint(f.X1, f.Y1),
                        new SKPoint(f.X2, f.Y2),
                        new SKPoint(f.X0, f.Y0),
                        new SKPoint(f.X2, f.Y2),
                        new SKPoint(f.X3, f.Y3),
                    };

                    var texs = new SKPoint[]
                    {
                        new SKPoint(f.T0.U * texW, f.T0.V * texH),
                        new SKPoint(f.T1.U * texW, f.T1.V * texH),
                        new SKPoint(f.T2.U * texW, f.T2.V * texH),
                        new SKPoint(f.T0.U * texW, f.T0.V * texH),
                        new SKPoint(f.T2.U * texW, f.T2.V * texH),
                        new SKPoint(f.T3.U * texW, f.T3.V * texH),
                    };

                    byte s = (byte)Math.Clamp(f.Shade * 255f, 0f, 255f);
                    var colors = new SKColor[]
                    {
                        new SKColor(s, s, s, 255),
                        new SKColor(s, s, s, 255),
                        new SKColor(s, s, s, 255),
                        new SKColor(s, s, s, 255),
                        new SKColor(s, s, s, 255),
                        new SKColor(s, s, s, 255),
                    };

                    using var shader = SKShader.CreateImage(
                        _skin,
                        SKShaderTileMode.Clamp,
                        SKShaderTileMode.Clamp);

                    using var paint = new SKPaint
                    {
                        IsAntialias = false,
#pragma warning disable CS0618
                        FilterQuality = SKFilterQuality.None,
#pragma warning restore CS0618
                        Shader = shader
                    };

                    canvas.DrawVertices(SKVertexMode.Triangles, positions, texs, colors, paint);
                }
            }
        }
    }
}
```

---

## 3. 管线原理逐层解读

> 这一节解释"为什么代码长这样"，迁移时必须保住这些行为，否则画面会退化。

### 3.1 坐标系约定

| 空间 | 约定 |
|---|---|
| 模型空间 | **Y 轴向上**，原点在脚底中心；单位约等于 1 个 Minecraft 像素 |
| 屏幕空间 | 左上原点，**Y 轴向下**，单位是 Avalonia 逻辑像素 |
| 相机 | 固定在 +Z 方向朝 -Z 看，`cameraDist = 60` 单位 |

玩家模型整体高度 32 单位（头 24→32，身体 12→24，腿 0→12），中心点取 `(0, 16, 0)`。

> 方块模型只需把中心点改为 `(0, 0.5, 0)`、尺寸改为 1×1×1，管线本身不用改。

### 3.2 顶点变换（`Pivot3DRenderer.Transform`）

四步，全部标量展开（没有用矩阵类）：

```
1. 平移到以模型中心为原点
   x = v.X - center.X;  y = v.Y - center.Y;  z = v.Z - center.Z

2. Yaw（绕 Y 轴）
   x1 =  x·cosY + z·sinY
   z1 = -x·sinY + z·cosY

3. Pitch（绕 X 轴）
   y1 = y·cosP - z1·sinP
   z2 = y·sinP + z1·cosP

4. 透视投影 + 屏幕映射
   depth  = cameraDist - z2            ← 相机在 +Z，+Z 面离相机更近
   depth  = max(depth, 1)              ← 防止除零/穿透
   persp  = cameraDist / depth
   sx = cx + x1·scale·persp
   sy = cy - y1·scale·persp            ← 注意这里取负，因为屏幕 Y 向下
```

`scale = viewH * scaleFactor / 32`，即"整个模型高度占视口高度的 `scaleFactor` 比例"。`scaleFactor` 由 `Pivot3DControl.Render()` 传 `0.78f`（`Pivot3DControl.cs:228`）。

### 3.3 背面剔除（`Pivot3DRenderer.cs:48-49`）

```csharp
float cross = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
if (cross <= 0f) continue;
```

在**屏幕空间**做二维叉乘判断绕向。逆时针（`cross > 0`）视为正面保留。

> ⚠️ 这依赖 `MinecraftSkinModel.AddBox()` 里手写的顶点顺序。迁移时**务必保持每个面的顶点绕向**，否则整块模型会"里外翻转"。

### 3.4 画家算法（`Pivot3DRenderer.cs:67`）

```csharp
result.Sort((a, b) => b.Depth.CompareTo(a.Depth));
```

按面四个顶点平均深度的**降序**排序，先画远的、后画近的。

**局限（迁移后依然存在，属已知限制）**：

- 没有 Z-buffer；
- 面相交 / 共面时会出现排序错误（画家算法的经典问题）；
- 对 Minecraft 模型这种"轴对齐盒子拼装"足够用。

### 3.5 光照（`Pivot3DRenderer.cs:99-134`）

```
1. 面法线 = (V1-V0) × (V2-V0)，归一化后取反（模型绕向给的法线朝内）
2. 光源方向世界空间 (0.3, 0.6, 0.7) 归一化
3. 把光源方向从相机空间逆变换回模型空间（逆 Pitch 再逆 Yaw）
   —— 效果是"光源跟着相机走"，人物/方块转到任何角度，朝向相机那面都是亮的
4. shade = 0.6 + 0.4 · max(0, n·l)
```

**第 3 步是刻意设计**，不要"优化"掉。它解决的是"转到某个角度就死黑一片"的问题（代码注释里明确写了"★ 修复"）。

> 方块投影工具里，等轴测预览通常希望**光照固定在世界空间**（顶面始终最亮），而不是跟着相机。迁移时把这条做成可选项，两种模式都支持。

### 3.6 UV 约定

- `MinecraftSkinModel.AddFace()` 把 UV 从**像素坐标**转成 **0~1 归一化**（除以 64）；
- `Pivot3DControl` 绘制时再乘回 `_skin.Width / _skin.Height`（`Pivot3DControl.cs:285-286`）；
- 皮肤贴图标准布局：64×64，头在 `(0,0)-(32,16)`，身体在 `(16,16)` 起，手臂 `(40,16)`，腿 `(0,16)`。

> 方块贴图是 16×16，迁移后用归一化 UV 就能复用同一套绘制逻辑。

### 3.7 像素风（`Pivot3DControl.cs:326-333`）

```csharp
using var paint = new SKPaint
{
    IsAntialias = false,          // 关抗锯齿
    FilterQuality = SKFilterQuality.None,  // 贴图采样用最近邻
    Shader = shader
};
```

**迁移后必须保留**。一旦开了线性采样，像素风就会糊掉——而方块投影工具的核心卖点就是像素风。

### 3.8 渲染接入方式（`Pivot3DControl.cs:257-338`）

Avalonia 的自定义绘制三段式：

```
① Control.Render(DrawingContext ctx)
      ctx.Custom(new Pivot3DDrawOperation(bounds, faces, skinImage));

② class Xxx : ICustomDrawOperation
      Rect Bounds { get; }
      void Render(ImmediateDrawingContext context)

③ var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
   using var l = lease!.Lease();
   SKCanvas canvas = l.SkCanvas;      // 拿到原生 Skia canvas
```

这是 Avalonia 里**访问底层 Skia** 的唯一正规途径。迁移时整段照搬即可。

---

## 4. 迁移目标架构

### 4.1 目标：主项目不再自己渲染

**迁移前**：

```
Project.Launch（主项目）
└── MainSrc\Tools\Player3DConfig\
    ├── MinecraftSkinModel.cs      ← 主项目自己的模型
    ├── Pivot3DRenderer.cs         ← 主项目自己的管线
    └── Pivot3DControl.cs          ← 主项目自己的控件
```

**迁移后**：

```
Projection3D（本工具，独立可运行 + 可被引用）
├── Toolkits\                       ← 【核心】纯 C#，零 Avalonia 依赖
│   ├── Math\                       ← Vec2 / Vec3
│   ├── Model\                      ← Face / Model / CubeModel / PlayerModel
│   ├── Render\                     ← ProjectionRenderer（通用管线）
│   └── Camera\                     ← 视角参数
├── ViewUI\                         ← 【视图】Avalonia
│   └── ModelViewer.cs              ← 通用控件（任何 Model 都能显示）
└── textures\                       ← 原版贴图库（数据源）

Project.Launch（主项目）
└── 只保留零渲染代码
    MainWindow.axaml 里：
    <vui:ModelViewer Model="Player" Skin="{Binding ...}" />
```

**主项目最终状态**：`MainSrc\Tools\Player3DConfig\` 整个目录删除。

### 4.2 为什么玩家模型和方块模型能共用一套管线

因为二者在结构上**完全同构**：

| 概念 | 玩家模型 | 方块模型 |
|---|---|---|
| 基本单元 | 轴对齐长方体 | 单个立方体 |
| 数量 | 12 个 | 1 个 |
| 每盒面数 | 6 | 6 |
| 面 = ? | 4 顶点 + 4 UV | 4 顶点 + 4 UV |
| 渲染方式 | `DrawVertices` 三角形 | `DrawVertices` 三角形 |

**差异只存在于"模型数据"这一层，管线（变换/投影/剔除/排序/光照/光栅化）完全一致。**

所以迁移的正确做法不是"搬过去再改"，而是**抽象出通用的 Model 概念，玩家和方块都只是 Model 的一个实例**。

### 4.3 需要的通用抽象（迁移时的重构目标）

| 类型 | 职责 | 从哪来 |
|---|---|---|
| `Vec2` | UV / 屏幕点 | 照搬 `MinecraftSkinModel.Vec2` |
| `Vec3` | 模型空间顶点 | 照搬 `MinecraftSkinModel.Vec3` |
| `ModelFace` | 4 顶点 + 4 UV + 所属部件名 + 是否 overlay | 照搬 `SkinFace`，**改名**更中性 |
| `IModel` / `Model` | `List<ModelFace>` 的容器 | 新建 |
| `CubeModel` | 1 个立方体，6 面 | 新建（方块工具用） |
| `CubeTextureSet` | 一个方块 6 面对应的贴图（支持"六面相同"和"顶侧底不同"） | 新建（配合 §1.3.1） |
| `PlayerModel` | 12 个盒子 | 照搬 `MinecraftSkinModel.Build()` |
| `BoxBuilder` | 给定 x0/y0/z0/x1/y1/z1 + 六面 UV，产出 6 个面 | 照搬 `AddBox` / `AddFace` |
| `Camera` | yaw / pitch / distance / scaleFactor / 中心点 | 新建（从 `Render()` 参数提出） |
| `ProjectedFace` | 屏幕四边形 + UV + 深度 + 明暗 | 照搬 `Pivot3DRenderer.ProjectedFace` |
| `ProjectionRenderer` | `Render(IModel, Camera, w, h) → List<ProjectedFace>` | 照搬 `Pivot3DRenderer` |
| `FacePainter` | `List<ProjectedFace>` + 贴图 → `SKCanvas` | 从 `Pivot3DDrawOperation.Render` 抽出 |
| `ModelViewer` | Avalonia 控件，绑 Model + 贴图 | 从 `Pivot3DControl` 抽取 |

### 4.4 迁移前后的 API 对照

| 现在（主项目） | 迁移后（本工具） |
|---|---|
| `MinecraftSkinModel.Build(SkinModelType.Classic)` | `new PlayerModel()` / `PlayerModel.Build(PlayerModelType.Classic)` |
| `Pivot3DRenderer.Render(faces, yaw, pitch, w, h, 0.78f)` | `ProjectionRenderer.Render(model, camera, w, h)` |
| `Pivot3DControl`（含皮肤检测 + 交互） | `ModelViewer`（同一套交互搬过去） |
| `SkinFace` | `ModelFace` |
| `Vec3` / `Vec2` | 同名，挪到 `Toolkits.Math` |
| 命名空间 `Project.Launch.Tools.Player3DConfig` | `Projection3D.Toolkits.*` / `Projection3D.ViewUI` |

---

## 5. 主项目改动清单

> 本节是**迁移完成后**主项目要做的改动。**阶段 1 不要做**（阶段 1 只建本工具自己的应用）。

### 5.1 删除

```
ProjectLaunch\MainSrc\Tools\Player3DConfig\
├── MinecraftSkinModel.cs      ← 删
├── Pivot3DRenderer.cs         ← 删
└── Pivot3DControl.cs          ← 删
```

### 5.2 `Project.Launch.csproj` 改动

**(a) 加引用：**

```xml
<ItemGroup>
  <ProjectReference Include="3DProjection\.src\3dproj.csproj" />
</ItemGroup>
```

**(b) 加排除（防 glob 污染）：**

```xml
<ItemGroup>
  <Compile Remove="3DProjection\**" />
  <AvaloniaXaml Remove="3DProjection\**" />
  <AvaloniaResource Remove="3DProjection\**" />
  <EmbeddedResource Remove="3DProjection\**" />
  <None Remove="3DProjection\**" />
</ItemGroup>
```

> 当前 `.src` 因点开头被 .NET SDK 的 `**/.*/**` 隐式排除，但**不要依赖这个隐式规则**，显式写死更安全。
> ⚠️ 注意 `.src\textures\` 里有 4000+ 张 PNG：如果哪天加了 `<AvaloniaResource Include="3DProjection\**" />` 这类通配，会把整个贴图库打进主项目。保持排除。

### 5.3 `MainWindow.axaml` 改动

```diff
- xmlns:p3d="clr-namespace:Project.Launch.Tools.Player3DConfig"
+ xmlns:vui="using:Projection3D.ViewUI"
```

```diff
- <p3d:Pivot3DControl x:Name="SkinViewer" ... />
+ <vui:ModelViewer x:Name="SkinViewer" ... />
```

### 5.4 `MainWindow.cs` 改动

```diff
- using Project.Launch.Tools.Player3DConfig;
+ using Projection3D.ViewUI;
+ using Projection3D.Toolkits.Model;
```

如果主项目里有 `SkinViewer.Skin = ...` 之类的赋值，属性名保持一致就不用改。

### 5.5 迁移后主项目的渲染代码量

| 项 | 迁移前 | 迁移后 |
|---|---|---|
| 主项目自有渲染代码 | 760 行 | **0 行** |
| 主项目依赖 | 无 | 引用 3DProjection |

---

## 6. 迁移步骤（分阶段，按顺序做）

> 每一阶段结束都要能编译、能跑，不要一次改完再调试。

### 阶段 A：在本工具里建通用管线（照搬，不改逻辑）

1. 建 `Toolkits\Math\Vec2.cs`、`Vec3.cs`（照搬）
2. 建 `Toolkits\Model\ModelFace.cs`（照搬 `SkinFace`，改名）
3. 建 `Toolkits\Render\ProjectionRenderer.cs`（照搬 `Pivot3DRenderer`，把 `yaw/pitch/viewW/viewH/scaleFactor` 收进 `Camera` 类）
4. 建 `Toolkits\Render\ProjectedFace.cs`（照搬）
5. **验收**：喂固定输入，比对输出的顶点坐标与主项目一致

### 阶段 B：迁模型

1. 建 `Toolkits\Model\BoxBuilder.cs`（照搬 `AddBox` / `AddFace`）
2. 建 `Toolkits\Model\PlayerModel.cs`（照搬 `MinecraftSkinModel.Build` 的 12 个盒子）
3. 建 `Toolkits\Model\CubeModel.cs`（方块：1 个 1×1×1 立方体，6 面）
4. 建 `Toolkits\Model\CubeTextureSet.cs`（实现 §1.3.1 的贴图映射规则）
5. **验收**：`PlayerModel` 产出的面数 = 72；`CubeModel` = 6

### 阶段 C：迁视图

1. 建 `ViewUI\ModelViewer.cs`：把 `Pivot3DControl` 的 `Render` + `Pivot3DDrawOperation` + 交互逻辑搬过来
2. 把"皮肤检测"从控件里**抽出去**（那是玩家皮肤专属逻辑，不该在通用控件里）→ 放到 `Toolkits\Model\PlayerSkinDetector.cs`
3. **验收**：本工具的 HelloWorld 窗口里放一个 `ModelViewer`，能显示一个可拖拽旋转的盒子

### 阶段 D：主项目切换

1. 主项目 csproj 加 `ProjectReference` + 排除项
2. `MainWindow.axaml` / `.cs` 改命名空间和类型
3. 删除 `MainSrc\Tools\Player3DConfig\`
4. **验收**：主项目 `dotnet build` 0 error；运行后玩家皮肤显示效果与迁移前**逐像素一致**

### 阶段 E：收尾

1. 主项目文档（`HANDOVER.md`）更新渲染章节
2. 本工具 `HANDOVER.md` 的 §7"可复用既有实现"改为"已迁移"

---

## 7. 验收标准

### 7.1 阶段 A-C（本工具内）

| # | 检查 | 通过标准 |
|---|---|---|
| 1 | `dotnet build` | 0 error |
| 2 | 管线输出 | 相同输入下，顶点坐标与主项目实现一致（误差 < 0.01） |
| 3 | 面数 | PlayerModel = 72，CubeModel = 6 |
| 4 | 画面 | 可拖拽旋转、2 秒后自动旋转、无穿模、转角不出现死黑 |
| 5 | 像素风 | 放大后边缘是硬边，没有模糊 |

### 7.2 阶段 D（主项目切换）

| # | 检查 | 通过标准 |
|---|---|---|
| 1 | 主项目 `dotnet build` | 0 error |
| 2 | 主项目运行 | 玩家模型显示正常，与迁移前肉眼无差别 |
| 3 | 主项目源码 | `MainSrc\Tools\Player3DConfig\` 已不存在 |
| 4 | 依赖 | 主项目未引入任何 3D 引擎 |

---

## 8. 迁移时的常见坑

| 坑 | 表现 | 解决 |
|---|---|---|
| 顶点绕向改了 | 模型里外翻转（看到内壁） | 保持 `AddBox` 里每个面的顶点顺序原样 |
| 光照方向没转回模型空间 | 转到某角度整块发黑 | 照搬 `EstimateShade` 的逆变换那几行 |
| `FilterQuality` 设错 | 像素糊 | 必须 `IsAntialias=false` + `FilterQuality.None` |
| `depth` 没做 `max(1)` 保护 | 偶尔除零 / 图形炸裂 | 保留 `if (depth < 1f) depth = 1f;` |
| 屏幕 Y 没取负 | 图形上下颠倒 | `sy = cy - y1 * ...` |
| Overlay 层没外扩 | 外套和内层 z-fighting | Overlay 盒子坐标 ±0.25，照搬 |
| 把皮肤检测塞进通用控件 | 方块工具里出现"检测玩家皮肤"的怪代码 | 抽到 `PlayerSkinDetector`，别放 `ModelViewer` |
| 方块只有一张贴图 | 草方块四个面全糊成草皮 | 用 `CubeTextureSet` 实现顶/侧/底分离 |
| 一次性加载 3829 张贴图 | 启动卡住 / 内存暴涨 | 按需加载 + 缓存 |
| 迁移时顺手"优化"算法 | 画面变了，且很难查 | **先 1:1 照搬，跑通后再单独做优化提交** |

---

## 9. 参考

| 用途 | 路径（相对仓库根 `ProjectLaunch\`） |
|---|---|
| 本文档 | `3DProjection\.src\RENDER_PIPELINE.md` |
| 项目接手总纲 | `3DProjection\.src\HANDOVER.md` |
| 贴图库 | `3DProjection\.src\textures\`（4004 文件 / 2.0 MB） |
| 迁移源 ① | `MainSrc\Tools\Player3DConfig\MinecraftSkinModel.cs` |
| 迁移源 ② | `MainSrc\Tools\Player3DConfig\Pivot3DRenderer.cs` |
| 迁移源 ③ | `MainSrc\Tools\Player3DConfig\Pivot3DControl.cs` |
| 主项目中的使用处 | `MainSrc\Views\MainWindow.axaml:247`（`<p3d:Pivot3DControl x:Name="SkinViewer">`） |
| 方块视觉基准 | `MainSrc\Views\imgs\Main\images\Blocks\chest.png` |

---

## 10. 交接约定

1. **阶段 1 不做本文档的迁移**。阶段 1 只做 `HANDOVER.md` 里的 HelloWorld 窗口。本文档是阶段 2 及以后的事。
2. **先照搬，后重构**。迁移初期禁止"顺手优化"，任何算法改动单独提交、单独验证。
3. **改主项目前先问用户**。
4. 本文档 §2 的源码是快照，如果主项目又改了那三个文件，以磁盘为准并重新同步本节。

---

# 附录：引用文件的完整源码

> **为什么要这个附录**：接手方可能在网页端与 AI 对话，无法读取仓库文件。本附录把本文档引用到的**所有可复制文本文件**完整贴出，方便直接粘贴给 AI。
>
> **不会出现在这里的内容**（无法或不适合贴）：
> - 二进制：`chest.png` 等图片、`textures\` 下 3829 张 PNG
> - 超大文件：`MainWindow.axaml`（786 行 / 42.7 KB）、`MainWindow.cs`（1372 行 / 55.9 KB）、`SetupWindow.axaml.cs`（762 行 / 27.9 KB）—— 只贴相关片段
> - 主项目 `HANDOVER.md`（632 KB）
> - `LiquidGlassAvaloniaUI\` 的 13 个 `.cs`（vendored 第三方库，迁移不涉及，全文约 4400 行）

## A.1 `Project.Launch.csproj`（主项目工程文件，迁移时必改）

路径：`ProjectLaunch\Project.Launch.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>MainSrc\Views\imgs\Main\images\Launcher\MainLogo.ico</ApplicationIcon>

    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <DebugType>none</DebugType>
    <PublishReadyToRun>false</PublishReadyToRun>
    <PublishTrimmed>false</PublishTrimmed>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <SatelliteResourceLanguages>zh-Hans;en</SatelliteResourceLanguages>
  </PropertyGroup>

  <!-- ★ 指定 Debug 构建输出到 MainSrc\BuiltDebugRun\ -->
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <OutputPath>MainSrc\BuiltDebugRun\</OutputPath>
    <!-- 去除中间目录，让 exe 和 dll 直接铺在 BuiltDebugRun 根目录 -->
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <!-- ★ 排除「非项目源码目录」：若在项目内放置第三方/参考资料源码，
         默认通配（**/*.cs、**/*.resx）会把它们吸进本项目编译与资源处理，
         导致构建失败或类型冲突。该目录删除后，下面这 6 行可一并移除。 -->
    <Compile Remove="pclsource\**" />
    <AvaloniaXaml Remove="pclsource\**" />
    <AvaloniaResource Remove="pclsource\**" />
    <EmbeddedResource Remove="pclsource\**" />
    <None Remove="pclsource\**" />
    <Content Remove="pclsource\**" />

    <!-- ★ 玻璃库已搬到 MainSrc\Tools\LiquidGlassAvaloniaUI\（2026-10 架构调整）
         必须排除它的源码与 obj，否则会被主项目的 **/*.cs 通配吸进来，
         与主项目自己的 obj/AssemblyInfo.cs 撞成 CS0579（程序集特性重复） -->
    <Compile Remove="MainSrc\Tools\LiquidGlassAvaloniaUI\**" />
    <AvaloniaXaml Remove="MainSrc\Tools\LiquidGlassAvaloniaUI\**" />
    <AvaloniaResource Remove="MainSrc\Tools\LiquidGlassAvaloniaUI\**" />
    <EmbeddedResource Remove="MainSrc\Tools\LiquidGlassAvaloniaUI\**" />
    <None Remove="MainSrc\Tools\LiquidGlassAvaloniaUI\**" />

    <!-- ★ 排除输出目录，防止生成的 .cs 被重复编译导致 CS0579 错误 -->
    <Compile Remove="MainSrc\BuiltDebugRun\**" />
    <AvaloniaXaml Remove="MainSrc\BuiltDebugRun\**" />
    <AvaloniaResource Remove="MainSrc\BuiltDebugRun\**" />
    <EmbeddedResource Remove="MainSrc\BuiltDebugRun\**" />
    <None Remove="MainSrc\BuiltDebugRun\**" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.2" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.2" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.2" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="12.1.2" />
    <PackageReference Include="AvaloniaUI.DiagnosticsSupport" Version="2.2.3">
      <IncludeAssets Condition="'$(Configuration)' != 'Debug'">None</IncludeAssets>
      <PrivateAssets Condition="'$(Configuration)' != 'Debug'">All</PrivateAssets>
    </PackageReference>

    <PackageReference Include="NAudio" Version="2.2.1" />

    <PackageReference Include="SkiaSharp" Version="3.119.4" />

    <ProjectReference Include="MainSrc\Tools\LiquidGlassAvaloniaUI\LiquidGlassAvaloniaUI.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- ★ 用 Update（不是 Include），避免和默认 glob 重复 -->
    <AvaloniaXaml Update="App.axaml" />
    <AvaloniaXaml Update="MainSrc\Views\SetupWindow.axaml" />
    <AvaloniaXaml Update="MainSrc\Views\MainWindow.axaml" />
    <AvaloniaXaml Update="MainSrc\Views\AccountManagerWindow.axaml" />
    <AvaloniaXaml Update="MainSrc\Views\DownloadWindow.axaml" />

    <!-- 样式：作为 AvaloniaResource 读，避免被当独立 XAML 编译 -->
    <AvaloniaResource Include="MainSrc\Views\Styles.axaml" />
    <AvaloniaResource Include="MainSrc\Views\Glass.axaml" />

    <!-- 图片和音效 -->
    <AvaloniaResource Include="MainSrc\Views\imgs\**" Exclude="MainSrc\Views\imgs\Main\images\Launcher\MainLogo.ico" />
    <AvaloniaResource Include="MainSrc\Sound\**" />

    <!-- 任务栏图标 -->
    <Content Include="MainSrc\Views\imgs\Main\images\Launcher\MainLogo.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>

    <Compile Update="MainSrc\Views\MainWindow.cs">
      <DependentUpon>MainWindow.axaml</DependentUpon>
    </Compile>
    <Compile Update="App.axaml.cs" />
  </ItemGroup>
</Project>
```

## A.2 `Program.cs`（Avalonia 应用入口范式）

路径：`ProjectLaunch\Program.cs`

> 新建 `3dproj` 应用时照抄这份结构，只改命名空间和崩溃日志路径。

```csharp
using System;
using System.IO;
using System.Threading;
using Avalonia;
using Project.Launch.Tools;

using Project.Launch.Tools.JavaConfig;
using Project.Launch.Tools.LogsConfig;
using Project.Launch.Download;
namespace Project.Launch
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                LogHelper.Write(LauncherPaths.StartupLog,
                    "程序入口执行", "Program entry executed");

                // 启动即探测系统代理：代理客户端没运行（但注册表仍开着代理）时自动改直连，
                // 否则所有请求都会被送去一个没人监听的本地端口，表现为「获取版本列表失败」
                ProxyEnvironment.EnsureInitialized();

                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(LauncherPaths.CrashLog,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{ex}");
                }
                catch { }

                Console.WriteLine("=== CRASH ===");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("=============");
                Console.WriteLine("崩溃日志已写入 / Crash log written: " + LauncherPaths.CrashLog);
                Console.ReadKey();
                Environment.Exit(1);
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
```

## A.3 `App.axaml`（应用级 XAML）

路径：`ProjectLaunch\App.axaml`

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Project.Launch.App"
             RequestedThemeVariant="Default">

    <Application.Styles>
        <FluentTheme />
        <!-- 主题色 + Miret 样式 -->
        <StyleInclude Source="avares://Project.Launch/MainSrc/Views/Styles.axaml" />
        <!-- ★ 液态玻璃参数表 + Glass 按钮样式 -->
        <StyleInclude Source="avares://Project.Launch/MainSrc/Views/Glass.axaml" />
    </Application.Styles>
</Application>
```

## A.4 `App.axaml.cs`（Application 子类，含全局异常兜底）

路径：`ProjectLaunch\App.axaml.cs`

> 里面有两块可以直接搬进新工具：`InstallCrashGuards()`（全局异常兜底）和 `OnFrameworkInitializationCompleted()`（创建主窗口）。

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Project.Launch.Views;
using System;
using Project.Launch.Tools;

using Project.Launch.Auth;
using Project.Launch.Tools.JavaConfig;
using Project.Launch.Tools.LogsConfig;
namespace Project.Launch
{
    public partial class App : Application
    {
        public static bool LoginCheck { get; set; }
        public static bool IsLoggedIn { get; set; }
        public static string? PlayerName { get; set; }
        public static string? OfflineUuid { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            InstallCrashGuards();
        }

        /// <summary>
        /// 全局异常兜底。
        /// 之前 MainWindow 的 async void 事件处理器里一旦抛异常（例如文件夹选择器内部报错），
        /// 异常会回到 Dispatcher 并终止整个进程 —— 用户看到的是「窗口无响应然后崩溃」。
        /// 这里把 UI 线程上的未处理异常记录后吞掉，保证单个交互出错不会带走整个启动器。
        /// </summary>
        private static void InstallCrashGuards()
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                try
                {
                    LogHelper.Write(LauncherPaths.AppLog,
                        $"UI 线程未处理异常（已拦截，程序继续运行）: {e.Exception}",
                        $"Unhandled UI exception (suppressed): {e.Exception}");

                    System.IO.File.AppendAllText(LauncherPaths.CrashLog,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n[UI-Thread Suppressed]\n{e.Exception}\n\n");
                }
                catch { }

                // 标记为已处理：避免单个事件处理器异常把整个应用带崩
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    System.IO.File.AppendAllText(LauncherPaths.CrashLog,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n[AppDomain]\n{e.ExceptionObject}\n\n");
                }
                catch { }
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try
                {
                    LogHelper.Write(LauncherPaths.AppLog,
                        $"未观察的任务异常: {e.Exception?.GetBaseException().Message}",
                        $"Unobserved task exception: {e.Exception?.GetBaseException().Message}");
                }
                catch { }

                e.SetObserved();
            };
        }

        public override void OnFrameworkInitializationCompleted()
        {
            LogHelper.Write(LauncherPaths.AppLog,
                "应用初始化开始", "Application initialization started");

            var account = AccountService.GetSelectedAccount();
            if (account != null)
            {
                PlayerName = account.Username;
                OfflineUuid = account.Uuid;
                IsLoggedIn = true;
                LoginCheck = AccountService.GetLoginCheck();

                LogHelper.Write(LauncherPaths.AppLog,
                    $"已加载账户: {PlayerName}，跳过登录: {LoginCheck}",
                    $"Account loaded: {PlayerName}, LoginCheck: {LoginCheck}");
            }
            else
            {
                PlayerName = null;
                OfflineUuid = null;
                IsLoggedIn = false;
                LoginCheck = false;

                AccountService.SetLoginCheck(false);

                LogHelper.Write(LauncherPaths.AppLog,
                    "未找到账户，LoginCheck 设为 false",
                    "No account found, LoginCheck set to false");
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
                base.OnFrameworkInitializationCompleted();
            }

            LogHelper.Write(LauncherPaths.AppLog,
                "应用初始化完成", "Application initialization complete");
        }
    }
}
```

## A.5 `SkinCropper.cs`（2D 头像裁剪，**不迁移**，仅作边界说明）

路径：`ProjectLaunch\MainSrc\Tools\JavaConfig\SkinCropper.cs`

> 这是用 `RenderTargetBitmap` 做的 2D 图像裁剪，**不属于 3D 管线**，不在迁移范围。列出来是为了让接手方确认"2D 头像处理"和"3D 模型渲染"是两件事。

```csharp
using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Project.Launch.Tools.JavaConfig
{
    /// <summary>
    /// 皮肤裁切工具：从皮肤 PNG 裁出方形头像
    /// </summary>
    public static class SkinCropper
    {
        /// <summary>
        /// 同步裁切：返回 (face 头像, hat 帽子层)
        /// 支持 64×64 / 128×128 / 256×256 等任意倍率皮肤
        /// </summary>
        public static (Bitmap? face, Bitmap? hat) Crop(Stream stream)
        {
            try
            {
                using var src = new Bitmap(stream);
                int width = src.PixelSize.Width;
                int height = src.PixelSize.Height;

                // 动态计算比例
                int scale = width / 64;
                if (scale < 1) return (null, null);

                int faceX = 8 * scale;
                int faceY = 8 * scale;
                int faceSize = 8 * scale;
                int hatX = 40 * scale;
                int hatY = 8 * scale;

                var faceCropped = new CroppedBitmap(src, new PixelRect(faceX, faceY, faceSize, faceSize));
                var hatCropped = new CroppedBitmap(src, new PixelRect(hatX, hatY, faceSize, faceSize));

                var faceBmp = new RenderTargetBitmap(new PixelSize(faceSize, faceSize), new Vector(96, 96));
                using (var ctx = faceBmp.CreateDrawingContext())
                    ctx.DrawImage(faceCropped, new Rect(0, 0, faceSize, faceSize));

                var hatBmp = new RenderTargetBitmap(new PixelSize(faceSize, faceSize), new Vector(96, 96));
                using (var ctx = hatBmp.CreateDrawingContext())
                    ctx.DrawImage(hatCropped, new Rect(0, 0, faceSize, faceSize));

                return (faceBmp, hatBmp);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// 从资源 URI 同步裁切 face 头像
        /// </summary>
        public static Bitmap? CropFaceFromAsset(string assetUri)
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri(assetUri));
                var (face, _) = Crop(stream);
                return face;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从资源 URI 同步裁切 face + hat
        /// </summary>
        public static (Bitmap? face, Bitmap? hat) CropFromAsset(string assetUri)
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri(assetUri));
                return Crop(stream);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
```

## A.6 `LiquidGlassAvaloniaUI.csproj`（独立库 csproj 的写法范式）

路径：`ProjectLaunch\MainSrc\Tools\LiquidGlassAvaloniaUI\LiquidGlassAvaloniaUI.csproj`

> 主项目里已有"独立类库 + 主项目引用"的先例（就是这个玻璃库）。以后把 `Toolkits\` 抽成 `3DProjection.Core.csproj` 时，参考这个文件的 `PackageId` / `Description` / `PackageLicenseExpression` 写法。

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFramework>net8.0</TargetFramework>
      <LangVersion>9</LangVersion>
      <Nullable>enable</Nullable>
      <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
      <PackageId>LiquidGlassAvaloniaUI</PackageId>
      <Authors>KaranocaVe</Authors>
      <Description>AvaloniaUI liquid-glass backdrop and lens effects powered by Skia.</Description>
      <PackageLicenseExpression>MIT</PackageLicenseExpression>
      <PackageReadmeFile>README.md</PackageReadmeFile>
      <RepositoryUrl>https://github.com/KaranocaVe/LiquidGlassAvaloniaUI</RepositoryUrl>
      <RepositoryType>git</RepositoryType>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Avalonia" Version="12.0.1" />
      <PackageReference Include="Avalonia.Skia" Version="12.0.1" />
      <PackageReference Include="SkiaSharp" Version="3.119.3-preview.1.1" />
    </ItemGroup>

    <ItemGroup>
      <None Include="..\README.md" Pack="true" PackagePath="\" />
      <None Remove="Assets\Shaders\LiquidGlassShader.sksl" />
      <AvaloniaResource Include="Assets\Shaders\LiquidGlassShader.sksl" />
      <None Remove="Assets\Shaders\LiquidGlassHighlight.sksl" />
      <AvaloniaResource Include="Assets\Shaders\LiquidGlassHighlight.sksl" />
      <None Remove="Assets\Shaders\LiquidGlassInteractiveHighlight.sksl" />
      <AvaloniaResource Include="Assets\Shaders\LiquidGlassInteractiveHighlight.sksl" />
      <None Remove="Assets\Shaders\LiquidGlassGamma.sksl" />
      <AvaloniaResource Include="Assets\Shaders\LiquidGlassGamma.sksl" />
      <None Remove="Assets\Shaders\LiquidGlassProgressiveMask.sksl" />
      <AvaloniaResource Include="Assets\Shaders\LiquidGlassProgressiveMask.sksl" />
      <None Remove="Assets\Shaders\LiquidGlassBackdropTransform.sksl" />
      <AvaloniaResource Include="Assets\Shaders\LiquidGlassBackdropTransform.sksl" />
    </ItemGroup>

</Project>
```

## A.7 `MainWindow.axaml` 中 3D 控件的使用片段

路径：`ProjectLaunch\MainSrc\Views\MainWindow.axaml`（第 247-252 行，全文 786 行不贴）

```xml
                    <p3d:Pivot3DControl x:Name="SkinViewer"
                                          Grid.Row="0"
                                          HorizontalAlignment="Stretch"
                                          VerticalAlignment="Stretch"
                                          Margin="0,10,0,10"
                                          IsVisible="False" />
```

**迁移后改成：**

```xml
<vui:ModelViewer x:Name="SkinViewer"
                 Grid.Row="0"
                 HorizontalAlignment="Stretch"
                 VerticalAlignment="Stretch"
                 Margin="0,10,0,10"
                 IsVisible="False" />
```

## A.8 `MainWindow.cs` 中 `CropSkinFromBitmap` 方法（2D，**不迁移**）

路径：`ProjectLaunch\MainSrc\Views\MainWindow.cs`（第 1276-1304 行，全文 1372 行不贴）

```csharp
        private (Bitmap? face, Bitmap? hat) CropSkinFromBitmap(Bitmap bitmap)
        {
            try
            {
                int width = bitmap.PixelSize.Width;
                int height = bitmap.PixelSize.Height;

                int scale = width / 64;
                if (scale < 1) return (null, null);

                int faceX = 8 * scale;
                int faceY = 8 * scale;
                int faceSize = 8 * scale;
                int hatX = 40 * scale;
                int hatY = 8 * scale;

                var faceCropped = new CroppedBitmap(bitmap, new PixelRect(faceX, faceY, faceSize, faceSize));
                var hatCropped = new CroppedBitmap(bitmap, new PixelRect(hatX, hatY, faceSize, faceSize));

                var faceBmp = new RenderTargetBitmap(new PixelSize(faceSize, faceSize), new Vector(96, 96));
                using (var ctx = faceBmp.CreateDrawingContext()) ctx.DrawImage(faceCropped, new Rect(0, 0, faceSize, faceSize));

                var hatBmp = new RenderTargetBitmap(new PixelSize(faceSize, faceSize), new Vector(96, 96));
                using (var ctx = hatBmp.CreateDrawingContext()) ctx.DrawImage(hatCropped, new Rect(0, 0, faceSize, faceSize));

                return (faceBmp, hatBmp);
            }
            catch { return (null, null); }
        }
```

## A.9 `SetupWindow.axaml.cs` 中头像裁剪片段（2D，**不迁移**）

路径：`ProjectLaunch\MainSrc\Views\SetupWindow.axaml.cs`（第 738-752 行，全文 762 行不贴）

```csharp
                        var faceBmp = new RenderTargetBitmap(new PixelSize(faceSize, faceSize), new Vector(96, 96));
                        using (var ctx = faceBmp.CreateDrawingContext())
                            ctx.DrawImage(faceCropped, new Rect(0, 0, faceSize, faceSize));

                        var hatBmp = new RenderTargetBitmap(new PixelSize(faceSize, faceSize), new Vector(96, 96));
                        using (var ctx = hatBmp.CreateDrawingContext())
                            ctx.DrawImage(hatCropped, new Rect(0, 0, faceSize, faceSize));

                        return (faceBmp, hatBmp);
                    }
                    catch
                    {
                        return (null, null);
                    }
                }).ConfigureAwait(false);
```

---

## A.10 附录清单核对

| # | 文件 | 完整/片段 | 行数 | 迁移相关 |
|---|---|---|---|---|
| A.1 | `Project.Launch.csproj` | 完整 | 101 | ✅ 必改 |
| A.2 | `Program.cs` | 完整 | 56 | ✅ 参考 |
| A.3 | `App.axaml` | 完整 | 13 | ✅ 参考 |
| A.4 | `App.axaml.cs` | 完整 | 117 | ✅ 参考 |
| A.5 | `SkinCropper.cs` | 完整 | 88 | ❌ 2D，不迁 |
| A.6 | `LiquidGlassAvaloniaUI.csproj` | 完整 | 39 | ⚪ 仅范式参考 |
| A.7 | `MainWindow.axaml` | 片段 | 6 | ✅ 必改 |
| A.8 | `MainWindow.cs` | 片段 | 29 | ❌ 2D，不迁 |
| A.9 | `SetupWindow.axaml.cs` | 片段 | 15 | ❌ 2D，不迁 |
| §2.1 | `MinecraftSkinModel.cs` | 完整 | 284 | ✅ 迁移源 |
| §2.2 | `Pivot3DRenderer.cs` | 完整 | 136 | ✅ 迁移源 |
| §2.3 | `Pivot3DControl.cs` | 完整 | 340 | ✅ 迁移源 |
