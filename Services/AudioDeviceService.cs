using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using oru.Models;

namespace oru.Services;

public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly EndpointNotificationClient _notificationClient;

    public AudioDeviceService()
    {
        _notificationClient = new EndpointNotificationClient();
        _notificationClient.DevicesChanged += () => DevicesChanged?.Invoke();
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public event Action? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var defaultRenderId = TryGetDefaultId(_enumerator, DataFlow.Render);
        var devices = EnumerateRenderDevices(_enumerator, defaultRenderId)
            .Where(IsRelevantRenderDevice)
            .ToList();

        DebugLogService.Write($"Enumerated devices. renderDefault={defaultRenderId ?? "none"} total={devices.Count}");

        return devices
            .OrderByDescending(device => device.IsActive)
            .ThenByDescending(device => device.IsAvailable)
            .ThenByDescending(device => device.KindTag == "BT")
            .ThenByDescending(device => device.KindTag == "HP")
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SetDefaultOutputDevice(string deviceId)
    {
        var policyConfig = (IPolicyConfig)new PolicyConfigClientComObject();

        try
        {
            DebugLogService.Write($"Setting default endpoint: {deviceId}");
            foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
            {
                Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(deviceId, role));
            }
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"SetDefaultEndpoint failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            Marshal.ReleaseComObject(policyConfig);
        }
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _enumerator.Dispose();
    }

    private static IEnumerable<AudioDeviceInfo> EnumerateRenderDevices(MMDeviceEnumerator enumerator, string? defaultId)
    {
        MMDeviceCollection collection;
        try
        {
            collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            if (collection.Count == 0)
            {
                collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Unplugged);
            }
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"EnumerateAudioEndPoints failed for render devices: {ex.GetType().Name}: {ex.Message}");
            return [];
        }

        var devices = new List<AudioDeviceInfo>();
        foreach (var endpoint in collection)
        {
            try
            {
                var id = endpoint.ID;
                var name = string.IsNullOrWhiteSpace(endpoint.FriendlyName) ? endpoint.DeviceFriendlyName : endpoint.FriendlyName;
                var isAvailable = endpoint.State.HasFlag(DeviceState.Active);

                devices.Add(new AudioDeviceInfo
                {
                    Id = id,
                    Name = name ?? "Unknown Audio Device",
                    IsInput = false,
                    IsAvailable = isAvailable,
                    IsActive = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch
            {
                DebugLogService.Write("Endpoint parse failed for render devices");
            }
        }

        return devices;
    }

    private static bool IsRelevantRenderDevice(AudioDeviceInfo device)
    {
        if (!device.IsAvailable && !device.IsActive)
        {
            return false;
        }

        var name = (device.Name ?? "Unknown Device").ToLowerInvariant();

        if (name.Contains("microphone") || name.Contains("mic") || name.Contains("line in") || name.Contains("stereo mix"))
        {
            return false;
        }

        if (name.Contains("hands-free"))
        {
            return false;
        }

        // Exclude Bluetooth devices from System Audio - they belong in Bluetooth section
        if (name.Contains("bluetooth") || name.Contains("airpods"))
        {
            return false;
        }

        // We used to whitelist specific names here (speaker, headphone, etc.)
        // But that caused perfectly valid USB DACs, Type-C earphones, or generic "USB Audio Device" to be hidden!
        // It's safer to default to true for output devices.
        // Exclude virtual/junk devices that clutter the list
        if (name.Contains("steam streaming") || name.Contains("nvidia broadcast") || 
            name.Contains("teams audio") || name.Contains("webex") || 
            name.Contains("zoom audio") || name.Contains("sonar") ||
            name.Contains("virtual desktop") || name.Contains("oculus") ||
            name.Contains("realtek digital output") || name.Contains("s/pdif"))
        {
            return false;
        }

        return true;
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            using var endpoint = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return endpoint.ID;
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"Default endpoint lookup failed for {flow}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private sealed class EndpointNotificationClient : IMMNotificationClient
    {
        public event Action? DevicesChanged;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            DevicesChanged?.Invoke();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            DevicesChanged?.Invoke();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            DevicesChanged?.Invoke();
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render)
            {
                DevicesChanged?.Invoke();
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            DevicesChanged?.Invoke();
        }
    }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClientComObject
    {
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat();
        int GetDeviceFormat();
        int ResetDeviceFormat();
        int SetDeviceFormat();
        int GetProcessingPeriod();
        int SetProcessingPeriod();
        int GetShareMode();
        int SetShareMode();
        int GetPropertyValue();
        int SetPropertyValue();
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        int SetEndpointVisibility();
    }
}
