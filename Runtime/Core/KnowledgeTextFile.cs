using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WManager.Knowledge
{
    internal static class KnowledgeTextFile
    {
        internal static string Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string text;
            try
            {
                // Check UTF-32 before UTF-16 because their little-endian BOMs overlap.
                if (StartsWith(bytes, 0xff, 0xfe, 0x00, 0x00))
                    text = new UTF32Encoding(false, false, true).GetString(bytes, 4, bytes.Length - 4);
                else if (StartsWith(bytes, 0x00, 0x00, 0xfe, 0xff))
                    text = new UTF32Encoding(true, false, true).GetString(bytes, 4, bytes.Length - 4);
                else if (StartsWith(bytes, 0xff, 0xfe))
                    text = new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
                else if (StartsWith(bytes, 0xfe, 0xff))
                    text = new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
                else if (StartsWith(bytes, 0xef, 0xbb, 0xbf))
                    text = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
                else
                    text = DecodeWithoutBom(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("Cannot decode text file '" + path + "'. Save it as UTF-8, or UTF-16/UTF-32 with a BOM. Windows also supports GBK/GB18030. The file may contain incomplete or invalid bytes.", ex);
            }

            if (text.IndexOf('\0') >= 0)
                throw new InvalidDataException("Text file '" + path + "' contains null characters. Check that it is a text document; save UTF-16/UTF-32 files with a BOM or convert them to UTF-8.");
            return text;
        }

        private static string DecodeWithoutBom(byte[] bytes)
        {
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw;
                // Use Windows code pages so Unity Players need no Mono I18N assemblies.
                string text = DecodeWindows(bytes, 54936) ?? DecodeWindows(bytes, 936);
                if (text == null) throw;
                return text;
            }
        }

        private static string DecodeWindows(byte[] bytes, uint codePage)
        {
            const uint rejectInvalidBytes = 0x00000008;
            int count = MultiByteToWideChar(codePage, rejectInvalidBytes, bytes, bytes.Length, null, 0);
            if (count == 0) return null;
            var characters = new char[count];
            int written = MultiByteToWideChar(codePage, rejectInvalidBytes, bytes, bytes.Length, characters, characters.Length);
            return written == count ? new string(characters) : null;
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (bytes[i] != prefix[i]) return false;
            return true;
        }

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] bytes, int byteCount,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] characters, int characterCount);
    }
}
