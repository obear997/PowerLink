# AGENTS.md

本文档为 Agent 在此仓库中工作时提供指导。

## 项目身份

**PowerLink** —— Windows 上的 NTFS 链接工具。用硬链接就地合并重复文件、在不复制数据的前提下克隆目录、创建目录联接（junction），并全量接入资源管理器右键菜单。桌面 UI（WinUI 3）+ 配套 CLI + 原生 C/C++ shell 扩展三件套。

核心定位：一个 **NTFS 链接工作流工具**。数据只存一份，多个路径同时指向同一份字节；`File.Copy` 等常规 API 透明工作。典型场景：本地 AI 模型目录里大量重复的 `.safetensors` 配套文件。

**当前阶段：pre-release。** Setup.exe + Portable.zip 走 Velopack 发布，尚未代码签名（首跑会触发 SmartScreen）。详见 [README.md](README.md) 的「已知限制」。

## 技术栈（锁定）

| 层面      | 技术                                      |
| --------- | ----------------------------------------- |
| 桌面 UI   | WinUI 3 / Windows App SDK 1.7.x，.NET 8    |
| UI 层框架 | CommunityToolkit.Mvvm 8.4.x（MVVM + RelayCommand）|
| CLI       | .NET 8 + System.CommandLine（preview）     |
| 核心引擎  | .NET 8；`System.IO.Hashing`（XxHash128）；`Microsoft.Extensions.Logging.Abstractions` |
| Shell 扩展| 原生 C++ COM DLL（overlay / 拖放 / Win11 现代菜单），自研 `ShellExtUtils.cpp` 可测工具函数 |
| 打包发布  | Velopack 0.0.1298；MSIX 稀疏包（现代菜单实验性功能）|
| 测试      | xUnit 2.5.3（Core）；框架无关 C++ 控制台 exe（ShellExt 工具函数）|
| CI        | Gitea Actions（`.gitea/workflows/release.yml`，tag `v*` 触发）|

> 目标框架：App `net8.0-windows10.0.19041.0`，平台 x64 / ARM64（ARM64 仅编译，未真机验证）。Core/CLI 为 `net8.0`。依赖以各 `.csproj` 为准，如与实际不一致以实际代码为准并同步更新本表。

## 核心原则（必须遵守）

1. **数据安全第一**：替换重复文件涉及删除操作，任何失败路径都不能丢用户数据。替换采用「先改名 staging → 建硬链接 → 删 staging」的原子步骤，失败必须回滚；回滚也失败时把 staging 路径上报给用户手动恢复。
2. **同卷约束**：NTFS 硬链接不能跨卷。所有链接操作前检查源/目标是否同一卷，FAT/exFAT 卷要给出友好错误，不要硬碰。
3. **最小权限**：shell 集成严格区分 HKCU（按用户，免管理员）与 HKLM（需提权，如 overlay 图标）。默认不写注册表，用户从设置页明确勾选后才写。
4. **默认保守**：去重这类改写操作默认空跑（dry-run / 先扫描），用户确认后才执行。`--verify-content` 之外的路径走分层校验（硬身份 + mtime），不要悄悄放松。
5. **UI 文案统一中文**：新增/修改的界面文案使用简体中文，术语与 README 保持一致（硬链接、目录联接、去重、克隆、检查器）。
6. **不泄露凭据**：CI/发布脚本中的 token 走 Gitea Secrets / 环境变量，不得硬编码。

## 架构硬约束（强制执行）

1. **App / Core / ShellExt 三层分离**

   - Why：Core 无 UI 依赖、可独立测试；ShellExt 是原生 C++；App 只是壳。
   - How to apply：业务逻辑放进 Core，ViewModels 只负责绑定与编排，严禁把文件系统操作塞进 XAML code-behind。

2. **界面文案中文化**

   - Why：产品面向中文用户；文案散落在 XAML 与 ViewModel 中，需保持一致。
   - How to apply：XAML 直接写中文 `Text`/`Content`/`Header`/`ToolTipService.ToolTip`；C# 侧动态状态文案也写中文，数字/单位格式化保持 `N0`、`F1` 等既有写法。若后续要支持多语言，再统一迁移到 `Strings/<lang>/Resources.resw` + `x:Uid`，不要在未迁移时混用两种机制。

3. **shell 扩展的注册表路径不得漂移**

   - Why：`ShellExtensionService.cs` 与 `PowerLink.Cli` 的 `OverlayInstaller`、`DropHandler.h` 里的 CLSID 必须一致，漂移会导致注册了却找不到 DLL。
   - How to apply：改 CLSID / 注册名先核对四处副本（`ShellExtensionService.cs` 常量、`OverlayInstaller`、`DropHandler.h`、`ModernMenuCommand`）。

4. **新增 CLI 命令必须三处同步**

   - Why：README 的 CLI 表、`PowerLink.Cli` 的实现、App 内 Settings 对 `install-overlay` / `uninstall-overlay` 的调用。
   - How to apply：加命令时同步更新 README 与调用点。

5. **同卷硬链接 + 目录联接的差异要分清楚**

   - 硬链接 = 同一文件多个名字，**不能跨卷**；联接 = 目录指针，**可跨本地卷**、可指向空目标（`--force`）。别把两者混在一个校验里。

6. **测试脚本 / 一次性调试产物用完即删**

   - 见「核心规则」第 2 条。`tools/` 下保留的是可复用生成器（如 `generate-overlay-icon.py`），不是临时产物。

## 仓库结构（已落地）

```
PowerLink/
│
├── src/
│   ├── PowerLink.App/        # WinUI 3 桌面应用
│   │   ├── Pages/            # 五个页面：去重 / 克隆 / 目录联接 / 检查器 / 设置
│   │   ├── ViewModels/       # MVVM 视图模型（CommunityToolkit.Mvvm）
│   │   ├── Services/         # Picker / Update / ShellExtension / ModernMenu / Velopack shim
│   │   └── Converters/       # BytesToHuman / Log2BufferSize
│   ├── PowerLink.Cli/        # 命令行接口 + 提权 overlay 安装器（System.CommandLine）
│   ├── PowerLink.Core/       # 核心引擎：Scanning / Dedup / Clone / Native / Models / State（无 UI 依赖）
│   └── PowerLink.ShellExt/   # 原生 C++ COM DLL：overlay / 拖放 / Win11 现代菜单 + ShellExtUtils
│
├── tests/
│   ├── PowerLink.Core.Tests/      # xUnit 单元 + 集成测试（~85）
│   └── PowerLink.ShellExt.Tests/  # 无框架 C++ 控制台 exe，测 ShellExtUtils 工具函数
│
├── docs/media/              # README 截图
├── tools/                   # 可复用生成脚本（如 overlay 图标生成器）
├── .gitea/workflows/        # release.yml（tag 触发构建 + 发布）
├── PowerLink.slnx           # 解决方案（单文件 sln）
└── README.md / CHANGELOG.md / LICENSE
```

## 构建与测试

```powershell
# 全量构建（App 的 BeforeBuild 会自动调 MSBuild 编 C++ shell 扩展）
dotnet build PowerLink.slnx -c Release

# .NET 单元测试
dotnet test tests/PowerLink.Core.Tests/PowerLink.Core.Tests.csproj

# 原生 shell-ext 工具测试（退出码非零 = 失败）
msbuild tests/PowerLink.ShellExt.Tests/PowerLink.ShellExt.Tests.vcxproj -p:Configuration=Debug -p:Platform=x64
.\tests\PowerLink.ShellExt.Tests\x64\Debug\PowerLink.ShellExt.Tests.exe
```

> 依赖原生 `PowerLink.ShellExt.dll` 的集成测试，在未构建该 DLL 时静默跳过。

## 发布流程

1. tag 用 `v*`（如 `v0.0.1`），推送到 `origin/main`，Gitea Actions `release.yml` 自动：装 .NET 9 SDK → 构建 + 跑 ShellExt 测试 → `dotnet publish` App 与 Cli → Velopack 打包（默认全量包）→ 通过 Gitea REST API 创建 release 并上传 `Setup.exe` / `Portable.zip` / `.nupkg` / `releases.win.json` / `assets.win.json`。
2. 发布说明从 `CHANGELOG.md` 中按版本号自动摘取对应 `##` 小节。
3. 打 tag 前先在 `CHANGELOG.md` 更新相应版本条目。
4. 运行器标签 `windows-latest` 需与自建 Gitea runner 的标签一致；自动令牌走 `secrets.GITHUB_TOKEN`（act_runner 注入）。

## 核心规则

1. **专注任务**：不做与任务无关的任何修改，不捎带重构或机会主义清理。
2. **清理临时文件**：任务产生的临时文件（测试脚本、草稿、调试输出）完成后立即删除。
3. **先读后改**：编辑前先阅读相关代码/测试/文档，不要只凭文件名猜测。
4. **最小必要改动**：采用最小必要改动，不过度设计、不猜测性抽象、不静默移除已有行为。
5. **格式化是交付前置条件**：C# / XAML 修改后保持工程内既有风格；C++ 遵循现有命名约定（CamelCase 成员、`{` 新行）。
6. **不泄露凭据**：不得在任何输出或持久化内容中硬编码 API key、token、数据库连接串等敏感信息，统一走环境变量/配置文件。

## Git 规范

- **远程仓库**：`https://gitea.obear.top/obear/PowerLink.git`
- **分支策略**：`main`（单一主分支，直接推送；发布靠 tag 触发 CI）
- **提交规范**：Conventional Commits —— `feat(module):`、`fix(module):`、`docs:`、`chore:`、`refactor:`
- **提交前 Code Review**：每次 Git 提交前必须先完成一次针对全部变更的 Code Review，重点检查业务逻辑漏洞、异常处理、空值/权限/并发等边界条件，以及敏感信息泄露、输入校验等安全风险；发现问题须修复并复审后方可提交。
- **不提交敏感信息**：Gitea token、CI Secrets、本地路径等不得写死在代码/脚本里。

## 文档维护

- 本项目的统一规范源文件为 `AGENTS.md`，`CLAUDE.md` 仅作为入口指向本文件。
- 当用户要求更新、添加或修剪项目的开发规范/配置/指令时，**只能修改 `AGENTS.md`**，不要直接修改 `CLAUDE.md`。
- 进入开发后，技术栈表、仓库结构、CLI 命令表等若与实际落地不一致，须在首次触及对应内容时同步更新本文件与 [README.md](README.md)。
