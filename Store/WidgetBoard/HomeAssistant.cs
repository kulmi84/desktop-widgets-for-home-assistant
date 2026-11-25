using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

sealed class HomeAssistant(string address, string token)
{
    internal string?[] SolarEntities { get; private init; } = [];
    static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    internal static HomeAssistant? Load()
    {
        string path = Path.Combine(BoardSelection.Folder, "settings.json");
        if (!File.Exists(path)) return null;
        using var settings = JsonDocument.Parse(ReadProfileFile(path));
        string? url = settings.RootElement.GetProperty("Url").GetString();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException();
        byte[] encrypted = Convert.FromBase64String(settings.RootElement.GetProperty("Token").GetString()!);
        byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            var entities = settings.RootElement.TryGetProperty("Entities", out var saved) &&
                saved.ValueKind == JsonValueKind.Array
                ? saved.EnumerateArray().Take(4).Select(entity =>
                    entity.ValueKind == JsonValueKind.String ? entity.GetString() : null).ToArray()
                : [];
            return new HomeAssistant(uri.AbsoluteUri.TrimEnd('/'), Encoding.UTF8.GetString(plaintext))
            {
                SolarEntities = entities
            };
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal static string ReadProfileFile(string path)
    {
        // The desktop app atomically replaces settings while the widget can be reading them.
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal async Task<EntityState?> ReadAsync(string entity)
    {
        using var request = Request(HttpMethod.Get, "/api/states/" + Uri.EscapeDataString(entity));
        using var response = await Client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EntityState>();
    }

    internal Task SetSwitchAsync(string entity, bool on)
    {
        if (!BoardSelection.IsSwitch(entity)) throw new ArgumentException(nameof(entity));
        return CallServiceAsync(entity, on ? "turn_on" : "turn_off");
    }

    internal Task PressButtonAsync(string entity)
    {
        if (!BoardSelection.IsButton(entity)) throw new ArgumentException(nameof(entity));
        return CallServiceAsync(entity, "press");
    }

    async Task CallServiceAsync(string entity, string service)
    {
        string domain = entity[..entity.IndexOf('.')];
        using var request = Request(HttpMethod.Post, "/api/services/" + domain + "/" + service);
        request.Content = JsonContent.Create(new { entity_id = entity });
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    HttpRequestMessage Request(HttpMethod method, string path) => new(method, address + path)
    {
        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
    };
}

sealed record EntityState(string State, EntityAttributes? Attributes);

sealed record EntityAttributes(
    [property: JsonPropertyName("friendly_name")] string? Name,
    [property: JsonPropertyName("unit_of_measurement")] string? Unit,
    [property: JsonPropertyName("device_class")] string? DeviceClass);
