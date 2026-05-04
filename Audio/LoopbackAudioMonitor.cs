using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace hui.Audio;

internal sealed class LoopbackAudioMonitor : IAudioMonitor
{
    private const int AnalysisSize = 2048;
    private const int HistorySize = 4096;

    private readonly object _sync = new();
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _device;
    private readonly WasapiLoopbackCapture _capture;
    private readonly float[] _history = new float[HistorySize];
    private int _historyWriteIndex;
    private int _historyCount;
    private double _leftLevel;
    private double _rightLevel;
    private double _overallLevel;
    private double _peakLevel;
    private double _transientLevel;
    private bool _started;

    private LoopbackAudioMonitor(MMDeviceEnumerator enumerator, MMDevice device)
    {
        _enumerator = enumerator;
        _device = device;
        _capture = new WasapiLoopbackCapture(device);
        _capture.DataAvailable += OnDataAvailable;
    }

    public string DeviceName => _device.FriendlyName;

    public static IReadOnlyList<AudioDeviceInfo> ListRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        var result = new List<AudioDeviceInfo>(devices.Count);
        for (var index = 0; index < devices.Count; index++)
        {
            using var device = devices[index];
            result.Add(new AudioDeviceInfo(index, device.FriendlyName, device.ID, device.ID == defaultDevice.ID));
        }

        return result;
    }

    public static LoopbackAudioMonitor Create(int? deviceIndex)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice device;

        if (deviceIndex.HasValue)
        {
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            if (deviceIndex.Value < 0 || deviceIndex.Value >= devices.Count)
            {
                enumerator.Dispose();
                throw new InvalidOperationException($"Render device index {deviceIndex.Value} not found.");
            }

            device = devices[deviceIndex.Value];
        }
        else
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        return new LoopbackAudioMonitor(enumerator, device);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _capture.StartRecording();
        _started = true;
    }

    public AudioFrame GetFrame()
    {
        var mono = new float[AnalysisSize];
        double left;
        double right;
        double overall;
        double peak;
        double transient;
        int sampleRate;

        lock (_sync)
        {
            CopyRecentSamples(mono);
            left = _leftLevel;
            right = _rightLevel;
            overall = _overallLevel;
            peak = _peakLevel;
            transient = _transientLevel;
            sampleRate = _capture.WaveFormat.SampleRate;
        }

        var (bass, mid, treble) = AnalyzeBands(mono, sampleRate);
        return new AudioFrame(left, right, overall, peak, transient, bass, mid, treble);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        var format = _capture.WaveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        var channels = format.Channels;
        if (bytesPerSample <= 0 || channels <= 0)
        {
            return;
        }

        var frameSize = format.BlockAlign;
        var frameCount = eventArgs.BytesRecorded / frameSize;
        if (frameCount == 0)
        {
            return;
        }

        double leftSquareSum = 0;
        double rightSquareSum = 0;
        double peak = 0;
        var systemVolume = GetSystemVolumeScale();

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var offset = frameIndex * frameSize;
            var left = ReadSample(eventArgs.Buffer, offset, format);
            var right = channels > 1
                ? ReadSample(eventArgs.Buffer, offset + bytesPerSample, format)
                : left;
            var mono = (left + right) * 0.5f;

            leftSquareSum += left * left;
            rightSquareSum += right * right;
            peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));

            lock (_sync)
            {
                _history[_historyWriteIndex] = mono;
                _historyWriteIndex = (_historyWriteIndex + 1) % HistorySize;
                _historyCount = Math.Min(_historyCount + 1, HistorySize);
            }
        }

        var leftRms = NormalizeLevel(Math.Sqrt(leftSquareSum / frameCount), systemVolume);
        var rightRms = NormalizeLevel(Math.Sqrt(rightSquareSum / frameCount), systemVolume);
        var overallRms = NormalizeLevel(Math.Sqrt((leftSquareSum + rightSquareSum) / (frameCount * 2d)), systemVolume);
        peak = NormalizeLevel(peak, systemVolume);

        lock (_sync)
        {
            var transientTarget = Math.Clamp(
                (Math.Max(0, peak - _peakLevel) * 3.2) +
                (Math.Max(0, overallRms - _overallLevel) * 7.5) +
                (peak * 0.08) -
                0.03,
                0,
                1);

            _leftLevel = Smooth(_leftLevel, leftRms);
            _rightLevel = Smooth(_rightLevel, rightRms);
            _overallLevel = Smooth(_overallLevel, overallRms);
            _peakLevel = Smooth(_peakLevel, peak);
            _transientLevel = Smooth(_transientLevel, transientTarget, attack: 1.0, release: 0.62);
        }
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        return format.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat when format.BitsPerSample == 32 => BitConverter.ToSingle(buffer, offset),
            WaveFormatEncoding.Pcm when format.BitsPerSample == 16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            WaveFormatEncoding.Pcm when format.BitsPerSample == 24 => Read24BitSample(buffer, offset),
            WaveFormatEncoding.Pcm when format.BitsPerSample == 32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => throw new NotSupportedException(
                $"Unsupported loopback format {format.Encoding} {format.BitsPerSample}-bit. Use standard Windows shared-mode output.")
        };
    }

    private double GetSystemVolumeScale()
    {
        var endpointVolume = _device.AudioEndpointVolume;
        return endpointVolume.Mute
            ? 0
            : Math.Clamp(endpointVolume.MasterVolumeLevelScalar, 0, 1);
    }

    private static double NormalizeLevel(double level, double systemVolume)
    {
        if (systemVolume <= 0.0001)
        {
            return 0;
        }

        return Math.Clamp(level / systemVolume, 0, 1);
    }

    private static float Read24BitSample(byte[] buffer, int offset)
    {
        var sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((sample & 0x0080_0000) != 0)
        {
            sample |= unchecked((int)0xFF00_0000);
        }

        return sample / 8388608f;
    }

    private void CopyRecentSamples(float[] destination)
    {
        Array.Clear(destination);
        var available = Math.Min(_historyCount, destination.Length);
        if (available == 0)
        {
            return;
        }

        var sourceIndex = (_historyWriteIndex - available + HistorySize) % HistorySize;
        var destinationIndex = destination.Length - available;

        for (var index = 0; index < available; index++)
        {
            destination[destinationIndex + index] = _history[(sourceIndex + index) % HistorySize];
        }
    }

    private static (double Bass, double Mid, double Treble) AnalyzeBands(float[] samples, int sampleRate)
    {
        var spectrum = new Complex[AnalysisSize];
        var fftBits = (int)Math.Log2(AnalysisSize);

        for (var index = 0; index < AnalysisSize; index++)
        {
            var window = 0.54f - 0.46f * MathF.Cos(2 * MathF.PI * index / (AnalysisSize - 1));
            spectrum[index].X = samples[index] * window;
            spectrum[index].Y = 0;
        }

        FastFourierTransform.FFT(true, fftBits, spectrum);

        double bass = 0;
        double mid = 0;
        double treble = 0;

        for (var index = 1; index < AnalysisSize / 2; index++)
        {
            var frequency = index * sampleRate / (double)AnalysisSize;
            if (frequency < 30 || frequency > 12000)
            {
                continue;
            }

            var magnitude = Math.Sqrt((spectrum[index].X * spectrum[index].X) + (spectrum[index].Y * spectrum[index].Y));

            if (frequency < 250)
            {
                bass += magnitude;
            }
            else if (frequency < 2000)
            {
                mid += magnitude;
            }
            else
            {
                treble += magnitude;
            }
        }

        var total = bass + mid + treble;
        if (total <= double.Epsilon)
        {
            return (0, 0, 0);
        }

        return (bass / total, mid / total, treble / total);
    }

    private static double Smooth(double current, double target, double attack = 0.45, double release = 0.16)
    {
        var factor = target >= current ? attack : release;
        return current + ((target - current) * factor);
    }

    public void Dispose()
    {
        if (_started)
        {
            _capture.StopRecording();
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
        _device.Dispose();
        _enumerator.Dispose();
    }
}

internal sealed record AudioDeviceInfo(int Index, string Name, string Id, bool IsDefault);

internal sealed record AudioFrame(
    double LeftLevel,
    double RightLevel,
    double OverallLevel,
    double PeakLevel,
    double TransientLevel,
    double BassRatio,
    double MidRatio,
    double TrebleRatio);


