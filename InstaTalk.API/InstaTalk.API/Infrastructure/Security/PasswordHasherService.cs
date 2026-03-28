using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace InstaTalk.API.Infrastructure.Security;

public class PasswordHasherService
{
    // Gera um salt aleatório forte
    private byte[] CreateSalt() => RandomNumberGenerator.GetBytes(16);

    public string HashPassword(string password)
    {
        var salt = CreateSalt();
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = 8, // Usa múltiplas threads
            Iterations = 4,          // Dificuldade de processamento
            MemorySize = 65536       // Consome 64MB de RAM por hash (Defense in depth)
        };

        var hash = argon2.GetBytes(32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = parts[1];

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = 8,
            Iterations = 4,
            MemorySize = 65536
        };

        var actualHash = Convert.ToBase64String(argon2.GetBytes(32));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualHash),
            Encoding.UTF8.GetBytes(expectedHash)); // Previne Timing Attacks
    }
}
