namespace hui.Modes;

internal sealed class LightingModeCatalog(IEnumerable<ILightingMode> modes)
{
    private readonly IReadOnlyList<ILightingMode> _orderedModes = modes.OrderBy(mode => mode.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly Dictionary<string, ILightingMode> _byId = modes.ToDictionary(mode => mode.Id, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ILightingMode> Modes => _orderedModes;

    public ILightingMode Get(string modeId)
    {
        return _byId.TryGetValue(modeId, out var mode)
            ? mode
            : _byId[ModeIds.AudioReactive];
    }

    public ILightingMode GetCurrent(Configuration.AppSettings settings) => Get(settings.CurrentModeId);

    public string GetNextId(string currentId)
    {
        var index = FindIndex(currentId);
        return _orderedModes[(index + 1) % _orderedModes.Count].Id;
    }

    public string GetPreviousId(string currentId)
    {
        var index = FindIndex(currentId);
        return _orderedModes[(index - 1 + _orderedModes.Count) % _orderedModes.Count].Id;
    }

    private int FindIndex(string currentId)
    {
        for (var index = 0; index < _orderedModes.Count; index++)
        {
            if (string.Equals(_orderedModes[index].Id, currentId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }
}

