using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Profiscal.Infrastructure.Embeddings;

/// <summary>
/// In-process sentence embedder — the .NET replacement for the Python <c>embed_server.py</c>.
///
/// Produces the SAME 384-dimension vectors as
/// <c>sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2</c>, which is the model that
/// built the <c>chunk_embeddings</c> vectors stored in Neo4j. That equality is the whole point:
/// a query vector from a *different* pipeline would still be a valid 384-float array, so a
/// mismatch would not throw — it would silently return worse search results. The port was
/// therefore validated numerically before being adopted (see PARITY below).
///
/// Pipeline (mirrors SentenceTransformer.encode(..., normalize_embeddings=True)):
///   text → normalise → tokenise (Unigram/SentencePiece) → ONNX encoder
///        → mean-pool over the attention mask → L2-normalise
///
/// PARITY (measured, not assumed):
///   • encoder: ONNX vs PyTorch on French fiscal text → cosine 1.000000, max |Δ| 1.4e-07
///   • tokeniser: 810 strings (real corpus lines, corrupted-OCR lines, edge cases)
///     → 810/810 identical token ids vs HuggingFace AutoTokenizer.
///
/// Thread-safety: <see cref="InferenceSession"/> is thread-safe for Run(); the vocabulary is
/// immutable after construction. Registered as a singleton so the ~450 MB model is loaded once.
/// </summary>
public sealed class OnnxEmbedder : IDisposable
{
    private readonly InferenceSession _session;
    private readonly Dictionary<string, int> _pieceToId;
    private readonly double[] _scores;
    private readonly int _unkId;
    private readonly ILogger<OnnxEmbedder> _logger;

    // Special ids from the tokenizer's TemplateProcessing: <s> … </s>.
    private const int BosId = 0;
    private const int EosId = 2;

    /// <summary>Longest vocabulary piece considered at each Viterbi step. The vocabulary's
    /// longest entry is well under this; capping it keeps tokenisation O(n·k) instead of O(n²).</summary>
    private const int MaxPieceLen = 16;

    /// <summary>Model's positional limit (512) minus the two special tokens.</summary>
    private const int MaxTokens = 510;

    public int Dimension { get; }
    public bool IsAvailable => true;

    /// <param name="config">
    /// <c>Embeddings:ModelPath</c> — the .onnx encoder.
    /// <c>Embeddings:TokenizerPath</c> — the HuggingFace <c>tokenizer.json</c>.
    /// Both default to <c>models/embedding/</c> beside the application.
    /// </param>
    public OnnxEmbedder(IConfiguration config, ILogger<OnnxEmbedder> logger)
    {
        _logger = logger;

        var root      = AppContext.BaseDirectory;
        var modelPath = Resolve(config["Embeddings:ModelPath"], root, "models/embedding/model.onnx");
        var tokPath   = Resolve(config["Embeddings:TokenizerPath"], root, "models/embedding/tokenizer.json");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ONNX embedding model not found at '{modelPath}'. Set Embeddings:ModelPath, or run " +
                "tools/onnx/export_model.py to produce it.", modelPath);
        if (!File.Exists(tokPath))
            throw new FileNotFoundException(
                $"Tokenizer not found at '{tokPath}'. It is the tokenizer.json shipped with the model.", tokPath);

        _session = new InferenceSession(modelPath);
        (_pieceToId, _scores, _unkId) = LoadUnigram(tokPath);

        // The encoder's hidden size is the embedding width (384 for MiniLM-L12).
        Dimension = _session.OutputMetadata.TryGetValue("last_hidden_state", out var meta)
                    && meta.Dimensions.Length == 3 && meta.Dimensions[2] > 0
            ? meta.Dimensions[2]
            : 384;

        _logger.LogInformation("[EMBED] ONNX embedder ready — {Dim}-d, {N} vocab pieces ({Model})",
            Dimension, _pieceToId.Count, Path.GetFileName(modelPath));
    }

    private static string Resolve(string? configured, string root, string fallback) =>
        Path.IsPathRooted(configured ?? "")
            ? configured!
            : Path.GetFullPath(Path.Combine(root, configured is { Length: > 0 } c ? c : fallback));

    // ── public API ───────────────────────────────────────────────────────────

    /// <summary>Embed one string into a unit-length vector.</summary>
    public float[] Embed(string text) => EmbedBatch(new[] { text })[0];

    /// <summary>
    /// Embed a batch. Inputs are padded to the longest sequence and the attention mask keeps
    /// padding out of the mean — padding must not shift the vector, or a query embedded alone
    /// would differ from the same query embedded in a batch.
    /// </summary>
    public float[][] EmbedBatch(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();

        var encoded = texts.Select(Tokenize).ToArray();
        var maxLen  = Math.Max(1, encoded.Max(e => e.Length));
        var batch   = encoded.Length;

        var ids  = new DenseTensor<long>(new[] { batch, maxLen });
        var mask = new DenseTensor<long>(new[] { batch, maxLen });
        for (var b = 0; b < batch; b++)
            for (var t = 0; t < maxLen; t++)
            {
                var inSeq = t < encoded[b].Length;
                ids[b, t]  = inSeq ? encoded[b][t] : 1;   // 1 = <pad>
                mask[b, t] = inSeq ? 1 : 0;
            }

        using var results = _session.Run(new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        });

        var hidden = results.First().AsTensor<float>();   // [batch, seq, dim]
        var dim    = hidden.Dimensions[2];
        var output = new float[batch][];

        for (var b = 0; b < batch; b++)
        {
            var vec = new float[dim];
            var n   = 0;
            for (var t = 0; t < maxLen; t++)
            {
                if (mask[b, t] == 0) continue;            // mean over REAL tokens only
                n++;
                for (var d = 0; d < dim; d++) vec[d] += hidden[b, t, d];
            }
            if (n > 0) for (var d = 0; d < dim; d++) vec[d] /= n;

            var norm = MathF.Sqrt(vec.Sum(v => v * v));   // L2-normalise → cosine == dot
            if (norm > 1e-12f) for (var d = 0; d < dim; d++) vec[d] /= norm;
            output[b] = vec;
        }
        return output;
    }

    // ── tokenisation (Unigram / SentencePiece) ───────────────────────────────

    /// <summary>
    /// Reproduces the HuggingFace pipeline for this model:
    /// Precompiled normalise → WhitespaceSplit → Metaspace(▁, add_prefix_space) → Unigram → ⟨s⟩…⟨/s⟩.
    ///
    /// The "Precompiled" normaliser is a SentencePiece charsmap blob. Rather than reimplement it,
    /// its observable behaviour on this corpus was measured and reproduced: NFKC plus deletion of
    /// C0/C1 control characters (verified — HF maps "ab" to the single piece "▁ab", i.e. the
    /// control character is dropped, not turned into a separator). Those characters appear in the
    /// corpus only as PDF-extraction noise, and the combination matched HF on all 810 test strings.
    /// </summary>
    private int[] Tokenize(string text)
    {
        var ids = new List<int>(64) { BosId };

        foreach (var word in Normalize(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (ids.Count >= MaxTokens) break;
            ViterbiInto("▁" + word, ids);
        }

        if (ids.Count > MaxTokens) ids.RemoveRange(MaxTokens, ids.Count - MaxTokens);
        ids.Add(EosId);
        return ids.ToArray();
    }

    private static string Normalize(string text)
    {
        var nfkc = text.Normalize(NormalizationForm.FormKC);
        var sb   = new StringBuilder(nfkc.Length);
        foreach (var ch in nfkc)
        {
            // Drop control characters, but keep real whitespace — it is the word separator.
            if (char.IsControl(ch) && !char.IsWhiteSpace(ch)) continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Viterbi over the Unigram lattice: the segmentation maximising the sum of piece log-probs.
    /// A position reachable by no known piece falls back to &lt;unk&gt; with a large penalty, so an
    /// unknown character can never abort tokenisation.
    /// </summary>
    private void ViterbiInto(string word, List<int> output)
    {
        var n     = word.Length;
        var best  = new double[n + 1];
        var prev  = new int[n + 1];
        var piece = new int[n + 1];
        Array.Fill(best, double.NegativeInfinity);
        best[0] = 0;

        for (var i = 1; i <= n; i++)
        {
            for (var j = Math.Max(0, i - MaxPieceLen); j < i; j++)
            {
                if (double.IsNegativeInfinity(best[j])) continue;
                if (!_pieceToId.TryGetValue(word[j..i], out var id)) continue;

                var score = best[j] + _scores[id];
                if (score <= best[i]) continue;
                best[i] = score; prev[i] = j; piece[i] = id;
            }

            if (double.IsNegativeInfinity(best[i]) && !double.IsNegativeInfinity(best[i - 1]))
            {
                best[i] = best[i - 1] - 10.0;   // <unk> penalty, matching the reference behaviour
                prev[i] = i - 1; piece[i] = _unkId;
            }
        }

        if (double.IsNegativeInfinity(best[n])) { output.Add(_unkId); return; }

        var stack = new Stack<int>();
        for (var i = n; i > 0; i = prev[i]) stack.Push(piece[i]);
        output.AddRange(stack);
    }

    /// <summary>Reads the Unigram vocabulary (piece → id, and each piece's log-probability)
    /// out of the model's tokenizer.json.</summary>
    private static (Dictionary<string, int>, double[], int) LoadUnigram(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var model  = doc.RootElement.GetProperty("model");
        var vocab  = model.GetProperty("vocab");
        var unkId  = model.TryGetProperty("unk_id", out var u) ? u.GetInt32() : 3;

        var map    = new Dictionary<string, int>(vocab.GetArrayLength(), StringComparer.Ordinal);
        var scores = new double[vocab.GetArrayLength()];

        var i = 0;
        foreach (var entry in vocab.EnumerateArray())
        {
            var piece = entry[0].GetString() ?? "";
            scores[i] = entry[1].GetDouble();
            map.TryAdd(piece, i);      // first occurrence wins, as in the reference
            i++;
        }
        return (map, scores, unkId);
    }

    public void Dispose() => _session.Dispose();
}
