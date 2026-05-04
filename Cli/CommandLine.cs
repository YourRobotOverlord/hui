using System.Globalization;

namespace hui.Cli;

internal static class CommandLine
{
    public static Command Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new UiCommand();
        }

        if (args.Any(argument => argument is "--help" or "-h"))
        {
            return new HelpCommand();
        }

        var commandName = args[0].Trim().ToLowerInvariant();
        if (commandName is "help" or "--help" or "-h")
        {
            return new HelpCommand();
        }

        var options = ParseOptions(args.Skip(1).ToArray());

        return commandName switch
        {
            "pair" => new PairCommand(
                GetRequired(options, "--bridge"),
                GetOptional(options, "--app-name", "hue-audio-sync"),
                GetOptional(options, "--instance-name", Environment.MachineName)),

            "list-areas" => new ListAreasCommand(
                GetRequired(options, "--bridge"),
                GetRequired(options, "--app-key")),

            "list-devices" => new ListDevicesCommand(),

            "ui" => new UiCommand(),

            "run" => new RunCommand(
                GetRequired(options, "--bridge"),
                GetRequired(options, "--app-key"),
                GetRequired(options, "--client-key"),
                GetRequired(options, "--area"),
                GetOptionalInt(options, "--device-index"),
                GetOptionalInt(options, "--fps", 30, min: 1, max: 60),
                GetMapperKind(options, "--mapper", MapperKind.AudioReactive),
                GetOptionalDouble(options, "--cycle-seconds", 6.0, min: 0.1),
                GetOptionalDouble(options, "--wave-seconds", 1.6, min: 0.1),
                GetOptionalDouble(options, "--sensitivity", 1.75, min: 0.01),
                GetOptionalDouble(options, "--brightness", 1.0, min: 0.0, max: 1.0),
                GetOptionalDouble(options, "--warm-hue", 18.0, min: 0.0, max: 360.0),
                GetOptionalDouble(options, "--cool-hue", 220.0, min: 0.0, max: 360.0)),

            _ => throw new CommandLineException($"Unknown command '{args[0]}'.")
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
"""
hui

Commands:
  ui            Launch interactive Terminal.Gui application
  pair          Pair app with bridge and request entertainment client key
  list-areas    Show entertainment areas and channel positions
  list-devices  Show Windows render devices usable for loopback capture
  run           Start live audio -> Hue entertainment sync

Examples:
  dotnet run -- ui
  dotnet run -- pair --bridge 192.168.1.20
  dotnet run -- list-areas --bridge 192.168.1.20 --app-key YOUR_APP_KEY
  dotnet run -- list-devices
  dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area YOUR_AREA_ID
  dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area LivingRoom --device-index 1 --fps 40 --brightness 0.8

Run options:
  --bridge        Bridge IP or hostname
  --app-key       Hue application key
  --client-key    Hue entertainment client key from pairing
  --area          Entertainment area id or exact name
  --device-index  Optional render device index from list-devices
  --fps           Stream rate, 1-60. Default 30
  --mapper        audio-reactive, cycle-strobe, sparkle, wave-travel, ambient-drift, or beat-pulse. Default audio-reactive
  --cycle-seconds Hue cycle length for cycle-strobe mapper. Default 6
  --wave-seconds  Travel time for wave-travel mapper. Default 1.6
  --sensitivity   Audio gain multiplier. Default 1.75
  --brightness    Max brightness, 0-1. Default 1
  --warm-hue      Hue for bass-heavy moments, 0-360. Default 18
  --cool-hue      Hue for treble-heavy moments, 0-360. Default 220

""");
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CommandLineException($"Unexpected token '{token}'.");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[token] = null;
                continue;
            }

            options[token] = args[++index];
        }

        return options;
    }

    private static string GetRequired(IReadOnlyDictionary<string, string?> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new CommandLineException($"Missing required option {name}.");
        }

        return value.Trim();
    }

    private static string GetOptional(IReadOnlyDictionary<string, string?> options, string name, string fallback)
    {
        return options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private static int? GetOptionalInt(IReadOnlyDictionary<string, string?> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new CommandLineException($"Option {name} must be integer.");
        }

        return parsed;
    }

    private static int GetOptionalInt(
        IReadOnlyDictionary<string, string?> options,
        string name,
        int fallback,
        int? min = null,
        int? max = null)
    {
        var parsed = GetOptionalInt(options, name) ?? fallback;
        if (min.HasValue && parsed < min.Value)
        {
            throw new CommandLineException($"Option {name} must be >= {min.Value}.");
        }

        if (max.HasValue && parsed > max.Value)
        {
            throw new CommandLineException($"Option {name} must be <= {max.Value}.");
        }

        return parsed;
    }

    private static double GetOptionalDouble(
        IReadOnlyDictionary<string, string?> options,
        string name,
        double fallback,
        double? min = null,
        double? max = null)
    {
        var parsed = fallback;
        if (options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                throw new CommandLineException($"Option {name} must be number.");
            }
        }

        if (min.HasValue && parsed < min.Value)
        {
            throw new CommandLineException($"Option {name} must be >= {min.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (max.HasValue && parsed > max.Value)
        {
            throw new CommandLineException($"Option {name} must be <= {max.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        return parsed;
    }

    private static MapperKind GetMapperKind(
        IReadOnlyDictionary<string, string?> options,
        string name,
        MapperKind fallback)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "audio-reactive" => MapperKind.AudioReactive,
            "cycle-strobe" => MapperKind.CycleStrobe,
            "sparkle" => MapperKind.Sparkle,
            "wave-travel" => MapperKind.WaveTravel,
            "ambient-drift" => MapperKind.AmbientDrift,
            "beat-pulse" => MapperKind.BeatPulse,
            _ => throw new CommandLineException($"Option {name} must be one of: audio-reactive, cycle-strobe, sparkle, wave-travel, ambient-drift, beat-pulse.")
        };
    }
}

internal abstract record Command;

internal sealed record HelpCommand : Command;
internal sealed record UiCommand : Command;

internal sealed record PairCommand(string Bridge, string AppName, string InstanceName) : Command;

internal sealed record ListAreasCommand(string Bridge, string AppKey) : Command;

internal sealed record ListDevicesCommand : Command;

internal sealed record RunCommand(
    string Bridge,
    string AppKey,
    string ClientKey,
    string Area,
    int? DeviceIndex,
    int Fps,
    MapperKind Mapper,
    double CycleSeconds,
    double WaveSeconds,
    double Sensitivity,
    double Brightness,
    double WarmHue,
    double CoolHue) : Command;

internal enum MapperKind
{
    AudioReactive,
    CycleStrobe,
    Sparkle,
    WaveTravel,
    AmbientDrift,
    BeatPulse
}

internal sealed class CommandLineException : Exception
{
    public CommandLineException(string message)
        : base(message)
    {
    }
}

