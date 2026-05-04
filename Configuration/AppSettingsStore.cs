using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using TgConfigLocations = Terminal.Gui.Configuration.ConfigLocations;
using TgConfigurationManager = Terminal.Gui.Configuration.ConfigurationManager;

namespace hui.Configuration;

internal interface IAppSettingsStore
{
    string SettingsPath { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}

internal sealed class JsonAppSettingsStore(IConfiguration configuration) : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsPath { get; } = AppSettingsPathResolver.GetSettingsPath();

    public AppSettings Load()
    {
        var settings = new AppSettings();
        configuration.GetSection("hui").Bind(settings);
        return settings.Normalize();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var normalized = settings.Clone();
        var payload = JsonSerializer.Serialize(new PersistedSettings { hui = normalized }, JsonOptions);
        File.WriteAllText(SettingsPath, payload);
    }

    private sealed class PersistedSettings
    {
        public AppSettings hui { get; set; } = new();
    }
}

internal static class AppSettingsPathResolver
{
    private const string AppConfigName = "hui";

    public static string GetSettingsPath()
    {
        TgConfigurationManager.AppName = AppConfigName;
        var rawPath = GetDocumentedAppHomePath();
        var resolvedPath = ExpandAppHomePath(rawPath);
        MigrateLegacyLiteralTildePath(rawPath, resolvedPath);
        MigrateProcessNamedAppHomePath(resolvedPath);
        MigrateLegacyAppDataPath(resolvedPath);
        return resolvedPath;
    }

    private static string GetDocumentedAppHomePath()
    {
        _ = TgConfigLocations.AppHome;
        return $"~/.tui/{AppConfigName}.config.json";
    }

    private static string ExpandAppHomePath(string path)
    {
        if (!path.StartsWith("~"))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Unable to resolve user home directory for ConfigLocations.AppHome.");
        }

        var suffix = path[1..].TrimStart('\\', '/');
        if (string.IsNullOrEmpty(suffix))
        {
            return home;
        }

        return Path.Combine(home, suffix.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void MigrateLegacyLiteralTildePath(string rawPath, string resolvedPath)
    {
        if (!rawPath.StartsWith("~") || string.Equals(rawPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var legacyPath = Path.GetFullPath(rawPath.Replace('/', Path.DirectorySeparatorChar), Environment.CurrentDirectory);
        if (!File.Exists(legacyPath) || File.Exists(resolvedPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        File.Move(legacyPath, resolvedPath);
    }

    private static void MigrateProcessNamedAppHomePath(string resolvedPath)
    {
        if (File.Exists(resolvedPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(resolvedPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        foreach (var candidateName in GetLegacyAppConfigNames())
        {
            var candidatePath = Path.Combine(directory, $"{candidateName}.config.json");
            if (!File.Exists(candidatePath) || string.Equals(candidatePath, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            File.Move(candidatePath, resolvedPath);
            return;
        }
    }

    private static void MigrateLegacyAppDataPath(string resolvedPath)
    {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConfigName,
            "settings.json");

        if (!File.Exists(legacyPath) || File.Exists(resolvedPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        File.Move(legacyPath, resolvedPath);
    }

    private static IEnumerable<string> GetLegacyAppConfigNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AppConfigName };
        foreach (var candidate in new[]
                 {
                     Process.GetCurrentProcess().ProcessName,
                     AppDomain.CurrentDomain.FriendlyName,
                     "dotnet",
                     "pwsh"
                 })
        {
            var normalized = Path.GetFileNameWithoutExtension(candidate);
            if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }
}

internal sealed class AppSettingsState(IAppSettingsStore store)
{
    private readonly object _sync = new();
    private AppSettings _settings = store.Load();

    public AppSettings Snapshot()
    {
        lock (_sync)
        {
            return _settings.Clone();
        }
    }

    public void Update(Action<AppSettings> update)
    {
        AppSettings snapshot;
        lock (_sync)
        {
            update(_settings);
            _settings = _settings.Normalize();
            snapshot = _settings.Clone();
        }

        store.Save(snapshot);
    }
}

