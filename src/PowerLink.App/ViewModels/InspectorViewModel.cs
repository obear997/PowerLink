using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerLink.App.Services;
using PowerLink.Core.Models;
using PowerLink.Core.Native;
using PowerLink.Core.Scanning;

namespace PowerLink.App.ViewModels;

public partial class InspectorViewModel : ObservableObject
{
    private readonly FileScanner _scanner = new();
    private readonly JunctionScanner _junctionScanner = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> Paths { get; } = new();
    public ObservableCollection<HardlinkGroupViewModel> Groups { get; } = new();
    public ObservableCollection<JunctionViewModel> Junctions { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; }
    [ObservableProperty] public partial string? SummaryText { get; set; }
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial bool IsProgressIndeterminate { get; set; }
    [ObservableProperty] public partial string? CurrentFileText { get; set; }
    [ObservableProperty] public partial string? FilesText { get; set; }

    public string HardlinksHeader => $"硬链接（{Groups.Count:N0}）";
    public string JunctionsHeader
    {
        get
        {
            var dangling = Junctions.Count(j => j.IsTargetMissing);
            var suffix = dangling > 0 ? $"，{dangling:N0} 个悬空" : string.Empty;
            return $"目录联接（{Junctions.Count:N0}{suffix}）";
        }
    }

    private void NotifyHeaderChanged()
    {
        OnPropertyChanged(nameof(HardlinksHeader));
        OnPropertyChanged(nameof(JunctionsHeader));
    }

    public InspectorViewModel()
    {
        StatusText = "添加要检查的文件夹。";
        Paths.CollectionChanged += (_, _) => ScanCommand.NotifyCanExecuteChanged();
        Groups.CollectionChanged += (_, _) => NotifyHeaderChanged();
        Junctions.CollectionChanged += (_, _) => NotifyHeaderChanged();
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
        if (Paths.Count == 0) return;

        IsScanning = true;
        Groups.Clear();
        Junctions.Clear();
        SummaryText = null;
        StatusText = "正在扫描…";
        IsProgressIndeterminate = true;
        ProgressValue = 0;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(OnProgress);
        var sw = Stopwatch.StartNew();

        try
        {
            var records = await _scanner.ScanAsync(Paths, minSizeBytes: 0, _cts.Token, progress);

            // Group by (volume, fileIndex) — same underlying physical file.
            // HardLinkCount from MFT tells us total links across the whole
            // volume; records show only those that live inside the scanned
            // folders, so LinksOutsideScan = total - in-scan.
            var groups = records
                .Where(r => r.HardLinkCount > 1)
                .GroupBy(r => (r.VolumeSerialNumber, r.FileIndex))
                .Select(g => HardlinkGroupViewModel.FromRecords(g.ToList()))
                .OrderByDescending(g => g.SavedBytes)
                .ToList();

            foreach (var g in groups) Groups.Add(g);

            var junctions = await _junctionScanner.ScanAsync(Paths, _cts.Token);
            foreach (var j in junctions.OrderBy(j => j.LinkPath, StringComparer.OrdinalIgnoreCase))
                Junctions.Add(JunctionViewModel.FromInfo(j));

            var totalSaved = groups.Sum(g => g.SavedBytes);
            var totalLinks = groups.Sum(g => g.LinksInScan);
            var dangling = Junctions.Count(j => j.IsTargetMissing);

            sw.Stop();
            SummaryText =
                $"扫描了 {records.Count:N0} 个文件，覆盖 {Paths.Count} 个位置，用时 {sw.Elapsed.TotalSeconds:F1} 秒。"
                + $"找到 {groups.Count:N0} 个硬链接文件（扫描内共 {totalLinks:N0} 个链接），共享 {FormatBytes(totalSaved)}。"
                + $"目录联接：{Junctions.Count:N0}"
                + (dangling > 0 ? $"（{dangling:N0} 个悬空）" : string.Empty) + "。";
            StatusText = groups.Count == 0 && Junctions.Count == 0
                ? "这些文件夹中未发现硬链接或目录联接。"
                : "扫描完成。";
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
            IsScanning = false;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            CurrentFileText = null;
            FilesText = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelOp))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void DeleteJunction(JunctionViewModel? jvm)
    {
        if (jvm is null) return;
        try
        {
            Win32Junction.Delete(jvm.LinkPath);
            Junctions.Remove(jvm);
            StatusText = $"已删除联接：{jvm.LinkPath}（目标已保留）";
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RepairJunctionAsync(JunctionViewModel? jvm)
    {
        if (jvm is null) return;
        var newTarget = await PickerService.PickFolderAsync();
        if (newTarget is null) return;

        try
        {
            Win32Junction.Repair(jvm.LinkPath, newTarget);
            var updated = Win32Junction.Read(jvm.LinkPath)!;
            var idx = Junctions.IndexOf(jvm);
            if (idx >= 0)
                Junctions[idx] = JunctionViewModel.FromInfo(updated);
            StatusText = $"已修复联接：{jvm.LinkPath} → {newTarget}";
        }
        catch (Exception ex)
        {
            StatusText = $"修复失败：{ex.Message}";
        }
    }

    private bool CanScan() => !IsScanning && Paths.Count > 0;
    private bool CanCancelOp() => IsScanning;

    private void OnProgress(ScanProgress p)
    {
        // Progress<T>.Report posts via SynchronizationContext — the callback
        // may run AFTER ScanAsync's finally block has already reset UI state.
        // Guard so a late-arriving progress event doesn't re-start the spinner.
        if (!IsScanning) return;

        FilesText = $"已处理 {p.FilesProcessed:N0} 个文件";
        CurrentFileText = string.IsNullOrEmpty(p.CurrentFile) ? null : Path.GetFileName(p.CurrentFile);
        IsProgressIndeterminate = true; // scanner reports count but no total
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:F2} {units[unit]}";
    }
}
