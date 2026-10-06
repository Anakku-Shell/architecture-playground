using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Domain.Common;

namespace Shop.Micro.Ordering.Api.Http;

/// <summary>
/// Turns the Ordering service's exceptions (domain and application) into ProblemDetails. Two are new in 05:
/// a concurrent change of the same order (<c>409</c>) and Catalog not answering (<c>503</c>, the request was
/// fine but another service was not). Guide: §8.6, "The error path".
/// </summary>
internal sealed class ErrorHandler(IProblemDetailsService problemDetails) : IExceptionHandler
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

            // Safety net: the use cases turn invalid values into ValidationException with field names first.
            DomainValidationException invalid => new HttpValidationProblemDetails(new Dictionary<string, string[]> { ["request"] = [invalid.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "Not found", notFound.Message),
            BusinessRuleViolationException rule => Problem(StatusCodes.Status409Conflict, "The request conflicts with a business rule", rule.Message),
            ConcurrencyConflictException conflict => Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
            CatalogUnavailableException unavailable => Problem(StatusCodes.Status503ServiceUnavailable, "A service the request needs is unavailable", unavailable.Message),
            _ => null,
        };
        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        if (!await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception }))
        {
            // The client's Accept header excluded JSON. Errors are always ProblemDetails, so write it anyway.
            await httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
        }

        return true;
    }

    private static ProblemDetails Problem(int status, string title, string detail) => new() { Status = status, Title = title, Detail = detail };
}
