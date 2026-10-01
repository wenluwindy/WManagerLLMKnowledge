using System;
using System.IO;

namespace WManager.Knowledge.Editor
{
    public static class KnowledgePackageExporter
    {
        public static string Export(string parentDirectory)
        {
            string source = Path.GetFullPath(KnowledgeCenterWindow.PackageDirectory);
            string parent = Path.GetFullPath(parentDirectory);
            string destination = Path.Combine(parent, "com.wmanager.knowledge");
            if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Export outside the source package.");
            if (Directory.Exists(destination)) throw new IOException("The destination package already exists. Select another parent directory.");
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, directory.Substring(source.Length + 1)));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(destination, file.Substring(source.Length + 1)));
            return destination;
        }
    }
}
