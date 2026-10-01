using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WManager.Knowledge
{
    public static class KnowledgeDeployment
    {
        public static Task InstallSeedAsync(string sourceDatabase, string sourceManifest, string destinationDatabase, CancellationToken ct, string expectedFingerprint = null)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(destinationDatabase)) return Task.CompletedTask;
            // Capture Unity serialization on the main thread; disk copying and hashing run in the background.
            var manifest = ReadSeedManifest(sourceDatabase, sourceManifest);
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(destinationDatabase)) return;
                KnowledgeIndex.ValidateSeed(sourceDatabase, manifest, expectedFingerprint);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationDatabase)));
                string temporary = destinationDatabase + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var source = File.OpenRead(sourceDatabase))
                    using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[65536];
                        int count;
                        while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                        { ct.ThrowIfCancellationRequested(); target.Write(buffer, 0, count); }
                        target.Flush(true);
                    }
                    ct.ThrowIfCancellationRequested();
                    if (KnowledgeHash.File(temporary) != manifest.databaseSha256.ToLowerInvariant()) throw new InvalidDataException("Copied seed checksum mismatch.");
                    File.Move(temporary, destinationDatabase);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, ct);
        }

        public static void WriteManifest(string databasePath, KnowledgeExport manifest)
        {
            string path = Path.ChangeExtension(databasePath, ".manifest.json");
            File.WriteAllText(path, JsonUtility.ToJson(manifest, true), new System.Text.UTF8Encoding(false));
        }

        public static KnowledgeExport ReadSeedManifest(string sourceDatabase, string sourceManifest)
        {
            if (!File.Exists(sourceDatabase)) throw new FileNotFoundException("配置的基础知识库不存在，请导出基础库或清空 seedDatabase。", sourceDatabase);
            if (!File.Exists(sourceManifest)) throw new FileNotFoundException("The seed database needs its exported manifest.", sourceManifest);
            return JsonUtility.FromJson<KnowledgeExport>(File.ReadAllText(sourceManifest)) ?? throw new InvalidDataException("Invalid seed manifest.");
        }
    }
}
