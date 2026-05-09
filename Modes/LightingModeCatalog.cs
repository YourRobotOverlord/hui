namespace hui.Modes;

internal sealed class LightingModeCatalog
{
    private readonly IReadOnlyList<ILightingMode> _orderedModes;
    private readonly Dictionary<string, ILightingMode> _byId;
    private readonly Dictionary<string, int> _indexById;

    public LightingModeCatalog(IEnumerable<ILightingMode> modes)
    {
        _orderedModes = modes.OrderBy(mode => mode.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        _byId = _orderedModes.ToDictionary(mode => mode.Id, StringComparer.OrdinalIgnoreCase);
        _indexById = new Dictionary<string, int>(_orderedModes.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _orderedModes.Count; i++)
        {
            _indexById[_orderedModes[i].Id] = i;
        }
    }

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

    private int FindIndex(string currentId) =>
        _indexById.TryGetValue(currentId, out var index) ? index : 0;
}

