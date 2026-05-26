using System.Net;
using System.Text.Json;
using FiscalPlatform.Domain.Exceptions;
using FluentValidation;

namespace FiscalPlatform.API.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (ValidationException ex)
        {
            var errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            logger.LogWarning("Validation: {E}", string.Join(", ", errors));
            await Write(ctx, HttpStatusCode.BadRequest, string.Join(" | ", errors));
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Argument error");
            await Write(ctx, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (NoSourcesFoundException ex)
        {
            logger.LogWarning(ex, "No sources found");
            await Write(ctx, HttpStatusCode.UnprocessableEntity, ex.Message);
        }
        catch (ConsultationGenerationException ex)
        {
            logger.LogError(ex, "Generation failed");
            await Write(ctx, HttpStatusCode.InternalServerError, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await Write(ctx, HttpStatusCode.InternalServerError, "Erreur interne. Vérifiez les logs.");
        }
    }

    private static async Task Write(HttpContext ctx, HttpStatusCode status, string message)
    {
        ctx.Response.StatusCode  = (int)status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = message, status = (int)status }));
    }
}
