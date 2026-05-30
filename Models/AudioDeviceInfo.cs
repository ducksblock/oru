namespace oru.Models;

public sealed class AudioDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required bool IsInput { get; init; }

    public required bool IsAvailable { get; init; }

    public bool IsActive { get; init; }

    /// <summary>
    /// Set to true when audio mirroring is active and this device is receiving mirrored audio.
    /// </summary>
    public bool IsMirrorTarget { get; set; }

    /// <summary>
    /// Set during merge when this audio endpoint matches a known Bluetooth device name.
    /// Used to filter BT endpoints out of the "System Audio" UI section.
    /// </summary>
    public bool IsBluetoothEndpoint { get; set; }

    /// <summary>
    /// True when this device is either the system default or a mirror target.
    /// Used by the UI to show the "Active" pill badge.
    /// </summary>
    public bool ShowAsActive => IsActive || IsMirrorTarget;

    public bool IsBuiltInSpeaker
    {
        get
        {
            var loweredName = Name.ToLowerInvariant();
            if (loweredName.Contains("head") || loweredName.Contains("buds") || loweredName.Contains("ear") || loweredName.Contains("usb"))
            {
                return false;
            }
            if (loweredName.Contains("speaker") || loweredName.Contains("realtek") || loweredName.Contains("hdmi") || loweredName.Contains("monitor") || loweredName.Contains("display") || loweredName.Contains("tv"))
            {
                return true;
            }
            return false;
        }
    }

    public bool IsCardOpaque => !IsBuiltInSpeaker && ShowAsActive;
    public bool ShowSwitchButton => !ShowAsActive && !IsBuiltInSpeaker;
    public bool ShowBuiltInLabel => IsBuiltInSpeaker;
    public bool ShowActivePill => ShowAsActive && !IsBuiltInSpeaker;

    public bool IsBluetooth => IsBluetoothEndpoint || Name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

    public string StatusLabel
    {
        get
        {
            if (IsBuiltInSpeaker)
            {
                return IsAvailable ? "Ready" : "Disconnected";
            }

            if (IsMirrorTarget && !IsActive)
            {
                return "Mirroring";
            }

            if (IsActive)
            {
                return IsMirrorTarget ? "Active · Mirroring" : "Active";
            }

            return IsAvailable ? "Ready" : "Disconnected";
        }
    }

    /// <summary>
    /// Returns a Segoe Fluent Icons glyph string based on device type.
    /// </summary>
    public string DeviceIconGlyph
    {
        get
        {
            if (IsInput)
            {
                return "\uE720"; // Microphone
            }

            var loweredName = Name.ToLowerInvariant();

            if (loweredName.Contains("bluetooth") || loweredName.Contains("airpods"))
            {
                return "\uE7F6"; // Bluetooth headset
            }

            if (loweredName.Contains("head") || loweredName.Contains("buds") || loweredName.Contains("ear"))
            {
                return "\uE7F6"; // Headphones
            }

            if (loweredName.Contains("hdmi") || loweredName.Contains("display") || loweredName.Contains("monitor") || loweredName.Contains("tv"))
            {
                return "\uE7F4"; // Display/Monitor
            }

            if (loweredName.Contains("usb"))
            {
                return "\uE88E"; // USB
            }

            return "\uE767"; // Speaker
        }
    }

    public string KindTag
    {
        get
        {
            if (IsInput)
            {
                return "IN";
            }

            var loweredName = Name.ToLowerInvariant();

            if (loweredName.Contains("bluetooth"))
            {
                return "BT";
            }

            if (loweredName.Contains("head") || loweredName.Contains("buds") || loweredName.Contains("ear"))
            {
                return "HP";
            }

            if (loweredName.Contains("hdmi") || loweredName.Contains("display") || loweredName.Contains("monitor") || loweredName.Contains("tv"))
            {
                return "HD";
            }

            return "SP";
        }
    }
}
