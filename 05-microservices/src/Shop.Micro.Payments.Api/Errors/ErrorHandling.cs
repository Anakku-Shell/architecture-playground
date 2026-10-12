using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Shop.Micro.Payments.Api.Errors;

// The Payments service's errors and their translation into ProblemDetails. Version 04 shared this code
// between modules; services share no code but Contracts, Messaging and ServiceDefaults, so each service
// keeps its own small copy. A little duplication is the price of deploying them independently.
// Guide: §8.6, "The error path".

/// <summary>The input breaks one or more validation rules. Becomes <c>400</c> with an <c>errors</c> member.</summary>
internal sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The payment does not exist. Becomes <c>404</c>.</summary>
internal sealed class NotFoundException(string message) : Exception(message);

internal sealed class ErrorHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            // Binding failed (malformed JSON, "abc" for a Guid). Thrown in Development; Production answers 400 itself.
            BadHttpRequestException badRequest => new() { Status = badRequest.StatusCode, Title = "Bad request", Detail = badRequest.Message },
            ValidationException validation => new HttpValidationProblemDetails(validation.Errors.ToDictionary())
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            NotFoundException notFound => new() { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = notFound.Message },
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
}
