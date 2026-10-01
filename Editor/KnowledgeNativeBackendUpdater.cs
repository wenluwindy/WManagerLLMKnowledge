using System;
using System.IO;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace WManager.Knowledge.Editor
{
    [InitializeOnLoad]
    internal static class KnowledgeNativeBackendUpdater
    {
        static KnowledgeNativeBackendUpdater() => EditorApplication.delayCall += ApplyPendingUpdate;

        private static void ApplyPendingUpdate()
        {
            string package = PackageInfo.FindForAssembly(typeof(KnowledgeNativeBackendUpdater).Assembly).resolvedPath;
            string pending = Path.Combine(package, "NativeUpdate~");
            string destination = Path.Combine(package, "Plugins/Windows/x86_64");
            string marker = Path.Combine(pending, "backend.version.txt");
            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != LlamaKnowledgeFactory.BackendVersion) return;
            string bundle = Path.Combine(pending, "backend.bundle.txt");
            if (!File.Exists(bundle) || File.ReadAllText(bundle).Trim() != LlamaKnowledgeFactory.BackendBundleVersion) return;
            string pendingCpu = Path.Combine(pending, "backend.cpu.txt");
            string installedCpu = Path.Combine(destination, "backend.cpu.txt");
            if (LlamaKnowledgeFactory.IsBackendInstalled(destination) && File.Exists(pendingCpu) && File.Exists(installedCpu)
                && File.ReadAllText(pendingCpu) == File.ReadAllText(installedCpu)) return;
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    foreach (System.Diagnostics.ProcessModule module in process.Modules)
                        if (module.FileName.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        { Debug.LogWarning("[Knowledge] CPU/Vulkan 加速后端已准备好。请完全退出并重新打开 Unity，以完成原生后端升级。"); return; }
                string gpuDestination = Path.Combine(destination, "Vulkan~");
                Directory.CreateDirectory(gpuDestination);
                foreach (string file in Directory.GetFiles(Path.Combine(pending, "Vulkan~"), "*.dll"))
                    File.Copy(file, Path.Combine(gpuDestination, Path.GetFileName(file)), true);
                foreach (string file in new[] { "ggml-base.dll", "ggml-cpu.dll", "ggml.dll", "llama.dll", "backend.cpu.txt" })
                    File.Copy(Path.Combine(pending, file), Path.Combine(destination, file), true);
                File.Copy(marker, Path.Combine(destination, "backend.version.txt"), true);
                File.Copy(bundle, Path.Combine(destination, "backend.bundle.txt"), true);
                AssetDatabase.Refresh();
                Debug.Log("[Knowledge] 已安装 LLamaSharp " + LlamaKnowledgeFactory.BackendVersion + " CPU/Vulkan 后端。");
            }
            catch (Exception exception)
            { Debug.LogWarning("[Knowledge] 原生后端升级未完成，请退出 Unity 后运行 Tools/Knowledge/InstallDependencies.ps1。" + exception.Message); }
        }
    }
}
