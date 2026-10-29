using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerLink.App.ViewModels;

namespace PowerLink.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
        // Cache the page so the user's pending checkbox edits aren't lost
        // when navigating away. OnNavigatedTo still calls Refresh() to pick
        // up registry changes made externally.
        NavigationCacheMode = NavigationCacheMode.Required;

        // Wire the VM's slot-war confirmation callback to a ContentDialog here
        // (ContentDialog needs a XamlRoot, which only the page can supply).
        ViewModel.ConfirmOverlaySlotWar = ShowOverlaySlotWarDialogAsync;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Refresh();
    }

    private async Task<bool> ShowOverlaySlotWarDialogAsync(IReadOnlyList<string> handlers)
    {
        var list = string.Join(Environment.NewLine,
            handlers.Select((h, i) => $"  {i + 1,2}. {h}"));

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "叠加槽位上限",
            Content =
                $"Windows 只按字母序加载前 15 个 shell 图标叠加处理程序。"
                + $"你的系统已经注册了 {handlers.Count} 个："
                + Environment.NewLine + Environment.NewLine
                + list
                + Environment.NewLine + Environment.NewLine
                + "PowerLink 用一个带前导空格的名字注册，以便排在最前 —— 但排在 "
                + "第 15 位之后的处理程序会被静默丢弃。仍然安装吗？",
            PrimaryButtonText = "安装叠加图标",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
