# 更新日志

精选的、面向用户的发布说明。条目大致遵循 [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)。
发布工作流会提取与所推 tag 匹配的章节（例如 `v0.4.0` → `## [0.4.0]`），用作 GitHub Release 的正文。
自动生成的 PR/提交列表仍会显示在发布页面的下方。

## [0.5.2] — 2026-06-17

### 修复

- **应用无需单独安装 Windows App Runtime 即可启动。** Portable zip 和 Setup 之前都是按依赖
  Windows App SDK 框架的方式构建的 —— `SelfContained=true` 只打包了 .NET 运行时，所以启动时
  引导程序会要求系统范围安装 “Windows App Runtime 1.7”（MSIX >= `7000.522.1444.0`），未安装时、
  或只装了更旧的 1.7 维护版本时，就会以 *“Required components of the Windows App Runtime are
  missing”* 中止。现在构建改为 `WindowsAppSDKSelfContained`，Windows App Runtime 随应用目录
  一起分发，PowerLink 开箱即用，无需再安装任何东西。代价是 Portable zip 和 Setup 体积增大约
  30–40 MB。

## [0.5.1] — 2026-06-02

### 修复（发布流水线）

- **发布重新带上增量包。** `vpk download github` 步骤把上一个发布的 `*-full.nupkg` 拉取到了
  vpk 的默认目录，而 `vpk pack --outputDir velopack-output` 只扫描 `velopack-output`。因此 pack
  永远看不到上一个发布，每次构建都只产全量包、没有 delta —— 用户每次更新都得重新下载整个应用。
  现在下载步骤写入 `--outputDir velopack-output`，pack 就能找到之前的 `*-full.nupkg`，并计算出
  正确的 `*-delta.nupkg`。

## [0.5.0] — 2026-04-20

首个端到端支持**目录联接（junction）**的版本。junction 是 NTFS 的文件夹指针 —— 用途与目录符号
链接相同，但与符号链接不同的是，它不需要管理员权限，也不需要开发者模式。它是“让某个文件夹同时
出现在本机另一条路径上”的正确工具，现在已成为 PowerLink 的一等公民。

### 新增

- **目录联接页面**（侧边栏新增，位于“克隆”与“检查器”之间）。选择目标文件夹、选择联接出现的
  父文件夹、点击创建。链接名默认取自目标的文件夹名，可编辑。可选的“允许目标缺失”开关用于有意
  制造的悬空联接（部署脚本场景）。
- **检查器现在会连同硬链接一起列出目录联接。** 两个上下排列的区块 —— `Hardlinks (N)` 和
  `Junctions (N, X dangling)` —— 都可展开。每条联接显示链接路径、目标路径和状态（正常 / 悬空）。
  每行支持修复（重新选择目标）和删除（只删除重解析点 —— 绝不触碰目标）。
- **三个菜单入口都支持目录联接的 Explorer 集成：**
  - 文件夹的**经典右键菜单**：“PowerLink: Create junction pointing at this folder”
    （打开应用并预填）。
  - 已选取源时对文件夹的**经典右键菜单**：“PowerLink: Drop as junction here”
    （与 Drop-as-hardlink 对称）。
  - **右键拖动**文件夹到另一文件夹：在单个 “PowerLink” 子菜单下，“Junction here” 与
    “Clone tree here (hardlinks)” 并列。右键拖动文件：只有 “Hardlink here”。子菜单把所有操作
    归到一个父项下，使拖放菜单在 Copy/Move/Create shortcut 旁边不至于臃肿。
  - **Win11 现代菜单**（稀疏 MSIX）：级联菜单中新增 “Drop as junction here” 和
    “Create junction pointing at this...”。
- **CLI**：`powerlink junction {create|info|delete|repair}` 与
  `powerlink drop-junction <target>`。`info` 退出码 0（正常）、2（悬空）、1（错误）。`delete`
  会拒绝非联接对象，防止误把真实目录 rmdir 掉。`repair` 原子地覆盖重解析数据。

### 变更

- **拖放菜单改为 “PowerLink” 子菜单**，不再是两个顶层条目。单一的 “PowerLink >” 会按当前
  选择级联出适用的操作。
- **检查器页面在内部更名为 “Link inspector”** —— 标题和说明现在同时涵盖硬链接与目录联接。
  侧边栏条目仍叫 “Inspector”，不破坏肌肉记忆。
- **设置 → Explorer 右键菜单**的文案提及目录联接；拖放说明列出了全部三种拖放操作。

### 修复（安全关键）

- **`scan` / `clone` / `dedup` 继续默认跳过重解析点 —— 现在由测试锁定。** 之前的版本恰好行为
  正确，因为 `EnumerationOptions.AttributesToSkip = ReparsePoint` 本来就已设置，但没有任何东西
  防止这个标志被随手删掉。现在有三个回归测试覆盖：自引用的联接环不会让扫描死锁；扫描树内部的
  联接不会被静默跟随；clone 绝不会硬链接通过联接到达的内容（否则后续 dedup 会把联接目标改写成
  指向另一份副本的硬链接 —— 静默的语义损坏）。

### 内部

- **`Win32Junction`** 封装：用 DeviceIoControl FSCTL_SET/GET_REPARSE_POINT 手工构造
  `IO_REPARSE_TAG_MOUNT_POINT` 缓冲区布局，替代名使用 NT 路径 `\??\` 前缀，支持悬空目标检测、
  长路径，拒绝 UNC 目标（联接无法解析网络路径）。19 个 xUnit 测试覆盖正常/边界/错误场景。
- **`JunctionScanner`**：手工递归遍历，记录重解析点目录但不进入。4 个测试验证环路安全与
  悬空检测。
- **`QueryContextMenuHResult`** 辅助函数抽到 ShellExtUtils，并在无框架依赖的测试 exe 中新增
  5 个 C++ 测试 —— 锁定拖放处理器中新增 `CMD_JUNCTION = 2` 偏移所依赖的“最大偏移 + 1，而非
  数量”契约。

## [0.4.2] — 2026-04-20

真正修复 v0.4.1 漏掉的 “Clone tree here” 问题。

### 修复

- **“Clone tree here” 现在真的能用了。** 7-Zip 对话框并不是 v0.4.1 回滚时以为的 v0.4.0 回归
  —— 它是 `PowerLinkDropHandler::QueryContextMenu` 里一个早已存在的 bug。处理器返回的是添加的
  条目*数量*（按那个看起来显然、实则错误的读法），而不是文档规定的*实际用到的最大命令 ID 偏移
  加一*。文件夹拖放时唯一插入的条目在偏移 1（CMD_CLONE），但我们返回的是 1 而不是 2。于是
  Windows 把下一个链式拖放处理器的 `idCmdFirst` 加了 1 —— 我们的 id 4097 与下一个处理器的
  4097 撞车。用户看到的是 “PowerLink: Clone tree here”，点击却被路由到后来抢到同一 id 的
  处理器（报告者机器上是 7-Zip）。现在我们记录实际用到的最大偏移，并返回它加一。

  文件拖放时的硬链接操作碰巧是好的，因为 CMD_HARDLINK 的偏移是 0，那种情况下
  `count==1==max+1` —— 错误公式偶然算出了正确数字。

### 新增（内部）

- DropHandler 现在把 Debug 构建的诊断跟踪写入 `%TEMP%\powerlink-menu.log`（与
  ModernMenuCommand 用的是同一个文件），带 `[drop]` 标签。对 Release 构建无影响 —— 完全是
  空操作。

## [0.4.1] — 2026-04-20

热修复。

### 修复

- **“Clone tree here” 不再弹出无关的应用对话框。** v0.4.0 把这个拖放动词改走
  `cmd /c start "" /b "<cli>" ...`，为一个实际上并不适用于经典拖放处理器（宿主在
  Explorer.exe，而不是 `dllhost.exe`）的 MSIX 代理场景添加进程 breakaway。至少在某一种配置下，
  `start` 这层间接调用落到了 ShellExecute 上，弹出的却是 7-Zip 对话框而不是启动
  `PowerLink.Cli`。现在回退到该动词一直到 v0.3.x 都在用的直接 `CreateProcessW` 调用。

## [0.4.0] — 2026-04-20

由多组件代码审查驱动的一轮清理。大部分工作是内部加固 —— 终端用户能感觉到的是：出问题时静默
失败变少了。

### 修复（安全关键）

- **硬链接步骤失败时，去重不再丢文件。** 之前的“先删后链”序列在 `CreateHardLink` 恰好失败时
  （杀毒软件占用文件、权限拒绝等）会永久删除一份重复文件。现在改为：先重命名到暂存位、创建
  硬链接、再删除暂存文件；失败时自动恢复原文件。如果连恢复都失败，暂存文件的路径会被记录并
  展示出来，以便手动找回数据。

### 修复（Explorer 集成）

- **长路径不再让现代右键菜单操作失效。** 超过约 316 个字符的路径以前会被静默截断，丢掉启动
  命令的收尾引号，产生被接收方 exe 错误解析的畸形命令行。
- **“Clone tree here” 在使用现代 AppX 注册的机器上可用。** 拖放 CMD_CLONE 路径缺少桌面应用
  breakaway 属性，导致派生的 `PowerLink.Cli` 继承了包标识并以 `0x80070032` 退出。两个菜单
  入口现在共用同一个启动器。
- **持锁期间 `DllCanUnloadNow` 不再报告可卸载。** 有缺陷的 COM 客户端做了一次不平衡的
  `LockServer(FALSE)`，会把全局引用计数压到零以下（有符号下溢）。现在递减以零为下限。
- **能检测过长的安装路径。** `GetModuleFileNameW` 截断时现在会用不断增大的缓冲区重试，而不是
  把截断的路径当成成功。

### 修复（CLI）

- **进度输出改走 stderr** —— `dedup ... | tee log.txt` 不再把数百条进度刷新与结构化 stdout
  报告交织在一起。即使 stdout 被重定向，终端里仍然能看到实时 spinner。
- **`scan`、`dedup`、`clone` 现在打印一行错误**，而不是在引擎抛出未处理异常时输出原始堆栈。
  退出码为 `1`，Ctrl+C 时为 `130`。
- **`drop` 在所选源是目录时会显示进度。** 之前它会静默阻塞直到整个克隆完成。

### 修复（安装 / 更新）

- **安装后/更新后的 shell 扩展联接设置对部分失败更健壮。** 如果稳定 shell 路径上已存在一个
  真实（非联接）目录，现在会给出描述性错误，而不是一个让 Velopack 安装钩子静默中止的
  `IOException`。钩子异常会被捕获并写入 `%LocalAppData%\PowerLink\shell-junction-error.log`，
  而不是杀死安装。
- **冷启动会重新确保联接存在。** 普通启动（既非首次安装、也非更新后）以前会跳过联接设置。
  如果联接被外部删除，shell 扩展就会静默失效，直到下一次更新。
- **修复 CTS 句柄泄漏。** 应用内反复的 scan/execute/run 循环会让上一个
  `CancellationTokenSource` 成为孤儿；现在会在分配新的之前释放旧实例。

### 修复（发布流水线）

- **`vpk download` 失败不再掩盖“发出损坏 delta”的问题。** 之前“吞掉任何非零退出码”的模式，
  会在认证/网络错误时产出一个缺失或损坏 delta nupkg 的发布。现在工作流先用 `gh` 预检查是否
  存在历史发布，并在预期应当成功时把 vpk 下载失败当作硬错误。

### 新增（内部）

- **C++ 工具测试 exe** 位于 `tests/PowerLink.ShellExt.Tests`。在 CI 中先于其他构建步骤运行；
  非零退出即让工作流失败。
- **通过本 CHANGELOG.md 提供精选发布说明。** 工作流提取与所推 tag 匹配的章节，用作 GitHub
  Release 的正文。

---

更早的版本（v0.3.x 及以下）由提交历史在各自的 GitHub 发布页上自动生成。
