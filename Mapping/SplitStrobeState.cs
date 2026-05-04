using hui.Audio;
using hui.Configuration;
using hui.Hue;

namespace hui.Mapping;

internal sealed class SplitStrobeState
{
    private readonly HashSet<int> _bassChannels = [];
    private readonly int _seed = Random.Shared.Next();
    private int _assignmentSignature;
    private double _lastElapsedSeconds = double.NaN;

    public double BassLevel { get; private set; }
    public double TrebleLevel { get; private set; }

    public bool IsBassChannel(int channelId) => _bassChannels.Contains(channelId);

    public void EnsureAssignments(IReadOnlyList<EntertainmentChannel> channels)
    {
        var signature = 17;
        foreach (var channel in channels)
        {
            signature = HashCode.Combine(signature, channel.ChannelId);
        }

        if (_assignmentSignature == signature && _bassChannels.Count > 0)
        {
            return;
        }

        _assignmentSignature = signature;
        _bassChannels.Clear();

        var channelIds = channels
            .Select(channel => channel.ChannelId)
            .OrderBy(id => id)
            .ToArray();

        var random = new Random(HashCode.Combine(_seed, signature, channels.Count));
        for (var index = channelIds.Length - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (channelIds[index], channelIds[swapIndex]) = (channelIds[swapIndex], channelIds[index]);
        }

        var bassCount = Math.Max(1, channelIds.Length / 2);
        for (var index = 0; index < bassCount; index++)
        {
            _bassChannels.Add(channelIds[index]);
        }
    }

    public void UpdateLevels(AudioFrame frame, double elapsedSeconds, SplitStrobeModeSettings settings)
    {
        var deltaSeconds = double.IsNaN(_lastElapsedSeconds) || elapsedSeconds <= _lastElapsedSeconds
            ? 1d / 30d
            : elapsedSeconds - _lastElapsedSeconds;
        _lastElapsedSeconds = elapsedSeconds;

        var maxBrightness = Math.Clamp(settings.Brightness, 0, 1);
        var background = Math.Clamp(settings.BackgroundLevel, 0, maxBrightness);
        var bassTarget = background + (ComputeBandDrive(frame, frame.BassRatio, settings.Sensitivity) * (maxBrightness - background));
        var trebleTarget = background + (ComputeBandDrive(frame, frame.TrebleRatio, settings.Sensitivity) * (maxBrightness - background));

        BassLevel = StepEnvelope(BassLevel, bassTarget, deltaSeconds, settings.AttackSeconds, settings.DecaySeconds);
        TrebleLevel = StepEnvelope(TrebleLevel, trebleTarget, deltaSeconds, settings.AttackSeconds, settings.DecaySeconds);
    }

    private static double ComputeBandDrive(AudioFrame frame, double bandRatio, double sensitivity)
    {
        return Math.Clamp(
            (((frame.OverallLevel * 0.65) + (frame.PeakLevel * 0.35) + (frame.TransientLevel * 0.45)) *
             (0.35 + (bandRatio * 0.95)) *
             sensitivity) - 0.03,
            0,
            1);
    }

    private static double StepEnvelope(double current, double target, double deltaSeconds, double attackSeconds, double decaySeconds)
    {
        var timeConstant = target > current ? Math.Max(attackSeconds, 0.01) : Math.Max(decaySeconds, 0.01);
        var amount = Math.Clamp(deltaSeconds / timeConstant, 0, 1);
        return current + ((target - current) * amount);
    }
}
