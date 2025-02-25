using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace NewwaysAdmin.Web.Models
{
    public class UserCredential
    {
        public string Username { get; set; } = string.Empty;

        // Make it public for serialization but JsonIgnore in memory
        [JsonIgnore]
        public string PasswordHash { get; private set; } = string.Empty;

        // This property is used for serialization
        public string StoredHash
        {
            get => PasswordHash;
            set => PasswordHash = value;
        }

        public string Role { get; set; } = "User";
        public DateTime LastLogin { get; set; }

        public void SetPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            PasswordHash = Convert.ToBase64String(hashedBytes);
        }

        public bool ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(PasswordHash))
                return false;

            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var hashedPassword = Convert.ToBase64String(hashedBytes);
            return PasswordHash == hashedPassword;
        }
    }
}