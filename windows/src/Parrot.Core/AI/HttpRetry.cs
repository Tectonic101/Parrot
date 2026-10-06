// Parrot for Windows. Derived from Parrot (GPL-3.0).
using System.Net;
using System.Text.Json.Nodes;

namespace Parrot.Core.AI;

internal static class HttpRetry
{
    /// Single retry with fixed backoff on 429/5xx, mirroring the Mac providers.
    public static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpClient http, Func<HttpRequestMessage> makeRequest, TimeSpan retryDelay, CancellationToken ct,
        bool retryOn429 = true)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = makeRequest();
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var code = (int)response.StatusCode;
            if (attempt == 0 && ((retryOn429 && code == 429) || code >= 500))
            {
                await Task.Delay(retryDelay, ct).ConfigureAwait(false);
                continue;
            }
            return (response.StatusCode, body);
        }
    }

    /// {"error":{"message":"..."}} or {"error":"..."}.
    public static string? ErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var error = node?["error"];
            if (error is JsonObject o && o["message"] is JsonValue m && m.TryGetValue<string>(out var msg)) return msg;
            if (error is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        }
        catch (Exception) { }
        return null;
    }
}
