namespace Profiscal.Domain.Exceptions;

public class DomainException(string message) : Exception(message);

public class NotFoundException(string entity, object key)
    : DomainException($"{entity} with id '{key}' was not found.");
