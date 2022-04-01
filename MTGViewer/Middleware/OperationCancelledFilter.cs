using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Middleware;

public class OperationCancelledFilter : ExceptionFilterAttribute
{
    private readonly ILogger<OperationCancelledFilter> _logger;

    public OperationCancelledFilter(ILogger<OperationCancelledFilter> logger)
    {
        _logger = logger;
    }

    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is OperationCanceledException)
        {
            _logger.LogInformation("Request was cancelled {Cancel}", context.Exception);

            context.ExceptionHandled = true;
            context.Result = new StatusCodeResult(499);
        }
    }
}