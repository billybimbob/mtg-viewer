using Microsoft.AspNetCore.Mvc.Filters;

namespace MTGViewer.Middleware;

public class ContentSecurityPolicyFilter : IAlwaysRunResultFilter
{
    private static readonly string Policy = string.Join(' ',
        "base-uri 'self';",
        "child-src 'none';",
        "default-src 'self';",
        "frame-ancestors 'none';",
        "img-src data: https:;",
        "object-src 'none';",
        "script-src 'self';",
        "style-src 'self' 'unsafe-hashes' 'sha256-9Nkt6zGDtyRXhEF45Xj607dE12YASsy2b157ViK9w6E=';",
        "upgrade-insecure-requests;");

    public void OnResultExecuting(ResultExecutingContext context)
    {
        context.HttpContext.Response.Headers.ContentSecurityPolicy = Policy;
    }

    public void OnResultExecuted(ResultExecutedContext context)
    { }
}
