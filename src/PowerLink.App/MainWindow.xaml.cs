using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using PowerLink.App.Pages;

namespace PowerLink.App;

public sealed partial class MainWindow : Window
{
    private readonly LaunchPreset? _preset;

    public MainWindow() : this(null) { }

    public MainWindow(LaunchPreset? preset)
    {
        _preset = preset;
        InitializeComponent();
        Title = "PowerLink";
        SetWindowIcon();
        SetWindowSize();
        SetMicaBackdrop();
        NavigateInitialPage();
    }

    private void NavigateInitialPage()
    {
        var preset = _preset;
        if (preset is null)
        {
            ContentFrame.Navigate(typeof(DedupPage));
            return;
        }

        var (pageType, navTag) = preset.Mode switch
        {
            LaunchMode.Dedup => (typeof(DedupPage), "dedup"),
            LaunchMode.Inspect => (typeof(InspectorPage), "inspector"),
            LaunchMode.Clone => (typeof(ClonePage), "clone"),
            LaunchMode.Junction => (typeof(JunctionPage), "junction"),
            _ => (typeof(DedupPage), "dedup"),
        };

        // Sync NavView selection so the sidebar highlights the chosen page.
        // SuppressNavigate used to avoid double-navigation from SelectionChanged.
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == navTag)
            {
                _suppressNavigate = true;
                Nav.SelectedItem = item;
                _suppressNavigate = false;
                break;
            }
        }

        ContentFrame.Navigate(pageType, preset.Path);
    }

    private bool _suppressNavigate;

    private void SetMicaBackdrop()
    {
        // Mica is supported on Windows 11; on older versions the property
        // assignment is a no-op (graceful degradation to opaque background).
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
    }

    private void SetWindowIcon()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");
            if (File.Exists(iconPath))
                appWindow.SetIcon(iconPath);
        }
        catch
        {
            // Non-fatal — fall back to default icon.
        }
    }

    // Minimum logical window size. The window opens at exactly this size and
    // can grow freely, but WM_GETMINMAXINFO stops the user from dragging it
    // any smaller. AppWindow sizes are physical pixels, so every use below
    // multiplies by the window's DPI scale — otherwise the limit would be
    // wrong on 125%/150%/200% displays.
    private const int MinWindowWidth = 1200;
    private const int MinWindowHeight = 800;

    private void SetWindowSize()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var scale = GetDpiForWindow(hwnd) / 96.0;
            appWindow.Resize(new Windows.Graphics.SizeInt32(
                (int)(MinWindowWidth * scale),
                (int)(MinWindowHeight * scale)));

            // AppWindow has no MinWidth/MinHeight, so enforce the floor at the
            // Win32 level instead.
            SetWindowSubclass(hwnd, MinSizeSubclassProc, MinSizeSubclassId, IntPtr.Zero);
        }
        catch
        {
            // Non-fatal — fall back to the system default size and no floor.
        }
    }

    // Held in a static field so the delegate isn't collected while the window
    // still points at it.
    private static readonly SubclassProc MinSizeSubclassProc = OnMinSizeMessage;
    private static readonly IntPtr MinSizeSubclassId = 1;

    private static IntPtr OnMinSizeMessage(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_NCDESTROY)
        {
            // Standard cleanup for SetWindowSubclass.
            RemoveWindowSubclass(hWnd, MinSizeSubclassProc, uIdSubclass);
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        var result = DefSubclassProc(hWnd, uMsg, wParam, lParam);

        // Apply the floor *after* the rest of the chain ran: DefWindowProc
        // fills in its own defaults for MINMAXINFO, so setting it beforehand
        // would get overwritten.
        if (uMsg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var scale = GetDpiForWindow(hWnd) / 96.0;
            info.ptMinTrackSize.X = (int)(MinWindowWidth * scale);
            info.ptMinTrackSize.Y = (int)(MinWindowHeight * scale);
            Marshal.StructureToPtr(info, lParam, false);
        }

        return result;
    }

    private const uint WM_GETMINMAXINFO = 0x0024;
    private const uint WM_NCDESTROY = 0x0082;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigate) return;
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;

        Type? target = tag switch
        {
            "dedup" => typeof(DedupPage),
            "clone" => typeof(ClonePage),
            "junction" => typeof(JunctionPage),
            "inspector" => typeof(InspectorPage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };

        // Skip re-navigation to the current page — Navigate() creates a new
        // Page + ViewModel each time, orphaning any scan already in flight.
        if (target is null || ContentFrame.SourcePageType == target) return;
        ContentFrame.Navigate(target);
    }

    private void ThemeToggleItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (Content is FrameworkElement root)
        {
            var current = root.ActualTheme;
            var next = current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            root.RequestedTheme = next;
            ThemeToggleIcon.Glyph = next == ElementTheme.Dark ? "\uE706" : "\uE708";
            ThemeToggleItem.Content = next == ElementTheme.Dark ? "浅色模式" : "深色模式";
        }
    }
}
