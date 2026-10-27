using System;
using System.Security.Cryptography;
using System.Text;

namespace FlapGame.GameLib.Save
{
    public enum SaveDecodeStatus
    {
        Ok = 0,
        /// <summary>The bytes are readable but the checksum does not match: hand-edited, or written with another secret.</summary>
        IntegrityFailed = 1,
        /// <summary>Not a save file, truncated, or encrypted with another secret.</summary>
        Malformed = 2,
    }

    /// <summary>
    /// The bytes of a save file: one header line, then the payload.
    /// <code>
    /// GLSAVE 1 json &lt;HMAC-SHA256 of the payload, hex&gt;
    /// {"format":1,...}
    /// </code>
    /// With encryption the mode is <c>aes</c> and the payload is base64(IV + AES-256-CBC ciphertext), MAC'd after
    /// encryption. Both modes are always readable, so turning encryption on or off keeps existing saves.
    /// <para>The secret ships inside the build: this stops casual editing and detects damage, it is not security.</para>
    /// </summary>
    public sealed class SaveCodec
    {
        public const string Magic = "GLSAVE";
        public const int FormatVersion = 1;
        private const string PlainMode = "json";
        private const string AesMode = "aes";

        private static readonly UTF8Encoding Utf8 = new(false);

        private readonly byte[] macKey;
        private readonly byte[] encryptionKey;

        /// <param name="secret">Key material. Changing it makes existing saves fail their checksum - keep it for the life of the game.</param>
        /// <param name="encrypt">Write encrypted payloads.</param>
        public SaveCodec(string secret, bool encrypt)
        {
            secret ??= string.Empty;
            Encrypt = encrypt;
            // Direct constructors, not SHA256.Create() / Aes.Create(): those resolve the type through CryptoConfig by name,
            // which IL2CPP managed stripping can remove.
            using var sha = new SHA256Managed();
            macKey = sha.ComputeHash(Utf8.GetBytes("gamelib.save.mac|" + secret));
            encryptionKey = sha.ComputeHash(Utf8.GetBytes("gamelib.save.aes|" + secret));
        }

        public bool Encrypt { get; }

        public byte[] Encode(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            string payload = Encrypt ? EncryptPayload(json) : json;
            byte[] body = Utf8.GetBytes(payload);
            string header = $"{Magic} {FormatVersion} {(Encrypt ? AesMode : PlainMode)} {Hex(Mac(body))}\n";
            byte[] head = Utf8.GetBytes(header);
            var result = new byte[head.Length + body.Length];
            Buffer.BlockCopy(head, 0, result, 0, head.Length);
            Buffer.BlockCopy(body, 0, result, head.Length, body.Length);
            return result;
        }

        /// <summary>
        /// Decodes <paramref name="bytes"/>. On <see cref="SaveDecodeStatus.IntegrityFailed"/> of a plain file
        /// <paramref name="json"/> is still set, so the Editor can accept a hand-edited save; an encrypted file with a bad
        /// checksum is never decrypted. A file with no header that starts with '{' is read as plain JSON with a failed checksum.
        /// </summary>
        public SaveDecodeStatus Decode(byte[] bytes, out string json)
        {
            json = null;
            if (bytes == null || bytes.Length == 0) return SaveDecodeStatus.Malformed;
            int newline = Array.IndexOf(bytes, (byte)'\n');
            string first = Utf8.GetString(bytes, 0, newline < 0 ? Math.Min(bytes.Length, 128) : newline);
            if (!first.StartsWith(Magic + " ", StringComparison.Ordinal))
            {
                string raw = Utf8.GetString(bytes).TrimStart('﻿', ' ', '\t', '\r', '\n');
                if (!raw.StartsWith("{", StringComparison.Ordinal)) return SaveDecodeStatus.Malformed;
                json = raw;
                return SaveDecodeStatus.IntegrityFailed;
            }
            if (newline < 0) return SaveDecodeStatus.Malformed;

            string[] parts = first.TrimEnd('\r').Split(' ');
            if (parts.Length != 4 || parts[1] != FormatVersion.ToString() || (parts[2] != PlainMode && parts[2] != AesMode))
                return SaveDecodeStatus.Malformed;

            int bodyStart = newline + 1;
            var body = new byte[bytes.Length - bodyStart];
            Buffer.BlockCopy(bytes, bodyStart, body, 0, body.Length);
            bool macOk = FixedTimeEquals(Mac(body), parts[3]);

            if (parts[2] == PlainMode)
            {
                json = Utf8.GetString(body);
                return macOk ? SaveDecodeStatus.Ok : SaveDecodeStatus.IntegrityFailed;
            }
            if (!macOk) return SaveDecodeStatus.Malformed;
            try
            {
                json = DecryptPayload(Utf8.GetString(body));
                return SaveDecodeStatus.Ok;
            }
            catch (Exception e) when (e is CryptographicException || e is FormatException)
            {
                json = null;
                return SaveDecodeStatus.Malformed;
            }
        }

        private byte[] Mac(byte[] body)
        {
            using var hmac = new HMACSHA256(macKey);
            return hmac.ComputeHash(body);
        }

        private string EncryptPayload(string json)
        {
            using var aes = new AesManaged();
            aes.Key = encryptionKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();
            byte[] plain = Utf8.GetBytes(json);
            using ICryptoTransform encryptor = aes.CreateEncryptor();
            byte[] cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
            var packed = new byte[aes.IV.Length + cipher.Length];
            Buffer.BlockCopy(aes.IV, 0, packed, 0, aes.IV.Length);
            Buffer.BlockCopy(cipher, 0, packed, aes.IV.Length, cipher.Length);
            return Convert.ToBase64String(packed);
        }

        private string DecryptPayload(string payload)
        {
            byte[] packed = Convert.FromBase64String(payload.Trim());
            if (packed.Length < 32) throw new FormatException("Encrypted payload is too short.");
            using var aes = new AesManaged();
            aes.Key = encryptionKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            var iv = new byte[16];
            Buffer.BlockCopy(packed, 0, iv, 0, iv.Length);
            aes.IV = iv;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plain = decryptor.TransformFinalBlock(packed, iv.Length, packed.Length - iv.Length);
            return Utf8.GetString(plain);
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static bool FixedTimeEquals(byte[] mac, string hex)
        {
            string expected = Hex(mac);
            if (hex == null || hex.Length != expected.Length) return false;
            int diff = 0;
            for (int i = 0; i < expected.Length; i++) diff |= expected[i] ^ char.ToLowerInvariant(hex[i]);
            return diff == 0;
        }
    }
}
