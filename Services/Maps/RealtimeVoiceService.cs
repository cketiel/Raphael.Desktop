using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services.Maps
{
    /// <summary>The ephemeral secret the map page connects to OpenAI Realtime with.</summary>
    public sealed record RealtimeToken(string? Value, string? Model, string? Error);

    /// <summary>
    /// Voice control of the live map, the way God's Eye View does it: this process plays the part of
    /// God's Eye's server (<c>/api/realtime/token</c>). It holds OPENAI_API_KEY, asks OpenAI for a
    /// short-lived client secret with the whole session configured (model, voice, instructions,
    /// tools), and hands only that secret to the page, which opens the WebRTC call itself.
    /// </summary>
    public interface IRealtimeVoiceService
    {
        Task<RealtimeToken> MintAsync(string? tier);
    }

    public sealed class RealtimeVoiceService : IRealtimeVoiceService
    {
        public const string StandardModel = "gpt-realtime-2";
        public const string MiniModel = "gpt-realtime-2.1-mini";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private readonly IProviderKeyStore _keys;

        public RealtimeVoiceService(IProviderKeyStore keys) => _keys = keys;

        public async Task<RealtimeToken> MintAsync(string? tier)
        {
            var apiKey = _keys.Get("OPENAI_API_KEY");
            if (apiKey is null) return new RealtimeToken(null, null, "OPENAI_API_KEY is not set");

            // An unknown tier is the standard one, as in God's Eye: a bad value degrades, never breaks.
            var model = tier == "mini" ? MiniModel : StandardModel;
            var body = new JsonObject
            {
                ["session"] = new JsonObject
                {
                    ["type"] = "realtime",
                    ["model"] = model,
                    ["reasoning"] = new JsonObject { ["effort"] = "low" },
                    ["truncation"] = new JsonObject
                    {
                        ["type"] = "retention_ratio",
                        ["retention_ratio"] = 0.5,
                        ["token_limits"] = new JsonObject { ["post_instructions"] = 3000 }
                    },
                    ["audio"] = new JsonObject
                    {
                        ["input"] = new JsonObject
                        {
                            ["noise_reduction"] = new JsonObject { ["type"] = "near_field" },
                            ["turn_detection"] = new JsonObject
                            {
                                ["type"] = "semantic_vad",
                                ["eagerness"] = "low",
                                ["create_response"] = true,
                                ["interrupt_response"] = false
                            }
                        },
                        ["output"] = new JsonObject { ["voice"] = "marin" }
                    },
                    ["instructions"] = LiveMapVoiceTools.Instructions,
                    ["tools"] = LiveMapVoiceTools.Definitions(),
                    ["tool_choice"] = "auto"
                }
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/realtime/client_secrets")
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var response = await Http.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    // The status only: the body may echo parts of the request.
                    Helpers.FileLogger.Log($"Live map voice: OpenAI refused the session (HTTP {(int)response.StatusCode}).");
                    return new RealtimeToken(null, model, $"Failed to create Realtime token (HTTP {(int)response.StatusCode})");
                }

                using var doc = JsonDocument.Parse(text);
                var r = doc.RootElement;
                string? value =
                    r.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()
                    : r.TryGetProperty("client_secret", out var cs) && cs.ValueKind == JsonValueKind.Object && cs.TryGetProperty("value", out var csv) ? csv.GetString()
                    : null;
                return value is null
                    ? new RealtimeToken(null, model, "Realtime token response did not include a client secret")
                    : new RealtimeToken(value, model, null);
            }
            catch (Exception ex)
            {
                Helpers.FileLogger.Log($"Live map voice: token request failed ({ex.GetType().Name}).");
                return new RealtimeToken(null, model, "Failed to create Realtime token");
            }
        }
    }
}
