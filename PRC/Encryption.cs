using Serilog;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Whispbot.PRC.Databases;
using Whispbot.PRC.Messages;

namespace Whispbot.PRC.PRC
{
    public static class Encryption
    {
        private static readonly string _encryptionKey = Environment.GetEnvironmentVariable("PRC_ENCRYPTION_KEY") ?? throw new InvalidOperationException("PRC_ENCRYPTION_KEY environment variable is not set.");

        public static string EncryptApiKey(string apiKey)
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(_encryptionKey);
            aes.GenerateIV();
            var iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, iv);
            using var ms = new MemoryStream();
            ms.Write(iv, 0, iv.Length);
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(apiKey);
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        public static string DecryptApiKey(string encryptedApiKey)
        {
            try
            {
                var fullCipher = Convert.FromBase64String(encryptedApiKey);

                using var aes = Aes.Create();
                aes.Key = Encoding.UTF8.GetBytes(_encryptionKey);
                var iv = new byte[aes.BlockSize / 8];
                Array.Copy(fullCipher, iv, iv.Length);

                using var decryptor = aes.CreateDecryptor(aes.Key, iv);
                using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                return sr.ReadToEnd();
            }
            catch (ArgumentException)
            {
                Log.Warning($"Invalid API key passed: {encryptedApiKey}");
                Postgres.Execute("UPDATE erlc_servers SET api_key = NULL, hashed_key = NULL WHERE api_key = @1", [encryptedApiKey]);
                return "";
            }
        }

        public static string BuildKey(string encryptedKey, string serverId)
        {
            var decryptedKey = DecryptApiKey(encryptedKey);
            return $"{decryptedKey}-{serverId}";
        }
    }
}
