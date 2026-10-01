using System;
using System.IO;

namespace WManager.Knowledge
{
    public static class KnowledgeIndex
    {
        public const int SplitterVersion = 2;
        public static string CreateFingerprint(KnowledgeOptions options, string embeddingFingerprint, int dimensions)
            => KnowledgeHash.Text("schema=1;splitter=" + SplitterVersion + ";normalize=l2;" + embeddingFingerprint
                + ";dimensions=" + dimensions + ";chunk=" + options.ChunkTokens + ";overlap=" + options.OverlapTokens);

        public static void ValidateSeed(string databasePath, KnowledgeExport manifest, string expectedFingerprint = null)
        {
            if (!File.Exists(databasePath)) throw new FileNotFoundException("配置的基础知识库不存在。请导出基础库，或清空 seedDatabase 以禁用安装。", databasePath);
            if (manifest == null || manifest.schemaVersion != 1 || manifest.dimensions < 1 || manifest.chunkTokens < 8
                || manifest.overlapTokens < 0 || manifest.overlapTokens >= manifest.chunkTokens || string.IsNullOrWhiteSpace(manifest.indexFingerprint)
                || string.IsNullOrWhiteSpace(manifest.embeddingFingerprint) || string.IsNullOrWhiteSpace(manifest.databaseSha256))
                throw new InvalidDataException("Invalid seed manifest.");
            if (!string.Equals(manifest.databaseFile, Path.GetFileName(databasePath), StringComparison.Ordinal))
                throw new InvalidDataException("Seed manifest filename mismatch.");
            if (!string.Equals(KnowledgeHash.File(databasePath), manifest.databaseSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Seed database checksum mismatch.");
            if (!string.Equals(SqliteKnowledgeStore.ReadFingerprint(databasePath), manifest.indexFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("Seed manifest and database index fingerprints differ.");
            if (expectedFingerprint != null && manifest.indexFingerprint != expectedFingerprint)
                throw new InvalidDataException("基础库与当前向量模型、分块版本或配置不兼容，请重新导出基础库。");
        }
    }
}
