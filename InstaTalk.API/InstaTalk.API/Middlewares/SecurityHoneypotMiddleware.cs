using StackExchange.Redis;

namespace InstaTalk.API.Middlewares;

public class SecurityHoneypotMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer _redis;

    public SecurityHoneypotMiddleware(RequestDelegate next, IConnectionMultiplexer redis)
    {
        _next = next;
        _redis = redis;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var db = _redis.GetDatabase();
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var clientFingerprint = context.Request.Headers["User-Agent"].ToString();
        var banKey = $"banned:{ipAddress}:{clientFingerprint}";

        // 1. Verifica se já está banido (Drop Silencioso, economiza CPU)
        if (await db.KeyExistsAsync(banKey))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return; // Interrompe o request aqui. Sem gastar ciclos de banco de dados.
        }

        // 2. A Armadilha (Honeypot Endpoint)
        if (context.Request.Path.StartsWithSegments("/api/v1/admin/debug"))
        {
            // Atacante detectado. Banimento permanente.
            await db.StringSetAsync(banKey, "honeypot_triggered", TimeSpan.FromDays(365));
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }
}
