namespace AuthWithAdmin.Server.AuthHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;


//לא לגעת - ניהול טוקנים
public class AuthCheck : IAsyncActionFilter
{
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly TokenService _tokenService;

    public AuthCheck(ITokenBlacklistService tokenBlacklistService, TokenService tokenService)
    {
        _tokenBlacklistService = tokenBlacklistService;
        _tokenService = tokenService;
    }
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var authorizationHeader = context.HttpContext.Request.Headers["Authorization"].ToString();
        var token = authorizationHeader.StartsWith("Bearer ") ? authorizationHeader.Substring("Bearer ".Length).Trim() : authorizationHeader;

        if (string.IsNullOrEmpty(token))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Check if the token is blacklisted
        if (_tokenBlacklistService.IsBlacklisted(token))
        {
            context.Result = new UnauthorizedResult();
            return;
        }


          
        var principal = _tokenService?.ValidateToken(token);
        if (principal == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var jwtToken = new JwtSecurityTokenHandler().ReadToken(token) as JwtSecurityToken;
        if (jwtToken.ValidTo < DateTime.UtcNow)
        {
            context.Result = new UnauthorizedResult();
            return;
        }


        var userId = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        context.ActionArguments["authUserId"] = Convert.ToInt32(userId);

        await next();
    }
}
