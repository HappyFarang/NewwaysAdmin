using System;
using System.Text;
using System.Security;
using System.Security.Cryptography;
using System.Collections.Generic;
using NewwaysAdmin.FileSync.Models;
using NewwaysAdmin.FileSync.Security;

namespace NewwaysAdmin.FileSync.Security
{
    public class SecureMessageHandler
    {
        private readonly PgpEncryption _pgp;
        private readonly byte[] _privateKey;
        private readonly string _privateKeyPassword;
        private readonly Dictionary<string, byte[]> _clientPublicKeys;
        private readonly Dictionary<string, string> _clientApiKeys;
        private readonly TimeSpan _messageTimeout = TimeSpan.FromMinutes(5);

        private readonly HashSet<string> _usedMessageIds = new();
        private readonly object _messageIdsLock = new();

        public SecureMessageHandler(
            PgpEncryption pgp,
            byte[] privateKey,
            string privateKeyPassword,
            Dictionary<string, byte[]> clientPublicKeys,
            Dictionary<string, string> clientApiKeys)
        {
            _pgp = pgp;
            _privateKey = privateKey;
            _privateKeyPassword = privateKeyPassword;
            _clientPublicKeys = clientPublicKeys;
            _clientApiKeys = clientApiKeys;

            // Start cleanup of old message IDs
            StartMessageIdCleanup();
        }

        public SecureMessage CreateSecureMessage<T>(T payload, string clientId)
        {
            if (!_clientPublicKeys.TryGetValue(clientId, out var clientPublicKey))
                throw new SecurityException("Unknown client");

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var data = Encoding.UTF8.GetBytes(json);

            var encrypted = _pgp.EncryptAndSign(
                data,
                clientPublicKey,
                _privateKey,
                _privateKeyPassword
            );

            return new SecureMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                ClientId = clientId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                EncryptedPayload = Convert.ToBase64String(encrypted),
                Signature = GenerateSignature(clientId, encrypted)
            };
        }

        public T DecryptMessage<T>(SecureMessage message)
        {
            // Verify message hasn't expired
            var messageTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(message.Timestamp));
            if (DateTimeOffset.UtcNow - messageTime > _messageTimeout)
                throw new SecurityException("Message has expired");

            // Prevent replay attacks
            if (!IsUniqueMessageId(message.MessageId))
                throw new SecurityException("Message has already been processed");

            // Verify client is known
            if (!_clientPublicKeys.TryGetValue(message.ClientId, out var clientPublicKey))
                throw new SecurityException("Unknown client");

            // Verify signature
            if (!VerifySignature(message))
                throw new SecurityException("Invalid message signature");

            var encryptedData = Convert.FromBase64String(message.EncryptedPayload);

            var decrypted = _pgp.DecryptAndVerify(
                encryptedData,
                _privateKey,
                _privateKeyPassword,
                clientPublicKey
            );

            var json = Encoding.UTF8.GetString(decrypted);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
        }

        private string GenerateSignature(string clientId, byte[] data)
        {
            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_clientApiKeys[clientId]));
            var hash = hmac.ComputeHash(data);
            return Convert.ToBase64String(hash);
        }

        private bool VerifySignature(SecureMessage message)
        {
            if (!_clientApiKeys.TryGetValue(message.ClientId, out var apiKey))
                return false;

            var encryptedData = Convert.FromBase64String(message.EncryptedPayload);
            var expectedSignature = GenerateSignature(message.ClientId, encryptedData);

            return message.Signature == expectedSignature;
        }

        private bool IsUniqueMessageId(string messageId)
        {
            lock (_messageIdsLock)
            {
                if (_usedMessageIds.Contains(messageId))
                    return false;

                _usedMessageIds.Add(messageId);
                return true;
            }
        }

        private void StartMessageIdCleanup()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));

                    lock (_messageIdsLock)
                    {
                        _usedMessageIds.Clear();
                    }
                }
            });
        }
    }
}