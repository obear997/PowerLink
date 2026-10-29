using System.Diagnostics;
using Microsoft.Win32;

namespace PowerLink.App.Services;

public enum ShellVerbTarget { Cli, App }

public enum ShellVerbKind { ContextMenu, OverlayHandler, DropHandler, ModernMenu }

public enum ContextMenuLayout { Flat, Grouped }

public sealed record ShellVerbKey(string RelativeKeyPath, string CommandArgs);

public sealed record ShellVerb
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public required string TargetsText { get; init; }
    public required ShellVerbTarget Executable { get; init; }
    public required IReadOnlyList<ShellVerbKey> Keys { get; init; }
    public ShellVerbKind Kind { get; init; } = ShellVerbKind.ContextMenu;
    public bool RequiresElevation { get; init; }
}

public static class ShellExtensionService
{
    // Context-menu registrations live under HKCU so no admin elevation is needed.
    // Overlay-handler registrations live under HKLM (Windows reads overlays
    // only from HKLM) — those go through PowerLink.Cli with UAC elevation.
    private const string ClassesRoot = @"Software\Classes";

    // Mirrors PowerLink.Cli.OverlayInstaller — keep in sync.
    private const string OverlayClsid = "{8E62D9DE-27D3-4C1E-8A55-CADF97D3EB20}";
    private const string OverlayRegName = " PowerLinkHardlink";
    private const string OverlayRoot =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers";

    // Mirrors CLSID_PowerLinkDropHandler in DropHandler.h.
    private const string DropClsid = "{4BA9E8F3-7D1A-4E6C-9E3B-8F2D7C4A1B59}";
    private const string DropRegName = "PowerLinkDrop";
    private const string DropDescription = "PowerLink Drop Handler";

    // Preferences key for things like the chosen context-menu layout.
    private const string PrefsKey = @"Software\PowerLink";

    // Grouped layout: each Explorer target type gets its own submenu store under
    // HKCU\Software\Classes (mirror of HKCR) so ExtendedSubCommandsKey can point
    // at it. Keeping separate stores per target lets us show only the verbs that
    // actually apply to that context without any dynamic filtering.
    private static readonly (string Target, string Store)[] GroupedStores =
    {
        (@"*",                    "PowerLinkSubmenu.File"),
        (@"Folder",               "PowerLinkSubmenu.Folder"),
        (@"Directory\Background", "PowerLinkSubmenu.Background"),
    };

    public static IReadOnlyList<ShellVerb> AllVerbs { get; } = new[]
    {
        new ShellVerb
        {
            Id = "PowerLinkPick",
            Label = "PowerLink: 选为链接源",
            Description = "记住此路径，作为之后“在此创建硬链接”的源。两者必须在同一 NTFS 卷上。",
            TargetsText = "文件、文件夹",
            Executable = ShellVerbTarget.Cli,
            Keys = new[]
            {
                new ShellVerbKey(@"*\shell\PowerLinkPick", "pick \"%1\""),
                new ShellVerbKey(@"Folder\shell\PowerLinkPick", "pick \"%1\""),
            },
        },
        new ShellVerb
        {
            Id = "PowerLinkDrop",
            Label = "PowerLink: 在此创建硬链接",
            Description = "从已选取的源创建硬链接（文件）或硬链接树（文件夹）。共享数据 —— 不是复制，也不是符号链接。",
            TargetsText = "文件夹、文件夹背景",
            Executable = ShellVerbTarget.Cli,
            Keys = new[]
            {
                new ShellVerbKey(@"Folder\shell\PowerLinkDrop", "drop \"%1\""),
                new ShellVerbKey(@"Directory\Background\shell\PowerLinkDrop", "drop \"%V\""),
            },
        },
        new ShellVerb
        {
            Id = "PowerLinkDropJunction",
            Label = "PowerLink: 在此创建目录联接",
            Description = "在此创建指向已选取文件夹的目录联接。仅限文件夹源（联接不适用于文件）。无需管理员；删除联接不会影响目标。",
            TargetsText = "文件夹、文件夹背景",
            Executable = ShellVerbTarget.Cli,
            Keys = new[]
            {
                new ShellVerbKey(@"Folder\shell\PowerLinkDropJunction", "drop-junction \"%1\""),
                new ShellVerbKey(@"Directory\Background\shell\PowerLinkDropJunction", "drop-junction \"%V\""),
            },
        },
        new ShellVerb
        {
            Id = "PowerLinkShowLinks",
            Label = "PowerLink: 显示硬链接",
            Description = "显示此卷上指向该文件数据的所有路径。快速对话框，无需扫描。",
            TargetsText = "文件",
            Executable = ShellVerbTarget.Cli,
            Keys = new[]
            {
                new ShellVerbKey(@"*\shell\PowerLinkShowLinks", "show-links \"%1\""),
            },
        },
        new ShellVerb
        {
            Id = "PowerLinkInspect",
            Label = "PowerLink: 检查硬链接",
            Description = "用此文件夹打开 PowerLink 检查器 —— 找出其中已是硬链接的文件并显示所有同级路径。",
            TargetsText = "文件夹、文件夹背景",
            Executable = ShellVerbTarget.App,
            Keys = new[]
            {
                new ShellVerbKey(@"Folder\shell\PowerLinkInspect", "--inspect \"%1\""),
                new ShellVerbKey(@"Directory\Background\shell\PowerLinkInspect", "--inspect \"%V\""),
            },
        },
        new ShellVerb
        {
            Id = "PowerLinkDedup",
            Label = "PowerLink: 去重文件夹",
            Description = "用此文件夹打开 PowerLink 去重页面。找出完全相同的文件并用硬链接替换重复项以回收磁盘空间。",
            TargetsText = "文件夹、文件夹背景",
            Executable = ShellVerbTarget.App,
            Keys = new[]
            {
                new ShellVerbKey(@"Folder\shell\PowerLinkDedup", "--dedup \"%1\""),
                new ShellVerbKey(@"Directory\Background\shell\PowerLinkDedup", "--dedup \"%V\""),
            },
        },
        new ShellVerb
        {
            Id = "PowerLinkClone",
            Label = "PowerLink: 克隆文件夹（硬链接）",
            Description = "以该文件夹为源打开 PowerLink 克隆页面。把目录树镜像为硬链接 —— 不是复制，不额外占用磁盘，仅限同一卷。",
            TargetsText = "文件夹",
            Executable = ShellVerbTarget.App,
            Keys = new[]
            {
                new ShellVerbKey(@"Folder\shell\PowerLinkClone", "--clone \"%1\""),
            },
        },
        new ShellVerb
        {
            Id = "PowerLinkJunction",
            Label = "PowerLink: 创建指向此文件夹的目录联接",
            Description = "用此文件夹预填为目标打开 PowerLink 目录联接页面。选择联接出现的父文件夹。无需管理员或开发者模式；删除联接永远不会影响目标内容。",
            TargetsText = "文件夹",
            Executable = ShellVerbTarget.App,
            Keys = new[]
            {
                new ShellVerbKey(@"Folder\shell\PowerLinkJunction", "--junction \"%1\""),
            },
        },
    };

    public static IReadOnlyList<ShellVerb> OverlayVerbs { get; } = new[]
    {
        new ShellVerb
        {
            Id = "PowerLinkHardlinkOverlay",
            Label = "硬链接叠加图标",
            Description = "为硬链接文件（磁盘上有 2 个及以上名字）的图标显示一个小角标。每个资源管理器窗口都能一眼看到。占用 15 个 Windows 叠加槽位中的 1 个。",
            TargetsText = "资源管理器中的所有文件",
            Executable = ShellVerbTarget.Cli,
            Keys = Array.Empty<ShellVerbKey>(),
            Kind = ShellVerbKind.OverlayHandler,
            RequiresElevation = true,
        },
    };

    public static IReadOnlyList<ShellVerb> DropVerbs { get; } = new[]
    {
        new ShellVerb
        {
            Id = "PowerLinkDropHandler",
            Label = "PowerLink 拖放处理程序",
            Description = "右键拖动文件或文件夹到某个文件夹上，选择 “在此创建硬链接”（文件）、“在此克隆目录树（硬链接）”（文件夹）或 “在此创建目录联接”（文件夹）。一个 DLL 提供全部三个菜单项。按用户写 HKCU，无需管理员。",
            TargetsText = "右键拖动到文件夹上",
            Executable = ShellVerbTarget.App,
            Keys = Array.Empty<ShellVerbKey>(),
            Kind = ShellVerbKind.DropHandler,
            RequiresElevation = false,
        },
    };

    public static IReadOnlyList<ShellVerb> ModernMenuVerbs { get; } = new[]
    {
        new ShellVerb
        {
            Id = "PowerLinkModernMenu",
            Label = "Windows 11 现代菜单（实验性）",
            Description = "在 Win11 文件/文件夹右键菜单顶部区域添加 PowerLink 子菜单 —— 无需“显示更多选项”。注册稀疏 MSIX 包；需要 Windows 开发者模式。",
            TargetsText = "右键单击任意文件或文件夹",
            Executable = ShellVerbTarget.App,
            Keys = Array.Empty<ShellVerbKey>(),
            Kind = ShellVerbKind.ModernMenu,
            RequiresElevation = false,
        },
    };

    public static bool IsInstalled(ShellVerb verb)
    {
        if (verb.Kind == ShellVerbKind.OverlayHandler)
            return IsOverlayInstalled();
        if (verb.Kind == ShellVerbKind.DropHandler)
            return IsDropHandlerInstalled();
        if (verb.Kind == ShellVerbKind.ModernMenu)
            return ModernMenuService.IsInstalled();

        var layout = GetLayout();
        foreach (var key in verb.Keys)
        {
            var path = layout == ContextMenuLayout.Grouped
                ? GroupedLookupPath(key.RelativeKeyPath)
                : $@"{ClassesRoot}\{key.RelativeKeyPath}";
            if (path is null) continue;
            using var k = OpenSubKey(path);
            if (k is null) return false;
        }
        return true;
    }

    public static bool IsAnyInstalled() => AllVerbs.Any(IsInstalled);

    public static ContextMenuLayout GetLayout()
    {
        using var k = Registry.CurrentUser.OpenSubKey(PrefsKey);
        var s = k?.GetValue("ContextMenuLayout") as string;
        return string.Equals(s, nameof(ContextMenuLayout.Grouped), StringComparison.OrdinalIgnoreCase)
            ? ContextMenuLayout.Grouped
            : ContextMenuLayout.Flat;
    }

    public static void SetLayout(ContextMenuLayout layout)
    {
        using var k = Registry.CurrentUser.CreateSubKey(PrefsKey, writable: true)
            ?? throw new InvalidOperationException("无法创建 PowerLink 首选项注册表项。");
        k.SetValue("ContextMenuLayout", layout.ToString(), RegistryValueKind.String);
    }

    private static string? ExtractTarget(string relKey)
    {
        var idx = relKey.IndexOf(@"\shell\", StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? null : relKey[..idx];
    }

    private static string ExtractVerbId(string relKey)
    {
        var idx = relKey.LastIndexOf('\\');
        return idx < 0 ? relKey : relKey[(idx + 1)..];
    }

    private static string? GroupedLookupPath(string relKey)
    {
        var target = ExtractTarget(relKey);
        if (target is null) return null;
        var store = GroupedStores.FirstOrDefault(
            g => g.Target.Equals(target, StringComparison.OrdinalIgnoreCase)).Store;
        if (string.IsNullOrEmpty(store)) return null;
        return $@"{ClassesRoot}\{store}\shell\{ExtractVerbId(relKey)}";
    }

    public static bool IsOverlayInstalled()
    {
        using var cls = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Classes\CLSID\{OverlayClsid}\InprocServer32");
        using var overlay = Registry.LocalMachine.OpenSubKey(
            $@"{OverlayRoot}\{OverlayRegName}");
        if (cls is null || overlay is null) return false;

        // Guard against stale registrations: if the DLL the registry points at
        // no longer exists (user cleaned bin/, moved the build, reinstalled
        // somewhere else), surface that as "not installed" so Settings and
        // Apply reflect reality instead of a phantom registration.
        return cls.GetValue(string.Empty) is string dll && File.Exists(dll);
    }

    public static int CountOverlayHandlersOnSystem()
    {
        using var root = Registry.LocalMachine.OpenSubKey(OverlayRoot);
        return root?.GetSubKeyNames().Length ?? 0;
    }

    /// <summary>
    /// Returns the raw key names under HKLM's ShellIconOverlayIdentifiers —
    /// leading-space tricks (OneDrive, ours) are preserved so the user sees
    /// exactly what's competing for the 15 slots Windows loads.
    /// </summary>
    public static IReadOnlyList<string> ListOverlayHandlers()
    {
        using var root = Registry.LocalMachine.OpenSubKey(OverlayRoot);
        if (root is null) return Array.Empty<string>();
        return root.GetSubKeyNames()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsDropHandlerInstalled()
    {
        using var cls = Registry.CurrentUser.OpenSubKey(
            $@"{ClassesRoot}\CLSID\{DropClsid}\InprocServer32");
        using var drop = Registry.CurrentUser.OpenSubKey(
            $@"{ClassesRoot}\Directory\shellex\DragDropHandlers\{DropRegName}");
        if (cls is null || drop is null) return false;

        // Same stale-registration guard as IsOverlayInstalled — if the DLL has
        // disappeared since we registered it, don't claim the handler is live.
        return cls.GetValue(string.Empty) is string dll && File.Exists(dll);
    }

    public static void InstallDropHandler(string dllPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dllPath);
        if (!File.Exists(dllPath))
            throw new FileNotFoundException("未找到 shell 扩展 DLL。", dllPath);

        using (var clsid = Registry.CurrentUser.CreateSubKey(
            $@"{ClassesRoot}\CLSID\{DropClsid}", writable: true)
            ?? throw new InvalidOperationException("无法创建 CLSID 注册表项。"))
        {
            clsid.SetValue(string.Empty, DropDescription, RegistryValueKind.String);
            using var inproc = clsid.CreateSubKey("InprocServer32", writable: true)
                ?? throw new InvalidOperationException("无法创建 InprocServer32 注册表项。");
            inproc.SetValue(string.Empty, dllPath, RegistryValueKind.String);
            inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
        }

        using var drop = Registry.CurrentUser.CreateSubKey(
            $@"{ClassesRoot}\Directory\shellex\DragDropHandlers\{DropRegName}", writable: true)
            ?? throw new InvalidOperationException("无法创建拖放处理程序注册表项。");
        drop.SetValue(string.Empty, DropClsid, RegistryValueKind.String);
    }

    public static void UninstallDropHandler()
    {
        DeleteSubKeyTree($@"{ClassesRoot}\Directory\shellex\DragDropHandlers\{DropRegName}");
        DeleteSubKeyTree($@"{ClassesRoot}\CLSID\{DropClsid}");
    }

    public static void Install(ShellVerb verb, string cliPath, string appPath, string iconPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cliPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(appPath);

        var exe = verb.Executable == ShellVerbTarget.Cli ? cliPath : appPath;
        var icon = string.IsNullOrWhiteSpace(iconPath) ? null : $"\"{iconPath}\",0";

        foreach (var key in verb.Keys)
        {
            var command = $"\"{exe}\" {key.CommandArgs}";
            WriteVerb($@"{ClassesRoot}\{key.RelativeKeyPath}", verb.Label, icon, command);
        }
    }

    public static void Uninstall(ShellVerb verb)
    {
        foreach (var key in verb.Keys)
            DeleteSubKeyTree($@"{ClassesRoot}\{key.RelativeKeyPath}");
    }

    /// <summary>
    /// Wipes every PowerLink context-menu registration under HKCU — both flat
    /// and grouped layouts, parent entries and submenu stores alike. Called at
    /// the start of an Apply to guarantee the resulting state matches exactly
    /// what the UI asked for, without orphaned entries from a previous layout.
    /// </summary>
    public static void UninstallAll()
    {
        foreach (var verb in AllVerbs)
            Uninstall(verb);

        foreach (var (target, store) in GroupedStores)
        {
            DeleteSubKeyTree($@"{ClassesRoot}\{target}\shell\PowerLink");
            DeleteSubKeyTree($@"{ClassesRoot}\{store}");
        }
    }

    /// <summary>
    /// Single entry point for Apply: tears down any previous PowerLink HKCU
    /// state, persists the layout preference, then writes the verbs the user
    /// selected under the shape that layout dictates.
    /// </summary>
    public static void ApplyContextMenuSelection(
        IEnumerable<(ShellVerb verb, bool install)> selection,
        string cliPath, string appPath, string iconPath,
        ContextMenuLayout layout)
    {
        UninstallAll();
        SetLayout(layout);

        var toInstall = selection.Where(s => s.install).Select(s => s.verb).ToList();
        if (toInstall.Count == 0) return;

        if (layout == ContextMenuLayout.Flat)
        {
            foreach (var verb in toInstall)
                Install(verb, cliPath, appPath, iconPath);
            return;
        }

        var targetsWithVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var verb in toInstall)
        {
            InstallGrouped(verb, cliPath, appPath, iconPath);
            foreach (var key in verb.Keys)
            {
                var target = ExtractTarget(key.RelativeKeyPath);
                if (target is not null) targetsWithVerbs.Add(target);
            }
        }
        WriteGroupedParents(iconPath, targetsWithVerbs);
    }

    private static void InstallGrouped(ShellVerb verb, string cliPath, string appPath, string iconPath)
    {
        var exe = verb.Executable == ShellVerbTarget.Cli ? cliPath : appPath;
        var icon = string.IsNullOrWhiteSpace(iconPath) ? null : $"\"{iconPath}\",0";

        // Inside a cascading submenu the "PowerLink:" prefix is redundant — the
        // parent already says PowerLink. Strip it for cleaner submenu labels.
        var label = verb.Label.StartsWith("PowerLink: ", StringComparison.Ordinal)
            ? verb.Label["PowerLink: ".Length..]
            : verb.Label;

        foreach (var key in verb.Keys)
        {
            var target = ExtractTarget(key.RelativeKeyPath);
            if (target is null) continue;
            var store = GroupedStores.FirstOrDefault(
                g => g.Target.Equals(target, StringComparison.OrdinalIgnoreCase)).Store;
            if (string.IsNullOrEmpty(store)) continue;

            var subKey = $@"{ClassesRoot}\{store}\shell\{ExtractVerbId(key.RelativeKeyPath)}";
            var command = $"\"{exe}\" {key.CommandArgs}";
            WriteVerb(subKey, label, icon, command);
        }
    }

    private static void WriteGroupedParents(string iconPath, HashSet<string> targetsWithVerbs)
    {
        var icon = string.IsNullOrWhiteSpace(iconPath) ? null : $"\"{iconPath}\",0";
        foreach (var (target, store) in GroupedStores)
        {
            var parentPath = $@"{ClassesRoot}\{target}\shell\PowerLink";
            if (!targetsWithVerbs.Contains(target))
            {
                DeleteSubKeyTree(parentPath);
                continue;
            }

            using var k = Registry.CurrentUser.CreateSubKey(parentPath, writable: true)
                ?? throw new InvalidOperationException($"无法创建父级动作注册表项：{parentPath}");
            k.SetValue("MUIVerb", "PowerLink", RegistryValueKind.String);
            if (icon is not null) k.SetValue("Icon", icon, RegistryValueKind.String);
            // SubCommands="" + ExtendedSubCommandsKey together tell the shell to
            // cascade this verb into the store rather than treat it as a single
            // invocable verb.
            k.SetValue("SubCommands", string.Empty, RegistryValueKind.String);
            k.SetValue("ExtendedSubCommandsKey", store, RegistryValueKind.String);
        }
    }

    public static string DetectCliPath()
    {
        var baseDir = AppContext.BaseDirectory;

        // Production layout — Cli sits next to App.
        var sameFolder = Path.Combine(baseDir, "PowerLink.Cli.exe");
        if (File.Exists(sameFolder)) return sameFolder;

        // Development layout — walk up to find a sibling PowerLink.Cli/bin
        // and glob for the exe under whatever TFM it was built for. Levels
        // cover both `bin/<plat>/<cfg>/<tfm>` and `bin/<cfg>/<tfm>`.
        for (var up = 3; up <= 5; up++)
        {
            var segments = new string[up + 1];
            segments[0] = baseDir;
            for (var i = 1; i <= up; i++) segments[i] = "..";
            var ancestor = Path.GetFullPath(Path.Combine(segments));

            var cliBin = Path.Combine(ancestor, "PowerLink.Cli", "bin");
            if (!Directory.Exists(cliBin)) continue;

            var match = Directory.EnumerateFiles(cliBin, "PowerLink.Cli.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (match is not null) return match;
        }

        return sameFolder;
    }

    public static string DetectAppPath()
    {
        // The running App's own exe is the right target for --dedup / --inspect / --clone.
        var self = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(self) && File.Exists(self)) return self;
        return Path.Combine(AppContext.BaseDirectory, "PowerLink.App.exe");
    }

    public static string DetectShellExtDllPath()
    {
        var baseDir = AppContext.BaseDirectory;

        // Production layout — DLL sits next to App.exe (post-build copy).
        var sameFolder = Path.Combine(baseDir, "PowerLink.ShellExt.dll");
        if (File.Exists(sameFolder)) return sameFolder;

        // Development layout — sibling project's bin folder.
        for (var up = 3; up <= 6; up++)
        {
            var segments = new string[up + 1];
            segments[0] = baseDir;
            for (var i = 1; i <= up; i++) segments[i] = "..";
            var ancestor = Path.GetFullPath(Path.Combine(segments));

            var dllBin = Path.Combine(ancestor, "PowerLink.ShellExt", "x64");
            if (!Directory.Exists(dllBin)) continue;

            var match = Directory.EnumerateFiles(dllBin, "PowerLink.ShellExt.dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (match is not null) return match;
        }

        return sameFolder;
    }

    public static string DetectIconPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var ico = Path.Combine(baseDir, "Assets", "Icon.ico");
        if (File.Exists(ico)) return ico;
        return Path.Combine(baseDir, "PowerLink.App.exe");
    }

    public static void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { /* explorer auto-restarts itself */ }
            }
        }
        catch
        {
            // Best effort. Windows respawns explorer.exe automatically.
        }
    }

    /// <summary>
    /// Force-clears Explorer's icon cache. Mostly useful during overlay-handler
    /// development — Win10/11 caches rendered overlays aggressively, so a
    /// rebuilt DLL keeps showing old badges until the cache is wiped.
    /// Returns the number of cache files deleted.
    /// </summary>
    public static int ClearIconCache()
    {
        // Cache db files are locked while explorer.exe runs.
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
            }
        }
        catch { }

        var deleted = 0;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (TryDeleteFile(Path.Combine(local, "IconCache.db"))) deleted++;

        var explorerCacheDir = Path.Combine(local, "Microsoft", "Windows", "Explorer");
        if (Directory.Exists(explorerCacheDir))
        {
            // iconcache_*.db only — never touch thumbcache_*.db, which holds
            // user-content thumbnails (photos, videos) and is expensive to
            // rebuild while being unrelated to overlay handlers.
            try
            {
                foreach (var f in Directory.EnumerateFiles(explorerCacheDir, "iconcache_*.db"))
                    if (TryDeleteFile(f)) deleted++;
            }
            catch { }
        }

        // Windows will respawn explorer.exe on its own.
        return deleted;
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; /* locked — best effort */ }
    }

    private static void WriteVerb(string keyPath, string menuText, string? icon, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException($"无法创建注册表项：{keyPath}");
        key.SetValue(string.Empty, menuText, RegistryValueKind.String);
        if (icon is not null)
            key.SetValue("Icon", icon, RegistryValueKind.String);

        using var cmdKey = key.CreateSubKey("command", writable: true)
            ?? throw new InvalidOperationException($"无法在以下路径创建 command 子项：{keyPath}");
        cmdKey.SetValue(string.Empty, command, RegistryValueKind.String);
    }

    private static void DeleteSubKeyTree(string keyPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Silently ignore — uninstall is idempotent.
        }
    }

    private static RegistryKey? OpenSubKey(string keyPath)
        => Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
}
