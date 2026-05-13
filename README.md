# Legendary Input Assistant

> 当前发行版：v3.1.1

Legendary Input Assistant 是从 AutoHotkey v2 脚本迁移到 C# / WPF 的 Windows 输入自动化工具。v3.1.1 的重点不是简单复刻旧版脚本，而是把界面、配置、全局输入监听、图像识别和运行时性能重新整理成更稳定、可维护的桌面程序。

本项目仅用于本地学习、自动化逻辑研究和受控环境测试。

## 为什么迁移到 C#

旧版 AHK 脚本适合快速迭代，但功能变多后会遇到几个问题：

- GUI 布局和状态同步越来越难维护。
- 配置、档案、图像识别参数混在脚本逻辑里，修改成本高。
- 高频图像识别和日志刷新容易影响前台程序流畅度。
- 复杂热键、鼠标监听、按键发送更适合拆成独立服务模块。

v3.1.1 使用 C# / WPF 重做后，主要变化是：

- 界面从 AHK GUI 迁移到 WPF，控件、状态栏、说明窗口和调试浮窗更清晰。
- 全局热键和输入监听改为 Win32 hook / hotkey 方案。
- 按键与鼠标输入发送统一封装到 `InputService`。
- 配置从 INI 转为 JSON，并拆分为通用配置和图像识别配置。
- 档案保存到 Windows 用户数据目录，不再污染 exe 所在目录。
- 图像识别从脚本式 PixelSearch 迁移为 C# 截图、锁定位图、逐像素搜索。

## v3.1.1 功能优化

### 图像识别性能

- 搜索区域只截取用户框选范围，不再每次扫描整块虚拟屏幕。
- 扫描循环从 UI 线程挪到后台任务，降低低间隔扫描时的窗口卡顿。
- 复用截图用的 `Bitmap` / `Graphics`，减少高频分配和 GC 压力。
- 像素匹配改为直接比较 RGB 通道范围，减少逐像素函数调用。
- 命中结果和调试浮窗降频刷新，触发判断仍按扫描间隔执行。
- 调试浮窗移除无意义扫描计数，只保留当前状态和区域信息。

### 图像识别可用性

- 支持框选搜索区域。
- 支持鼠标取色，并用红框标出实际目标像素。
- 取色结果先放入独立小框，不会自动覆盖目标颜色。
- 容差范围明确为 `0-255`。
- 支持连续命中 N 次后触发，减少一闪而过的误触发。
- 支持触发方式：`Tap`、`Down`、`Up`、`Auto`。
- 支持图像识别诊断，会保存程序实际读取到的区域截图 `image-diagnostic.png`。

### 使用体验

- 右上角提供“使用说明”按钮，说明内容维护在 `UsageGuide.cs`。
- 日志框最多保留固定行数，避免长时间运行后拖慢 UI。
- 档案分为通用档案和图像识别档案。
- 旧版 exe 旁边的 `Profiles` 会自动迁移到用户数据目录。
- 支持 DPI 感知，改善高缩放屏幕下的坐标偏移。

## 当前功能

- 总开关：默认 `PageDown`
- 图像识别独立开关：`F2`
- 侧键 + 左键压枪
- 侧键屏息
- 半自动模式
- 滚轮下触发 31 切枪
- 鼠标取色
- 框选识别区域
- 搜色测试
- 图像识别自动触发
- 弹道预览
- 通用档案保存 / 加载 / 删除
- 图像识别档案保存 / 加载 / 删除
- 程序内使用说明

## 数据位置

主配置仍保存在 exe 所在目录：

```text
LegendaryCSharp.settings.json
LegendaryCSharp.image-recognition.json
```

档案保存在 Windows 用户数据目录：

```text
%AppData%\Legendary\Profiles
```

旧版发布目录里的 `Profiles` 会在启动时自动读取并迁移，新版不会再往 `.exe` 旁边新建这个目录。

## 开发环境

- Windows 10 / 11 x64
- .NET SDK 10.0 或更新版本
- Visual Studio / Rider / VS Code 均可

项目使用：

- WPF
- Windows Forms 屏幕信息
- Win32 `RegisterHotKey`
- Win32 low-level mouse hook
- Win32 `SendInput`
- `System.Drawing` GDI 截图

## 本地运行

```powershell
cd C:\Users\Legen\Desktop\LegendaryCSharp
dotnet run
```

## 编译检查

```powershell
cd C:\Users\Legen\Desktop\LegendaryCSharp
dotnet build
```

## 生成 exe

生成 Windows x64 单文件发行版：

```powershell
cd C:\Users\Legen\Desktop\LegendaryCSharp
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

生成后的 exe 在：

```text
C:\Users\Legen\Desktop\LegendaryCSharp\bin\Release\net10.0-windows\win-x64\publish\LegendaryCSharp.exe
```

发布目录中常见文件：

- `LegendaryCSharp.exe`：主程序。
- `LegendaryCSharp.settings.json`：通用配置。
- `LegendaryCSharp.image-recognition.json`：图像识别配置。
- `image-diagnostic.png`：点击“诊断”后生成的截图文件。

## 维护约定

程序内说明在 `UsageGuide.cs`。

修改功能、按钮、配置项、热键、触发逻辑或数据位置时，需要同步更新：

- `UsageGuide.cs`
- `README.md`

## v3.1.1 相对 AHK 旧版的核心变化

- 从脚本运行迁移为独立 C# 桌面程序。
- 从 AHK GUI 迁移为 WPF 界面。
- 从 INI 配置迁移为 JSON 配置。
- 从单文件脚本逻辑拆分为服务模块。
- 图像识别加入后台扫描、区域截图、诊断截图和 UI 降频。
- 档案迁移到 `%AppData%`，发布目录更干净。
- 说明书内置到程序窗口中，并要求后续功能变更同步维护。
