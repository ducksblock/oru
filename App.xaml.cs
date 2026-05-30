using Microsoft.UI.Xaml;
using oru.Services;
using oru.ViewModels;
using oru.Views;

namespace oru;

public partial class App : Application
{
    private TrayIconService? _trayIconService;
    private AudioDeviceService? _audioDeviceService;
    private BluetoothService? _bluetoothService;
    private AudioMirrorService? _audioMirrorService;
    private SettingsService? _settingsService;
    private MainViewModel? _mainViewModel;
    private FlyoutWindow? _flyoutWindow;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _audioDeviceService = new AudioDeviceService();
        _audioDeviceService.DevicesChanged += OnDevicesChanged;

        _bluetoothService = new BluetoothService();

        _audioMirrorService = new AudioMirrorService();

        _settingsService = new SettingsService();

        _mainViewModel = new MainViewModel(_audioDeviceService, _bluetoothService, _audioMirrorService, _settingsService);
        _mainViewModel.SwitchCompleted += OnSwitchCompleted;

        _flyoutWindow = new FlyoutWindow(_mainViewModel, _settingsService);

        _trayIconService = new TrayIconService();
        _trayIconService.TrayActivated += OnTrayActivated;
        _trayIconService.ExitRequested += OnExitRequested;

        _mainViewModel.BeginRefreshInBackground();

        // Show the flyout immediately on launch
        _flyoutWindow.ShowFlyout();
    }

    private void OnTrayActivated()
    {
        if (_flyoutWindow is null || _mainViewModel is null)
        {
            return;
        }

        if (_flyoutWindow.IsFlyoutVisible)
        {
            _flyoutWindow.HideFlyout();
            return;
        }

        _flyoutWindow.ShowFlyout();

        if (_mainViewModel.ShouldRefreshOnOpen)
        {
            _mainViewModel.BeginRefreshInBackground();
        }
    }

    private async void OnSwitchCompleted()
    {
        if (_flyoutWindow is null || !_flyoutWindow.IsFlyoutVisible)
        {
            return;
        }

        await Task.Delay(400);

        if (_flyoutWindow.IsFlyoutVisible)
        {
            _flyoutWindow.HideFlyout();
        }
    }

    private void OnExitRequested()
    {
        Cleanup();
        Environment.Exit(0);
    }

    private void OnDevicesChanged()
    {
        _flyoutWindow?.DispatcherQueue?.TryEnqueue(() =>
        {
            _mainViewModel?.BeginRefreshInBackground();
        });
    }

    private void Cleanup()
    {
        if (_mainViewModel is not null)
        {
            _mainViewModel.SwitchCompleted -= OnSwitchCompleted;
        }

        if (_audioDeviceService is not null)
        {
            _audioDeviceService.DevicesChanged -= OnDevicesChanged;
            _audioDeviceService.Dispose();
        }

        _bluetoothService?.Dispose();
        _audioMirrorService?.Dispose();
        _trayIconService?.Dispose();
    }
}
