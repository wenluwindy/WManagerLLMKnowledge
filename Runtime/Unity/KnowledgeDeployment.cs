using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WManager.Knowledge
{
    public static class KnowledgeDeployment
    {
        public static Task InstallSeedAsync(string sourceDatabase, string sourceManifest, string destinationDatabase, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(destinationDatabase)) return Task.CompletedTask;
            // Capture Unity serialization on the main thread; disk copying and hashing run in the background.
            KnowledgeExport manifest = null;
            if (File.Exists(sourceDatabase))
            {
                if (!File.Exists(sourceManifest)) throw new FileNotFoundException("The seed database needs its exported manifest.", sourceManifest);
                manifest = JsonUtility.FromJson<KnowledgeExport>(File.ReadAllText(sourceManifest));
                if (manifest == null || manifest.schemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.databaseSha256))
                    throw new InvalidDataException("Invalid seed manifest.");
            }
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(destinationDatabase) || manifest == null) return;
                if (!string.Equals(manifest.databaseFile, Path.GetFileName(sourceDatabase), StringComparison.Ordinal))
                    throw new InvalidDataException("Seed manifest filename mismatch.");
                if (!string.Equals(KnowledgeHash.File(sourceDatabase), manifest.databaseSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Seed database checksum mismatch.");
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
    }
}
