using System;
using System.IO;
using UnityEngine;

namespace WManager.Knowledge
{
    public abstract class LlamaModelSettings : ScriptableObject
    {
        [Header("Model")]
        public string generationModel = "Knowledge/Models/Qwen3-4B-Q4_K_M.gguf";
        [Header("Generation")]
        public int contextTokens = 4096;
        public int maximumOutputTokens = 512;
        [Range(0f, 2f)] public float temperature = 0.2f;
        [Range(0.01f, 1f)] public float topP = 0.9f;
        public int samplingTopK = 40;
        [Range(1f, 2f)] public float repeatPenalty = 1.1f;
        public bool disableThinking = true;
        [Header("Acceleration")]
        public LlamaAcceleration acceleration = LlamaAcceleration.Auto;
        [Range(0, 128)] public int gpuLayers = 99;
        public int threads;
        public int batchThreads;
        public int batchTokens = 512;
        public int microBatchTokens = 512;
        public bool warmupGpu = true;

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

        public virtual LlamaBackendOptions CreateBackendOptions(string nativeDirectory) => new LlamaBackendOptions
        {
            NativeLibraryDirectory = nativeDirectory, GenerationModelPath = ResolveModelPath(generationModel),
            ContextTokens = contextTokens, MaximumOutputTokens = maximumOutputTokens,
            Threads = threads > 0 ? threads : Math.Max(1, Environment.ProcessorCount / 2),
            BatchThreads = batchThreads, BatchTokens = batchTokens, MicroBatchTokens = microBatchTokens,
            Temperature = temperature, TopP = topP, SamplingTopK = samplingTopK, RepeatPenalty = repeatPenalty,
            DisableThinking = disableThinking, Acceleration = acceleration, GpuLayers = gpuLayers, WarmupGpu = warmupGpu,
            Log = message => Debug.Log("[Knowledge] " + message)
        };
    }
}
