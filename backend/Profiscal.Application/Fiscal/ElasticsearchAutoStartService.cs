using System.Diagnostics;

namespace Profiscal.Application.Fiscal;

/// <summary>
/// Optional convenience for local/demo machines: on API startup, make sure Elasticsearch is
/// running (and, if empty, populated) so the operator does not have to open a separate terminal
/// for elasticsearch.bat and remember the `--force` reindex. It automates the manual steps; it
/// does NOT bundle Elasticsearch — the ES binaries and the processed_documents corpus must still
/// be present on disk. ES itself cannot be embedded in a .NET build (it is a separate Java server).
///
/// DESIGN — this must never make the app worse:
///   • OFF by default (Elasticsearch:AutoStart:Enabled = false). With it off this class does
///     nothing at all, so it cannot change behaviour for anyone who has not opted in.
///   • Never throws into startup. Every failure is logged and swallowed; the API continues.
///     If auto-start fails, you are exactly where you were before — ES simply isn't up.
///   • Idempotent: if ES already answers on :9200 it launches nothing (so no double-launch,
///     no port clash), and it only runs the indexer when the index is genuinely empty.
///   • Launches ES as an INDEPENDENT process, not a child — so ES survives API restarts and the
///     reachability check keeps the next start from launching a second copy.
///
/// Config (appsettings or .env with the double-underscore form):
///   Elasticsearch:AutoStart:Enabled              true
///   Elasticsearch:AutoStart:Path                 C:\es\elasticsearch-8.13.0\bin\elasticsearch.bat
///   Elasticsearch:AutoStart:HeapMb               1024        (caps ES heap; avoids the 16 GB mmap crash)
///   Elasticsearch:AutoStart:ReadyTimeoutSeconds  180
///   Elasticsearch:AutoStart:Indexer:Command      python elasticsearch_indexer.py --force   (optional)
///   Elasticsearch:AutoStart:Indexer:WorkingDirectory  C:\taxmind_index_build                (optional)
/// </summary>
public sealed class ElasticsearchAutoStartService(
    IConfiguration config,
    IHttpClientFactory httpFactory,
    ILogger<ElasticsearchAutoStartService> logger) : BackgroundService
{
    private string Host  => (config["Elasticsearch:Host"] ?? "http://localhost:9200").TrimEnd('/');
    private string Index => config["Elasticsearch:Index"] ?? "tunisian_legal";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            if (!config.GetValue("Elasticsearch:AutoStart:Enabled", false))
                return; // opted out — do absolutely nothing

            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(3);

            // 1) Already up? Then never launch a second instance.
            if (await IsReachableAsync(http, ct))
                logger.LogInformation("[ES-AUTOSTART] Elasticsearch already reachable at {Host} — not launching.", Host);
            else if (!await LaunchAndWaitAsync(http, ct))
                return; // launch failed/timed out — already logged; leave the app running

            // 2) Populate the index if (and only if) it is empty and an indexer command is set.
            await EnsureIndexedAsync(http, ct);
        }
        catch (Exception ex)
        {
            // Belt and braces: this feature must never take startup down.
            logger.LogWarning(ex, "[ES-AUTOSTART] auto-start step failed — continuing without it.");
        }
    }

    private async Task<bool> IsReachableAsync(HttpClient http, CancellationToken ct)
    {
        try { return (await http.GetAsync($"{Host}/_cluster/health", ct)).IsSuccessStatusCode; }
        catch { return false; }
    }

    private async Task<bool> LaunchAndWaitAsync(HttpClient http, CancellationToken ct)
    {
        var path = config["Elasticsearch:AutoStart:Path"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            logger.LogWarning("[ES-AUTOSTART] Elasticsearch:AutoStart:Path is not set or not found ('{Path}') — " +
                              "cannot auto-launch. Start elasticsearch.bat manually, or set the path.", path);
            return false;
        }

        var heap = config.GetValue("Elasticsearch:AutoStart:HeapMb", 1024);
        try
        {
            // cmd /c so the .bat runs; ES_JAVA_OPTS caps the heap so ES doesn't try to grab ~16 GB
            // and die with an mmap failure on a smaller machine. UseShellExecute MUST be false to
            // pass environment variables (.NET forbids env vars with ShellExecute); the ES process
            // is a java grandchild and keeps running after this API exits, and the reachability
            // check above stops a second launch on the next start.
            var psi = new ProcessStartInfo("cmd.exe", $"/c \"\"{path}\"\"")
            {
                UseShellExecute  = false,
                CreateNoWindow   = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            };
            psi.EnvironmentVariables["ES_JAVA_OPTS"] = $"-Xms{heap}m -Xmx{heap}m";
            Process.Start(psi);
            logger.LogInformation("[ES-AUTOSTART] Launched Elasticsearch ({Path}, heap {Heap}MB) — waiting for it to be ready…",
                path, heap);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ES-AUTOSTART] failed to launch Elasticsearch — start it manually.");
            return false;
        }

        var timeout = config.GetValue("Elasticsearch:AutoStart:ReadyTimeoutSeconds", 180);
        var deadline = DateTime.UtcNow.AddSeconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested) return false;
            if (await IsReachableAsync(http, ct))
            {
                logger.LogInformation("[ES-AUTOSTART] Elasticsearch is up.");
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
        logger.LogWarning("[ES-AUTOSTART] Elasticsearch did not become ready within {N}s — continuing without it.", timeout);
        return false;
    }

    private async Task EnsureIndexedAsync(HttpClient http, CancellationToken ct)
    {
        var cmd = config["Elasticsearch:AutoStart:Indexer:Command"];
        if (string.IsNullOrWhiteSpace(cmd))
            return; // no indexer configured — assume the operator manages the index

        // Only index when the index is missing or has zero docs. A populated index survives
        // reboots on disk, so this is a one-time cost on a fresh machine, never on every start.
        long count = -1;
        try
        {
            var r = await http.GetAsync($"{Host}/{Index}/_count", ct);
            if (r.IsSuccessStatusCode)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
                count = doc.RootElement.TryGetProperty("count", out var c) ? c.GetInt64() : -1;
            }
        }
        catch { /* index likely doesn't exist yet → treat as empty */ }

        if (count > 0)
        {
            logger.LogInformation("[ES-AUTOSTART] index '{Index}' already has {N} docs — skipping indexer.", Index, count);
            return;
        }

        var workDir = config["Elasticsearch:AutoStart:Indexer:WorkingDirectory"] ?? "";
        logger.LogInformation("[ES-AUTOSTART] index '{Index}' is empty — running indexer: {Cmd} (in {Dir})", Index, cmd, workDir);
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c {cmd}")
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                WorkingDirectory       = workDir,
            };
            using var p = Process.Start(psi);
            if (p is null) { logger.LogWarning("[ES-AUTOSTART] indexer failed to start."); return; }

            // Give the (minutes-long) indexer a bounded window; it's fine if this outlives startup.
            var stdout = await p.StandardOutput.ReadToEndAsync(ct);
            var stderr = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);

            var tail = (stdout + stderr).Split('\n').TakeLast(3);
            if (p.ExitCode == 0)
                logger.LogInformation("[ES-AUTOSTART] indexer finished (exit 0). {Tail}", string.Join(" | ", tail));
            else
                logger.LogWarning("[ES-AUTOSTART] indexer exited {Code}. {Tail}", p.ExitCode, string.Join(" | ", tail));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ES-AUTOSTART] indexer run failed — index the corpus manually.");
        }
    }
}
