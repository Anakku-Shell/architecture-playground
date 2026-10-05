using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shop.Modular.BuildingBlocks.Infrastructure.Errors;
using Shop.Modular.Ordering.Domain.Common;

namespace Shop.Modular.Ordering.Infrastructure.Http;

/// <summary>
/// Turns the Ordering domain's own exceptions into ProblemDetails. They are this module's types, so this
/// module translates them; the Host and the shared handler never learn they exist. Guide: §7.5, "The error path".
/// </summary>
internal sealed class OrderingExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            // Safety net: the use cases turn invalid values into ValidationException with field names first.
            DomainValidationException invalid => ProblemDetailsWriter.Validation(new Dictionary<string, string[]> { ["request"] = [invalid.Message] }),
            BusinessRuleViolationException rule => ProblemDetailsWriter.Problem(StatusCodes.Status409Conflict, "The request conflicts with a business rule", rule.Message),
            _ => null,
        };

        return problem is null
            ? ValueTask.FromResult(false)
            : ProblemDetailsWriter.WriteAsync(problemDetails, httpContext, problem, exception, cancellationToken);
    }
}
