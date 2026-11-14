# 3DProjection · 接手文档（HANDOVER）

> **读者**：接手本项目的 AI 开发者
> **生成时间**：2026-11-14
> **本文档自包含**：读完即可开始编码，不需要再读其它任何文档
> **当前阶段**：阶段 0（骨架已建，代码为零）→ 目标是阶段 1（能 `dotnet run` 弹出 Hello World 窗口）

---

## 0. TL;DR — 你现在要做什么

在 `C:\NoiRaceing\KilimMinecraftLauncherProject\ProjectLaunch\3DProjection\.src\` 下，把一个**只有空占位文件**的目录，变成**能编译、能运行**的 Avalonia 桌面应用：

```powershell
cd C:\NoiRaceing\KilimMinecraftLauncherProject\ProjectLaunch\3DProjection\.src
dotnet run
```

→ 弹出一个窗口，标题 `3DProjection`，内容 `Hello World`。

**这一阶段只做这个，不要提前实现 3D 渲染。** 先把"项目能跑起来"这个地基打牢。

---

## 1. 项目定位

### 1.1 这是什么

**独立开源的 Minecraft 方块三面投影（等轴测 / isometric）素材生成工具。**

- 输入：一个方块（贴图或方块 ID）
- 输出：像素风的 PNG 图标，显示**三个面**（顶面 + 左面 + 右面）
- 用途：给启动器 / 资源包 / 网页生成方块图标素材

### 1.2 为什么要做（产品动机）

Minecraft 素材制作圈**没有成熟的"方块三面投影生成器"**。开发者普遍的做法是：

- 手写一次性渲染脚本，跑完就丢；
- 或者直接用现成的图标（画质、风格不统一）；
- 或者手工在图像软件里拼三个面。

结果就是素材难复现、难批量、风格不统一。本项目要把这件事工具化、开源化。

### 1.3 视觉基准（最重要的验收参考）

仓库里现有的人工素材就是目标效果。用眼睛对标这个文件：

```
MainSrc\Views\imgs\Main\images\Blocks\chest.png      ← 300×300，标准等轴测三面投影
```

特征（必须复现）：

- 立方体露出 **3 个面**：顶面（菱形）+ 左面 + 右面
- **像素风**：贴图放大用最近邻（nearest-neighbor），不允许模糊
- 面与面之间有清晰的**明暗差异**（顶面最亮、左面次之、右面最暗或反之）
- 透明背景（PNG 带 alpha）

现有素材的尺寸规格不统一，说明输出尺寸需要可配置：`64×64` / `150×150` / `256×256` / `300×300` 都有。

### 1.4 最终形态

1. **工具应用**：选方块 → 实时 3D 预览 → 导出 PNG
2. **可复用库**：Kilim 启动器（主项目）引用同一套渲染核心，自己生成图标
3. **开源发布**：独立的 GitHub 仓库 / NuGet 包

---

## 2. 第一个里程碑（阶段 1，唯一验收目标）

### 2.1 验收标准

| # | 检查项 | 通过标准 |
|---|---|---|
| 1 | `cd 3DProjection\.src` 后 `dotnet run` | 命令成功，进程不退出 |
| 2 | 出现窗口 | 标题 `3DProjection`，可见内容 `Hello World` |
| 3 | 窗口可交互 | 能拖动、能点右上角关闭 |
| 4 | 关闭后 | 进程正常退出，任务管理器无残留 |
| 5 | 控制台 | 无未处理异常堆栈 |
| 6 | `dotnet build` | 0 error（warning 可以有） |

### 2.2 明确不要做的事

- ❌ 不要在这一阶段写任何 3D 渲染代码
- ❌ 不要改主项目 `Project.Launch.csproj` / `MainSrc\` 下的任何文件
- ❌ 不要引入 3D 引擎（见 §5.2 硬约束）
- ❌ 不要动 `.git`、不要提交

---

## 3. 技术栈（必须与主项目严格对齐）

| 项 | 值 | 说明 |
|---|---|---|
| .NET SDK | **10.0.401** | 已安装；`dotnet --version` 可验证 |
| TargetFramework | **`net10.0`** | 与主项目一致，不要用 net8.0 |
| UI 框架 | **Avalonia 12.1.2** | ⚠️ 模板默认给 12.1.1，**必须手动改成 12.1.2** |
| Avalonia.Desktop | 12.1.2 | 桌面后端 |
| Avalonia.Themes.Fluent | 12.1.2 | 主题 |
| Avalonia.Fonts.Inter | 12.1.2 | 字体 |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | 仅 Debug 用（主项目同款配置） |
| 光栅化 | **SkiaSharp 3.119.4** | 阶段 2 才需要加，阶段 1 可以先不加 |
| 语言特性 | `Nullable=enable`、`AllowUnsafeBlocks=true` | 后续 Skia 像素操作需要 unsafe |
| 平台 | win-x64 | 与主项目一致 |

### 3.1 模板说明

本机**已安装 Avalonia 模板**（`dotnet new list` 可见 `avalonia.app`）。但要注意：

- 本机模板版本是 **Avalonia.Templates 12.1.1**，生成出来的 csproj 是 `12.1.1`
- 主项目用的是 **12.1.2**
- → 生成后**必须**把 csproj 里 4 个 `PackageReference` 的版本号改成 `12.1.2`

（可选：`dotnet new install Avalonia.Templates@12.1.6` 升级模板，但不是必须。）

---

## 4. 目录结构

### 4.1 现状（用户已建好的骨架）

```
3DProjection\.src\
├── 3dproj.csproj              ← 0 字节，待填
├── HANDOVER.md                ← 本文档
├── RENDER_PIPELINE.md         ← 渲染管线专项接手文档（含主项目 760 行源码）
├── Toolkits\                  ← 空目录
├── ViewUI\
│   ├── MainWindow.axaml       ← 0 字节，待填
│   └── MainWindow.xaml.cs     ← 0 字节，待填
└── textures\                  ← ★ 已就位的 Minecraft 原版贴图库（见 §4.4）
```

**占位文件可以直接覆盖/填充，没有内容需要保留。**

> ⚠️ 注意 `MainWindow.*` 两个文件在 **`ViewUI\` 子目录**，不在 `.src` 根目录。这不影响编译（SDK 默认 glob 会递归收子目录），但**影响新建文件放在哪** —— 见 §4.2。
>
> ⚠️ 占位文件叫 `MainWindow.xaml.cs`，而 Avalonia 模板生成的是 `MainWindow.axaml.cs`（多点一个 `a`）。**建议统一用模板的 `.axaml.cs`**，删掉占位的那两个文件，见 §8.1。

### 4.2 目标结构（阶段 1）

```
3DProjection\.src\
├── 3dproj.csproj              ← ★ 唯一的应用项目（WinExe），`dotnet run` 在这里执行
├── HANDOVER.md
├── RENDER_PIPELINE.md         ← 渲染管线迁移专项文档
│
├── Program.cs                 ← 新建：程序入口
├── App.axaml                  ← 新建：应用级 XAML 资源
├── App.axaml.cs               ← 新建：Application 子类
├── app.manifest               ← 新建：Windows 清单（模板自带）
│
├── ViewUI\                    ← 【视图层】所有窗口和自定义控件都在这里
│   ├── MainWindow.axaml       ← 填充：Hello World 窗口
│   ├── MainWindow.axaml.cs    ← 填充：code-behind
│   └── ProjectionView.cs      ← （阶段 2）自定义绘制控件
│
├── Toolkits\                  ← 【核心层】纯 C#，零 UI 依赖（阶段 2 起填）
│   ├── Math\                  ← Vec2 / Vec3 / Mat4
│   ├── Model\                 ← 方块模型、面定义、UV
│   └── Render\                ← 变换 / 投影 / 剔除 / 排序 / 光照
│
└── textures\                  ← ★ Minecraft 原版贴图库（数据源，已就位，不要动）
```

**关键约定：**

| 内容 | 放哪 | 命名空间 |
|---|---|---|
| 程序入口 / App | `.src\` 根目录 | `Projection3D` |
| 窗口、控件、XAML | `ViewUI\` | `Projection3D.ViewUI` |
| 数学、模型、渲染算法 | `Toolkits\` | `Projection3D.Toolkits.*` |
| 贴图素材 | `textures\`（只读） | 不参与编译 |

### 4.3 为什么这样分层（重要）

`Toolkits\` 和 `ViewUI\` 的分界线就是**能不能脱离 UI 单独测试**：

- `Toolkits\` 只允许引用 `System.*`（纯数学 + 数据结构）→ 未来抽成类库给启动器用
- `ViewUI\` 允许引用 Avalonia / SkiaSharp → 只负责把 `Toolkits\` 算出的面片画到屏幕上

**阶段 1 两个子目录里的业务代码都先不填**（`ViewUI\` 只填 MainWindow），等阶段 2 再填。

### 4.4 `textures\` — 原版贴图库（只读数据源）

项目里已经放好了一整套 **Minecraft 原版贴图**，共 **4004 个文件 / 约 2.0 MB**：

| 目录 | 文件数 | 用途 |
|---|---|---|
| `textures\block\` | 1405 | **方块贴图（本工具主要数据源）** |
| `textures\item\` | 831 | 物品贴图 |
| `textures\entity\` | 759 | 实体贴图 |
| `textures\gui\` | 616 | GUI 贴图 |
| `textures\particle\` | 301 | 粒子 |
| `textures\painting\` | 52 | 画 |
| `textures\mob_effect\` | 40 | 状态效果图标 |

- `.png` 3829 个，标准尺寸 **16×16**
- `.mcmeta` 175 个，元数据（如 `{"texture":{"mipmap_strategy":"strict_cutout"}}`），生成静态图标时可忽略

**使用约定：**

1. **只读**。不要修改、不要重新压缩、不要删除。
2. **不参与编译**。`.csproj` 里**不要**加 `<AvaloniaResource Include="textures\**" />`，否则 4000 张图会全打进程序集。运行时按文件路径读取即可。
3. **按需加载 + 缓存**。不要启动时一次性加载。
4. 贴图是 16×16，**采样必须最近邻**（`SKFilterQuality.None`），否则像素风会糊。
5. 一个方块的 6 个面不一定用同一张贴图（草方块、原木、熔炉都是"顶/侧/底不同"）。详细映射规则见 `RENDER_PIPELINE.md` §1.3.1。

---

## 5. 双 csproj / 双应用框架（重点章节）

### 5.1 整体关系

整个解决方案最终有**两个独立应用**，各有一个 csproj：

| # | 项目文件 | 路径 | 类型 | 角色 | 状态 |
|---|---|---|---|---|---|
| 1 | `Project.Launch.csproj` | 仓库根 `ProjectLaunch\` | WinExe / net10.0 / Avalonia 12.1.2 | **Kilim 启动器** | ✅ 已存在，**本阶段不要动** |
| 2 | `3dproj.csproj` | `ProjectLaunch\3DProjection\.src\` | WinExe / net10.0 / Avalonia 12.1.2 | **3D 投影工具**（本项目） | 🔨 本阶段要建 |

两者**互不依赖**，各自独立运行、独立调试。共享方式见 §5.3。

### 5.2 硬约束（违反即返工）

1. **渲染管线 100% 自研**。禁止引入任何 3D 引擎/数学库：
   - ❌ Unity / Godot / MonoGame / OpenTK / Silk.NET / HelixToolkit / SharpDX
   - ❌ 任何 Vulkan/DirectX/OpenGL 封装
   - ✅ 只允许 `System.Numerics`（向量矩阵基础）+ 手写管线 + SkiaSharp（2D 光栅化）
2. **不依赖主项目**。`3dproj.csproj` 不得 `ProjectReference` 主项目（否则耦合、开源不干净）。
3. **主项目不得被修改**（本阶段）。阶段 2 才做接入。

### 5.3 演进路径（三个阶段）

```
阶段 1（现在）
  3dproj 独立开发、独立 dotnet run
  主项目完全不知道 3DProjection 的存在

        ↓

阶段 2
  3dproj 里实现渲染核心（Toolkits/）
  3dproj 自己能用（GUI 预览 + 导出 PNG）

        ↓

阶段 3（接入主项目，二选一）
  方案 A（推荐）：把 Toolkits/ 抽成 Class Library
        ├── 3DProjection.Core.csproj   ← 纯库，零 Avalonia 依赖
        ├── 3dproj.csproj              ← 工具应用，引用 Core
        └── Project.Launch.csproj      ← 启动器，引用 Core
  方案 B（快速）：主项目直接 ProjectReference 3dproj.csproj
        （缺点：把工具的 UI 依赖和入口也带进启动器，不干净）
```

**阶段 1 不要考虑阶段 3，但请保持 `Toolkits\` 的纯净**，这样阶段 3 抽库时零成本。

### 5.4 阶段 3 接入时的关键改动（预留给未来，现在别做）

主项目 `Project.Launch.csproj` 需要**同时**做两件事：

```xml
<!-- ① 引用 3D 项目 -->
<ItemGroup>
  <ProjectReference Include="3DProjection\.src\3dproj.csproj" />
</ItemGroup>

<!-- ② 排除 3DProjection 目录，防止被主项目的通配 glob 吸进编译 -->
<ItemGroup>
  <Compile Remove="3DProjection\**" />
  <AvaloniaXaml Remove="3DProjection\**" />
  <AvaloniaResource Remove="3DProjection\**" />
  <EmbeddedResource Remove="3DProjection\**" />
  <None Remove="3DProjection\**" />
</ItemGroup>
```

> 为什么需要 ②？见 §6 陷阱 2。主项目用的是默认 `**/*.cs` 通配，会把子孙目录的所有 `.cs` 吸进自己的编译单元，造成类型重复 / CS0579。

---

## 6. ⚠️ 关键陷阱（踩了会浪费大量时间）

### 陷阱 1：`.src` 以点开头 —— 这是**故意的保护**

.NET SDK 的默认排除规则里有 `**/.*/**`（点开头目录不参与默认通配）。

**好消息**：主项目 `Project.Launch.csproj` 的 `**/*.cs` 不会把 `3DProjection\.src\**` 吸进去。已验证：主项目的 `Compile` / `AvaloniaXaml` 项目项里都不包含 `3DProjection`。

**坏消息**：如果你以后把代码放到 `3DProjection\` 下**非点开头**的目录（比如 `3DProjection\src\`），主项目立刻会吸入，构建炸掉。

**结论**：所有源码保持在 `.src\` 里面；或者显式加 §5.4 的 `<Compile Remove>`。

### 陷阱 2：主项目通配 glob 污染

主项目 csproj 没有为本项目预留排除项。虽然当前因 `.src` 点开头而幸运躲过，但这**依赖 SDK 的隐式规则**，非常脆弱。

阶段 3 接入时务必显式加排除（见 §5.4）。

### 陷阱 3：Avalonia 12 的 `InitializeComponent` 在 VSCode 里报 CS0103（误报，忽略）

在 VSCode 里打开 `.axaml.cs` 文件，`InitializeComponent()` 下面可能出现红色波浪线 `CS0103: 当前上下文中不存在名称"InitializeComponent"`。

**这是误报，不影响构建。** 原因：

- Avalonia 的 `InitializeComponent` 由**源生成器 + IL 注入**在编译期生成，不是手写代码；
- Avalonia 的构建目标里有 `Condition="... AND $(DesignTimeBuild) != true AND ..."`，**设计时构建会跳过 XAML 编译**；
- VSCode 的 C# 语言服务器用设计时构建做分析，所以看不到该方法。

**判断真假的方法**：跑 `dotnet build`。如果 0 error，就是误报。修复办法：VSCode 里 `Ctrl+Shift+P` → `Developer: Reload Window`。

### 陷阱 4：模板版本号 12.1.1 ≠ 主项目 12.1.2

`dotnet new avalonia.app` 生成的是 12.1.1，必须手动改成 12.1.2（见 §3.1）。

### 陷阱 5：Debug 输出目录

主项目把 Debug 输出改到了 `MainSrc\BuiltDebugRun\`（csproj 里 `OutputPath` 覆写）。

**本项目不要学它**，保持默认 `bin\Debug\net10.0\` 即可。放 `.src` 目录里更清晰。

若担心输出被占用（程序还在跑），用：

```powershell
dotnet build -p:OutputPath="$env:TEMP\3dproj_check\"
```

### 陷阱 6：项目名 `3dproj` 以数字开头 —— 要显式固定命名空间

csproj 文件名是 `3dproj.csproj`，MSBuild 会据此推断：

- `AssemblyName` 默认 = `3dproj`（程序集名允许数字开头，**合法**）
- `RootNamespace` 默认 = `3dproj`（**C# 命名空间不允许数字开头，非法**）

**实测结论**：只要代码里的命名空间是**显式写死**的（模板生成的 `namespace Projection3D;`），改名成 `3dproj.csproj` 后 `dotnet build` 依然 **0 错误**。所以这不是硬性阻塞。

**但仍然建议**显式固定，避免未来新增文件/工具按 `RootNamespace` 自动生成非法标识符：

```xml
<AssemblyName>Projection3D</AssemblyName>
<RootNamespace>Projection3D</RootNamespace>
```

代码里统一用 `namespace Projection3D;` / `Projection3D.Toolkits` / `Projection3D.ViewUI`。

> csproj **文件名**保持 `3dproj.csproj`（用户已建），与程序集名/命名空间无关。

### 陷阱 7：Windows 代理

主项目踩过的大坑：`.NET HttpClient` 继承 WinINET 系统代理，代理软件没开时所有网络请求失败。

**本项目阶段 1/2 不联网**（贴图从本地文件读），所以不涉及。但如果以后加"从 Mojang 下载方块贴图"，必须复用主项目的 `MainSrc\Download\ProxyEnvironment.cs` 的思路。

### 陷阱 8：`dotnet run` 找项目文件

`dotnet run` 只在**当前目录**找 `.csproj`。所以：

- ✅ `cd 3DProjection\.src` 然后 `dotnet run` —— 找得到 `3dproj.csproj`
- ❌ 在 `ProjectLaunch\` 根目录 `dotnet run` —— 会跑主项目（`Project.Launch.csproj`）去

### 陷阱 9：文件编码

主项目的 AXAML 里有中文，历史上有过编码混乱（表现为 `鐧诲綍` 这种乱码）。**新文件一律用 UTF-8（无 BOM 或带 BOM 都可以，但保持一致）**，中文注释不要用 ANSI/GBK 保存。

---

## 7. 可复用的既有实现（主项目里现成的自研管线）

**不要从零发明轮子。** 主项目里已有一套已验证的自研软件 3D 管线，位于：

```
MainSrc\Tools\Player3DConfig\
├── MinecraftSkinModel.cs   284 行   盒子模型 + 六面 UV 映射
├── Pivot3DRenderer.cs      136 行   变换 / 投影 / 剔除 / 排序 / 光照
└── Pivot3DControl.cs       340 行   Avalonia 接入 + SkiaSharp 绘制
```

### 7.1 可以直接借鉴/移植的部分

| 能力 | 位置 | 说明 |
|---|---|---|
| Yaw/Pitch 旋转 | `Pivot3DRenderer.cs:80-87` | 手写矩阵乘展开，无库依赖 |
| 透视投影 | `Pivot3DRenderer.cs:88-94` | `depth = cameraDist - z; persp = cameraDist / depth` |
| 背面剔除 | `Pivot3DRenderer.cs:48-49` | 屏幕空间叉乘，`cross <= 0` 丢弃 |
| 画家算法排序 | `Pivot3DRenderer.cs:67` | 按面平均深度降序，远→近绘制 |
| Lambert 光照 | `Pivot3DRenderer.cs:99-134` | 面法线叉乘 + 环境光 0.6 + 漫反射 0.4 |
| Skia 接入 | `Pivot3DControl.cs:277-337` | `ICustomDrawOperation` → `ISkiaSharpApiLeaseFeature` → `SKCanvas` |
| 纹理三角形绘制 | `Pivot3DControl.cs:335` | `canvas.DrawVertices(SKVertexMode.Triangles, positions, texs, colors, paint)` |
| 贴图绑定为 shader | `Pivot3DControl.cs:321` | `SKShader.CreateImage(...)` |
| Avalonia Bitmap → SKImage | `Pivot3DControl.cs:234-255` | `CopyPixels` + `SKImage.FromPixelCopy` |
| 最近邻像素风 | `Pivot3DControl.cs:328-331` | `IsAntialias = false` + `FilterQuality.None` |

⚠️ 注意：主项目这套代码在**玩家模型**坐标系里（Y 轴向上、中心 `(0,16,0)`、单位是 Minecraft 像素）。方块模型是 `1×1×1`，需要换算。

### 7.2 不能直接用的部分

| 能力 | 原因 |
|---|---|
| 12 个盒子的玩家模型 | 方块只要 1 个立方体，6 个面 |
| 骨骼动画 | 项目里没有，也不需要 |
| Classic/Slim 判定 | 那是玩家皮肤的，方块贴图不适用 |
| 拖拽自动旋转逻辑 | 可参考，但方块工具可能需要不同的交互（转盘 / 预设角度） |

### 7.3 主项目的 Avalonia 应用骨架（照抄这份最稳）

主项目已验证可运行的入口范式（`Program.cs` / `App.axaml` / `App.axaml.cs`），结构与 §8.2 的模板完全一致，区别只是命名空间。

---

## 8. 阶段 1 实施步骤

### 8.1 推荐做法：用模板生成，再把窗口挪到 ViewUI\

模板已安装，这是最不容易出错的路。**注意模板会把 `MainWindow.*` 生成在 `.src` 根目录，但本项目的约定是放在 `ViewUI\`，所以要挪过去。**

```powershell
cd C:\NoiRaceing\KilimMinecraftLauncherProject\ProjectLaunch\3DProjection\.src

# 1. 先删掉 0 字节占位（推荐）
#    实测：若不删，模板会报 “创建此模板将更改现有文件” 并以退出码 73 失败
#    也可以跳过删除、直接给第 2 步加 --force 强制覆盖
Remove-Item 3dproj.csproj -Force
Remove-Item ViewUI\MainWindow.axaml, ViewUI\MainWindow.xaml.cs -Force

# 2. 用模板生成到 .src 根（-n 决定命名空间和默认文件名）
dotnet new avalonia.app -n Projection3D -o .

# 3. 把生成的 Projection3D.csproj 改名为 3dproj.csproj
Rename-Item Projection3D.csproj 3dproj.csproj

# 4. ★ 把模板生成的两个 MainWindow 文件挪进 ViewUI\
#    注意：必须分成两条命令。-Destination 指向目录时是安全的；
#    若两条合写成 -Destination ViewUI\ 会被当成"目标文件名"，产生同名覆盖错误
Move-Item MainWindow.axaml    -Destination ViewUI\ -Force
Move-Item MainWindow.axaml.cs -Destination ViewUI\ -Force

# 5. 按 §8.1.1 改 x:Class / namespace / App.axaml.cs（或直接跑那段的脚本）

# 6. 确认能跑
dotnet run
```

> **已验证**：完整链路（模板生成 → 改名 csproj → 移动两个 MainWindow 文件 → §8.1.1 三处改名 → `dotnet build`）在干净目录实测 **0 警告 0 错误**，产物结构符合 §8.3。

### 8.1.1 挪动文件后必须同步的 3 处

> ⚠️ **文件名注意**：模板生成的 code-behind 是 **`MainWindow.axaml.cs`**（不是 `MainWindow.xaml.cs`）。你原有的占位文件叫 `MainWindow.xaml.cs`，两者不同名。**建议统一用模板的 `MainWindow.axaml.cs`**（与 Avalonia 约定一致，且自动关联 `.axaml`）。

模板生成的 `MainWindow` 命名空间是 `Projection3D`（因为在根目录）。挪进 `ViewUI\` 后需要手动改三处，**少改任何一处都会编译失败**：

**(a) `ViewUI\MainWindow.axaml` 的 `x:Class`：**

```xml
x:Class="Projection3D.ViewUI.MainWindow"
```

**(b) `ViewUI\MainWindow.axaml.cs` 的命名空间：**

```csharp
namespace Projection3D.ViewUI;
```

**(c) `App.axaml.cs` 里创建窗口的地方：**

```csharp
desktop.MainWindow = new ViewUI.MainWindow();
```

#### 漏改会报什么错（实测）

| 漏改 | 报错 |
|---|---|
| 只改 `x:Class` 不改 namespace | `error CS0103: 当前上下文中不存在名称"InitializeComponent"` + `AXN0001: x:Class 类型 'Projection3D.ViewUI.MainWindow' was not found in the project` |
| 漏改 `App.axaml.cs` | `error CS0246: 未能找到类型或命名空间名"ViewUI"` |

> **这正是本文档陷阱 3 说的 CS0103 的"真身"场景**：当 `x:Class` 指向的类型在项目中找不到时，Avalonia 无法生成 `InitializeComponent`，这个错误就是**真的**了。用 `dotnet build` 就能区分误报与真货。

#### 一次改完的 PowerShell（可选）

```powershell
$ax = Join-Path $PWD 'ViewUI\MainWindow.axaml'
(Get-Content $ax -Raw) -replace 'x:Class="Projection3D\.MainWindow"','x:Class="Projection3D.ViewUI.MainWindow"' |
    Set-Content $ax -NoNewline

$cs = Join-Path $PWD 'ViewUI\MainWindow.axaml.cs'
(Get-Content $cs -Raw) -replace 'namespace Projection3D;','namespace Projection3D.ViewUI;' |
    Set-Content $cs -NoNewline

$app = Join-Path $PWD 'App.axaml.cs'
(Get-Content $app -Raw) -replace 'new MainWindow\(\)','new ViewUI.MainWindow()' |
    Set-Content $app -NoNewline
```

### 8.1.2 也可以让 ViewUI 命名空间自动化（可选）

如果希望 `ViewUI\` 下的新文件**自动**获得 `Projection3D.ViewUI` 命名空间（不必逐文件手写），可以在 csproj 里加：

```xml
<ItemGroup>
  <Compile Update="ViewUI\**\*.cs">
    <RootNamespace>Projection3D.ViewUI</RootNamespace>
  </Compile>
</ItemGroup>
```

> 这是可选优化。最省事的做法就是 §8.1.1 那样手改两行。

### 8.2 模板生成后应当做的 4 处修改

**(a) 版本号 12.1.1 → 12.1.2**（4 个 PackageReference）

**(b) 固定程序集名和根命名空间**（建议，原因见陷阱 6）：

```xml
<AssemblyName>Projection3D</AssemblyName>
<RootNamespace>Projection3D</RootNamespace>
<Nullable>enable</Nullable>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

**(c) 保证产物不污染主项目**：确认 csproj 在 `.src\` 下（默认就满足）。

**(d) 把窗口标题改成 `3DProjection`、内容改成 `Hello World`**（文件在 `ViewUI\` 下）：

- `ViewUI\MainWindow.axaml` 的 `Title="3DProjection"`、`x:Class="Projection3D.ViewUI.MainWindow"`
- 窗口内容放一个居中的 `TextBlock`，`Text="Hello World"`

`ViewUI\MainWindow.axaml` 的最小可用版本：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Projection3D.ViewUI.MainWindow"
        Title="3DProjection"
        Width="800" Height="600">
    <TextBlock Text="Hello World"
               FontSize="32"
               HorizontalAlignment="Center"
               VerticalAlignment="Center" />
</Window>
```

### 8.3 阶段 1 完成后的文件清单

```
3DProjection\.src\
├── 3dproj.csproj          ← 唯一的应用项目
├── Program.cs             ← 入口（根目录）
├── App.axaml              ← 应用资源（根目录）
├── App.axaml.cs           ← App 子类（根目录）
├── app.manifest           ← Windows 清单（根目录）
├── HANDOVER.md            ← 本文档
│
├── ViewUI\                ← 窗口都在这里
│   ├── MainWindow.axaml
│   └── MainWindow.axaml.cs    ← 注意扩展名是 .axaml.cs
│
└── Toolkits\              ← 空目录，阶段 2 填
```

### 8.4 严禁的做法

- ❌ 把 `dotnet new` 生成在 `ProjectLaunch\` 根目录（会多出一个 csproj 和主项目打架）
- ❌ 把 `MainWindow.*` 留在 `.src` 根目录（约定放在 `ViewUI\`，见 §4.2）
- ❌ 用 `net8.0`（虽然模板默认 net10.0，但别改低）
- ❌ 在阶段 1 就加 SkiaSharp / 3D 代码
- ❌ 提交 git

---

## 9. 后续路线图（阶段 2+，本阶段不要做）

| 阶段 | 目标 | 关键产出 |
|---|---|---|
| **2.1** | 静态三面投影渲染 | `Toolkits\Render\ProjectionRenderer.cs`，能在屏幕中央画出一个无贴图的纯色立方体 |
| **2.2** | 贴图映射 | 方块 6 面贴图 → UV → `DrawVertices` |
| **2.3** | 等轴测预设 | Yaw=45°、Pitch≈30°/35.264°，输出标准 Minecraft 风格 |
| **2.4** | 交互 | 拖拽旋转、滚轮缩放、角度复位 |
| **2.5** | PNG 导出 | 多尺寸（64/150/256/300）、透明背景、最近邻缩放、批量导出 |
| **2.6** | 贴图源 | 从 `.minecraft/versions/*/client.jar` 或本地文件夹读取方块贴图 |
| **2.7** | 抽库 | `3DProjection.Core.csproj`（纯 C#），接入主项目 |

### 9.1 等轴测投影的关键参数（阶段 2.3 会用到）

Minecraft 社区方块图标的经典视角：

- **Yaw（绕 Y 轴）** = 45°，让顶面呈对称菱形
- **Pitch（绕 X 轴）** = 30°（常见简化）或 **35.264°**（真正的等轴测，`atan(1/√2)`）
- 像素风：**禁止抗锯齿**、贴图采样用 `SKFilterQuality.None`
- 输出：先在高分辨率画布渲染，再无插值缩放到目标尺寸（或直接按目标尺寸渲染并做最近邻）

### 9.2 参考数据

现有素材尺寸（供导出功能设计参考）：

| 文件 | 尺寸 |
|---|---|
| `Anvil.png` / `CobbleStone.png` / `Grass.png` 等 | 64×64 |
| `grass2.png` | 150×150 |
| `hmcl.png` | 256×256 |
| `chest.png` / `command.png` / `craft_table.png` / `furnace.png` | 300×300 |

→ 导出尺寸必须可配置，默认建议 300×300。

---

## 10. 验收自检清单（交付前逐条打勾）

```
[ ] cd 3DProjection\.src && dotnet build        → 0 error
[ ] cd 3DProjection\.src && dotnet run          → 弹出窗口
[ ] 窗口标题 = 3DProjection
[ ] 窗口内容 = Hello World
[ ] MainWindow.* 位于 ViewUI\ 子目录（不在 .src 根）
[ ] ViewUI\MainWindow.axaml 的 x:Class = "Projection3D.ViewUI.MainWindow"
[ ] 关闭窗口后进程退出（任务管理器无 Projection3D.exe 残留）
[ ] 主项目未受影响：dotnet build Project.Launch.csproj → 仍然是 0 error
     验证命令（避免占用输出目录）：
     dotnet build Project.Launch.csproj -c Debug -p:OutputPath="$env:TEMP\kilim_check\"
[ ] 主项目 Compile 项里仍不含 3DProjection：
     dotnet msbuild Project.Launch.csproj -getItem:Compile -p:Configuration=Debug
[ ] 未新增任何 3D 引擎依赖
[ ] Toolkits\ 空目录仍在（阶段 2 的落点）
```

---

## 11. 参考索引

| 用途 | 路径（相对仓库根 `ProjectLaunch\`） |
|---|---|
| 本文档 | `3DProjection\.src\HANDOVER.md` |
| 本项目应用项目 | `3DProjection\.src\3dproj.csproj` |
| 本项目窗口 | `3DProjection\.src\ViewUI\MainWindow.axaml` |
| 目标视觉基准 | `MainSrc\Views\imgs\Main\images\Blocks\chest.png` |
| 可复用管线（模型） | `MainSrc\Tools\Player3DConfig\MinecraftSkinModel.cs` |
| 可复用管线（变换/光照） | `MainSrc\Tools\Player3DConfig\Pivot3DRenderer.cs` |
| 可复用管线（Skia 接入） | `MainSrc\Tools\Player3DConfig\Pivot3DControl.cs` |
| 主项目 | `Project.Launch.csproj` |
| 主项目文档 | `HANDOVER.md`（仓库根，632 KB，自包含） |
| 独立库参考范式 | `MainSrc\Tools\LiquidGlassAvaloniaUI\LiquidGlassAvaloniaUI.csproj` |

---

## 12. 交接约定

1. **本文件会过时**。§4 的结构、§8 的步骤在阶段 1 完成后就已达成；阶段 2 开始后请以磁盘上的实际代码为准。
2. **只做被要求的阶段**。当前只做阶段 1，做完停下向用户汇报，不要自动进入阶段 2。
3. **遇到本文没写的决策**，倾向：更简单的方案、更少的依赖、与主项目风格一致。
4. **改任何主项目文件前先问用户**。

---

## 13. 变更记录

| 日期 | 变更 |
|---|---|
| 2026-11-14 | 初版 |
| 2026-11-14 | **修正目录结构**：`MainWindow.axaml` / `MainWindow.axaml.cs` 位于 `ViewUI\` 子目录（原文档误写在 `.src` 根）。`3dproj.csproj`、`Program.cs`、`App.*` 仍在 `.src` 根目录。新增：§8.1 移动文件步骤、§8.1.1 三处命名空间同步 + 实测报错对照、§8.1.2 自动命名空间、§8.2(d) 窗口最小示例、§8.3 更新文件清单、§10 新增 2 条自检项。 |
| 2026-11-14 | 记录模板 code-behind 实际文件名是 `MainWindow.axaml.cs`（非 `.xaml.cs`），并标注占位文件命名差异。 |
| 2026-11-14 | 新增 §4.4 **`textures\` 原版贴图库**（4004 文件 / 2.0 MB，7 个子目录），明确"只读、不参与编译、按需加载、最近邻采样"四条约定；§4.1/§4.2 目录结构同步加入 `textures\`。 |
| 2026-11-14 | 新增配套文档 `RENDER_PIPELINE.md`（渲染管线迁移专项，内嵌主项目 760 行渲染源码）。 |

---

# 附录：引用文件的完整源码

> **为什么要这个附录**：接手方可能在网页端与 AI 对话，无法读取仓库文件。这里把本文档引用到的**可复制文本文件**完整贴出。
>
> **不在本附录的内容**：
> - 二进制：`textures\` 下 3829 张 PNG、`chest.png` 等
> - 主项目 `HANDOVER.md`（632 KB）
> - **渲染管线相关源码（760 行）**：见同目录 `RENDER_PIPELINE.md` §2 与附录
> - `MainWindow.axaml` / `MainWindow.cs` / `SetupWindow.axaml.cs`：过大，见 `RENDER_PIPELINE.md` 附录中的片段

## B.1 `Project.Launch.csproj`（主项目工程文件）

路径：`ProjectLaunch\Project.Launch.csproj`

> 阶段 1 不需要改它。它列出的是主项目现状，供参考"主项目依赖哪些包"。

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

## B.2 `Program.cs`（Avalonia 应用入口，阶段 1 照抄这份）

路径：`ProjectLaunch\Program.cs`

> **阶段 1 的 HelloWorld 窗口就是照这个结构搭的。** 只需要改命名空间（`Project.Launch` → `Projection3D`）和去掉日志/代理相关调用。

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

## B.3 `App.axaml`（应用级 XAML）

路径：`ProjectLaunch\App.axaml`

> 简化版只需要 `<FluentTheme />` 一行，其余 `StyleInclude` 是主项目自己的样式。

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

## B.4 `App.axaml.cs`（Application 子类）

路径：`ProjectLaunch\App.axaml.cs`

> **可以直接搬到新工具的两块**：
> - `InstallCrashGuards()` —— 全局异常兜底（UI 线程异常不再带崩进程）
> - `OnFrameworkInitializationCompleted()` —— 创建主窗口
>
> 其余的账户恢复逻辑是主项目业务，不需要。

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

## B.5 `LiquidGlassAvaloniaUI.csproj`（独立库 csproj 写法范式）

路径：`ProjectLaunch\MainSrc\Tools\LiquidGlassAvaloniaUI\LiquidGlassAvaloniaUI.csproj`

> 主项目里"独立类库 + 主项目引用"的现成先例。以后把 `Toolkits\` 抽成 `3DProjection.Core.csproj` 时参考它的 `PackageId` / `Description` / `PackageLicenseExpression`。

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

---

## B.6 附录清单

| # | 文件 | 行数 | 阶段 1 是否要用 |
|---|---|---|---|
| B.1 | `Project.Launch.csproj` | 101 | ⚪ 仅参考 |
| B.2 | `Program.cs` | 56 | ✅ 照抄 |
| B.3 | `App.axaml` | 13 | ✅ 照抄 |
| B.4 | `App.axaml.cs` | 117 | ✅ 照抄（取部分） |
| B.5 | `LiquidGlassAvaloniaUI.csproj` | 39 | ⚪ 仅范式 |
