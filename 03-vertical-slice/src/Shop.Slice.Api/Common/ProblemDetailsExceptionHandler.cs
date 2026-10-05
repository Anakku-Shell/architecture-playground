using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shop.Slice.Api.Domain.Common;

namespace Shop.Slice.Api.Common;

/// <summary>
/// Translates the exceptions of the slices and the domain into ProblemDetails. The domain
/// and the slices say <i>what</i> went wrong; only this class decides which status code that is.
/// Guide: §6.5, "The error path".
/// </summary>
public sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            // Binding failed (malformed JSON, "abc" for a Guid). Thrown in Development; Production answers 400 itself.
            BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "Bad request", badRequest.Message),
            ValidationException validation => new HttpValidationProblemDetails(validation.Errors.ToDictionary())
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            // Safety net: the slices turn invalid values into ValidationException with field names first.
            DomainValidationException invalid => new HttpValidationProblemDetails(new Dictionary<string, string[]> { ["request"] = [invalid.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "Not found", notFound.Message),
            BusinessRuleViolationException rule => Problem(StatusCodes.Status409Conflict, "The request conflicts with a business rule", rule.Message),
            ConflictException conflict => Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

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

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
