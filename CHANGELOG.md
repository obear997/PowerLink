# 更新日志

本项目的版本变更记录。条目大体遵循 [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)。
发布工作流会提取与所推送 tag 匹配的章节（例如 `v0.0.1` → `## [0.0.1]`），
用作 Gitea Release 的正文。

## [0.0.1] — 2026-11-20

首个自维护版本，基于上游 [dawidope/PowerLink](https://github.com/dawidope/PowerLink)
**v0.5.2**（2026-06-17，提交 `3d77ea0`）。上游的硬链接去重、目录克隆、目录联接、
链接检查、资源管理器集成与 CLI 等功能全部保留，并在此基础上做了以下改动。

### 新增

- **界面中文化。** WinUI 页面、ViewModel 状态文案、服务提示，以及原生 shell
  扩展的右键菜单和错误信息全部改为简体中文；应用默认语言设为 `zh-CN`。
- **Gitea 发布流水线。** 用 `.gitea/workflows/release.yml` 替换原 GitHub Actions
  工作流，通过 Gitea REST API 创建 Release 并上传 `Setup.exe` / `Portable.zip` /
  `.nupkg` 等产物；应用内更新源改为 Velopack `GiteaSource`。
- **窗口尺寸固定。** 主窗口默认以 1200×800 逻辑像素打开，并限制最小尺寸
  （按显示器 DPI 缩放），避免在不同显示器和缩放下窗口过小。

### 修复

- **中文系统下 ShellExt 编译失败。** C++ 源文件是无 BOM 的 UTF-8 且含中文字面量，
  未加 `/utf-8` 时 MSVC 按系统代码页（CP936）解析会报 C2001「常量中有换行符」；
  已为 ShellExt 工程添加 `/utf-8` 编译选项。
- **本地打包发现的前置问题。**
  - 增加 `-p:SkipShellExt=true`，使没有 VS C++ 工具链时也能构建并打包应用。
  - CI 改用 .NET 9 SDK：ViewModel 使用 C# 13 partial properties，
    .NET 8 SDK 的 Roslyn 会报 CS9248。
  - `vpk pack` 显式传入 `--runtime win-x64` 和 `--icon`，避免默认按 x86 打包、
    安装到 Program Files (x86)。

### 文档与仓库

- README 与 CHANGELOG 中文化，并在 README 中补充项目来源与上游版本说明。
- 新增 `AGENTS.md` / `CLAUDE.md` 项目开发规范。
- `.gitignore` 忽略 `velopack-output/` 和本地 `.tools/` 目录。
