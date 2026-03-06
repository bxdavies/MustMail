using Microsoft.AspNetCore.StaticFiles;
using System.Net;
using System.Security.Claims;

namespace MustMail.Auth;

public static class MaildropStaticFileAuth
{
    public static void OnPrepareResponse(StaticFileResponseContext ctx)
    {
        ClaimsPrincipal? user = ctx.Context.User;

        // Require authentication
        if (user?.Identity?.IsAuthenticated != true)
        {
            Deny(ctx, HttpStatusCode.Unauthorized);
            return;
        }

        string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Null check for user id, shouldn't be null but sanity check
        if (string.IsNullOrEmpty(userId))
        {
            Deny(ctx, HttpStatusCode.Unauthorized);
            return;
        }

        // URL segments after "/maildrop"
        string[] segments = ctx.Context.Request.Path.Value!
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        // segments: ["maildrop", "{userId}", ...]
        if (segments.Length < 3 || segments[0] != "maildrop")
        {
            Deny(ctx, HttpStatusCode.NotFound);
            return;
        }

        string requestedUserId = segments[1];

        // Only allow user access to their own files
        if (!string.Equals(requestedUserId, userId, StringComparison.Ordinal))
        {
            Deny(ctx, HttpStatusCode.Unauthorized);
            return;
        }

        // Allow only these patterns: 
        // /maildrop/{userId}/{messageId}.eml   => segments.Length == 3
        // /maildrop/{userId}/{messageId}/{file} => segments.Length == 4
        if (segments.Length is not (3 or 4))
        {
            Deny(ctx, HttpStatusCode.NotFound);
            return;
        }

        // Force download rather than inline
        ctx.Context.Response.Headers.ContentDisposition = "attachment";
    }

    private static void Deny(StaticFileResponseContext ctx, HttpStatusCode statusCode)
    {
        // Prevent caching of error responses
        ctx.Context.Response.Headers.CacheControl = "no-store";
        ctx.Context.Response.Headers.Pragma = "no-cache";
        ctx.Context.Response.Headers.Expires = "0";

        ctx.Context.Response.StatusCode = (int)statusCode;
        ctx.Context.Response.ContentLength = 0;
        ctx.Context.Response.Body = Stream.Null;
    }
}