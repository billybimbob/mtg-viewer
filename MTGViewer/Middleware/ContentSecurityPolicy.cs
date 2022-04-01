using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MTGViewer.Middleware;

public class ContentSecurityPolicy
{
    private static readonly string Policy = string.Join(' ',
        "base-uri 'self';",
        "child-src 'none';",
        "default-src 'self';",
        "frame-ancestors 'none';",
        "img-src data: https:;",
        "object-src 'none';",
        "script-src 'self';",
        "style-src 'self' 'sha256-9Nkt6zGDtyRXhEF45Xj607dE12YASsy2b157ViK9w6E=' 'unsafe-hashes';",
        "upgrade-insecure-requests;");


    private readonly RequestDelegate _next;

    public ContentSecurityPolicy(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.ContentSecurityPolicy = Policy;

        await _next.Invoke(context);
    }
}