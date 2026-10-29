using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Win32Exception = System.ComponentModel.Win32Exception;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using PowerLink.App.Services;
using PowerLink.Core.State;
using Velopack;

namespace PowerLink.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED — user cancelled UAC prompt

    private readonly UpdateService _updateService = new();
    private UpdateInfo? _pendingUpdate;

    public ObservableCollection<ShellVerbViewModel> Verbs { get; } = new();
    public ObservableCollection<ShellVerbViewModel> OverlayVerbs { get; } = new();
    public ObservableCollection<ShellVerbViewModel> DropVerbs { get; } = new();
    public ObservableCollection<ShellVerbViewModel> ModernVerbs { get; } = new();

    [ObservableProperty] public partial string CliPath { get; set; }
    [ObservableProperty] public partial string AppPath { get; set; }
    [ObservableProperty] public partial string? PickedPathText { get; set; }
    [ObservableProperty] public partial string? OperationStatus { get; set; }
    [ObservableProperty] public partial string? OverlayStatus { get; set; }
    [ObservableProperty] public partial string? DropStatus { get; set; }
    [ObservableProperty] public partial string? ModernStatus { get; set; }
    [ObservableProperty] public partial bool GroupedLayout { get; set; }

    // Populated in Refresh() with a non-empty message when the current machine
    // can't register the modern menu package (Windows 10 doesn't expose the
    // top-section API at all, and unsigned packages require Developer Mode).
    // Bound to the InfoBar in the Modern menu Settings section.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModernRequirementsVisibility))]
    public partial string? ModernRequirementsWarning { get; set; }

    public Visibility ModernRequirementsVisibility =>
        string.IsNullOrEmpty(ModernRequirementsWarning) ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickedVisibility))]
    [NotifyPropertyChangedFor(nameof(NoPickVisibility))]
    [NotifyCanExecuteChangedFor(nameof(ClearPickCommand))]
    public partial bool HasPicked { get; set; }

    public Visibility PickedVisibility => HasPicked ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoPickVisibility => HasPicked ? Visibility.Collapsed : Visibility.Visible;

    // --- Updates section ----------------------------------------------------

    [ObservableProperty] public partial string CurrentVersionText { get; set; }

    // True when this process was launched out of a Velopack-managed install
    // (%LocalAppData%\PowerLink\). Drives which "apply update" verb the page
    // exposes — Install vs Open Releases page.
    // Auto-updates work for both Setup.exe install and Velopack Portable.zip
    // (both ship with Update.exe). False means we're running an ad-hoc dev
    // build out of bin\Debug — in-app update can't restart it safely.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateModeText))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(InstallButtonVisibility))]
    public partial bool IsVelopackInstalled { get; set; }

    public string UpdateModeText => IsVelopackInstalled
        ? "已启用自动更新。"
        : "开发构建 —— 自动更新已禁用。请从 Releases 页面获取正式版本。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial bool IsCheckingForUpdates { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(InstallButtonVisibility))]
    public partial bool IsUpdateAvailable { get; set; }

    [ObservableProperty] public partial string? UpdateStatus { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial bool IsInstallingUpdate { get; set; }

    public Visibility InstallButtonVisibility =>
        IsUpdateAvailable && IsVelopackInstalled ? Visibility.Visible : Visibility.Collapsed;

    // Injected by SettingsPage — returns true if the user confirmed installing
    // the overlay despite the system being at/over Windows' 15-handler limit.
    // Kept as a simple Func to avoid dragging a view reference into the VM.
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmOverlaySlotWar { get; set; }

    public SettingsViewModel()
    {
        // Defensive defaults: Refresh() below replaces both with detected
        // paths, but a partial-property without an implicit-default init
        // would leave them null until then.
        CliPath = string.Empty;
        AppPath = string.Empty;
        CurrentVersionText = _updateService.CurrentVersionText;
        IsVelopackInstalled = _updateService.IsVelopackInstalled;

        foreach (var verb in ShellExtensionService.AllVerbs)
            Verbs.Add(new ShellVerbViewModel { Verb = verb });
        foreach (var verb in ShellExtensionService.OverlayVerbs)
            OverlayVerbs.Add(new ShellVerbViewModel { Verb = verb });
        foreach (var verb in ShellExtensionService.DropVerbs)
            DropVerbs.Add(new ShellVerbViewModel { Verb = verb });
        foreach (var verb in ShellExtensionService.ModernMenuVerbs)
            ModernVerbs.Add(new ShellVerbViewModel { Verb = verb });
        Refresh();
    }

    public void Refresh()
    {
        CliPath = ShellExtensionService.DetectCliPath();
        AppPath = ShellExtensionService.DetectAppPath();
        GroupedLayout = ShellExtensionService.GetLayout() == ContextMenuLayout.Grouped;
        ModernRequirementsWarning = ComputeModernRequirementsWarning();

        SyncVerbStates(Verbs);
        SyncVerbStates(OverlayVerbs);
        SyncVerbStates(DropVerbs);
        SyncVerbStates(ModernVerbs);

        var picked = PickedSourceStore.TryLoad();
        HasPicked = picked is not null;
        PickedPathText = picked is null
            ? null
            : $"{(picked.IsDirectory ? "文件夹" : "文件")}：{picked.Path}\n选取时间：{picked.PickedAtUtc.ToLocalTime():g}";
    }

    /// <summary>
    /// Re-reads each verb's installed state from the registry. If the user
    /// has a pending checkbox change (ShouldInstall != IsInstalled before
    /// the read), we preserve their intent rather than overwriting it —
    /// otherwise the page-cached Settings page would lose pending edits
    /// every time the user navigates away and back.
    /// </summary>
    private static void SyncVerbStates(System.Collections.Generic.IEnumerable<ShellVerbViewModel> verbs)
    {
        foreach (var v in verbs)
        {
            var oldInstalled = v.IsInstalled;
            var userHasPendingChange = v.ShouldInstall != oldInstalled;

            var newInstalled = ShellExtensionService.IsInstalled(v.Verb);
            v.IsInstalled = newInstalled;
            if (!userHasPendingChange)
                v.ShouldInstall = newInstalled;
        }
    }

    [RelayCommand]
    private void ApplyChanges()
    {
        try
        {
            var cli = ShellExtensionService.DetectCliPath();
            var app = ShellExtensionService.DetectAppPath();
            var icon = ShellExtensionService.DetectIconPath();

            var oldLayout = ShellExtensionService.GetLayout();
            var newLayout = GroupedLayout ? ContextMenuLayout.Grouped : ContextMenuLayout.Flat;

            // Capture diff before Apply rewrites state so the status message
            // reflects the user's actual delta, not the post-write reality.
            var installing = Verbs.Count(v => v.ShouldInstall && !v.IsInstalled);
            var uninstalling = Verbs.Count(v => !v.ShouldInstall && v.IsInstalled);

            var selection = Verbs.Select(v => (v.Verb, v.ShouldInstall)).ToList();
            ShellExtensionService.ApplyContextMenuSelection(selection, cli, app, icon, newLayout);

            Refresh();
            var layoutChanged = oldLayout != newLayout;
            OperationStatus = (installing, uninstalling, layoutChanged) switch
            {
                (0, 0, false) => "无更改。",
                (0, 0, true)  => $"布局已切换为 {newLayout}。重启资源管理器以生效。",
                (_, 0, _)     => $"已安装 {installing} 个动作。重启资源管理器以查看。",
                (0, _, _)     => $"已卸载 {uninstalling} 个动作。重启资源管理器以生效。",
                _             => $"已安装 {installing} 个，卸载 {uninstalling} 个。重启资源管理器以生效。",
            };
        }
        catch (Exception ex)
        {
            OperationStatus = $"应用失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void UninstallAll()
    {
        try
        {
            ShellExtensionService.UninstallAll();
            Refresh();
            OperationStatus = "已卸载全部动作。重启资源管理器以生效。";
        }
        catch (Exception ex)
        {
            OperationStatus = $"卸载失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyOverlayChangesAsync()
    {
        var install = OverlayVerbs.Any(v => v.ShouldInstall && !v.IsInstalled);
        var uninstall = OverlayVerbs.Any(v => !v.ShouldInstall && v.IsInstalled);
        if (!install && !uninstall)
        {
            OverlayStatus = "无更改。";
            return;
        }

        var dllPath = ShellExtensionService.DetectShellExtDllPath();
        if (install && !File.Exists(dllPath))
        {
            OverlayStatus = $"未在 {dllPath} 找到 PowerLink.ShellExt.dll。请先构建 C++ 项目。";
            return;
        }

        if (install)
        {
            var handlers = ShellExtensionService.ListOverlayHandlers();
            // 14 rather than 15: adding ours would push the total to 15+, right
            // at the limit where Windows starts ignoring handlers. Ask first so
            // the user sees whose slot they might steal.
            if (handlers.Count >= 14 && ConfirmOverlaySlotWar is not null)
            {
                var proceed = await ConfirmOverlaySlotWar(handlers);
                if (!proceed)
                {
                    OverlayStatus = "已取消 —— 未尝试安装叠加图标。";
                    return;
                }
            }
        }

        var args = install
            ? $"install-overlay \"{dllPath}\""
            : "uninstall-overlay";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ShellExtensionService.DetectCliPath(),
                Arguments = args,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                OverlayStatus = "已取消。";
                return;
            }

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                OverlayStatus = $"叠加图标{(install ? "安装" : "卸载")}失败（退出码 {process.ExitCode}）。";
                Refresh();
                return;
            }

            Refresh();
            OverlayStatus = install
                ? "叠加图标已安装。重启资源管理器以查看角标。"
                : "叠加图标已卸载。重启资源管理器以移除角标。";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            OverlayStatus = "已取消 —— 叠加处理程序需要管理员权限。";
            Refresh();
        }
        catch (Exception ex)
        {
            OverlayStatus = $"叠加图标更改失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ApplyDropChanges()
    {
        var install = DropVerbs.Any(v => v.ShouldInstall && !v.IsInstalled);
        var uninstall = DropVerbs.Any(v => !v.ShouldInstall && v.IsInstalled);
        if (!install && !uninstall)
        {
            DropStatus = "无更改。";
            return;
        }

        try
        {
            var installed = 0;
            var uninstalled = 0;
            foreach (var v in DropVerbs)
            {
                if (v.ShouldInstall && !v.IsInstalled)
                {
                    var dllPath = ShellExtensionService.DetectShellExtDllPath();
                    if (!File.Exists(dllPath))
                    {
                        DropStatus = $"未在 {dllPath} 找到 PowerLink.ShellExt.dll。请先构建 C++ 项目。";
                        return;
                    }
                    ShellExtensionService.InstallDropHandler(dllPath);
                    installed++;
                }
                else if (!v.ShouldInstall && v.IsInstalled)
                {
                    ShellExtensionService.UninstallDropHandler();
                    uninstalled++;
                }
            }
            Refresh();
            DropStatus = (installed, uninstalled) switch
            {
                (_, 0) => "拖放处理程序已安装。重启资源管理器以激活。",
                (0, _) => "拖放处理程序已卸载。重启资源管理器以移除。",
                _ => "已应用。请重启资源管理器。",
            };
        }
        catch (Exception ex)
        {
            DropStatus = $"应用失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyModernChangesAsync()
    {
        var install = ModernVerbs.Any(v => v.ShouldInstall && !v.IsInstalled);
        var uninstall = ModernVerbs.Any(v => !v.ShouldInstall && v.IsInstalled);
        if (!install && !uninstall)
        {
            ModernStatus = "无更改。";
            return;
        }

        if (install && !ModernMenuService.IsDeveloperModeEnabled())
        {
            ModernStatus = "开发者模式已关闭。请在 设置 → 隐私和安全性 → 开发者选项 中启用后重试。（未签名包需要开发者模式。）";
            Refresh();
            return;
        }

        try
        {
            if (install)
            {
                await ModernMenuService.InstallAsync();
                Refresh();
                ModernStatus = "现代菜单已注册。重启资源管理器后右键单击任意文件/文件夹即可试用。";
            }
            else
            {
                await ModernMenuService.UninstallAsync();
                Refresh();
                ModernStatus = "现代菜单已注销。重启资源管理器以移除该条目。";
            }
        }
        catch (Exception ex)
        {
            ModernStatus = $"现代菜单更改失败：{ex.Message}";
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        UpdateStatus = "正在检查…";
        IsUpdateAvailable = false;
        try
        {
            var result = await _updateService.CheckAsync();
            switch (result.Availability)
            {
                case UpdateAvailability.UpToDate:
                    UpdateStatus = $"已是最新版本（v{CurrentVersionText}）。";
                    break;
                case UpdateAvailability.Available:
                    _pendingUpdate = result.VelopackInfo;
                    IsUpdateAvailable = true;
                    UpdateStatus = IsVelopackInstalled
                        ? $"有可用更新：v{result.AvailableVersion}。点击“立即安装”以应用。"
                        : $"有可用更新：v{result.AvailableVersion}。打开 Releases 页面获取 Setup.exe 或 Portable.zip。";
                    break;
                case UpdateAvailability.Failed:
                    UpdateStatus = $"检查失败：{result.ErrorMessage}";
                    break;
            }
        }
        finally { IsCheckingForUpdates = false; }
    }

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null) return;
        IsInstallingUpdate = true;
        UpdateStatus = "正在下载并应用… 应用将重启。";
        try
        {
            await _updateService.ApplyAsync(_pendingUpdate);
            UpdateStatus = "重启未发生 —— 请重试或手动下载。";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"安装失败：{ex.Message}";
        }
        finally { IsInstallingUpdate = false; }
    }

    [RelayCommand]
    private void OpenReleasesPage() => _updateService.OpenReleasesPage();

    private bool CanCheckForUpdates() => !IsCheckingForUpdates && !IsInstallingUpdate;
    private bool CanInstallUpdate() =>
        IsUpdateAvailable && IsVelopackInstalled && !IsCheckingForUpdates && !IsInstallingUpdate;

    [RelayCommand]
    private void RestartExplorer()
    {
        ShellExtensionService.RestartExplorer();
        OperationStatus = "资源管理器已重启。";
        OverlayStatus = "资源管理器已重启。";
        DropStatus = "资源管理器已重启。";
        ModernStatus = "资源管理器已重启。";
    }

    [RelayCommand]
    private void ClearIconCache()
    {
        var deleted = ShellExtensionService.ClearIconCache();
        OverlayStatus = deleted == 0
            ? "未删除任何缓存文件（已是干净状态或被占用）。资源管理器正在重启…"
            : $"已清除 {deleted} 个图标缓存文件。资源管理器正在重启；缓存重建期间首次打开文件夹会稍慢。";
    }

    [RelayCommand(CanExecute = nameof(CanClearPick))]
    private void ClearPick()
    {
        PickedSourceStore.Clear();
        Refresh();
    }

    [RelayCommand]
    private void RefreshPicked() => Refresh();

    private bool CanClearPick() => HasPicked;

    private static string? ComputeModernRequirementsWarning()
    {
        // Win11 is 10.0.22000+. Earlier builds don't have the top-section
        // fileExplorerContextMenus extension point at all — no amount of
        // Developer Mode will make it light up, so surface that distinctly.
        if (Environment.OSVersion.Version.Build < 22000)
            return "需要 Windows 11（build 22000 或更高）—— Windows 10 上不存在顶部区域右键菜单 API。";
        if (!ModernMenuService.IsDeveloperModeEnabled())
            return "Windows 开发者模式已关闭。请在 设置 → 隐私和安全性 → 开发者选项 中启用，以便未签名的稀疏包完成注册。";
        return null;
    }
}
