using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using PowerLink.App.Services;
using PowerLink.Core.Dedup;
using PowerLink.Core.Models;
using PowerLink.Core.Scanning;

namespace PowerLink.App.ViewModels;

public partial class DedupViewModel : ObservableObject
{
    private readonly FileScanner _scanner = new();
    private readonly DedupEngine _engine = new();
    private readonly DedupExecutor _executor = new();
    private CancellationTokenSource? _cts;

    private readonly Stopwatch _phaseStopwatch = new();
    private ScanPhase? _currentPhase;
    private DateTime _lastUiFlush = DateTime.MinValue;
    private const int UiUpdateIntervalMs = 100;

    public ObservableCollection<string> Paths { get; } = new();
    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();

    public Visibility GroupsVisibility => Groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] public partial double MinSizeMiB { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BufferSizeKiB))]
    [NotifyPropertyChangedFor(nameof(BufferSizeText))]
    public partial double BufferSizeLog2 { get; set; }

    // Forwarded to DedupExecutorOptions.AlwaysVerifyContent. Off by default
    // because the tiered verify already re-hashes whenever mtime drifted —
    // the toggle exists for environments where mtime can't be trusted (sync
    // tools that restore it post-write).
    [ObservableProperty] public partial bool AlwaysVerifyContent { get; set; }

    public int BufferSizeKiB => 1 << (int)BufferSizeLog2;
    public string BufferSizeText => BufferSizeKiB >= 1024
        ? $"{BufferSizeKiB / 1024} MiB"
        : $"{BufferSizeKiB} KiB";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteDedupCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteDedupCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsExecuting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteDedupCommand))]
    public partial DedupPlan? Plan { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; }
    [ObservableProperty] public partial string? SummaryText { get; set; }
    [ObservableProperty] public partial string? PhaseText { get; set; }
    [ObservableProperty] public partial string? FilesText { get; set; }
    [ObservableProperty] public partial string? BytesText { get; set; }
    [ObservableProperty] public partial string? SpeedText { get; set; }
    [ObservableProperty] public partial string? EtaText { get; set; }
    [ObservableProperty] public partial string? CurrentFileText { get; set; }
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial bool IsProgressIndeterminate { get; set; }

    public DedupViewModel()
    {
        MinSizeMiB = 1;
        BufferSizeLog2 = 6; // 2^6 = 64 KiB
        StatusText = "就绪。";
        Paths.CollectionChanged += (_, _) => ScanCommand.NotifyCanExecuteChanged();
        Groups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(GroupsVisibility));
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var path = await PickerService.PickFolderAsync();
        if (path is not null && !Paths.Contains(path))
            Paths.Add(path);
    }

    [RelayCommand]
    private void RemoveFolder(string? path)
    {
        if (path is not null && Paths.Contains(path))
            Paths.Remove(path);
    }

    [RelayCommand]
    private void ClearFolders() => Paths.Clear();

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        Groups.Clear();
        Plan = null;
        SummaryText = null;
        StatusText = "正在扫描…";
        ResetProgress(indeterminate: true);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(OnProgress);

        try
        {
            var minBytes = (long)(MinSizeMiB * 1024 * 1024);
            var bufferBytes = BufferSizeKiB * 1024;
            var records = await _scanner.ScanAsync(Paths, minBytes, _cts.Token, progress);
            var result = await _engine.AnalyzeAsync(records, _cts.Token, progress, bufferBytes);

            foreach (var g in result.Groups)
                Groups.Add(new DuplicateGroupViewModel(g));

            Plan = DedupEngine.CreatePlan(result);

            var prefix = result.WasCancelled ? "已停止。部分结果：" : string.Empty;
            SummaryText =
                $"{prefix}扫描了 {result.TotalFilesScanned:N0} 个文件，用时 {result.ScanDuration.TotalSeconds:F1} 秒。"
                + $"找到 {result.Groups.Count:N0} 个分组、{result.TotalDuplicates:N0} 个重复项，"
                + $"可回收：{FormatBytes(result.TotalWastedBytes)}。";
            StatusText = result.WasCancelled
                ? "已停止。显示部分结果 —— 你仍然可以执行去重。"
                : (result.Groups.Count == 0 ? "未发现重复文件。" : "扫描完成。");
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描完成前已取消。";
        }
        catch (Exception ex)
        {
            StatusText = $"错误：{ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ClearProgress();
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ExecuteDedupAsync()
    {
        if (Plan is null || Plan.ActionCount == 0) return;

        IsExecuting = true;
        StatusText = $"正在执行 {Plan.ActionCount:N0} 个操作…";
        ResetProgress(indeterminate: false);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(OnProgress);

        try
        {
            var options = new DedupExecutorOptions { AlwaysVerifyContent = AlwaysVerifyContent };
            var result = await _executor.ExecuteAsync(Plan, _cts.Token, progress, options);
            var prefix = result.WasCancelled ? "已停止。部分结果：" : string.Empty;
            var alreadyLinked = result.AlreadyLinkedCount > 0
                ? $"，已链接：{result.AlreadyLinkedCount:N0}"
                : string.Empty;
            SummaryText =
                $"{prefix}成功：{result.SuccessCount:N0}，失败：{result.FailureCount:N0}{alreadyLinked}，"
                + $"已回收：{FormatBytes(result.BytesRecovered)}。";
            StatusText = result.WasCancelled
                ? "在所有操作完成前已停止。"
                : (result.FailureCount == 0 ? "去重完成。" : "已完成，但有失败项。");
            if (!result.WasCancelled)
            {
                Plan = null;
                Groups.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消。";
        }
        catch (Exception ex)
        {
            StatusText = $"错误：{ex.Message}";
        }
        finally
        {
            IsExecuting = false;
            ClearProgress();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelOp))]
    private void Cancel() => _cts?.Cancel();

    private bool CanScan() => Paths.Count > 0 && !IsScanning && !IsExecuting;
    private bool CanExecute() => Plan is { ActionCount: > 0 } && !IsScanning && !IsExecuting;
    private bool CanCancelOp() => IsScanning || IsExecuting;

    private void OnProgress(ScanProgress p)
    {
        // Progress<T>.Report posts via SynchronizationContext — a late
        // callback can land after the finally block cleared UI state.
        if (!IsScanning && !IsExecuting) return;

        if (_currentPhase != p.Phase)
        {
            _currentPhase = p.Phase;
            _phaseStopwatch.Restart();
            _lastUiFlush = DateTime.MinValue;
        }

        var isFinalForPhase = p.TotalFiles > 0 && p.FilesProcessed >= p.TotalFiles;
        var now = DateTime.UtcNow;
        if (!isFinalForPhase && (now - _lastUiFlush).TotalMilliseconds < UiUpdateIntervalMs)
            return;
        _lastUiFlush = now;

        PhaseText = FormatPhase(p.Phase);
        FilesText = p.TotalFiles > 0
            ? $"{p.FilesProcessed:N0} / {p.TotalFiles:N0} 个文件"
            : $"{p.FilesProcessed:N0} 个文件";
        CurrentFileText = string.IsNullOrEmpty(p.CurrentFile)
            ? null
            : Path.GetFileName(p.CurrentFile);

        var elapsedSec = _phaseStopwatch.Elapsed.TotalSeconds;
        var hasBytes = p.Phase == ScanPhase.FullHashing && p.TotalBytes > 0;

        if (hasBytes)
        {
            BytesText = $"{FormatBytes(p.BytesProcessed)} / {FormatBytes(p.TotalBytes)}";

            if (elapsedSec >= 0.5 && p.BytesProcessed > 0)
            {
                var bytesPerSec = p.BytesProcessed / elapsedSec;
                SpeedText = $"{FormatBytes((long)bytesPerSec)}/秒";
                var remaining = p.TotalBytes - p.BytesProcessed;
                EtaText = bytesPerSec > 0 ? $"剩余 {FormatDuration(remaining / bytesPerSec)}" : null;
            }

            IsProgressIndeterminate = false;
            ProgressValue = (double)p.BytesProcessed / p.TotalBytes * 100.0;
        }
        else if (p.TotalFiles > 0)
        {
            BytesText = null;

            if (elapsedSec >= 0.5 && p.FilesProcessed > 0)
            {
                var filesPerSec = p.FilesProcessed / elapsedSec;
                SpeedText = $"{filesPerSec:F0} 个文件/秒";
                var remainingFiles = p.TotalFiles - p.FilesProcessed;
                EtaText = filesPerSec > 0 ? $"剩余 {FormatDuration(remainingFiles / filesPerSec)}" : null;
            }

            IsProgressIndeterminate = false;
            ProgressValue = (double)p.FilesProcessed / p.TotalFiles * 100.0;
        }
        else
        {
            IsProgressIndeterminate = true;
            BytesText = null;
            if (elapsedSec >= 0.5 && p.FilesProcessed > 0)
            {
                var filesPerSec = p.FilesProcessed / elapsedSec;
                SpeedText = $"{filesPerSec:F0} 个文件/秒";
            }
            EtaText = null;
        }
    }

    private void ResetProgress(bool indeterminate)
    {
        _currentPhase = null;
        _phaseStopwatch.Reset();
        _lastUiFlush = DateTime.MinValue;
        IsProgressIndeterminate = indeterminate;
        ProgressValue = 0;
        PhaseText = null;
        FilesText = null;
        BytesText = null;
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
        BytesText = null;
        SpeedText = null;
        EtaText = null;
        CurrentFileText = null;
    }

    private static string FormatPhase(ScanPhase phase) => phase switch
    {
        ScanPhase.Enumerating => "枚举文件",
        ScanPhase.PrefixHashing => "哈希文件前缀",
        ScanPhase.FullHashing => "哈希文件",
        ScanPhase.Planning => "规划",
        ScanPhase.Executing => "执行",
        _ => phase.ToString(),
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:F2} {units[unit]}";
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
