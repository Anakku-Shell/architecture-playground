using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shop.Layered.Business.Errors;

namespace Shop.Layered.Api.ErrorHandling;

/// <summary>
/// Translates the Business layer's exceptions into HTTP: the only place that knows which status code each
/// kind of failure gets. Any other exception is not handled here and ends as a <c>500</c>. Guide: §4.5,
/// "The error path".
/// </summary>
public sealed class BusinessExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            // Thrown by the framework while binding (malformed JSON, "abc" for a Guid). In Development
            // Minimal APIs throw it instead of answering 400 themselves, so it is translated here too.
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "Bad request",
                Detail = badRequest.Message,
            },
            ValidationException validation => new HttpValidationProblemDetails(validation.Errors.ToDictionary())
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = notFound.Message,
            },
            BusinessRuleException rule => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The request conflicts with a business rule",
                Detail = rule.Message,
            },
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
}
