using System;
using System.Collections.Generic;
using System.IO;

namespace WManager.Knowledge.Editor
{
    public static class KnowledgePackageExporter
    {
        public static string Export(string parentDirectory)
        {
            string source = Path.GetFullPath(KnowledgeCenterWindow.PackageDirectory);
            if (!LlamaKnowledgeFactory.IsBackendInstalled(Path.Combine(source, "Plugins/Windows/x86_64")))
                throw new InvalidOperationException("请先完全退出并重新打开 Unity，完成原生后端升级后再导出 SDK。");
            string parent = Path.GetFullPath(parentDirectory);
            string destination = Path.Combine(parent, "com.wmanager.knowledge");
            if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Export outside the source package.");
            if (Directory.Exists(destination)) throw new IOException("The destination package already exists. Select another parent directory.");
            ValidateAssetMetadata(source);
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, directory.Substring(source.Length + 1)));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(destination, file.Substring(source.Length + 1)));
            return destination;
        }

        internal static void ValidateAssetMetadata(string source)
        {
            var missing = new List<string>();
            void Visit(string directory)
            {
                foreach (string entry in Directory.GetFileSystemEntries(directory))
                {
                    string name = Path.GetFileName(entry);
                    if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal)
                        || name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(entry + ".meta")) missing.Add(entry.Substring(source.Length + 1));
                    if (Directory.Exists(entry)) Visit(entry);
                }
            }
            Visit(source);
            if (missing.Count > 0)
                throw new InvalidOperationException("包资源缺少 .meta，无法导出可供其他工程使用的 UPM 包。请在可写的源包中补齐并重新导出：\n"
                    + string.Join("\n", missing));
        }
    }
}
