using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using oru.Models;
using oru.ViewModels;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace oru.Views;

public sealed partial class FlyoutWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hWnd;

    // Must be stored as a field to prevent GC from collecting the delegate
    // while the subclass is active. Without this, the app will crash.
    private readonly SUBCLASSPROC _subclassProc;

    private readonly ViewModels.SettingsViewModel _settingsViewModel;

    public FlyoutWindow(MainViewModel viewModel, Services.SettingsService settingsService)
    {
        InitializeComponent();

        ViewModel = viewModel;
        ViewModel.SetDispatcherQueue(DispatcherQueue);
        RootGrid.DataContext = ViewModel;

        _settingsViewModel = new ViewModels.SettingsViewModel(settingsService);
        _settingsViewModel.ThemeChanged += OnSettingsThemeChanged;
        SettingsOverlay.DataContext = _settingsViewModel;
        SettingsOverlay.BackRequested += OnSettingsBackRequested;

        _hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        try
        {
            var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "oru.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Icon load failed — continue without custom window icon
        }

        // ── Subclass the window to intercept non-client messages ──
        // This is the ONLY reliable way to remove the DWM border:
        // we tell Windows that the non-client area is 0px.
        _subclassProc = SubclassWndProc;
        SetWindowSubclass(_hWnd, _subclassProc, UIntPtr.Zero, UIntPtr.Zero);

        ApplySurfaceTheme(settingsService.SurfaceTheme, persist: false);
        ConfigureWindowChrome();

        Activated += OnWindowActivated;
    }

    public MainViewModel ViewModel { get; }

    public bool IsFlyoutVisible { get; private set; }

    public void ShowFlyout()
    {
        PositionNearTray();
        _appWindow.Show();
        this.Activate(); // Ensure window is actually focused so Deactivated triggers later
        IsFlyoutVisible = true;
    }

    public void HideFlyout()
    {
        _appWindow.Hide();
        IsFlyoutVisible = false;
    }

    // ────────────────────────────────────────────────────────────
    //  Window chrome: use WinUI 3 APIs only, no Win32 style hacks
    // ────────────────────────────────────────────────────────────

    private void ConfigureWindowChrome()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        _appWindow.SetPresenter(presenter);

        // Hide from taskbar and Alt+Tab
        var exStyle = GetWindowLongPtr(_hWnd, GWL_EXSTYLE);
        exStyle = (nint)((long)exStyle | WS_EX_TOOLWINDOW);
        exStyle = (nint)((long)exStyle & ~WS_EX_APPWINDOW);
        SetWindowLongPtr(_hWnd, GWL_EXSTYLE, exStyle);

        // DWM: rounded corners + dark mode
        var roundedCorners = (int)DwmWindowCornerPreference.Round;
        DwmSetWindowAttribute(_hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundedCorners, sizeof(int));

        var darkMode = 1;
        DwmSetWindowAttribute(_hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        _appWindow.Hide();
    }

    // ────────────────────────────────────────────────────────────
    //  WndProc subclass: the actual border removal mechanism
    // ────────────────────────────────────────────────────────────

    private IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        switch (msg)
        {
            case WM_ACTIVATE:
                // WA_INACTIVE is 0
                if ((wParam.ToInt32() & 0xFFFF) == 0)
                {
                    if (IsFlyoutVisible)
                    {
                        HideFlyout();
                    }
                }
                break;

            case WM_NCCALCSIZE:
                // Returning 0 tells Windows: "my non-client area is 0px on all sides."
                // This eliminates the DWM border entirely because there is no
                // non-client frame left for DWM to render.
                if (wParam != IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
                break;

            case WM_NCPAINT:
                // Suppress non-client painting — we have no non-client area to paint.
                return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    // ────────────────────────────────────────────────────────────
    //  Positioning
    // ────────────────────────────────────────────────────────────

    private void PositionNearTray()
    {
        var cursor = Forms.Control.MousePosition;
        var screen = Forms.Screen.FromPoint(cursor);
        var workingArea = screen.WorkingArea;
        var screenBounds = screen.Bounds;
        var dpi = GetDpiForWindow(_hWnd);
        var scale = dpi / 96.0;

        var flyoutWidthPx = (int)(360 * scale);
        var flyoutHeightPx = (int)(430 * scale);
        var marginPx = (int)(12 * scale);

        var edge = DetectTaskbarEdge(screenBounds, workingArea);

        int left, top;

        switch (edge)
        {
            case TaskbarEdge.Top:
                left = workingArea.Right - flyoutWidthPx - marginPx;
                top = workingArea.Top + marginPx;
                break;
            case TaskbarEdge.Left:
                left = workingArea.Left + marginPx;
                top = workingArea.Bottom - flyoutHeightPx - marginPx;
                break;
            case TaskbarEdge.Right:
            default:
                left = workingArea.Right - flyoutWidthPx - marginPx;
                top = workingArea.Bottom - flyoutHeightPx - marginPx;
                break;
        }

        left = Math.Max(workingArea.Left + marginPx, Math.Min(left, workingArea.Right - flyoutWidthPx - marginPx));
        top = Math.Max(workingArea.Top + marginPx, Math.Min(top, workingArea.Bottom - flyoutHeightPx - marginPx));

        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(left, top, flyoutWidthPx, flyoutHeightPx));
    }

    // ────────────────────────────────────────────────────────────
    //  Event handlers
    // ────────────────────────────────────────────────────────────

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && IsFlyoutVisible)
        {
            HideFlyout();
        }
    }

    private async void OnAudioDeviceCardPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AudioDeviceInfo device)
        {
            if (!device.IsAvailable)
            {
                ViewModel.ErrorMessage = "This device is disconnected";
                return;
            }

            if (device.IsActive || device.ShowAsActive)
            {
                return;
            }

            if (device.IsBuiltInSpeaker)
            {
                return;
            }

            if (ViewModel.IsMirroring)
            {
                ViewModel.ErrorMessage = "Stop mirroring before switching devices.";
                return;
            }

            ViewModel.SelectedDevice = device;
            await ViewModel.ActivateSelectedDeviceCommand.ExecuteAsync(null);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel.MinimizeToTrayOnClose)
        {
            HideFlyout();
        }
        else
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    private void OnCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("CardBackgroundFillColorSecondaryBrush", out var brush))
            {
                border.Background = (Microsoft.UI.Xaml.Media.Brush)brush;
            }
        }
    }

    private void OnCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var brush))
            {
                border.Background = (Microsoft.UI.Xaml.Media.Brush)brush;
            }
        }
    }

    private async void OnBluetoothToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            if (toggle.IsOn != ViewModel.IsBluetoothOn)
            {
                await ViewModel.ToggleBluetoothCommand.ExecuteAsync(null);
            }
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        FlyoutSurface.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        SettingsOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private void OnSettingsBackRequested()
    {
        SettingsOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        FlyoutSurface.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private void OnSettingsThemeChanged(string theme)
    {
        ApplySurfaceTheme(theme);
    }

    // ────────────────────────────────────────────────────────────
    //  Theme
    // ────────────────────────────────────────────────────────────

    private void ApplySurfaceTheme(string theme, bool persist = true)
    {
        SystemBackdrop = theme == "Mica"
            ? new MicaBackdrop()
            : new DesktopAcrylicBackdrop();

        // Surface is always transparent — the SystemBackdrop provides the visual
        FlyoutSurface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    // ────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────

    private static TaskbarEdge DetectTaskbarEdge(System.Drawing.Rectangle screenBounds, System.Drawing.Rectangle workingArea)
    {
        if (workingArea.Top > screenBounds.Top) return TaskbarEdge.Top;
        if (workingArea.Left > screenBounds.Left) return TaskbarEdge.Left;
        if (workingArea.Right < screenBounds.Right) return TaskbarEdge.Right;
        return TaskbarEdge.Bottom;
    }

    private enum TaskbarEdge { Left, Top, Right, Bottom }
    private enum DwmWindowCornerPreference { Default = 0, DoNotRound = 1, Round = 2, RoundSmall = 3 }

    // ────────────────────────────────────────────────────────────
    //  Win32 interop — only what we actually need
    // ────────────────────────────────────────────────────────────

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint WM_NCPAINT = 0x0085;
    private const uint WM_ACTIVATE = 0x0006;

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
