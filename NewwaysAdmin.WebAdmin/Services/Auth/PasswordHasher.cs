using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace NewwaysAdmin.WebAdmin.Services.Auth;

public static class PasswordHasher
{
    private const int IterationCount = 100000;

    public static string GenerateSalt()
    {
        var salt = RandomNumberGenerator.GetBytes(128 / 8);
        return Convert.ToBase64String(salt);
    }

    public static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);

        return Convert.ToBase64String(KeyDerivation.Pbkdf2(
            password: password,
            salt: saltBytes,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: IterationCount,
            numBytesRequested: 256 / 8));
    }
}