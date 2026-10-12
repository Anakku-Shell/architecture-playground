using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Shop.Modular.BuildingBlocks.Infrastructure.Errors;

/// <summary>
/// Translates the shared errors of <see cref="BuildingBlocks"/> into ProblemDetails, the same way for every
/// module. A module with errors of its own (Ordering's domain exceptions) adds its own handler, so the Host
/// never needs to know a module's internal types. Guide: §7.5, "The error path".
/// </summary>
public sealed class SharedErrorsExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            // Binding failed (malformed JSON, "abc" for a Guid). Thrown in Development; Production answers 400 itself.
            BadHttpRequestException badRequest => ProblemDetailsWriter.Problem(badRequest.StatusCode, "Bad request", badRequest.Message),
            ValidationException validation => ProblemDetailsWriter.Validation(validation.Errors.ToDictionary()),
            NotFoundException notFound => ProblemDetailsWriter.Problem(StatusCodes.Status404NotFound, "Not found", notFound.Message),
            ConflictException conflict => ProblemDetailsWriter.Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
            _ => null,
        };

        return problem is null
            ? ValueTask.FromResult(false)
            : ProblemDetailsWriter.WriteAsync(problemDetails, httpContext, problem, exception, cancellationToken);
    }
}

/// <summary>Builds and writes ProblemDetails; shared by every module's exception handler.</summary>
public static class ProblemDetailsWriter
{
    public static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    public static HttpValidationProblemDetails Validation(IDictionary<string, string[]> errors) =>
        new(errors) { Status = StatusCodes.Status400BadRequest, Title = "One or more validation errors occurred." };

    public static async ValueTask<bool> WriteAsync(
        IProblemDetailsService problemDetails, HttpContext httpContext, ProblemDetails problem, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problemDetails);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(problem);
        httpContext.Response.StatusCode = problem.Status!.Value;
        var written = await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
        if (!written)
        {
            // The client's Accept header excluded JSON. Errors are always ProblemDetails, so write it anyway.
            await httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
        }

        return true;
    }
}
