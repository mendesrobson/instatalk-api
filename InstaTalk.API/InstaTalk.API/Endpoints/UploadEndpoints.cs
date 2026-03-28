using InstaTalk.API.Infrastructure.Storage;

namespace InstaTalk.API.Endpoints;

public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/uploads")
                       .WithTags("Uploads")
                       .RequireAuthorization()
                       .RequireRateLimiting("StrictPolicy");

        // DisableAntiforgery é necessário no .NET 8+ para aceitar multipart/form-data em Minimal APIs
        group.MapPost("/image", UploadImage).DisableAntiforgery();
    }

    private static async Task<IResult> UploadImage(
        IFormFile file,
        HttpContext context,
        IWebHostEnvironment env)
    {
        // 1. Defesa 1: Arquivo existe e Limite de Tamanho (5MB)
        const long maxSizeBytes = 5 * 1024 * 1024;
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "No file uploaded." });

        if (file.Length > maxSizeBytes)
            return Results.BadRequest(new { error = "File exceeds the 5MB limit." });

        // 2. Defesa 2: Validação de Magic Bytes (O coração da segurança)
        if (!FileValidator.IsValidImage(file))
            return Results.BadRequest(new { error = "Invalid file type. Only real JPG and PNG are allowed." });

        // 3. Defesa 3: Sanitização de Nome e Path Traversal
        // NUNCA use file.FileName. Gere um novo nome aleatório.
        var secureExtension = FileValidator.GetExtension(file);
        var secureFileName = $"{Guid.NewGuid()}{secureExtension}";

        // Define o diretório wwwroot/uploads
        var uploadPath = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");

        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        var finalFilePath = Path.Combine(uploadPath, secureFileName);

        // 4. Salva o arquivo no disco
        using (var stream = new FileStream(finalFilePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // 5. Retorna a URL relativa estrita pertencente ao nosso domínio (Previne SSRF)
        // O cliente construirá a URL completa no frontend, ou o backend entrega o caminho relativo
        var fileUrl = $"/uploads/{secureFileName}";

        return Results.Created(fileUrl, new { url = fileUrl });
    }
}
