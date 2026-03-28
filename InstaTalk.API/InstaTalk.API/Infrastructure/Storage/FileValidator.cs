namespace InstaTalk.API.Infrastructure.Storage;

public static class FileValidator
{
    // Assinaturas binárias reais de formatos de imagem
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };

    public static bool IsValidImage(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var reader = new BinaryReader(stream);

        // Lê apenas os primeiros 4 bytes (não carrega o arquivo todo na RAM)
        var headerBytes = reader.ReadBytes(4);

        var isJpeg = headerBytes.Take(3).SequenceEqual(Jpeg);
        var isPng = headerBytes.SequenceEqual(Png);

        // Retorna o ponteiro do stream para o início para que o arquivo possa ser salvo depois
        stream.Position = 0;

        return isJpeg || isPng;
    }

    public static string GetExtension(IFormFile file)
    {
        // Se passou na validação acima, podemos inferir a extensão segura
        using var stream = file.OpenReadStream();
        using var reader = new BinaryReader(stream);
        var headerBytes = reader.ReadBytes(4);
        stream.Position = 0;

        return headerBytes.Take(3).SequenceEqual(Jpeg) ? ".jpg" : ".png";
    }
}
