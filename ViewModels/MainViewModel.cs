using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using oru.Models;
using oru.Services;

namespace oru.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AudioDeviceService _audioDeviceService;
    private readonly BluetoothService _bluetoothService;
    private readonly AudioMirrorService _audioMirrorService;
    private readonly SettingsService _settingsService;
    private DispatcherQueue? _dispatcherQueue;
    private DateTimeOffset _lastRefreshAt = DateTimeOffset.MinValue;
    private volatile bool _pendingRefresh;
    private readonly HashSet<string> _connectingBluetoothDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial AudioDeviceInfo? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial string ActiveDeviceName { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasLoadedOnce { get; set; }

    [ObservableProperty]
    public partial bool IsBluetoothOn { get; set; }

    public string BluetoothStatusText => IsBluetoothOn ? "On" : "Off";

    public bool ShowNoBluetoothDevices => IsBluetoothOn && BluetoothDevices.Count == 0;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLoadingIndicator));
    }

    partial void OnIsBluetoothOnChanged(bool value)
    {
        OnPropertyChanged(nameof(BluetoothStatusText));
        OnPropertyChanged(nameof(ShowNoBluetoothDevices));
    }

    public MainViewModel(AudioDeviceService audioDeviceService, BluetoothService bluetoothService, AudioMirrorService audioMirrorService, SettingsService settingsService)
    {
        _audioDeviceService = audioDeviceService;
        _bluetoothService = bluetoothService;
        _audioMirrorService = audioMirrorService;
        _settingsService = settingsService;

        // Defaults for partial properties (can't use inline initializers)
        ActiveDeviceName = "No active device";
        StatusText = "Loading audio devices\u2026";

        _audioDeviceService.DevicesChanged += OnAudioDevicesChanged;
        _bluetoothService.DevicesChanged += OnBluetoothDevicesChanged;
        _bluetoothService.RadioStateChanged += OnBluetoothRadioStateChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;

        _ = _bluetoothService.InitializeAsync();
    }

    private void OnSettingsChanged()
    {
        RunOnUiThread(BeginRefreshInBackground);
    }

    /// <summary>
    /// Must be called from the UI thread after the window is created,
    /// so we can marshal Bluetooth watcher callbacks onto the UI thread.
    /// </summary>
    public void SetDispatcherQueue(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = new();

    /// <summary>
    /// Filtered view: only non-Bluetooth endpoints, for the "System Audio" UI section.
    /// </summary>
    public ObservableCollection<AudioDeviceInfo> SystemDevices { get; } = new();

    public ObservableCollection<BluetoothDeviceInfo> BluetoothDevices { get; } = new();

    public event Action? SwitchCompleted;

    public bool ShowLoadingIndicator => IsBusy && Devices.Count == 0;

    public bool ShouldRefreshOnOpen =>
        !HasLoadedOnce || DateTimeOffset.UtcNow - _lastRefreshAt > TimeSpan.FromSeconds(2);

    public void BeginRefreshInBackground()
    {
        _ = RefreshDevicesAsync();
    }

    [RelayCommand]
    private async Task ManualRefreshAsync()
    {
        await RefreshDevicesAsync();
    }

    public async Task RefreshDevicesAsync()
    {
        if (IsBusy)
        {
            _pendingRefresh = true;
            return;
        }

        IsBusy = true;
        _pendingRefresh = false;
        ErrorMessage = null;

        if (!HasLoadedOnce)
        {
            StatusText = "Loading audio devices\u2026";
        }

        try
        {
            var devices = await Task.Run(_audioDeviceService.GetOutputDevices);

            MergeAudioDevices(devices);

            SelectedDevice = Devices.FirstOrDefault(d => d.IsActive)
                ?? Devices.FirstOrDefault(d => d.IsAvailable)
                ?? Devices.FirstOrDefault();

            ActiveDeviceName = Devices.FirstOrDefault(d => d.IsActive)?.Name ?? "No active device";
            StatusText = Devices.Count == 0 ? "No audio devices found" : string.Empty;
            HasLoadedOnce = true;
            _lastRefreshAt = DateTimeOffset.UtcNow;

            RefreshBluetoothDevices();

            if (_settingsService.AutoResumeMirroring && _intendedToMirror)
            {
                _ = StartMirroringSessionAsync();
            }
        }
        catch
        {
            Devices.Clear();
            SelectedDevice = null;
            ActiveDeviceName = "No active device";
            ErrorMessage = "Couldn\u2019t load audio devices";
            StatusText = "No audio devices found";
            DebugLogService.Write($"RefreshDevices failed. LogPath={DebugLogService.GetLogPath()}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ShowLoadingIndicator));

            // If a device change came in while we were refreshing, do it again
            if (_pendingRefresh)
            {
                _pendingRefresh = false;
                _ = RefreshDevicesAsync();
            }
        }
    }

    [RelayCommand]
    private async Task ActivateSelectedDeviceAsync()
    {
        if (SelectedDevice is null || IsBusy)
        {
            return;
        }

        if (!SelectedDevice.IsAvailable)
        {
            ErrorMessage = "This device is disconnected";
            return;
        }

        if (SelectedDevice.IsActive)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        var switched = false;

        try
        {
            await Task.Run(() => _audioDeviceService.SetDefaultOutputDevice(SelectedDevice.Id));
            switched = true;
        }
        catch
        {
            ErrorMessage = "Couldn\u2019t switch output";
            DebugLogService.Write($"ActivateSelectedDevice failed. LogPath={DebugLogService.GetLogPath()}");
        }
        finally
        {
            IsBusy = false;
        }

        if (switched)
        {
            await RefreshDevicesAsync();
            SwitchCompleted?.Invoke();
        }
    }

    [RelayCommand]
    private async Task ToggleBluetoothAsync()
    {
        var newState = !IsBluetoothOn;
        await _bluetoothService.SetBluetoothStateAsync(newState);
        // The RadioStateChanged event will update IsBluetoothOn
    }

    [RelayCommand]
    private async Task ToggleBluetoothDeviceAsync(BluetoothDeviceInfo device)
    {
        if (device is null || device.IsConnected || _connectingBluetoothDeviceIds.Contains(device.Id))
        {
            return;
        }

        SetBluetoothDeviceConnectionInProgress(device.Id, true);
        ErrorMessage = null;

        try
        {
            var started = await _bluetoothService.ConnectDeviceAsync(device.Id);
            if (!started)
            {
                ErrorMessage = "Couldn't start Bluetooth connection";
                return;
            }

            var timeoutAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);
            while (DateTimeOffset.UtcNow < timeoutAt)
            {
                await Task.Delay(500);
                RefreshBluetoothDevices();

                var current = BluetoothDevices.FirstOrDefault(d => d.Id == device.Id);
                if (current?.IsConnected == true)
                {
                    return;
                }
            }

            ErrorMessage = "Bluetooth connection is still pending in Windows";
        }
        finally
        {
            SetBluetoothDeviceConnectionInProgress(device.Id, false);
            RefreshBluetoothDevices();
        }
    }

    public bool CanMirrorAudio
    {
        get
        {
            if (IsMirroring) return true;
            return Devices.Count(d => d.IsAvailable && !d.IsBuiltInSpeaker) >= 2;
        }
    }
    public bool IsMirroring => _audioMirrorService.IsMirroring;
    public bool ShowDeviceList => true; // Always show device list now
    public string MergeAudioButtonText => IsMirroring ? "Stop Mirroring" : "Mirror Audio";
    public string MirrorSourceName { get; private set; } = "";
    public string MirrorTargetNames { get; private set; } = "";
    public int MirrorTargetCount { get; private set; } = 0;
    public bool HasSingleMirrorTarget => MirrorTargetCount == 1;
    public bool HasMultipleMirrorTargets => MirrorTargetCount > 1;
    public ObservableCollection<AudioDeviceInfo> MirrorTargets { get; } = new();

    private bool _intendedToMirror;

    [RelayCommand]
    private async Task MergeAudioAsync()
    {
        ErrorMessage = null;

        if (IsMirroring)
        {
            _intendedToMirror = false;
            StopMirroringSession();
            return;
        }

        _intendedToMirror = true;
        await StartMirroringSessionAsync();
    }

    private void StopMirroringSession()
    {
        _audioMirrorService.StopMirroring();
        ClearMirrorFlags();
        MirrorSourceName = "";
        MirrorTargetNames = "";
        MirrorTargetCount = 0;
        MirrorTargets.Clear();
        UpdateMirrorProperties();
    }

    private async Task StartMirroringSessionAsync()
    {
        var sourceDevice = Devices.FirstOrDefault(d => d.IsActive && !d.IsBuiltInSpeaker);
        if (sourceDevice is null)
        {
            if (!_settingsService.AutoResumeMirroring) ErrorMessage = "No active external device to capture audio from.";
            StopMirroringSession();
            return;
        }

        var targetIds = Devices
            .Where(d => d.IsAvailable && !d.IsActive && !d.IsBuiltInSpeaker)
            .Select(d => d.Id)
            .ToList();

        if (targetIds.Count == 0)
        {
            if (!_settingsService.AutoResumeMirroring) ErrorMessage = "No available target devices to mirror audio to.";
            StopMirroringSession();
            return;
        }

        try
        {
            _audioMirrorService.StopMirroring();
            ClearMirrorFlags();

            await Task.Run(() => _audioMirrorService.StartMirroring(sourceDevice.Id, targetIds));

            var targetDevices = Devices.Where(d => targetIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();

            MirrorSourceName = sourceDevice.Name;
            MirrorTargetNames = string.Join(", ", targetDevices.Select(d => d.Name));
            MirrorTargetCount = targetDevices.Count;
            MirrorTargets.Clear();
            foreach (var t in targetDevices) MirrorTargets.Add(t);

            foreach (var device in Devices)
            {
                if (targetIds.Contains(device.Id, StringComparer.OrdinalIgnoreCase) || device.IsActive)
                {
                    device.IsMirrorTarget = true;
                }
            }

            for (int i = 0; i < Devices.Count; i++)
            {
                var d = Devices[i];
                Devices[i] = d;
            }

            UpdateMirrorProperties();
        }
        catch (Exception)
        {
            ErrorMessage = "Failed to start audio mirroring engine. Formats may not match.";
            DebugLogService.Write("Failed to start audio mirroring.");
            StopMirroringSession();
        }
    }

    private void UpdateMirrorProperties()
    {
        OnPropertyChanged(nameof(IsMirroring));
        OnPropertyChanged(nameof(ShowDeviceList));
        OnPropertyChanged(nameof(MergeAudioButtonText));
        OnPropertyChanged(nameof(MirrorSourceName));
        OnPropertyChanged(nameof(MirrorTargetNames));
        OnPropertyChanged(nameof(MirrorTargetCount));
        OnPropertyChanged(nameof(HasSingleMirrorTarget));
        OnPropertyChanged(nameof(HasMultipleMirrorTargets));
        OnPropertyChanged(nameof(CanMirrorAudio));
    }

    private void ClearMirrorFlags()
    {
        foreach (var device in Devices)
        {
            device.IsMirrorTarget = false;
        }

        // Force UI refresh
        for (int i = 0; i < Devices.Count; i++)
        {
            var d = Devices[i];
            Devices[i] = d;
        }
    }

    private void RefreshBluetoothDevices()
    {
        IsBluetoothOn = _bluetoothService.IsBluetoothOn;

        if (!IsBluetoothOn)
        {
            BluetoothDevices.Clear();
            OnPropertyChanged(nameof(ShowNoBluetoothDevices));
            return;
        }

        var btDevices = _bluetoothService.GetPairedDevices();
        
        if (!_settingsService.ShowDisconnectedBluetooth)
        {
            btDevices = btDevices.Where(d => d.IsConnected || d.IsConnectionInProgress).ToList();
        }

        MergeBluetoothDevices(btDevices);
    }

    /// <summary>
    /// Diff-merge audio devices so the list updates in-place without flicker.
    /// </summary>
    private void MergeAudioDevices(IReadOnlyList<AudioDeviceInfo> fresh)
    {
        var freshById = fresh.ToDictionary(d => d.Id);

        // Remove devices no longer present
        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            if (!freshById.ContainsKey(Devices[i].Id))
            {
                Devices.RemoveAt(i);
            }
        }

        // Add or update
        for (int i = 0; i < fresh.Count; i++)
        {
            var target = fresh[i];
            var existingIdx = -1;
            for (int j = 0; j < Devices.Count; j++)
            {
                if (Devices[j].Id == target.Id)
                {
                    existingIdx = j;
                    break;
                }
            }

            if (existingIdx < 0)
            {
                Devices.Insert(Math.Min(i, Devices.Count), target);
            }
            else if (Devices[existingIdx].IsActive != target.IsActive
                     || Devices[existingIdx].IsAvailable != target.IsAvailable
                     || Devices[existingIdx].Name != target.Name)
            {
                Devices[existingIdx] = target;
            }
        }

        // Cross-reference: flag audio endpoints that match known Bluetooth device names
        var btNames = BluetoothDevices.Select(b => b.Name.ToLowerInvariant()).ToHashSet();
        foreach (var device in Devices)
        {
            var lowered = device.Name.ToLowerInvariant();
            device.IsBluetoothEndpoint = btNames.Any(bt => lowered.Contains(bt) || bt.Contains(lowered))
                || lowered.Contains("bluetooth");
        }

        // Rebuild the filtered SystemDevices collection (wired-only for UI)
        RebuildSystemDevices();

        OnPropertyChanged(nameof(CanMirrorAudio));
    }

    private void RebuildSystemDevices()
    {
        var targetList = Devices.Where(d => !d.IsBluetoothEndpoint).ToList();

        // O(N) synchronization since targetList is already sorted
        for (int i = 0; i < targetList.Count; i++)
        {
            var target = targetList[i];

            if (i < SystemDevices.Count)
            {
                if (SystemDevices[i].Id == target.Id)
                {
                    // Force replace to trigger UI update for property changes
                    SystemDevices[i] = target;
                }
                else
                {
                    // Different item at this position, insert new one
                    SystemDevices.Insert(i, target);
                }
            }
            else
            {
                // Appending new items
                SystemDevices.Add(target);
            }
        }

        // Clean up any remaining trailing elements
        while (SystemDevices.Count > targetList.Count)
        {
            SystemDevices.RemoveAt(SystemDevices.Count - 1);
        }
    }

    /// <summary>
    /// Diff-merge Bluetooth devices so the list updates in-place without flicker.
    /// </summary>
    private void MergeBluetoothDevices(IReadOnlyList<BluetoothDeviceInfo> fresh)
    {
        var freshById = fresh.ToDictionary(d => d.Id);

        // Remove devices no longer present
        for (int i = BluetoothDevices.Count - 1; i >= 0; i--)
        {
            if (!freshById.ContainsKey(BluetoothDevices[i].Id))
            {
                BluetoothDevices.RemoveAt(i);
            }
        }

        // Add or update
        for (int i = 0; i < fresh.Count; i++)
        {
            var target = fresh[i];
            ApplyBluetoothConnectionState(target);

            var existingIdx = -1;
            for (int j = 0; j < BluetoothDevices.Count; j++)
            {
                if (BluetoothDevices[j].Id == target.Id)
                {
                    existingIdx = j;
                    break;
                }
            }

            if (existingIdx < 0)
            {
                BluetoothDevices.Insert(Math.Min(i, BluetoothDevices.Count), target);
            }
            else if (BluetoothDevices[existingIdx].IsConnected != target.IsConnected
                     || BluetoothDevices[existingIdx].Name != target.Name
                     || BluetoothDevices[existingIdx].IsConnectionInProgress != target.IsConnectionInProgress)
            {
                BluetoothDevices[existingIdx] = target;
            }
        }

        OnPropertyChanged(nameof(ShowNoBluetoothDevices));
    }

    private void SetBluetoothDeviceConnectionInProgress(string deviceId, bool isInProgress)
    {
        if (isInProgress)
        {
            _connectingBluetoothDeviceIds.Add(deviceId);
        }
        else
        {
            _connectingBluetoothDeviceIds.Remove(deviceId);
        }

        var existing = BluetoothDevices.FirstOrDefault(d => d.Id == deviceId);
        if (existing is not null)
        {
            existing.IsConnectionInProgress = isInProgress;
        }
    }

    private void ApplyBluetoothConnectionState(BluetoothDeviceInfo device)
    {
        if (device.IsConnected)
        {
            _connectingBluetoothDeviceIds.Remove(device.Id);
        }

        device.IsConnectionInProgress = _connectingBluetoothDeviceIds.Contains(device.Id);
    }

    private void OnBluetoothDevicesChanged()
    {
        RunOnUiThread(RefreshBluetoothDevices);
    }

    private void OnBluetoothRadioStateChanged()
    {
        RunOnUiThread(() =>
        {
            IsBluetoothOn = _bluetoothService.IsBluetoothOn;
            RefreshBluetoothDevices();
        });
    }

    private void OnAudioDevicesChanged()
    {
        RunOnUiThread(BeginRefreshInBackground);
    }

    /// <summary>
    /// Dispatches an action to the UI thread if possible, or runs it directly
    /// if we're already on the UI thread or no dispatcher is set.
    /// </summary>
    private void RunOnUiThread(Action action)
    {
        if (_dispatcherQueue is null)
        {
            action();
            return;
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }
}
