using System.Security.Cryptography;
using System.Text;
using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Regras de conta local: geração de handle, hash/verificação de senha.
/// (Sem servidor central; a validação por email real exigiria um backend.)
/// </summary>
public static class AccountService
{
    private static readonly Random Rng = new();

    /// <summary>Gera um handle curto e legível a partir do nome, ex.: @carlos-4821.</summary>
    public static string GenerateHandle(string name)
    {
        string slug = new string((name ?? "usuario")
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        if (string.IsNullOrEmpty(slug)) slug = "usuario";
        if (slug.Length > 12) slug = slug[..12];
        return $"@{slug}-{Rng.Next(1000, 9999)}";
    }

    public static (string hash, string salt) HashPassword(string password)
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
        string salt = Convert.ToBase64String(saltBytes);
        return (Hash(password, salt), salt);
    }

    public static bool Verify(string password, string hash, string salt)
        => !string.IsNullOrEmpty(hash) && Hash(password, salt) == hash;

    private static string Hash(string password, string salt)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(password + "::" + salt);
        byte[] digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest);
    }
}
