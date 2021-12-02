using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Filters;

public class OperationCancelledFilter : ExceptionFilterAttribute
{
    private readonly ILogger _logger;

    public OperationCancelledFilter(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<OperationCancelledFilter>();
    }


    public override void OnException(ExceptionContext context)
    {
        if(context.Exception is OperationCanceledException)
        {
            _logger.LogInformation("Request was cancelled");
            context.ExceptionHandled = true;
            context.Result = new StatusCodeResult(499);
        }
    }
}