<div align=center>

<img src="./docs/images/logo.png" width="100" height="100" />

# PowerLink

[![Github](https://img.shields.io/badge/Github-obear997%2FPowerLink-242424?logo=github&labelColor=242424)](https://github.com/obear997/PowerLink)

Windows 上的 NTFS 链接工具 —— 用硬链接就地合并重复文件，在不复制数据的情况下克隆目录，创建目录联接（junction），并且全部可以从资源管理器里直接调用。

> **项目来源：** 本项目基于上游 [dawidope/PowerLink](https://github.com/dawidope/PowerLink) 改造扩展，集成了上游 **v0.5.2**（2026-06-17，提交 `3d77ea0`）的完整功能；并在此基础上做了中文本地化、Gitea Actions 发布流水线、窗口尺寸与构建兼容性等改动。

</div>

## 为什么做这个

我本地装了多个 AI Agent —— Claude、Codex、DeepSeek Harness、WorkBuddy、Hermes 等等。每个 Agent 都自带一套 skill，很多内容彼此重复，各自在目录下存一份，C 盘空间被一点点吃掉，最后慢慢就不够用了。

Windows 对此没有一等公民式的解决方案。符号链接需要管理员权限或开发者模式才能创建；目录联接只能作用于文件夹；写时复制只有 ReFS 才有。**NTFS 硬链接**正好能胜任 —— 同一份字节以多个名字出现，`File.Copy` 依然透明工作，打开文件的工具毫不知情 —— 但在 Windows 上用它实在太难受了。在管理员 `cmd` 里用 `mklink /H` 一次处理一个文件，根本算不上工作流。

我找过不少现成的工具，最后找到了 [PowerLink](https://github.com/dawidope/PowerLink)。它正好能解决我的问题，但上游是英文项目，而且有些功能不满足我的需求，所以我在上游 v0.5.2 的基础上做了中文本地化和自定义开发。

现在用法很直接：指向装满重复文件的文件夹，它找出重复项、替换成硬链接，磁盘占用缩小，所有 Agent 照常工作。同样的操作也接入了资源管理器，让普通的复制/粘贴/拖拽流程也能用上硬链接。

## 它能做什么

**去重（Deduplicate）。** 扫描一个或多个文件夹，找出内容完全相同的文件，并把重复项替换为 NTFS 硬链接。两个路径都继续可用，而数据只存在一份。典型场景：机器学习模型目录中，同一个 `.safetensors` 散落在十几个子文件夹里。

**克隆（Clone）。** 镜像一棵目录树 —— 目标位置中的每个文件都是指回源文件的硬链接，不额外占用任何字节。仅限同一卷（NTFS 硬链接的限制）。

**目录联接（Junction）。** 创建一个目录联接 —— 一个文件夹指针。联接文件夹出现在某个路径下，但内容来自目标位置。无需管理员权限，也不需要开发者模式（与符号链接不同）。适合在不移动数据的前提下把某个文件夹重定向到另一块磁盘。检查器会列出扫描树中已有的联接，并标记出悬空的联接。

**检查（Inspect）。** 只读扫描，列出文件夹里的每一个硬链接组**以及**每一个联接，包括指向扫描树之外的路径。在你删除自以为的“副本”之前，或者重命名一个被活动联接指向的文件夹之前，这个功能很有用。

**查看链接（Show links）。** 给定任意文件，枚举该卷上指向同一份数据的所有名称，把被遗忘的硬链接揪出来。

**一眼识别硬链接。** 可选的图标叠加处理程序会在任何硬链接文件（即磁盘上有多个名称的文件）的图标上绘制一个小角标。工作方式类似 OneDrive 或 Dropbox 的同步角标 —— 每个资源管理器视图里都能看到，不必打开任何工具。安装是可选的，且需要管理员权限，因为 Windows 从 HKLM 读取叠加处理程序。

以上所有功能都由一个 WinUI 3 应用和配套的 CLI 驱动。shell 扩展把同样的操作接入资源管理器 —— 经典右键菜单、拖放处理程序（现已归入统一的 “PowerLink” 子菜单）、Windows 11 顶部菜单，以及上面说的图标角标。

## 截图


| 去重                                           | 链接检查器                                       |
| ---------------------------------------------- | ------------------------------------------------ |
| ![Deduplicate page](docs/media/dedup-page.png) | ![Inspector page](docs/media/inspector-page.png) |


| 克隆                                     | 目录联接                                       |
| ---------------------------------------- | ---------------------------------------------- |
| ![Clone page](docs/media/clone-page.png) | ![Junction page](docs/media/junction-page.png) |


| 设置                                           |  |
| ---------------------------------------------- | - |
| ![Settings page](docs/media/settings-page.png) |  |

**资源管理器右键菜单** —— 左侧是经典右键菜单（Windows 11 上位于“显示更多选项”之下），右侧是 Windows 11 现代顶部菜单。两者都展开了 PowerLink 子菜单：


| 经典                                                         | Windows 11 现代                                          |
| ------------------------------------------------------------ | -------------------------------------------------------- |
| ![Classic context menu](docs/media/context-menu-classic.png) | ![Modern Win11 menu](docs/media/context-menu-modern.png) |

**图标角标** —— PowerLink 会在每个硬链接文件上画一个小红箭头，复用了 Windows 已经用于快捷方式和联接的形状，让这三个“此对象另有出处”的指示在视觉上保持一致。截图中从左到右：


| 角标                 | 含义                               | 绘制者        |
| -------------------- | ---------------------------------- | ------------- |
| 文件夹上的蓝色箭头   | 联接（目录重解析点）               | Windows       |
| 无角标               | 普通文件                           | —            |
| **文件上的红色箭头** | **硬链接**（`nNumberOfLinks > 1`） | **PowerLink** |
| 文件上的蓝色箭头     | 快捷方式（`.lnk`）                 | Windows       |

![Overlay badge](docs/media/overlay-badge.png)

## 下载

[Releases](https://gitea.obear.top/obear/PowerLink/releases) 页面上每个带 tag 的版本提供两种形式。两者都内置 .NET 8 运行时（无需单独安装），也都能在 设置 → 更新 里自动更新。

- **`PowerLink-win-Setup.exe`** —— 安装程序。按用户安装（无需管理员），创建开始菜单项，注册到“添加/删除程序”。约 80 MB。
- **`PowerLink-win-Portable.zip`** —— 解压到任意位置，运行 `PowerLink.exe`。在你选择启用 shell 集成之前不会留下任何注册表痕迹。约 80 MB。

两者都由 [Velopack](https://velopack.io) 构建，共用同一套自动更新机制 —— 应用检查 Gitea Releases，下载新包，然后自我重启。除了安装痕迹之外，Setup.exe 和 Portable.zip 完全可互换。

首次运行会触发 Windows SmartScreen（可执行文件尚未签名）—— 点击 **更多信息 → 仍要运行**。

每个版本的整理版发布说明见 [CHANGELOG.md](CHANGELOG.md) 以及各个 Gitea release 页面（workflow 会自动提取对应章节）。

## 快速开始

1. 下载 Setup.exe（直接运行）**或** Portable.zip（解压到任意位置，运行 `PowerLink.exe`）。
2. 选择一项操作：
   - **去重（Deduplicate）** —— 添加文件夹，按“扫描”（F5），查看分组，按“用硬链接替换重复文件”。
   - **克隆（Clone）** —— 选择源和目标，可选“试运行”，按“运行”。
   - **目录联接（Junction）** —— 选择目标文件夹，选择联接出现的父文件夹，点“创建联接”。
   - **检查（Inspect）** —— 添加文件夹，按“扫描”查看所有已有的硬链接组和联接。
3. 可选：打开 **设置**，安装 shell 集成（见下文[资源管理器集成](#资源管理器集成)）。

没有后台服务，在从设置里主动启用 shell 集成之前不会写注册表。Setup.exe 会把自己注册到“添加/删除程序”（按用户，无需管理员）；Portable.zip 除了你解压到的文件夹之外不留任何痕迹。

## 资源管理器集成

四个扩展入口默认全部关闭。每一个都是设置里独立的复选框，只装你需要的。

**经典右键菜单**（按用户，无需管理员）。向右键菜单添加最多八个 PowerLink 动作。在 Windows 11 上，除非你同时安装下面的现代菜单，否则它们出现在“显示更多选项”里。两种布局：

- **平铺（Flat）** —— 每个动作都是独立的顶层条目。
- **分组（Grouped）** —— 单一的 “PowerLink” 级联子菜单，内部只显示与当前所选类型相关的动作。

动作：选为链接源、在此创建硬链接、在此创建目录联接、显示硬链接、检查硬链接、去重文件夹、克隆文件夹（硬链接）、创建指向此文件夹的目录联接。

**拖放处理程序**（按用户，无需管理员）。右键拖动文件/文件夹到某个文件夹上再释放：一个 “PowerLink” 子菜单会级联出 “在此创建硬链接”（文件）、“在此克隆目录树（硬链接）”（文件夹）和 “在此创建目录联接”（文件夹）。多选和跨卷错误会被就地处理。

**Windows 11 现代菜单** —— *实验性*（按用户、无需管理员，但**需要开发者模式**）。在 Win11 右键菜单的顶部区域添加 “PowerLink” 子菜单，这样就不必再点“显示更多选项”。它使用未签名的稀疏 MSIX 包；之所以需要开发者模式，正是因为要这样才能在没有签名证书的情况下完成注册。

**叠加图标**（需要管理员）。为每个硬链接文件（`nNumberOfLinks > 1`）添加一个小角标。它注册在 HKLM 下，受 Windows 15 个叠加槽位的限制 —— 安装之前，PowerLink 会统计已有的处理程序数量，如果再加一个会超限就给出警告。优先级设得较低，因此 OneDrive / Dropbox / Git 的角标在它们同样声明拥有的文件上仍然优先。

## CLI

`PowerLink.Cli.exe` 随应用一同发布。每个 GUI 操作都有对应的 CLI 命令。


| 命令                                                             | 作用                                                                                                                                              |
| ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `scan <paths...> [--min-size N]`                                 | 空跑扫描，打印重复分组和浪费的字节数。                                                                                                            |
| `dedup <paths...> [--min-size N] [--execute] [--verify-content]` | 先扫描，（配合`--execute`）再把重复项替换为硬链接。`--verify-content` 会在每次删除前重新哈希两个文件（偏执模式；默认只在 mtime 触发时重新哈希）。 |
| `clone <source> <dest> [--dry-run]`                              | 把一个文件夹镜像到目标位置，或把单个文件链接进去。                                                                                                |
| `pick <path>`                                                    | 记住一个路径，作为之后`drop` 的源。                                                                                                               |
| `drop <target>`                                                  | 把已选取的源落地到`<target>`（文件 = 一个硬链接，文件夹 = 整棵树）。                                                                              |
| `drop-junction <target>`                                         | 与`drop` 类似，但把已选取的文件夹以联接形式落地到 `<target>`（仅限文件夹）。                                                                      |
| `junction create <link> <target> [--force]`                      | 创建目录联接。`--force` 允许悬空的目标。                                                                                                          |
| `junction info <path>`                                           | 打印联接的目标和状态。退出码`0` = 正常，`2` = 悬空，`1` = 不是联接 / 出错。                                                                       |
| `junction repair <path> <new-target> [--force]`                  | 就地重写现有联接的目标。                                                                                                                          |
| `junction delete <path>`                                         | 删除一个联接。目标目录**不会**被触碰；对非联接会拒绝执行。                                                                                        |
| `show-links <file>`                                              | 列出该卷上共享此文件数据的所有路径。                                                                                                              |
| `install-overlay <dll-path>`                                     | 在 HKLM 中注册叠加处理程序。**需要管理员。**                                                                                                      |
| `uninstall-overlay`                                              | 移除叠加处理程序。**需要管理员。**                                                                                                                |

含空格的路径请加引号。去重的最小文件大小默认为 1 MiB。进度输出到 stderr，因此 `dedup ... | tee log.txt` 的结构化 stdout 报告依然保持干净。

## 系统要求

- **Windows 10 版本 1809**（build 17763）或更高 —— 用于 GUI 应用和 CLI。
- **Windows 11 build 22000+** —— 用于现代顶部菜单（Windows 10 缺少相应 API）。
- **NTFS** —— 所有硬链接操作。FAT/exFAT 卷会被拒绝并给出友好的错误提示。
- **x64 或 ARM64** —— ARM64 可以编译，但**未在真实硬件上测试**过。见[已知限制](#已知限制)。
- **.NET 8 运行时** —— 已内置于 Setup.exe 和 Portable.zip，无需单独安装。
- **开发者模式** —— 仅实验性的现代菜单需要，因为稀疏 MSIX 包未签名。
- **管理员** —— 仅安装叠加图标时需要（写 HKLM）。

同卷约束适用于所有硬链接操作 —— NTFS 硬链接不能跨卷。

## 从源码构建

前置条件：

- Visual Studio 2022，需要安装 **Desktop development with C++** 工作负载（用于原生 shell 扩展）和 **.NET desktop development** 工作负载。
- Windows App SDK / WinUI 3 项目模板（随 C++ 和 .NET 工作负载一起提供）。

```powershell
git clone <repo-url>
cd PowerLink
dotnet build PowerLink.slnx -c Release
```

`PowerLink.App.csproj` 通过 MSBuild 在 `BeforeBuild` 阶段驱动 C++ shell 扩展的构建，因此一次 `dotnet build` 就能产出完整的一套：

```
src/PowerLink.App/bin/x64/Release/net8.0-windows10.0.19041.0/
  PowerLink.App.exe
  PowerLink.Cli.exe
  PowerLink.ShellExt.dll
  Assets/Icon.ico
  ...
```

运行测试：

```powershell
# .NET 测试（xUnit）
dotnet test tests/PowerLink.Core.Tests/PowerLink.Core.Tests.csproj

# 原生 shell-ext 工具测试（小型控制台 exe —— 退出码非零即失败）
msbuild tests/PowerLink.ShellExt.Tests/PowerLink.ShellExt.Tests.vcxproj -p:Configuration=Debug -p:Platform=x64
.\tests\PowerLink.ShellExt.Tests\x64\Debug\PowerLink.ShellExt.Tests.exe
```

需要加载原生 DLL 的集成测试，如果当前配置下没有构建 `PowerLink.ShellExt.dll`，会静默跳过。原生测试 exe 针对 `ShellExtUtils.cpp` 中抽出的辅助函数（FormatArgs、GetModuleDir、SafeDecrement、ClampedSkip、QueryContextMenuHResult）运行，同时也接入了 CI（`release.yml` 在任何发布步骤之前都会运行它）。

## 架构

```
src/
  PowerLink.Core/         — 文件扫描、哈希（XxHash128）、去重引擎、克隆引擎、Win32 硬链接封装
  PowerLink.App/          — WinUI 3 桌面应用（去重、克隆、目录联接、检查器、设置页面、Velopack 更新器）
  PowerLink.Cli/          — 命令行接口 + 需要提权的叠加图标安装器
  PowerLink.ShellExt/     — 原生 C++ COM DLL：叠加处理程序、拖放处理程序、Win11 现代菜单命令，以及
                            `ShellExtUtils.{h,cpp}` 中可测试的辅助函数（FormatArgs、GetModuleDir 等）
tests/
  PowerLink.Core.Tests/   — 针对 C# 引擎的 xUnit 单元 + 集成测试（写作时约 85 个）
  PowerLink.ShellExt.Tests/ — 不依赖任何测试框架的 C++ 控制台 exe，用来检验 ShellExt 的辅助函数
```

应用是**单实例、多窗口**的：第二次启动 `PowerLink.App.exe` 会聚焦已存在的窗口，但携带目标路径的 shell 动作总是在新窗口中打开，这样你可以同时并排使用一个去重窗口和一个检查器窗口。

去重算法：枚举 → 按 `(volume, size)` 分组 → 4 KiB 前缀 XxHash128 → 重新分组 → 全文件 XxHash128 → 按可回收字节数排序得到最终分组。每组中保留的规范路径（canonical）是该组内 NTFS 文件索引最小的那个，因此对一棵已经去重过的目录树重复运行是无操作，而不是重新洗牌。

替换操作对部分失败是原子的：每个重复文件先被重命名为同级的 `.pl-stage-<guid>`，然后在原路径创建硬链接，最后删除暂存文件。如果 `CreateHardLink` 失败，暂存文件会被改回原名，用户文件绝不会丢失。如果连恢复也失败，执行器会连同暂存路径一起上报，以便手动恢复数据。

---

## 技术栈


| 层面       | 技术                                                                                  |
| ---------- | ------------------------------------------------------------------------------------- |
| 桌面应用   | WinUI 3 / Windows App SDK 1.7.250606001，.NET 8，C# 13（preview）                     |
| MVVM       | CommunityToolkit.Mvvm 8.4.2（源生成器 + partial properties）                          |
| CLI        | .NET 8 + System.CommandLine 3.0.0-preview                                             |
| 核心引擎   | .NET 8；System.IO.Hashing（XxHash128）；Microsoft.Extensions.Logging.Abstractions     |
| Shell 扩展 | 原生 C++ COM DLL（overlay / 拖放 / Win11 现代菜单），MSBuild（vcxproj）               |
| 打包与更新 | Velopack 0.0.1298（Setup.exe / Portable.zip / 自动更新），更新源为 Gitea Releases     |
| 测试       | xUnit 2.5.3 + Microsoft.NET.Test.Sdk（Core）；C++ 控制台测试 exe（ShellExt 工具函数） |
| CI / 发布  | Gitea Actions（`.gitea/workflows/release.yml`），tag `v*` 触发                        |
| 目标平台   | Windows 10 1809+（build 17763）/ Windows 11 22000+；x64、ARM64（仅编译）              |

> 依赖版本以各 `.csproj` / `.vcxproj` 为准；如与实际不一致，以代码与工程文件为准并同步更新本表。

---

## 已知限制

- **未签名。** shell 扩展 DLL 和稀疏 MSIX 包都未签名。Windows 会容忍叠加处理程序和拖放处理程序；现代菜单包则需要开发者模式。
- **ARM64 仅能编译。** vcxproj 里有 ARM64 配置，构建图也做了参数化，但没人在 ARM64 机器上跑过产物。预计会有毛刺。
- **15 个叠加槽位。** Windows 只按字母序加载前 15 个叠加处理程序。PowerLink 用一个带前导空格的名字注册以便排在最前，并在你的系统已有 14 个以上处理程序时先请求确认再安装。
- **不支持：** 符号链接、跨卷硬链接、ReFS。支持联接，但不支持 Smart Move（目标重命名时自动更新联接）—— 把目标从联接底下移走会让联接悬空，检查器会以警告形式呈现。
- **去重的校验默认信任 mtime。** 从扫描到执行之间，每个操作都会做分级校验：硬身份校验（大小、NTFS 文件索引、卷序列号 —— 用来捕获文件被替换、或从我们脚下被移走的情况），再加上软性的 mtime 检查，只要任一文件在扫描后被改动过就触发完整内容重新哈希。常见情况（没有并发写入）下，每个文件只花一次 `GetFileInformationByHandle`。如果你不信任 mtime —— 例如同步工具在写完之后把它恢复了 —— 在去重页面勾选 “删除前重新校验文件内容”（或给 CLI 传 `--verify-content`），强制对每个操作重新哈希。否则，一次隐蔽的就地重写（同时还恢复了原始 mtime）就会蒙混过关。
- **尚未进入 PowerToys。** 长期目标是以 PowerToys 模块的形式落地（见 [#2527](https://github.com/microsoft/PowerToys/issues/2527) 及其同伴）。目前它是一个独立项目，但使用同一套技术栈（WinUI 3 + C++/COM + .NET 8），以便日后迁移顺理成章。


## 许可证

MIT —— 见 [LICENSE](LICENSE)。版权所有 (c) 2026 Dawid Wenderski。
