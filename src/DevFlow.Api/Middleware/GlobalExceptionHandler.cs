using DevFlow.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Middleware;

// Translates the small set of exceptions Application services throw
// (NotFoundException, ForbiddenException, FluentValidation's
// ValidationException) into consistent ProblemDetails responses, so every
// controller doesn't need its own try/catch for the same cases.
// ConcurrencyConflictException<T> is deliberately NOT handled here — its
// 409 body needs to embed the specific resource's current state, which is
// endpoint-specific, so that stays as a local catch in TasksController.
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problemDetails = exception switch
        {
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource not found",
                Detail = notFound.Message
            },
            ForbiddenException forbidden => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "This action is not allowed",
                Detail = forbidden.Message
            },
            ValidationException validationException => new ValidationProblemDetails(
                validationException.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            _ => null
        };

        if (problemDetails is null)
        {
            _logger.LogError(exception, "Unhandled exception");
            return false;
        }

        httpContext.Response.StatusCode = problemDetails.Status!.Value;
        // Serialized by problemDetails' *runtime* type, not the ProblemDetails?
        // this switch expression is statically typed as — otherwise
        // ValidationProblemDetails.Errors (a member only the derived type
        // has) would silently be dropped from every validation response.
        await httpContext.Response.WriteAsJsonAsync(problemDetails, problemDetails.GetType(), cancellationToken);
        return true;
    }
}
