using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace NewwaysAdmin.WebAdmin.Services.Auth
{
    public static class PasswordHasher
    {
        public static string GenerateSalt()
        {
            var saltBytes = RandomNumberGenerator.GetBytes(128 / 8);
            return Convert.ToBase64String(saltBytes);
        }

        public static string HashPassword(string password, string salt)
        {
            var saltBytes = Convert.FromBase64String(salt);
            return Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: saltBytes,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 100000,
                numBytesRequested: 256 / 8));
        }

        public static bool VerifyPassword(string password, string salt, string hashedPassword)
        {
            var computedHash = HashPassword(password, salt);
            return computedHash == hashedPassword;
        }
    }
}