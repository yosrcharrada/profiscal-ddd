using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Common.Behaviours;

public sealed class LoggingBehaviour<TRequest, TResponse>(
    ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("→ Handling {Name}", name);
        var sw   = System.Diagnostics.Stopwatch.StartNew();
        var resp = await next();
        sw.Stop();
        logger.LogInformation("← Handled {Name} in {Ms:F0}ms", name, sw.Elapsed.TotalMilliseconds);
        return resp;
    }
}
