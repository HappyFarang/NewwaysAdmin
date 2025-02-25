using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;
using System.Security;
using System.Text;

namespace NewwaysAdmin.FileSync.Security
{
    public class PgpKeyPair
    {
        public required byte[] PublicKey { get; set; }
        public required byte[] PrivateKey { get; set; }
        public required string KeyFingerprint { get; set; }
    }

    public class PgpEncryption
    {
        private const int KEY_SIZE = 4096;  // Strong RSA key size
        private readonly SecureRandom _random = new();

        public PgpKeyPair GenerateKeyPair(string identity, string password)
        {
            var keyPairGenerator = GeneratorUtilities.GetKeyPairGenerator("RSA");
            keyPairGenerator.Init(new KeyGenerationParameters(_random, KEY_SIZE));

            var keyPair = keyPairGenerator.GenerateKeyPair();
            var secretKey = new PgpSecretKey(
                PgpSignature.DefaultCertification,
                PublicKeyAlgorithmTag.RsaGeneral,
                keyPair.Public,
                keyPair.Private,
                DateTime.UtcNow,
                identity,
                SymmetricKeyAlgorithmTag.Aes256,
                password.ToCharArray(),
                null,
                null,
                _random
            );

            // Export keys
            using var publicKeyStream = new MemoryStream();
            using var privateKeyStream = new MemoryStream();

            secretKey.PublicKey.Encode(publicKeyStream);
            secretKey.Encode(privateKeyStream);

            var fingerprint = BitConverter.ToString(secretKey.PublicKey.GetFingerprint()).Replace("-", "");

            return new PgpKeyPair
            {
                PublicKey = publicKeyStream.ToArray(),
                PrivateKey = privateKeyStream.ToArray(),
                KeyFingerprint = fingerprint
            };
        }

        public byte[] EncryptAndSign(byte[] data, byte[] recipientPublicKey, byte[] senderPrivateKey, string privateKeyPassword)
        {
            using var outputStream = new MemoryStream();

            // Load keys
            var publicKey = new PgpPublicKeyRing(recipientPublicKey).GetPublicKey();
            var privateKey = new PgpSecretKeyRing(senderPrivateKey).GetSecretKey();

            // Compress
            var compressedData = Compress(data);

            // Sign
            var signature = Sign(compressedData, privateKey, privateKeyPassword);

            // Encrypt
            Encrypt(signature, publicKey, outputStream);

            return outputStream.ToArray();
        }

        public byte[] DecryptAndVerify(byte[] encryptedData, byte[] recipientPrivateKey, string privateKeyPassword, byte[] senderPublicKey)
        {
            using var inputStream = new MemoryStream(encryptedData);

            // Load keys
            var privateKey = new PgpSecretKeyRing(recipientPrivateKey).GetSecretKey();
            var publicKey = new PgpPublicKeyRing(senderPublicKey).GetPublicKey();

            // Decrypt
            var decrypted = Decrypt(inputStream, privateKey, privateKeyPassword);

            // Verify signature
            if (!VerifySignature(decrypted, publicKey))
                throw new SecurityException("Invalid signature");

            // Decompress
            return Decompress(decrypted);
        }

        private byte[] Compress(byte[] data)
        {
            using var bOut = new MemoryStream();
            using var comData = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);
            using var compressedOut = comData.Open(bOut);

            compressedOut.Write(data, 0, data.Length);
            compressedOut.Close();

            return bOut.ToArray();
        }

        private byte[] Sign(byte[] data, PgpSecretKey secretKey, string password)
        {
            using var bOut = new MemoryStream();
            using var sigGen = new PgpSignatureGenerator(secretKey.PublicKey.Algorithm, HashAlgorithmTag.Sha512);

            sigGen.InitSign(PgpSignature.BinaryDocument, secretKey.ExtractPrivateKey(password.ToCharArray()));

            using var literalDataGen = new PgpLiteralDataGenerator();
            using var literalOut = literalDataGen.Open(bOut, PgpLiteralData.Binary, "filename", data.Length, DateTime.UtcNow);

            foreach (var b in data)
            {
                literalOut.WriteByte(b);
                sigGen.Update(b);
            }

            using var sigOut = new BcpgOutputStream(bOut);
            sigGen.Generate().Encode(sigOut);

            return bOut.ToArray();
        }

        private void Encrypt(byte[] data, PgpPublicKey publicKey, Stream outputStream)
        {
            using var encGen = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Aes256, true, _random);
            encGen.AddMethod(publicKey);

            using var encOut = encGen.Open(outputStream, data.Length);
            encOut.Write(data, 0, data.Length);
        }

        private byte[] Decrypt(Stream inputStream, PgpSecretKey secretKey, string password)
        {
            var encObjects = new PgpObjectFactory(PgpUtilities.GetDecoderStream(inputStream));
            var enc = (PgpEncryptedDataList)encObjects.NextPgpObject();

            var pbe = (PgpPublicKeyEncryptedData)enc[0];
            using var clear = pbe.GetDataStream(secretKey.ExtractPrivateKey(password.ToCharArray()));

            var plainFact = new PgpObjectFactory(clear);
            var message = plainFact.NextPgpObject();

            if (message is PgpCompressedData cData)
            {
                var compressedFact = new PgpObjectFactory(cData.GetDataStream());
                message = compressedFact.NextPgpObject();
            }

            var ld = (PgpLiteralData)message;
            using var unc = ld.GetInputStream();
            return unc.ReadAllBytes();
        }

        private bool VerifySignature(byte[] data, PgpPublicKey publicKey)
        {
            var pgpFact = new PgpObjectFactory(data);
            var message = pgpFact.NextPgpObject();

            var sig = (PgpSignature)message;
            sig.InitVerify(publicKey);

            return sig.Verify();
        }
    }

    public static class StreamExtensions
    {
        public static byte[] ReadAllBytes(this Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}