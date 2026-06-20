using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Chat.Queries.Chat;

public sealed record ChatQuery(string Question, List<string> History) : IRequest<ChatResponseDto>;
public sealed record ChatResponseDto(string Answer, List<SourceChunkDto> Sources, double ElapsedMs);

public sealed class ChatQueryHandler(
    IRetrievalAgent retrieval,
    ILlmAgent llm,
    IEmbedSearchAgent embed,
    ILogger<ChatQueryHandler> logger)
    : IRequestHandler<ChatQuery, ChatResponseDto>
{
    private const string ChatSystemPrompt =
        "Tu es un assistant fiscal tunisien expert. Reponds en te basant UNIQUEMENT sur les sources fournies. " +
        "Si la reponse n est pas dans les sources, dis-le clairement. Cite les sources pertinentes.";

    public async Task<ChatResponseDto> Handle(ChatQuery query, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        logger.LogInformation("Chat: '{Q}'", query.Question[..Math.Min(query.Question.Length, 60)]);

        // Retrieve relevant chunks using embed + keyword fallback
        var embedResults = await embed.SearchAsync(query.Question, topK: 10);
        var chunks       = embedResults.Count >= 3
            ? embedResults.Select(s => new SourceChunkDto
              { DocName = s.DocName, Text = s.Text, ArticleRef = s.ArticleRef,
                Score = s.Score, ChunkType = "text", Category = s.DocType }).ToList()
            : await retrieval.KeywordFallbackAsync(query.Question, topK: 8);

        // Build context from chunks
        var context = string.Join("\n\n", chunks.Take(8).Select((c, i) =>
            $"[Source {i+1}: {c.DocName} {c.ArticleRef}]\n{c.Text}"));

        // Build conversation with history
        var messages = new List<(string, string)>();
        foreach (var h in query.History.Chunk(2))
        {
            if (h.Length >= 1) messages.Add(("user", h[0]));
            if (h.Length >= 2) messages.Add(("assistant", h[1]));
        }
        messages.Add(("user", $"Sources disponibles:\n{context}\n\nQuestion: {query.Question}"));

        var answer = await llm.ChatAsync(messages, ChatSystemPrompt, ct) ?? "Je n'ai pas pu répondre.";
        sw.Stop();

        return new ChatResponseDto(answer, chunks, sw.Elapsed.TotalMilliseconds);
    }
}
