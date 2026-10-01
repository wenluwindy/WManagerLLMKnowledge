using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WManager.Knowledge
{
    public static class KnowledgeHash
    {
        public static string Text(string value)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
        }

        public static string File(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = System.IO.File.OpenRead(path))
                return Hex(sha.ComputeHash(stream));
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
