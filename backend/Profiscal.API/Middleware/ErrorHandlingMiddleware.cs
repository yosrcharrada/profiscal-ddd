using System.Net;
using System.Text.Json;
using Profiscal.Application.Exceptions;
using Profiscal.Domain.Exceptions;

namespace Profiscal.API.Middleware;

public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (NotFoundException ex)
        {
            await WriteResponse(context, HttpStatusCode.NotFound, ex.Message);
        }
        catch (ValidationException ex)
        {
            await WriteResponse(context, HttpStatusCode.BadRequest, ex.Message, ex.Errors);
        }
        catch (DomainException ex)
        {
            await WriteResponse(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (FiscalPlatform.Domain.Exceptions.NoSourcesFoundException)
        {
            await WriteResponse(context, HttpStatusCode.BadGateway,
                "The knowledge base returned no sources. Check that Neo4j is connected (engine status) and try a more specific question.");
        }
        catch (FiscalPlatform.Domain.Exceptions.ConsultationGenerationException ex)
        {
            logger.LogWarning(ex, "Consultation generation failed");
            await WriteResponse(context, HttpStatusCode.BadGateway,
                "Generation failed — the AI model did not return a usable result. Check that the LLM key is valid (engine status).");
        }
        catch (Exception ex) when (ex.GetType().Namespace == "FiscalPlatform.Domain.Exceptions")
        {
            await WriteResponse(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await WriteResponse(context, HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static Task WriteResponse(HttpContext ctx, HttpStatusCode status, string message,
        IEnumerable<string>? errors = null)
    {
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new { message, errors });
        return ctx.Response.WriteAsync(body);
    }
}
