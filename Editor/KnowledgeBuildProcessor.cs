using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace WManager.Knowledge.Editor
{
    internal sealed class KnowledgeBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var runtimes = UnityEngine.Object.FindObjectsByType<KnowledgeRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (runtimes.Length == 0) return;
            string nativeDirectory = Path.Combine(PackageInfo.FindForAssembly(typeof(KnowledgeBuildProcessor).Assembly).resolvedPath, "Plugins/Windows/x86_64");
            if (!LlamaKnowledgeFactory.IsBackendInstalled(nativeDirectory))
                throw new BuildFailedException("请先完全退出并重新打开 Unity，完成 LLamaSharp 配套原生库升级。");
            if (report.summary.platform != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("WManager Knowledge currently supports Windows x64 only.");
            foreach (var runtime in runtimes)
            {
                var settings = runtime.Settings;
                if (settings == null) throw new BuildFailedException("KnowledgeRuntime has no KnowledgeSettings asset.");
                foreach (string model in new[] { settings.generationModel, settings.embeddingModel })
                {
                    if (Path.IsPathRooted(model)) throw new BuildFailedException("Bundled builds require model paths relative to StreamingAssets.");
                    string path = settings.ResolveModelPath(model);
                    if (!File.Exists(path)) throw new BuildFailedException("Missing model: " + path);
                    GgufModelInfo.Read(path);
                }
                settings.CreateKnowledgeOptions(Path.Combine(Application.persistentDataPath, "Knowledge/Databases"));
            }
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
