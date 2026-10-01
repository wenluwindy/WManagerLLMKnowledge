using System;
using System.IO;
using UnityEngine;

namespace WManager.Knowledge
{
    [CreateAssetMenu(fileName = "KnowledgeSettings", menuName = "WManager/Knowledge Settings")]
    public sealed class KnowledgeSettings : LlamaModelSettings
    {
        [Header("Models (relative to StreamingAssets, or absolute paths)")]
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
        [Header("Answer instructions")]
        [TextArea(4, 10)] public string answerSystemPrompt = LlamaBackendOptions.DefaultKnowledgeSystemPrompt;
        [Header("Retrieval")]
        [Range(1, 30)] public int topK = 8;
        [Range(-1f, 1f)] public float minimumScore = 0.35f;
        [Range(1, 20)] public int maximumEvidence = 5;

        public KnowledgeOptions CreateKnowledgeOptions(string databaseDirectory)
        {
            if (string.IsNullOrWhiteSpace(databaseName) || Path.GetFileName(databaseName) != databaseName || databaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Database name must be a filename without directory components.");
            return new KnowledgeOptions { DatabasePath = Path.Combine(databaseDirectory, databaseName), ChunkTokens = chunkTokens, OverlapTokens = overlapTokens };
        }

        public override LlamaBackendOptions CreateBackendOptions(string nativeDirectory)
        {
            var backend = base.CreateBackendOptions(nativeDirectory);
            backend.EmbeddingModelPath = ResolveModelPath(embeddingModel);
            backend.EmbeddingContextTokens = embeddingContextTokens;
            backend.QueryInstruction = queryInstruction;
            backend.AnswerSystemPrompt = answerSystemPrompt;
            return backend;
        }
    }
}
