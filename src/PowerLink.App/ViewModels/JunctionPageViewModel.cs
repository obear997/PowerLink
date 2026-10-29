using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerLink.App.Services;
using PowerLink.Core.Native;

namespace PowerLink.App.ViewModels;

public partial class JunctionPageViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string? TargetPath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string? ParentPath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string? LinkName { get; set; }

    [ObservableProperty] public partial bool AllowMissingTarget { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; }
    [ObservableProperty] public partial string? SummaryText { get; set; }

    public JunctionPageViewModel()
    {
        StatusText = "请选择此联接要指向的目标文件夹，然后选择创建联接的父文件夹。";
    }

    [RelayCommand]
    private async Task PickTargetAsync()
    {
        var path = await PickerService.PickFolderAsync();
        if (path is null) return;

        TargetPath = path;

        // Auto-derive link name from target's basename. User can still edit
        // afterwards. Re-picking target will overwrite this value, which is
        // the normal flow — explicit override happens in the textbox.
        var basename = Path.GetFileName(path.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(basename))
            LinkName = basename;
    }

    [RelayCommand]
    private async Task PickParentAsync()
    {
        var path = await PickerService.PickFolderAsync();
        if (path is not null) ParentPath = path;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)
            || string.IsNullOrWhiteSpace(ParentPath)
            || string.IsNullOrWhiteSpace(LinkName))
            return;

        var linkPath = Path.Combine(ParentPath, LinkName);

        try
        {
            Win32Junction.Create(linkPath, TargetPath, allowMissingTarget: AllowMissingTarget);
            StatusText = "联接已创建。";
            SummaryText = $"{linkPath}  →  {TargetPath}";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or DirectoryNotFoundException)
        {
            StatusText = "创建失败。";
            SummaryText = ex.Message;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        TargetPath = null;
        ParentPath = null;
        LinkName = null;
        AllowMissingTarget = false;
        SummaryText = null;
        StatusText = "请选择此联接要指向的目标文件夹，然后选择创建联接的父文件夹。";
    }

    private bool CanCreate() =>
        !string.IsNullOrWhiteSpace(TargetPath) &&
        !string.IsNullOrWhiteSpace(ParentPath) &&
        !string.IsNullOrWhiteSpace(LinkName);
}
