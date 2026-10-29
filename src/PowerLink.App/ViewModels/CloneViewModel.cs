using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerLink.App.Services;
using PowerLink.Core.Clone;
using PowerLink.Core.Models;
using PowerLink.Core.Native;

namespace PowerLink.App.ViewModels;

public partial class CloneViewModel : ObservableObject
{
    private readonly CloneEngine _engine = new();
    private CancellationTokenSource? _cts;

    private readonly Stopwatch _phaseStopwatch = new();
    private DateTime _lastUiFlush = DateTime.MinValue;
    private const int UiUpdateIntervalMs = 100;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    public partial string? SourcePath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    public partial string? DestPath { get; set; }

    [ObservableProperty] public partial bool DryRun { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; }
    [ObservableProperty] public partial string? SummaryText { get; set; }
    [ObservableProperty] public partial string? PhaseText { get; set; }
    [ObservableProperty] public partial string? FilesText { get; set; }
    [ObservableProperty] public partial string? SpeedText { get; set; }
    [ObservableProperty] public partial string? EtaText { get; set; }
    [ObservableProperty] public partial string? CurrentFileText { get; set; }
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial bool IsProgressIndeterminate { get; set; }

    public CloneViewModel()
    {
        StatusText = "请选择源和目标。";
    }

    [RelayCommand]
    private async Task PickSourceFolderAsync()
    {
        var path = await PickerService.PickFolderAsync();
        if (path is not null) SourcePath = path;
    }

    [RelayCommand]
    private async Task PickSourceFileAsync()
    {
        var path = await PickerService.PickFileAsync();
        if (path is not null) SourcePath = path;
    }

    [RelayCommand]
    private async Task PickDestAsync()
    {
        var path = await PickerService.PickFolderAsync();
        if (path is not null) DestPath = path;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(DestPath)) return;

        IsRunning = true;
        SummaryText = null;
        ResetProgress(indeterminate: true);
        _phaseStopwatch.Restart();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var sourceFull = Path.GetFullPath(SourcePath);
            var destFull = Path.GetFullPath(DestPath);

            if (File.Exists(sourceFull))
            {
                StatusText = DryRun ? "试运行（文件）…" : "正在创建文件硬链接…";
                await LinkSingleFileAsync(sourceFull, destFull);
            }
            else if (Directory.Exists(sourceFull))
            {
                StatusText = DryRun ? "试运行（文件夹）…" : "正在克隆文件夹…";
                var progress = new Progress<ScanProgress>(OnProgress);
                var result = await _engine.CloneAsync(sourceFull, destFull, DryRun, _cts.Token, progress);
                SummaryText =
                    $"{(result.DryRun ? "【试运行】" : string.Empty)}"
                    + $"克隆到：{result.EffectiveDestPath}\n"
                    + $"目录：{result.DirectoriesCreated:N0}，已链接文件：{result.FilesLinked:N0}，失败：{result.FilesFailed:N0}。";
                StatusText = result.FilesFailed == 0 ? "完成。" : "已完成，但有失败项。";
            }
            else
            {
                StatusText = $"未找到源：{sourceFull}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消。";
        }
        catch (Exception ex)
        {
            (StatusText, SummaryText) = FormatFriendlyError(ex);
        }
        finally
        {
            IsRunning = false;
            _phaseStopwatch.Stop();
            ClearProgress();
        }
    }

    private async Task LinkSingleFileAsync(string sourceFile, string destDir)
    {
        var ct = _cts!.Token;
        var dryRun = DryRun;
        var linkPath = Path.Combine(destDir, Path.GetFileName(sourceFile));

        // Friendly pre-checks done on UI thread so we can short-circuit
        // before the background work and surface a readable message.
        if (string.Equals(linkPath, sourceFile, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "源和目标相同。";
            SummaryText = "请选择与文件所在文件夹不同的目标文件夹。";
            return;
        }
        if (File.Exists(linkPath))
        {
            StatusText = "目标位置已存在同名文件。";
            SummaryText = $"无法覆盖：{linkPath}";
            return;
        }
        if (!Win32Hardlink.AreSameVolume(sourceFile, destDir))
        {
            StatusText = "源和目标位于不同卷。";
            SummaryText = "硬链接只能在同一 NTFS 卷内工作。";
            return;
        }

        // All filesystem work runs on a background thread; UI properties are
        // assigned only after we're back on the dispatcher to avoid
        // RPC_E_WRONG_THREAD on the x:Bind setter.
        var destExisted = await Task.Run(() =>
        {
            var existed = Directory.Exists(destDir);
            if (!existed && !dryRun)
                Directory.CreateDirectory(destDir);
            if (!dryRun)
                Win32Hardlink.CreateHardLink(linkPath, sourceFile);
            return existed;
        }, ct);

        SummaryText = dryRun
            ? (destExisted
                ? $"【试运行】将创建硬链接：{linkPath} -> {sourceFile}"
                : $"【试运行】将创建 {destDir}，然后把 {Path.GetFileName(sourceFile)} 硬链接进去")
            : $"已创建硬链接：{linkPath} -> {sourceFile}";

        StatusText = "完成。";
    }

    private static (string Status, string Summary) FormatFriendlyError(Exception ex) => ex switch
    {
        IOException io when io.HResult == unchecked((int)0x800700B7) || io.Message.Contains("Win32 error 183")
            => ("目标已存在。", "目标位置已存在同名文件或文件夹。"),
        IOException io when io.HResult == unchecked((int)0x80070005) || io.Message.Contains("Win32 error 5")
            => ("访问被拒绝。", "Windows 拒绝了该操作。请检查文件夹权限，或确认文件未被其他程序占用。"),
        IOException io when io.HResult == unchecked((int)0x80070020) || io.Message.Contains("Win32 error 32")
            => ("文件正在使用中。", "另一个进程正打开着源或目标文件。请关闭后重试。"),
        UnauthorizedAccessException
            => ("访问被拒绝。", ex.Message),
        InvalidOperationException
            => ("不允许的操作。", ex.Message),
        DirectoryNotFoundException
            => ("未找到。", ex.Message),
        _ => ($"错误：{ex.GetType().Name}", ex.Message),
    };

    [RelayCommand(CanExecute = nameof(CanCancelOp))]
    private void Cancel() => _cts?.Cancel();

    private bool CanRun() =>
        !IsRunning &&
        !string.IsNullOrWhiteSpace(SourcePath) &&
        !string.IsNullOrWhiteSpace(DestPath);

    private bool CanCancelOp() => IsRunning;

    private void OnProgress(ScanProgress p)
    {
        // Progress<T>.Report posts via SynchronizationContext — a late
        // callback can land after the finally block cleared UI state.
        if (!IsRunning) return;

        var isFinal = p.TotalFiles > 0 && p.FilesProcessed >= p.TotalFiles;
        var now = DateTime.UtcNow;
        if (!isFinal && (now - _lastUiFlush).TotalMilliseconds < UiUpdateIntervalMs)
            return;
        _lastUiFlush = now;

        PhaseText = "正在链接文件";
        FilesText = p.TotalFiles > 0
            ? $"{p.FilesProcessed:N0} / {p.TotalFiles:N0} 个文件"
            : $"{p.FilesProcessed:N0} 个文件";
        CurrentFileText = string.IsNullOrEmpty(p.CurrentFile) ? null : Path.GetFileName(p.CurrentFile);

        var elapsedSec = _phaseStopwatch.Elapsed.TotalSeconds;
        if (p.TotalFiles > 0)
        {
            if (elapsedSec >= 0.5 && p.FilesProcessed > 0)
            {
                var rate = p.FilesProcessed / elapsedSec;
                SpeedText = $"{rate:F0} 个文件/秒";
                var remaining = p.TotalFiles - p.FilesProcessed;
                EtaText = rate > 0 ? $"剩余 {FormatDuration(remaining / rate)}" : null;
            }
            IsProgressIndeterminate = false;
            ProgressValue = (double)p.FilesProcessed / p.TotalFiles * 100.0;
        }
        else
        {
            IsProgressIndeterminate = true;
        }
    }

    private void ResetProgress(bool indeterminate)
    {
        _lastUiFlush = DateTime.MinValue;
        IsProgressIndeterminate = indeterminate;
        ProgressValue = 0;
        PhaseText = null;
        FilesText = null;
        SpeedText = null;
        EtaText = null;
        CurrentFileText = null;
    }

    private void ClearProgress()
    {
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        PhaseText = null;
        FilesText = null;
        SpeedText = null;
        EtaText = null;
        CurrentFileText = null;
    }

    private static string FormatDuration(double seconds)
    {
        if (double.IsInfinity(seconds) || double.IsNaN(seconds) || seconds < 0) return "?";
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}小时{t.Minutes}分";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}分{t.Seconds}秒";
        return $"{t.Seconds}秒";
    }
}
