using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WManager.Knowledge.Editor
{
    internal sealed class KnowledgeBuildProcessor : IPreprocessBuildWithReport, IProcessSceneWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        private readonly HashSet<int> validated = new HashSet<int>();

        public void OnPreprocessBuild(BuildReport report)
        {
            validated.Clear();
            ValidateDynamicSettings(report.summary.platform);
        }

        private void ValidateDynamicSettings(BuildTarget target)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:KnowledgeBuildConfiguration"))
            {
                var configuration = AssetDatabase.LoadAssetAtPath<KnowledgeBuildConfiguration>(AssetDatabase.GUIDToAssetPath(guid));
                if (configuration.dynamicSettings == null) throw new BuildFailedException("Dynamic knowledge settings list is missing.");
                foreach (var settings in configuration.dynamicSettings) ValidateSettings(settings, target);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("/Resources/", StringComparison.Ordinal) < 0) continue;
                foreach (var runtime in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<KnowledgeRuntime>(true))
                    ValidateSettings(runtime.Settings, target);
            }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            ValidateScene(scene, report.summary.platform);
        }

        private void ValidateScene(Scene scene, BuildTarget target)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var runtime in root.GetComponentsInChildren<KnowledgeRuntime>(true))
                    ValidateSettings(runtime.Settings, target);
        }

        private void ValidateSettings(KnowledgeSettings settings, BuildTarget target)
        {
            if (settings == null) throw new BuildFailedException("KnowledgeRuntime or dynamic build configuration has no KnowledgeSettings asset.");
            if (validated.Contains(settings.GetInstanceID())) return;
            string nativeDirectory = Path.Combine(PackageInfo.FindForAssembly(typeof(KnowledgeBuildProcessor).Assembly).resolvedPath, "Plugins/Windows/x86_64");
            if (!LlamaKnowledgeFactory.IsBackendInstalled(nativeDirectory))
                throw new BuildFailedException("请先完全退出并重新打开 Unity，完成 LLamaSharp 配套原生库升级。");
            if (target != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("WManager Knowledge currently supports Windows x64 only.");
            try
            {
                foreach (string model in new[] { settings.generationModel, settings.embeddingModel })
                {
                    if (Path.IsPathRooted(model)) throw new BuildFailedException("Bundled builds require model paths relative to StreamingAssets.");
                    string path = settings.ResolveModelPath(model);
                    if (!File.Exists(path)) throw new BuildFailedException("Missing model: " + path);
                    GgufModelInfo.Read(path);
                }
                var options = settings.CreateKnowledgeOptions(Path.Combine(Application.persistentDataPath, "Knowledge/Databases"));
                if (!string.IsNullOrWhiteSpace(settings.seedDatabase))
                {
                    string database = settings.ResolveStreamingPath(settings.seedDatabase);
                    var manifest = KnowledgeDeployment.ReadSeedManifest(database, settings.ResolveStreamingPath(settings.seedManifest));
                    string modelPath = settings.ResolveModelPath(settings.embeddingModel);
                    int dimensions = GgufModelInfo.Read(modelPath).EmbeddingDimensions;
                    if (dimensions < 1 || dimensions != manifest.dimensions) throw new InvalidDataException("基础库向量维度与 GGUF 模型不匹配。");
                    string fingerprint = LlamaKnowledgeFactory.CreateIndexFingerprint(options, settings.CreateBackendOptions(nativeDirectory));
                    KnowledgeIndex.ValidateSeed(database, manifest, fingerprint);
                }
            }
            catch (Exception exception) when (!(exception is BuildFailedException))
            { throw new BuildFailedException("Knowledge configuration '" + settings.name + "': " + exception.Message); }
            validated.Add(settings.GetInstanceID());
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
            string package = PackageInfo.FindForAssembly(typeof(KnowledgeBuildProcessor).Assembly).resolvedPath;
            string source = Path.Combine(package, "Plugins/Windows/x86_64");
            string application = report.summary.outputPath;
            string plugins = Path.Combine(Path.GetDirectoryName(application), Path.GetFileNameWithoutExtension(application) + "_Data", "Plugins", "x86_64");
            Directory.CreateDirectory(plugins);
            foreach (string file in Directory.GetFiles(source, "*.dll")) File.Copy(file, Path.Combine(plugins, Path.GetFileName(file)), true);
            string gpuPlugins = Path.Combine(plugins, "Vulkan~");
            Directory.CreateDirectory(gpuPlugins);
            foreach (string file in Directory.GetFiles(Path.Combine(source, "Vulkan~"), "*.dll"))
                File.Copy(file, Path.Combine(gpuPlugins, Path.GetFileName(file)), true);
            File.Copy(Path.Combine(source, "backend.version.txt"), Path.Combine(plugins, "backend.version.txt"), true);
            File.Copy(Path.Combine(source, "backend.bundle.txt"), Path.Combine(plugins, "backend.bundle.txt"), true);
            File.Copy(Path.Combine(source, "backend.cpu.txt"), Path.Combine(plugins, "backend.cpu.txt"), true);
        }
    }
}
