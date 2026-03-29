namespace AuthWithAdmin.Server.AuthHelpers;

public class TokenBlacklistMiddleware
{
    private readonly RequestDelegate _next;

    public TokenBlacklistMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, ITokenBlacklistService tokenBlacklistService)
    {
        var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

        if (!string.IsNullOrEmpty(token) && tokenBlacklistService.IsBlacklisted(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(context);
    }

}