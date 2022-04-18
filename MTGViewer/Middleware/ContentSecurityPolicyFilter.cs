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
        "script-src-elem 'self' https://ajax.aspnetcdn.com/",
            "'sha256-+fPW75vLIBgWpR7fY/Cy5avrOp3uSpkkuyeiEztic04='",
            "'sha256-60gvxwFPUMSsgTLBh44jwW0wr2J6IsmBWn4MeX9KpHw=';",
        "style-src 'self' 'unsafe-hashes'",
            "'sha256-9Nkt6zGDtyRXhEF45Xj607dE12YASsy2b157ViK9w6E=';",
        "upgrade-insecure-requests;");

    public void OnResultExecuting(ResultExecutingContext context)
    {
        context.HttpContext.Response.Headers.ContentSecurityPolicy = Policy;
    }

    public void OnResultExecuted(ResultExecutedContext context)
    { }
}
