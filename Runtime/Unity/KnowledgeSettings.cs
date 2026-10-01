using System;
using System.IO;
using UnityEngine;

namespace WManager.Knowledge
{
    [CreateAssetMenu(fileName = "KnowledgeSettings", menuName = "WManager/Knowledge Settings")]
    public sealed class KnowledgeSettings : ScriptableObject
    {
        [Header("Models (relative to StreamingAssets, or absolute paths)")]
        public string generationModel = "Knowledge/Models/Qwen3-4B-Q4_K_M.gguf";
        public string embeddingModel = "Knowledge/Models/bge-small-zh-v1.5-q8_0.gguf";
        [Header("Writable database")]
        public string databaseName = "default.db";
        public string seedDatabase = "Knowledge/Base/default.db";
        public string seedManifest = "Knowledge/Base/default.manifest.json";
        [Header("Embedding and splitting")]
        [Range(32, 480)] public int chunkTokens = 320;
        [Range(0, 160)] public int overlapTokens = 48;
        public int embeddingContextTokens = 512;
        public string queryInstruction = "为这个句子生成表示以用于检索相关文章：";
        [Header("Generation")]
        public int contextTokens = 4096;
        public int maximumOutputTokens = 512;
        [Range(0f, 2f)] public float temperature = 0.2f;
        public int threads;
        public bool disableThinking = true;
        [Header("Retrieval")]
        [Range(1, 30)] public int topK = 8;
        [Range(-1f, 1f)] public float minimumScore = 0.35f;

        public string ResolveModelPath(string value) => Path.IsPathRooted(value) ? Path.GetFullPath(value) : ResolveStreamingPath(value);

        public string ResolveStreamingPath(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) throw new ArgumentException("A StreamingAssets path is required.");
            string root = Path.GetFullPath(Application.streamingAssetsPath);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The relative file path must remain inside StreamingAssets.");
            return path;
        }

        public KnowledgeOptions CreateKnowledgeOptions(string databaseDirectory)
        {
            if (string.IsNullOrWhiteSpace(databaseName) || Path.GetFileName(databaseName) != databaseName || databaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Database name must be a filename without directory components.");
            return new KnowledgeOptions { DatabasePath = Path.Combine(databaseDirectory, databaseName), ChunkTokens = chunkTokens, OverlapTokens = overlapTokens };
        }

        public LlamaBackendOptions CreateBackendOptions(string nativeDirectory)
        {
            return new LlamaBackendOptions
            {
                NativeLibraryDirectory = nativeDirectory, GenerationModelPath = ResolveModelPath(generationModel), EmbeddingModelPath = ResolveModelPath(embeddingModel),
                ContextTokens = contextTokens, MaximumOutputTokens = maximumOutputTokens, EmbeddingContextTokens = embeddingContextTokens,
                Threads = threads > 0 ? threads : Math.Max(1, Environment.ProcessorCount / 2), QueryInstruction = queryInstruction,
                Temperature = temperature, DisableThinking = disableThinking
            };
        }
    }
}
