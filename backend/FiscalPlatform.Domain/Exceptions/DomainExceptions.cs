namespace FiscalPlatform.Domain.Exceptions;

public sealed class ConsultationGenerationException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class NoSourcesFoundException(string situation)
    : Exception($"No sources found in knowledge base for: {situation[..Math.Min(situation.Length, 80)]}");

public sealed class InvalidRatingException(int stars)
    : Exception($"Rating must be between 1 and 5, got: {stars}");

public sealed class SessionNotFoundException(string sessionId)
    : Exception($"Consultation session not found: {sessionId}");
