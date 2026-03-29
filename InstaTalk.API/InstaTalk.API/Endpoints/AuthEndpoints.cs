using InstaTalk.API.Domain.DTOs;
using InstaTalk.API.Domain.Entities;
using InstaTalk.API.Infrastructure.Data;
using InstaTalk.API.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace InstaTalk.API.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/register", Register).RequireRateLimiting("StrictPolicy");
        group.MapPost("/login", Login).RequireRateLimiting("StrictPolicy");
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapPost("/refresh", RefreshToken);
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterRequest request,
        AppDbContext db,
        PasswordHasherService hasher,
        IConnectionMultiplexer redis,
        HttpContext context)
    {
        // HONEYPOT TRAP: Se o campo invisível for preenchido, é bot.
        if (!string.IsNullOrEmpty(request.Website))
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var clientFingerprint = context.Request.Headers.UserAgent.ToString();
            await redis.GetDatabase().StringSetAsync($"banned:{ip}:{clientFingerprint}", "honeypot", TimeSpan.FromDays(365));

            // Retorna sucesso falso para não alertar o atacante
            return Results.Ok(new { Message = "User created successfully" });
        }

        if (await db.Users.AnyAsync(u => u.Email == request.Email))
            return Results.Conflict(new { error = "Email already in use" }); // Evita enumeração se preferir, ou retorna 400.

        var user = new User
        {
            Email = request.Email,
            PasswordHash = hasher.HashPassword(request.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Results.Created(string.Empty, new { Message = "User created successfully" });
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        AppDbContext db,
        PasswordHasherService hasher,
        JwtTokenGenerator jwtGen)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == request.Email);

        // REGRA DE SEGURANÇA: Retorno genérico para falha
        if (user == null || !hasher.VerifyPassword(request.Password, user.PasswordHash))
            return Results.Json(new { error = "Invalid Credentials" }, statusCode: 401);

        var response = new TokenResponse(
            jwtGen.GenerateAccessToken(user.Id),
            jwtGen.GenerateRefreshToken(user.Id)
        );

        return Results.Ok(response);
    }

    private static async Task<IResult> Logout(
        HttpContext context,
        IConnectionMultiplexer redis)
    {
        var jti = context.User.Claims.FirstOrDefault(c => c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;

        if (!string.IsNullOrEmpty(jti))
        {
            // Adiciona o ID do Token no Redis com expiração de 15 minutos (tempo de vida do Access Token)
            await redis.GetDatabase().StringSetAsync($"blacklist:{jti}", "revoked", TimeSpan.FromMinutes(15));
        }

        return Results.NoContent();
    }

    private static async Task<IResult> RefreshToken(
            [FromBody] RefreshTokenRequest request,
            AppDbContext db,
            JwtTokenGenerator tokenGenerator)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
            return Results.Unauthorized();

        var newAccessToken = tokenGenerator.GenerateAccessToken(user.Id);
        var newRefreshToken = Guid.NewGuid().ToString();

        return Results.Ok(new
        {
            accessToken = newAccessToken,
            refreshToken = newRefreshToken
        });
    }
}