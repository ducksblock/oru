using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using oru.Models;

namespace oru.Services;

public sealed class BluetoothService : IDisposable
{
    private DeviceWatcher? _classicWatcher;
    private DeviceWatcher? _bleWatcher;
    private Radio? _bluetoothRadio;
    private readonly object _lock = new();
    private readonly Dictionary<string, BluetoothDeviceInfo> _devices = new();

    // Debounce: coalesce rapid-fire watcher events into one UI update
    private Timer? _debounceTimer;
    private const int DebounceMs = 80;

    public event Action? DevicesChanged;
    public event Action? RadioStateChanged;

    public bool IsBluetoothOn { get; private set; }

    public IReadOnlyList<BluetoothDeviceInfo> GetPairedDevices()
    {
        if (!IsBluetoothOn)
        {
            return Array.Empty<BluetoothDeviceInfo>();
        }

        lock (_lock)
        {
            return _devices.Values
                .Where(d => IsAudioDevice(d.Name))
                .OrderByDescending(d => d.IsConnected)
                .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// Returns true if the device name suggests it's an audio device.
    /// Non-audio devices (mice, keyboards, controllers, etc.) are excluded.
    /// </summary>
    private static bool IsAudioDevice(string name)
    {
        var lower = name.ToLowerInvariant();

        // Explicitly exclude non-audio devices
        if (lower.Contains("mouse") || lower.Contains("trackpad")
            || lower.Contains("keyboard") || lower.Contains("kb")
            || lower.Contains("gamepad") || lower.Contains("controller")
            || lower.Contains("xbox") || lower.Contains("dualsense") || lower.Contains("dualshock")
            || lower.Contains("pen") || lower.Contains("stylus"))
        {
            return false;
        }

        // Include known audio device patterns
        if (lower.Contains("head") || lower.Contains("buds") || lower.Contains("ear")
            || lower.Contains("pods") || lower.Contains("speaker") || lower.Contains("soundbar")
            || lower.Contains("boom") || lower.Contains("audio") || lower.Contains("sound")
            || lower.Contains("wh-") || lower.Contains("wf-") || lower.Contains("wf1000")
            || lower.Contains("beats") || lower.Contains("jbl") || lower.Contains("bose")
            || lower.Contains("sony") || lower.Contains("sennheiser") || lower.Contains("jabra")
            || lower.Contains("marshall") || lower.Contains("bang") || lower.Contains("harman")
            || lower.Contains("anker") || lower.Contains("soundcore")
            || lower.Contains("airpods") || lower.Contains("pixel buds")
            || lower.Contains("galaxy buds") || lower.Contains("freebuds")
            || lower.Contains("momentum") || lower.Contains("quietcomfort")
            || lower.Contains("xm4") || lower.Contains("xm5")
            || lower.Contains("nord") || lower.Contains("oneplus"))
        {
            return true;
        }

        // Default: EXCLUDE to prevent weird non-audio BLE devices from showing up
        // and causing infinite loading hangs when Connect is triggered on dummy devices.
        return false;
    }

    public async Task InitializeAsync()
    {
        await InitializeRadioAsync();
        StartDeviceWatchers();
    }

    public async Task SetBluetoothStateAsync(bool turnOn)
    {
        if (_bluetoothRadio is null) return;

        try
        {
            var targetState = turnOn ? RadioState.On : RadioState.Off;
            var accessStatus = await _bluetoothRadio.SetStateAsync(targetState);
            if (accessStatus == RadioAccessStatus.Allowed)
            {
                ApplyRadioState(turnOn);
            }
            else
            {
                DebugLogService.Write($"Bluetooth toggle returned: {accessStatus}");
            }
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"Bluetooth toggle failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task<bool> ConnectDeviceAsync(string deviceId)
    {
        try
        {
            using var device = await BluetoothDevice.FromIdAsync(deviceId);
            if (device is null)
            {
                DebugLogService.Write($"Bluetooth device lookup returned null: {deviceId}");
                return false;
            }

            // Uncached service request forces the OS to wake and connect to the device.
            // This is a known UWP trick to trigger an implicit connection.
            await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
            return true;
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"Failed to trigger Bluetooth connection: {ex.Message}");
            return false;
        }
    }

    private async Task InitializeRadioAsync()
    {
        try
        {
            var accessStatus = await Radio.RequestAccessAsync();
            if (accessStatus != RadioAccessStatus.Allowed)
            {
                DebugLogService.Write($"Bluetooth radio access denied: {accessStatus}");
                return;
            }

            var radios = await Radio.GetRadiosAsync();
            _bluetoothRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

            if (_bluetoothRadio is not null)
            {
                IsBluetoothOn = _bluetoothRadio.State == RadioState.On;
                _bluetoothRadio.StateChanged += OnRadioStateChanged;
            }
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"Bluetooth radio init failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        ApplyRadioState(sender.State == RadioState.On);
    }

    private void ApplyRadioState(bool isOn)
    {
        IsBluetoothOn = isOn;

        if (!IsBluetoothOn)
        {
            lock (_lock)
            {
                _devices.Clear();
            }
        }

        RadioStateChanged?.Invoke();
        RaiseDevicesChangedDebounced();
    }

    private void StartDeviceWatchers()
    {
        try
        {
            // ── Classic Bluetooth paired devices ──
            var classicSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var classicRequestedProperties = new[]
            {
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.DeviceAddress"
            };

            _classicWatcher = DeviceInformation.CreateWatcher(
                classicSelector,
                classicRequestedProperties,
                DeviceInformationKind.AssociationEndpoint);

            _classicWatcher.Added += OnDeviceAdded;
            _classicWatcher.Updated += OnDeviceUpdated;
            _classicWatcher.Removed += OnDeviceRemoved;
            _classicWatcher.EnumerationCompleted += OnEnumerationCompleted;
            _classicWatcher.Stopped += OnWatcherStopped;
            _classicWatcher.Start();

            // ── BLE paired devices ──
            var bleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var bleRequestedProperties = new[]
            {
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.DeviceAddress"
            };

            _bleWatcher = DeviceInformation.CreateWatcher(
                bleSelector,
                bleRequestedProperties,
                DeviceInformationKind.AssociationEndpoint);

            _bleWatcher.Added += OnDeviceAdded;
            _bleWatcher.Updated += OnDeviceUpdated;
            _bleWatcher.Removed += OnDeviceRemoved;
            _bleWatcher.EnumerationCompleted += OnEnumerationCompleted;
            _bleWatcher.Stopped += OnWatcherStopped;
            _bleWatcher.Start();
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"Bluetooth watcher start failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
    {
        if (string.IsNullOrWhiteSpace(info.Name)) return;

        var device = CreateDeviceInfo(info);

        lock (_lock)
        {
            _devices[info.Id] = device;
        }

        RaiseDevicesChangedDebounced();
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(update.Id, out var existing)) return;

            var isConnected = existing.IsConnected;
            if (update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected))
            {
                isConnected = connected is true;
            }

            var name = existing.Name;
            if (update.Properties.TryGetValue("System.ItemNameDisplay", out var nameObj) && nameObj is string n && !string.IsNullOrWhiteSpace(n))
            {
                name = n;
            }

            _devices[update.Id] = new BluetoothDeviceInfo
            {
                Id = existing.Id,
                Name = name,
                IsConnected = isConnected
            };
        }

        RaiseDevicesChangedDebounced();
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_lock)
        {
            _devices.Remove(update.Id);
        }

        RaiseDevicesChangedDebounced();
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        RaiseDevicesChangedDebounced();
    }

    private void OnWatcherStopped(DeviceWatcher sender, object args)
    {
        // Auto-restart the watcher if it stops unexpectedly
        if (sender == _classicWatcher && _classicWatcher.Status == DeviceWatcherStatus.Stopped)
        {
            try { _classicWatcher.Start(); }
            catch (Exception ex)
            {
                DebugLogService.Write($"Classic watcher restart failed: {ex.Message}");
            }
        }
        else if (sender == _bleWatcher && _bleWatcher.Status == DeviceWatcherStatus.Stopped)
        {
            try { _bleWatcher.Start(); }
            catch (Exception ex)
            {
                DebugLogService.Write($"BLE watcher restart failed: {ex.Message}");
            }
        }
    }

    private void RaiseDevicesChangedDebounced()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ => DevicesChanged?.Invoke(), null, DebounceMs, Timeout.Infinite);
    }

    private static BluetoothDeviceInfo CreateDeviceInfo(DeviceInformation info)
    {
        var isConnected = false;
        if (info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected))
        {
            isConnected = connected is true;
        }

        return new BluetoothDeviceInfo
        {
            Id = info.Id,
            Name = info.Name,
            IsConnected = isConnected
        };
    }


    private static void StopWatcher(DeviceWatcher? watcher)
    {
        if (watcher is null) return;
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            try { watcher.Stop(); }
            catch { /* Best-effort cleanup */ }
        }
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();

        if (_classicWatcher is not null)
        {
            _classicWatcher.Added -= OnDeviceAdded;
            _classicWatcher.Updated -= OnDeviceUpdated;
            _classicWatcher.Removed -= OnDeviceRemoved;
            _classicWatcher.EnumerationCompleted -= OnEnumerationCompleted;
            _classicWatcher.Stopped -= OnWatcherStopped;
            StopWatcher(_classicWatcher);
        }

        if (_bleWatcher is not null)
        {
            _bleWatcher.Added -= OnDeviceAdded;
            _bleWatcher.Updated -= OnDeviceUpdated;
            _bleWatcher.Removed -= OnDeviceRemoved;
            _bleWatcher.EnumerationCompleted -= OnEnumerationCompleted;
            _bleWatcher.Stopped -= OnWatcherStopped;
            StopWatcher(_bleWatcher);
        }

        if (_bluetoothRadio is not null)
        {
            _bluetoothRadio.StateChanged -= OnRadioStateChanged;
        }
    }
}
