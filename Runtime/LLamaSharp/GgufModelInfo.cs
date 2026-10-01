using System;
using System.IO;
using System.Text;

namespace WManager.Knowledge
{
    public sealed class GgufModelInfo
    {
        public uint Version { get; private set; }
        public ulong TensorCount { get; private set; }
        public string Architecture { get; private set; }
        public int EmbeddingDimensions { get; private set; }

        public static GgufModelInfo Read(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
            {
                if (stream.Length < 24 || reader.ReadUInt32() != 0x46554747)
                    throw new InvalidDataException("文件不是 GGUF 模型，请确认没有下载为网页或 Git LFS 指针：" + path);
                var info = new GgufModelInfo { Version = reader.ReadUInt32() };
                if (info.Version != 2 && info.Version != 3) throw new InvalidDataException("不支持的 GGUF 文件版本：" + info.Version);
                info.TensorCount = reader.ReadUInt64();
                ulong count = reader.ReadUInt64();
                if (count > 100000) throw new InvalidDataException("GGUF metadata count exceeds its limit.");
                for (ulong i = 0; i < count; i++)
                {
                    string key = ReadString(reader);
                    uint type = reader.ReadUInt32();
                    if (key == "general.architecture" && type == 8)
                    {
                        info.Architecture = ReadString(reader);
                        continue;
                    }
                    if (key.EndsWith(".embedding_length", StringComparison.Ordinal) && type == 4)
                    { info.EmbeddingDimensions = checked((int)reader.ReadUInt32()); continue; }
                    SkipValue(reader, type);
                }
                if (!string.IsNullOrWhiteSpace(info.Architecture)) return info;
                throw new InvalidDataException("GGUF 缺少 general.architecture 元数据：" + path);
            }
        }

        private static string ReadString(BinaryReader reader)
        {
            ulong length = reader.ReadUInt64();
            if (length > 1048576 || length > (ulong)(reader.BaseStream.Length - reader.BaseStream.Position))
                throw new InvalidDataException("Invalid GGUF metadata string length.");
            byte[] bytes = reader.ReadBytes((int)length);
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        private static void SkipValue(BinaryReader reader, uint type)
        {
            if (type == 8) { SkipBytes(reader, reader.ReadUInt64()); return; }
            if (type == 9)
            {
                uint elementType = reader.ReadUInt32();
                ulong count = reader.ReadUInt64();
                if (elementType == 8)
                {
                    if (count > (ulong)(reader.BaseStream.Length - reader.BaseStream.Position) / 8) throw new InvalidDataException("Invalid GGUF string array.");
                    for (ulong i = 0; i < count; i++) SkipBytes(reader, reader.ReadUInt64());
                }
                else SkipBytes(reader, checked(count * (ulong)SizeOf(elementType)));
                return;
            }
            SkipBytes(reader, (ulong)SizeOf(type));
        }

        private static int SizeOf(uint type)
        {
            switch (type)
            {
                case 0: case 1: case 7: return 1;
                case 2: case 3: return 2;
                case 4: case 5: case 6: return 4;
                case 10: case 11: case 12: return 8;
                default: throw new InvalidDataException("Unsupported GGUF metadata type: " + type);
            }
        }

        private static void SkipBytes(BinaryReader reader, ulong length)
        {
            if (length > (ulong)(reader.BaseStream.Length - reader.BaseStream.Position)) throw new EndOfStreamException("GGUF metadata is truncated.");
            reader.BaseStream.Seek((long)length, SeekOrigin.Current);
        }
    }
}
