using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace hui.Hue;

internal sealed class HueBridgeClient
{
    private static readonly HttpClientHandler InsecureHandler = new()
    {
        ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
    };

    private static readonly HttpClient Client = new(InsecureHandler);

    public async Task<PairingResult> PairAsync(string bridge, string appName, string instanceName, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            devicetype = BuildDeviceType(appName, instanceName),
            generateclientkey = true
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://{bridge}/api")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hue bridge pairing failed: {response.StatusCode} {body}");
        }

        using var document = JsonDocument.Parse(body);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("success", out var success))
            {
                var appKey = success.GetProperty("username").GetString();
                var clientKey = success.TryGetProperty("clientkey", out var clientKeyProperty)
                    ? clientKeyProperty.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(clientKey))
                {
                    break;
                }

                return new PairingResult(appKey, clientKey);
            }

            if (item.TryGetProperty("error", out var error))
            {
                var description = error.TryGetProperty("description", out var descriptionProperty)
                    ? descriptionProperty.GetString()
                    : "unknown error";
                throw new InvalidOperationException($"Hue bridge rejected pairing request: {description}");
            }
        }

        throw new InvalidOperationException("Hue bridge pairing response did not include app key and client key.");
    }

    public async Task<IReadOnlyList<EntertainmentArea>> GetEntertainmentAreasAsync(
        string bridge,
        string appKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, bridge, appKey, "clip/v2/resource/entertainment_configuration");
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Entertainment area query failed: {response.StatusCode} {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var areas = new List<EntertainmentArea>();
        foreach (var areaElement in data.EnumerateArray())
        {
            var id = areaElement.GetProperty("id").GetString() ?? string.Empty;
            var name = areaElement.TryGetProperty("metadata", out var metadata)
                && metadata.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? id
                : id;
            var status = areaElement.TryGetProperty("status", out var statusElement)
                ? statusElement.ValueKind == JsonValueKind.String ? statusElement.GetString() ?? "unknown" : statusElement.ToString()
                : "unknown";

            var channels = new List<EntertainmentChannel>();
            if (areaElement.TryGetProperty("channels", out var channelsElement) && channelsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var channelElement in channelsElement.EnumerateArray())
                {
                    var channelId = channelElement.TryGetProperty("channel_id", out var channelIdElement)
                        ? channelIdElement.GetInt32()
                        : -1;
                    if (channelId < 0)
                    {
                        continue;
                    }

                    ChannelPosition position = default;
                    if (channelElement.TryGetProperty("position", out var positionElement))
                    {
                        position = new ChannelPosition(
                            ReadDouble(positionElement, "x"),
                            ReadDouble(positionElement, "y"),
                            ReadDouble(positionElement, "z"));
                    }

                    channels.Add(new EntertainmentChannel(channelId, position));
                }
            }

            areas.Add(new EntertainmentArea(id, name, status, channels.OrderBy(channel => channel.ChannelId).ToArray()));
        }

        return areas;
    }

    public async Task<EntertainmentArea> GetEntertainmentAreaAsync(
        string bridge,
        string appKey,
        string areaSelector,
        CancellationToken cancellationToken)
    {
        var areas = await GetEntertainmentAreasAsync(bridge, appKey, cancellationToken).ConfigureAwait(false);
        var area = areas.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, areaSelector, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Name, areaSelector, StringComparison.OrdinalIgnoreCase));

        if (area is null)
        {
            throw new InvalidOperationException($"Entertainment area '{areaSelector}' not found.");
        }

        return area;
    }

    public async Task SetStreamingActionAsync(
        string bridge,
        string appKey,
        string areaId,
        string action,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Put,
            bridge,
            appKey,
            $"clip/v2/resource/entertainment_configuration/{areaId}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { action }),
            Encoding.UTF8,
            "application/json");

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Entertainment action '{action}' failed: {response.StatusCode} {body}");
        }
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string bridge,
        string appKey,
        string relativePath)
    {
        var request = new HttpRequestMessage(method, $"https://{bridge}/{relativePath}");
        request.Headers.Add("hue-application-key", appKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string BuildDeviceType(string appName, string instanceName)
    {
        static string Sanitize(string value)
        {
            var filtered = new string(value
                .Trim()
                .ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray());

            return string.IsNullOrWhiteSpace(filtered) ? "hue-audio-sync" : filtered;
        }

        var deviceType = $"{Sanitize(appName)}#{Sanitize(instanceName)}";
        return deviceType.Length > 40 ? deviceType[..40] : deviceType;
    }

    private static double ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetDouble(),
            JsonValueKind.String when double.TryParse(property.GetString(), out var parsed) => parsed,
            _ => 0
        };
    }
}

internal sealed record PairingResult(string AppKey, string ClientKey);

internal sealed record EntertainmentArea(string Id, string Name, string Status, IReadOnlyList<EntertainmentChannel> Channels);

internal sealed record EntertainmentChannel(int ChannelId, ChannelPosition Position);

internal readonly record struct ChannelPosition(double X, double Y, double Z);

