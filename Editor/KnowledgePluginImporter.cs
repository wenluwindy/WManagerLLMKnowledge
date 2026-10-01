using System;
using UnityEditor;

namespace WManager.Knowledge.Editor
{
    internal sealed class KnowledgePluginImporter : AssetPostprocessor
    {
        private void OnPreprocessAsset()
        {
            if (!assetPath.StartsWith("Packages/com.wmanager.knowledge/Plugins/", StringComparison.Ordinal) || !assetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;
            var importer = assetImporter as PluginImporter;
            if (importer == null) return;
            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            if (importer.isNativePlugin)
            {
                importer.SetEditorData("CPU", "x86_64");
                importer.SetEditorData("OS", "Windows");
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            }
        }
    }
}
