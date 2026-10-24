# LaterVideo

基于 **WinUI 3 (Windows App SDK) + C#** 的桌面「多媒体文件暂存与预览」工具。
把散落在各处的图片、音乐、音频、视频、文本、配置文件、文档拖进来集中管理，随手预览、随手打开。

<div align="center">

[![Stars](https://img.shields.io/github/stars/LaterChip/LaterVideo?style=for-the-badge&label=Stars&color=6750A4)](https://github.com/LaterChip/LaterVideo/stargazers)
[![Release](https://img.shields.io/github/v/release/LaterChip/LaterVideo?style=for-the-badge&label=Release&color=6750A4)](https://github.com/LaterChip/LaterVideo/releases)
[![Issues](https://img.shields.io/github/issues/LaterChip/LaterVideo?style=for-the-badge&label=Issues&color=6750A4)](https://github.com/LaterChip/LaterVideo/issues)
[![Pull Requests](https://img.shields.io/github/issues-pr/LaterChip/LaterVideo?style=for-the-badge&label=Pull%20Requests&color=6750A4)](https://github.com/LaterChip/LaterVideo/pulls)

[![Downloads](https://img.shields.io/github/downloads/LaterChip/LaterVideo/total?style=for-the-badge&label=Downloads&color=6750A4)](https://github.com/LaterChip/LaterVideo/releases)
[![C#](https://img.shields.io/github/languages/top/LaterChip/LaterVideo?style=for-the-badge&label=C%23&color=6750A4)](https://github.com/LaterChip/LaterVideo)
[![WinUI 3](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK-6750A4?style=for-the-badge&labelColor=6A5B9F&logo=microsoft)](https://github.com/LaterChip/LaterVideo)
[![Windows](https://img.shields.io/badge/Windows%2010%201809%2B-Supported-6750A4?style=for-the-badge)](https://github.com/LaterChip/LaterVideo)

[![Trendshift](badges/trendshift.svg)](https://trendshift.io/repositories/318114)
[![GitHub Trending](badges/github-trending.svg)](https://github.com/trending)
[![Bilibili 动态](https://img.shields.io/badge/Bilibili%20UID-3546952564214479-FF6699?logo=bilibili&logoColor=white&style=for-the-badge)](https://space.bilibili.com/3546952564214479)

</div>

---

## 一、功能一览

| 功能 | 说明 |
| --- | --- |
| 拖拽导入 | 把文件或整个文件夹直接拖到窗口任意位置即可导入（文件夹会递归展开，单次最多 5000 个文件） |
| 添加文件 | 顶部工具栏「添加文件」按钮打开系统文件选择器，支持多选、可任意扩展名 |
| 自动分类 | 按扩展名自动归类为：图片 / 音乐·音频 / 视频 / 文本 / 配置文件 / 文档 / 其他 |
| 分类筛选 | 右上角「全部类型」下拉框可按分类过滤列表 |
| 关键字搜索 | 按文件名或完整路径实时搜索 |
| 排序 | 按名称、大小、修改时间升降序，或按类型分组排序 |
| 列表管理 | 多选（Ctrl / Shift）、移除选中、清空列表 —— **只操作列表，不删除磁盘上的原文件** |
| 列表展示 | 名称、类型、大小、修改时间四列，超长文件名自动省略 |
| 图片预览 | 选中即可在右侧内嵌查看图片 |
| 文本 / 配置预览 | 内嵌只读文本框预览（UTF-8 / 自动识别 BOM），大于 10 MB 或有二进制特征时提示跳过 |
| 音视频预览 | 内嵌播放器，带播放/进度/音量等传输控件 |
| 打开文件 | 用系统默认程序打开选中文件（双击列表行等效） |
| 打开所在位置 | 在文件资源管理器中定位并选中该文件 |
| 复制路径 | 一键复制完整路径到剪贴板 |
| 统计信息 | 底部状态栏实时显示文件总数、总大小、各分类数量、当前筛选后数量 |
| 拖拽悬停提示 | 拖入文件时全窗口半透明遮罩提示，松开即导入 |

---

## 二、项目结构

```
C:\Users\bfgh1\Desktop\LaterVideo\
├─ LaterVideo.sln               解决方案文件（用 Visual Studio 打开这个）
├─ LaterVideo.csproj            项目文件（目标框架 / WinUI 3 / 依赖声明）
├─ app.manifest                 应用清单（DPI 感知 PerMonitorV2、长路径支持、Win10/11 兼容）
├─ App.xaml                     应用级资源（XamlControlsResources + 列表样式）
├─ App.xaml.cs                  应用入口，创建主窗口
├─ MainWindow.xaml              主界面：工具栏 / 文件列表 / 预览面板 / 状态栏 / 拖拽遮罩
├─ MainWindow.xaml.cs           主界面全部交互逻辑
├─ Models\
│   ├─ FileCategory.cs          文件分类枚举
│   └─ FileItem.cs              列表条目模型（名称、路径、分类、大小、时间、图标）
├─ Services\
│   └─ FileTypeHelper.cs        扩展名识别、分类名/图标、文件大小格式化
├─ Properties\
│   └─ launchSettings.json      启动配置（unpackaged 方式直接启动 exe）
├─ .gitignore
└─ README.md
```

### 关键实现说明

- **免打包（Unpackaged）运行**：`WindowsPackageType=None`，编译产物是可直接双击运行的 exe，不需要 MSIX 打包、不需要开发者模式。
- **自带 Windows App SDK 运行时**：`WindowsAppSDKSelfContained=true`，运行不依赖目标机器是否安装 Windows App SDK Runtime。
- **界面框架**：`Microsoft.UI.Xaml.Controls`，XAML 中使用 `x:Bind`、`ListView`、`MediaPlayerElement`、`AutoSuggestBox` 等 WinUI 3 原生控件。
- **列表去重**：内部用 `HashSet<string>`（忽略大小写）保存路径，重复拖入同一文件不会重复出现。

---

## 三、环境要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 1809（17763）及以上 / Windows 11 |
| Visual Studio | Visual Studio 2022 / 2026，安装时勾选「使用 C++ 的桌面开发」或 **「Windows 应用开发 / Windows application development」** 工作负载 |
| .NET SDK | .NET 8 SDK（项目目标框架 `net8.0-windows10.0.19041.0`） |
| 依赖包 | `Microsoft.WindowsAppSDK`（首次还原时自动从 NuGet 下载） |

> 本机已具备：.NET SDK 10.0.402、Visual Studio Community 2026、.NET 8 与 .NET 10 运行时，可直接编译运行。

---

## 四、在 Visual Studio 中打开与运行

1. 打开 **Visual Studio**。
2. 菜单 `文件 → 打开 → 项目/解决方案`，选择：
   `C:\Users\bfgh1\Desktop\LaterVideo\LaterVideo.sln`
3. 等待首次还原（会自动从 NuGet 下载 `Microsoft.WindowsAppSDK`；若未自动还原，右键解决方案 → 「还原 NuGet 包」）。
4. 顶部工具栏把 **解决方案平台** 从 `Any CPU` 切换为 **`x64`**（重要，WinUI 3 需要具体平台）。
   调试配置保持 `Debug` 即可。
5. 按 **F5** 或点击 ▶「LaterVideo (Unpackaged)」启动，即可看到应用窗口。

### 命令行运行方式（可选）

```powershell
cd C:\Users\bfgh1\Desktop\LaterVideo

# 编译
dotnet build LaterVideo.csproj -c Debug -p:Platform=x64

# 直接运行
.\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\LaterVideo.exe
```

### 发布单机版（可选）

```powershell
dotnet publish LaterVideo.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true
```

---

## 五、使用说明

1. 启动后，把**图片 / 音乐 / 音频 / 视频 / 文本 / 配置文件 / 文档**直接从资源管理器拖到窗口里（也可以拖整个文件夹）。
2. 左侧列表点击任意文件，右侧立刻显示预览：
   - 图片 → 直接显示；
   - 文本 / 配置文件 → 只读文本框显示内容；
   - 音频 / 视频 → 内嵌播放器，可播放、拖动进度、调音量；
   - 其他类型 → 提示用「打开文件」交给系统程序。
3. 双击列表行 = 打开文件；`Ctrl` / `Shift` 可多选，再点「移除选中」批量移除。
4. 右上角可切换**分类筛选**与**排序方式**，顶部搜索框可按文件名/路径过滤。
5. 底部状态栏实时显示总数、总大小、各分类数量。

> ⚠️ 「移除选中」「清空列表」**只把条目从列表中拿掉**，不会删除磁盘上的任何文件。

---

## 六、常见问题

**Q：编译报错 `NETSDK1100` / 提示不支持当前平台？**
A：把解决方案平台切到 `x64`（或 `ARM64`、`x86` 中与本机匹配的一个），不要用 `Any CPU`。

**Q：启动后窗口不出现或闪退？**
A：确认 `LaterVideo.csproj` 中 `WindowsAppSDKSelfContained` 为 `true`（默认已开启），并且在 x64 平台下编译。

**Q：拖拽导入无反应？**
A：请以普通用户权限运行（不要用管理员身份运行，避免资源管理器拖拽被 UIPI 拦截）。

**Q：想让某个文件出现在列表里但我只是想看看它？**
A：本工具只做「登记 + 预览 + 打开」，不做任何文件内容修改；移除条目不会再删除文件。
*（内容由AI生成，仅供参考）*
