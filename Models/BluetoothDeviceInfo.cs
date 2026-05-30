using CommunityToolkit.Mvvm.ComponentModel;

namespace oru.Models;

public sealed class BluetoothDeviceInfo : ObservableObject
{
    private bool _isConnectionInProgress;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required bool IsConnected { get; init; }

    public bool IsConnectionInProgress
    {
        get => _isConnectionInProgress;
        set
        {
            if (SetProperty(ref _isConnectionInProgress, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(CanConnect));
            }
        }
    }

    public string StatusLabel
    {
        get
        {
            if (IsConnectionInProgress)
            {
                return IsConnected ? "Checking connection..." : "Connecting...";
            }

            return IsConnected ? "Connected" : "Disconnected";
        }
    }

    public string ConnectButtonText
    {
        get
        {
            if (IsConnectionInProgress)
            {
                return IsConnected ? "Checking" : "Connecting";
            }

            return IsConnected ? "Connected" : "Connect";
        }
    }

    public bool CanConnect => !IsConnected && !IsConnectionInProgress;

    /// <summary>
    /// Returns a Segoe Fluent Icons glyph string based on audio device type.
    /// </summary>
    public string DeviceIconGlyph
    {
        get
        {
            var loweredName = Name.ToLowerInvariant();

            if (loweredName.Contains("speaker") || loweredName.Contains("soundbar") || loweredName.Contains("boom"))
            {
                return "\uE767"; // Speaker
            }

            if (loweredName.Contains("head") || loweredName.Contains("buds") || loweredName.Contains("ear")
                || loweredName.Contains("pods") || loweredName.Contains("wh-") || loweredName.Contains("wf-"))
            {
                return "\uE7F6"; // Headphones
            }

            // Default: Bluetooth icon to differentiate from system audio
            return "\uE702";
        }
    }
}
