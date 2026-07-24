using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Profiscal.Domain.Abstractions.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Profiscal.Infrastructure.Agents;

/// <summary>
/// HTTP wrapper around OpenAI or Azure OpenAI.
/// Automatically detects which to use: if OpenAI:Endpoint is set → Azure format;
/// otherwise → standard api.openai.com.
///
/// Azure REJECTS max_tokens in the request body — we never send it.
/// Standard OpenAI requires "model" in the body; Azure uses the deployment URL instead.
/// </summary>
public sealed class LlmAgent(IConfiguration config, ILogger<LlmAgent> logger) : ILlmAgent
{
    // Read config first (binds OpenAI__* env vars + appsettings), then fall back to the
    // single-underscore OPENAI_* env vars so an existing .env from the original project works.
    private readonly string _model      = config["OpenAI:ChatModel"]  is { Length: > 0 } m  ? m  : (Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL")  ?? "gpt-4o");
    private readonly string _apiKey     = config["OpenAI:ApiKey"]     is { Length: > 0 } k  ? k  : (Environment.GetEnvironmentVariable("OPENAI_API_KEY")     ?? "");
    private readonly string _endpoint   = config["OpenAI:Endpoint"]   is { Length: > 0 } e  ? e  : (Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")    ?? "");
    private readonly string _apiVersion = config["OpenAI:ApiVersion"] is { Length: > 0 } av ? av : (Environment.GetEnvironmentVariable("OPENAI_API_VERSION") ?? "2024-02-15-preview");
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(4) };

    // true = Azure OpenAI (endpoint is set); false = standard api.openai.com
    private bool IsAzure => !string.IsNullOrEmpty(_endpoint);

    public Task<string?> CompleteAsync(string sys, string user, string label,
        int maxTokens = 3000, CancellationToken ct = default) =>
        SendAsync(sys, new[] { ("user", user) }, label, ct);

    public Task<string?> ChatAsync(IEnumerable<(string Role, string Content)> history,
        string sys, CancellationToken ct = default) =>
        SendAsync(sys, history, "Chat", ct);

    // ── Token streaming (stream:true) ─────────────────────────────────────────
    public async IAsyncEnumerable<string> StreamAsync(
        string sys, string user, string label,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            logger.LogError("[{L}] API key missing — set OpenAI__ApiKey in .env", label);
            yield break;
        }

        var allMsgs = new object[]
        {
            new { role = "system", content = sys },
            new { role = "user",   content = user },
        };

        string url;
        object bodyObj;
        if (IsAzure)
        {
            var apiVersion = _apiVersion.Trim().Trim('"').Trim('\'');
            url     = _endpoint.TrimEnd('/') + $"/openai/deployments/{_model}/chat/completions?api-version={apiVersion}";
            bodyObj = new { messages = allMsgs, temperature = 0, stream = true };
        }
        else
        {
            url     = "https://api.openai.com/v1/chat/completions";
            bodyObj = new { model = _model, messages = allMsgs, temperature = 0, stream = true };
        }

        logger.LogInformation("  [{L}] → {Mode} {M} (stream)", label, IsAzure ? "Azure" : "OpenAI", _model);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
            { Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json") };
        if (IsAzure) req.Headers.Add("api-key", _apiKey);
        else         req.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var rb = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError("  [{L}] stream HTTP {S}: {B}", label, (int)resp.StatusCode,
                rb.Length > 300 ? rb[..300] : rb);
            yield break;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:")) continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var c))
                    token = c.GetString();
            }
            catch { token = null; }

            if (!string.IsNullOrEmpty(token)) yield return token;
        }
    }

    private async Task<string?> SendAsync(
        string sys,
        IEnumerable<(string Role, string Content)> msgs,
        string label,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            logger.LogError("[{L}] API key missing — set OpenAI__ApiKey in .env", label);
            return null;
        }

        var allMsgs = new object[] { new { role = "system", content = sys } }
            .Concat(msgs.Select(m => (object)new { role = m.Role, content = m.Content }))
            .ToArray();

        // Azure: no "model" in body (it's in the deployment URL); no max_tokens (causes 400).
        // Standard OpenAI: "model" required in body; max_tokens optional (we omit it anyway).
        string url;
        object bodyObj;
        if (IsAzure)
        {
            var apiVersion = _apiVersion.Trim().Trim('"').Trim('\'');
            url     = _endpoint.TrimEnd('/') + $"/openai/deployments/{_model}/chat/completions?api-version={apiVersion}";
            bodyObj = new { messages = allMsgs, temperature = 0 };
        }
        else
        {
            url     = "https://api.openai.com/v1/chat/completions";
            bodyObj = new { model = _model, messages = allMsgs, temperature = 0 };
        }

        var body = JsonSerializer.Serialize(bodyObj);
        logger.LogInformation("  [{L}] → {Mode} {M}", label, IsAzure ? "Azure" : "OpenAI", _model);

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(3));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                logger.LogInformation("  [{L}] attempt {A}/2 — {C} chars",
                    label, attempt, body.Length);

                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

                // Azure uses api-key header; standard OpenAI uses Authorization Bearer
                if (IsAzure)
                    req.Headers.Add("api-key", _apiKey);
                else
                    req.Headers.Add("Authorization", $"Bearer {_apiKey}");

                var resp = await _http.SendAsync(req, cts.Token);
                var rb   = await resp.Content.ReadAsStringAsync(cts.Token);
                sw.Stop();

                if (!resp.IsSuccessStatusCode)
                {
                    var preview = rb.Length > 400 ? rb[..400] : rb;
                    logger.LogError("  [{L}] HTTP {S} ({Ms:F0}ms): {B}",
                        label, (int)resp.StatusCode, sw.Elapsed.TotalMilliseconds, preview);

                    if ((int)resp.StatusCode is 401 or 403)
                    {
                        logger.LogError("  [{L}] Auth failure — check OPENAI_API_KEY", label);
                        return null;
                    }

                    if (attempt < 2) { await Task.Delay(5000, ct); continue; }
                    return null;
                }

                using var doc = JsonDocument.Parse(rb);
                var text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()?.Trim();

                var finish = doc.RootElement
                    .GetProperty("choices")[0]
                    .TryGetProperty("finish_reason", out var fr)
                    ? fr.GetString() ?? "" : "";

                logger.LogInformation("  [{L}] ✓ {Ms:F0}ms | {N} chars | finish={F}",
                    label, sw.Elapsed.TotalMilliseconds, text?.Length ?? 0, finish);

                if (finish == "length")
                    logger.LogWarning("  [{L}] ⚠ response was truncated", label);

                return text;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                logger.LogWarning("  [{L}] timeout {A}/2 ({Ms:F0}ms)",
                    label, attempt, sw.Elapsed.TotalMilliseconds);
                if (attempt < 2) await Task.Delay(5000, ct);
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                logger.LogError("  [{L}] connection error ({Ms:F0}ms): {M}",
                    label, sw.Elapsed.TotalMilliseconds, ex.Message);
                if (attempt < 2) await Task.Delay(5000, ct);
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError("  [{L}] error ({Ms:F0}ms): {M}",
                    label, sw.Elapsed.TotalMilliseconds,
                    ex.Message[..Math.Min(ex.Message.Length, 200)]);
                if (attempt < 2) await Task.Delay(5000, ct);
            }
        }

        logger.LogError("  [{L}] all attempts failed", label);
        return null;
    }
}
