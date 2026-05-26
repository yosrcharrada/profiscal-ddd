using System.Net.Http;
using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Agents;

public sealed class LlmAgent(IConfiguration config, ILogger<LlmAgent> logger) : ILlmAgent
{
    private readonly string _model      = (config["OpenAI:ChatModel"]  is { Length: > 0 } m)  ? m  : (Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL")  ?? "gpt-4o");
    private readonly string _apiKey     = (config["OpenAI:ApiKey"]     is { Length: > 0 } k)  ? k  : (Environment.GetEnvironmentVariable("OPENAI_API_KEY")     ?? "");
    private readonly string _endpoint   = (config["OpenAI:Endpoint"]   is { Length: > 0 } e)  ? e  : (Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")    ?? "");
    private readonly string _apiVersion = (config["OpenAI:ApiVersion"] is { Length: > 0 } av) ? av : (Environment.GetEnvironmentVariable("OPENAI_API_VERSION") ?? "2024-02-15-preview");
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(4) };

    public Task<string?> CompleteAsync(string sys, string user, string label, int maxTokens = 3000, CancellationToken ct = default) =>
        SendAsync(sys, new[]{ ("user", user) }, label, maxTokens, ct);

    public Task<string?> ChatAsync(IEnumerable<(string Role, string Content)> history, string sys, CancellationToken ct = default) =>
        SendAsync(sys, history, "Chat", 2000, ct);

    private async Task<string?> SendAsync(string sys, IEnumerable<(string Role, string Content)> msgs, string label, int maxTokens, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey)) { logger.LogError("[{L}] API key missing", label); return null; }
        var url = _endpoint.TrimEnd('/') + $"/openai/deployments/{_model}/chat/completions?api-version={_apiVersion}";
        var allMsgs = new object[]{ new { role="system", content=sys } }
            .Concat(msgs.Select(m => (object)new { role=m.Role, content=m.Content })).ToArray();
        var body = JsonSerializer.Serialize(new { messages = allMsgs, temperature = 0, max_tokens = maxTokens });

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                logger.LogInformation("  [{L}] attempt {A}/2", label, attempt);
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Add("api-key", _apiKey);
                var resp = await _http.SendAsync(req, cts.Token);
                var rb   = await resp.Content.ReadAsStringAsync(cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogError("  [{L}] HTTP {S}", label, (int)resp.StatusCode);
                    if (attempt < 2) { await Task.Delay(5000, ct); continue; }
                    return null;
                }
                using var doc = JsonDocument.Parse(rb);
                var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
                logger.LogInformation("  [{L}] ✓ {N} chars", label, text?.Length ?? 0);
                return text;
            }
            catch (OperationCanceledException) { logger.LogWarning("  [{L}] timeout {A}/2", label, attempt); if (attempt < 2) await Task.Delay(5000, ct); }
            catch (Exception ex) { logger.LogError("  [{L}] {M}", label, ex.Message); if (attempt < 2) await Task.Delay(5000, ct); }
        }
        return null;
    }
}
