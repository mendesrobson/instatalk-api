using InstaTalk.API.Endpoints;
using InstaTalk.API.Infrastructure.Data;
using InstaTalk.API.Infrastructure.Security;
using InstaTalk.API.Middlewares; // Assumindo que o GlobalExceptionHandler e o HoneypotMiddleware estão aqui
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpLogging;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// RADAR DE SEGURANÇA E AUDITORIA (HTTP LOGGING)
// ==========================================
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders |
                            HttpLoggingFields.ResponseStatusCode;

    // DEFENSE IN DEPTH: Mascara cabeçalhos sensíveis para evitar vazamento de credenciais nos logs
    logging.RequestHeaders.Add("Authorization");
    logging.RequestHeaders.Add("Cookie");
});

// --- 1. INFRAESTRUTURA DE BANCO E CACHE ---
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:Configuration"]!));

// --- 2. SERVIÇOS DE SEGURANÇA ---
builder.Services.AddSingleton<PasswordHasherService>();
builder.Services.AddSingleton<JwtTokenGenerator>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// --- 3. AUTENTICAÇÃO JWT E SWAGGER ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:AccessSecret"]!))
        };

        // Validação de Blacklist no Redis em cada requisição autenticada
        opts.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var redis = context.HttpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                var jti = context.Principal?.Claims.FirstOrDefault(c => c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;

                if (jti != null && await redis.GetDatabase().KeyExistsAsync($"blacklist:{jti}"))
                {
                    context.Fail("Token has been revoked.");
                }
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();

// --- 4. RATE LIMITING ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("StrictPolicy", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return RateLimitPartition.GetFixedWindowLimiter($"{ip}|{userAgent}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) });
    });
});

var app = builder.Build();

// ==========================================
// PIPELINE HTTP (A ORDEM IMPORTA)
// ==========================================
app.UseHttpLogging();

// --- 5. PIPELINE HTTP (A ORDEM IMPORTA) ---
app.UseExceptionHandler();
app.UseMiddleware<SecurityHoneypotMiddleware>(); // Derruba conexões banidas instantaneamente

app.UseStaticFiles(); // Permite servir arquivos da pasta wwwroot
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// --- 6. MAPEAMENTO DOS ENDPOINTS ---
app.MapAuthEndpoints();
app.MapPostEndpoints();
app.MapUploadEndpoints();
app.MapSystemEndpoints();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("🛡️ InstaTalk API iniciada e blindada. Escutando requisições...");
});

app.Run();