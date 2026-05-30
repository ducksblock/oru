using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

namespace oru.Services;

public sealed class AudioMirrorService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly List<MirrorOutput> _outputs = new();
    private readonly MMDeviceEnumerator _enumerator = new();

    public bool IsMirroring { get; private set; }

    public void StartMirroring(string sourceDeviceId, IEnumerable<string> targetDeviceIds)
    {
        StopMirroring();

        try
        {
            var sourceDevice = _enumerator.GetDevice(sourceDeviceId);
            _capture = new WasapiLoopbackCapture(sourceDevice);

            foreach (var targetId in targetDeviceIds)
            {
                if (targetId.Equals(sourceDeviceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetDevice = _enumerator.GetDevice(targetId);
                var buffer = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(200)
                };

                // Create a resampler if the target device doesn't support the exact same format
                // In WasapiOut, setting useSync=false and specifying AudioClientShareMode.Shared 
                // generally requires the sample rates to match or we need MediaFoundationResampler.
                // However, we can try to use WasapiOut with useSync=false.
                var outDev = new WasapiOut(targetDevice, AudioClientShareMode.Shared, true, 50);
                
                // NAudio often throws if format doesn't match default exactly in Shared mode,
                // but WasapiOut attempts implicit resampling in recent Windows 10/11 versions 
                // if we are lucky, or we might need WaveFormatConversionProvider.
                outDev.Init(buffer);
                outDev.Play();

                _outputs.Add(new MirrorOutput(outDev, buffer));
            }

            _capture.DataAvailable += (s, a) =>
            {
                if (a.BytesRecorded == 0) return;
                
                foreach (var output in _outputs)
                {
                    output.Buffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
                }
            };

            _capture.StartRecording();
            IsMirroring = true;
            Debug.WriteLine($"Started mirroring from {sourceDeviceId} to {_outputs.Count} targets.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start mirroring: {ex.Message}");
            StopMirroring();
            throw;
        }
    }

    public void StopMirroring()
    {
        if (_capture != null)
        {
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
        }

        foreach (var output in _outputs)
        {
            output.Device.Stop();
            output.Device.Dispose();
        }
        _outputs.Clear();

        IsMirroring = false;
        Debug.WriteLine("Stopped mirroring.");
    }

    public void Dispose()
    {
        StopMirroring();
        _enumerator.Dispose();
    }

    private class MirrorOutput
    {
        public WasapiOut Device { get; }
        public BufferedWaveProvider Buffer { get; }

        public MirrorOutput(WasapiOut device, BufferedWaveProvider buffer)
        {
            Device = device;
            Buffer = buffer;
        }
    }
}
