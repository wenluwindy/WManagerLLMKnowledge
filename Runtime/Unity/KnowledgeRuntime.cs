using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WManager.Knowledge
{
    public sealed class KnowledgeRuntime : MonoBehaviour
    {
        [SerializeField] private KnowledgeSettings settings;
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private string editorNativeLibraryDirectory;
        private CancellationTokenSource lifetime;
        private Task initialization;
        private Task<KnowledgeService> pendingCreation;
        private KnowledgeService service;
        private bool shuttingDown;
        public KnowledgeSettings Settings { get => settings; set => settings = value; }
        public bool IsReady => service != null;
        public IKnowledgeService Service => service ?? throw new InvalidOperationException("Initialize the knowledge runtime first.");
        public string LastError { get; private set; }
        public string EditorNativeLibraryDirectory { set => editorNativeLibraryDirectory = value; }

        private void Awake() => lifetime = new CancellationTokenSource();
        private async void Start()
        {
            if (!initializeOnStart) return;
            try { await InitializeAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { LastError = exception.Message; Debug.LogException(exception, this); }
        }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            if (shuttingDown) throw new ObjectDisposedException(nameof(KnowledgeRuntime));
            if (service != null) return Task.CompletedTask;
            if (initialization != null && !initialization.IsCompleted) return initialization;
            if (lifetime == null) throw new InvalidOperationException("KnowledgeRuntime must be attached to an active GameObject.");
            initialization = InitializeInternalAsync(ct);
            return initialization;
        }

        private async Task InitializeInternalAsync(CancellationToken ct)
        {
            if (settings == null) throw new InvalidOperationException("Assign a KnowledgeSettings asset.");
            LastError = null;
            string databaseDirectory = Path.Combine(Application.persistentDataPath, "Knowledge", "Databases");
            var options = settings.CreateKnowledgeOptions(databaseDirectory);
            string nativeDirectory = Application.isEditor
                ? (string.IsNullOrWhiteSpace(editorNativeLibraryDirectory)
                    ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.wmanager.knowledge/Plugins/Windows/x86_64"))
                    : editorNativeLibraryDirectory)
                : Path.Combine(Application.dataPath, "Plugins", "x86_64");
            var backend = settings.CreateBackendOptions(nativeDirectory);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(settings.seedDatabase))
                        await KnowledgeDeployment.InstallSeedAsync(settings.ResolveStreamingPath(settings.seedDatabase), settings.ResolveStreamingPath(settings.seedManifest), options.DatabasePath, linked.Token);
                    pendingCreation = LlamaKnowledgeFactory.CreateAsync(options, backend, linked.Token);
                    var created = await pendingCreation;
                    pendingCreation = null;
                    if (linked.IsCancellationRequested || shuttingDown) { created.Dispose(); throw new OperationCanceledException(linked.Token); }
                    service = created;
                }
                catch (Exception exception) { LastError = exception.Message; throw; }
            }
        }

        public async Task<ImportResult> ImportFileAsync(string path, IProgress<ImportProgress> progress = null, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            var request = await Task.Run(() => ImportRequest.FromFile(path), ct);
            return await Service.ImportAsync(request, progress, ct);
        }

        public void Shutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            lifetime?.Cancel();
            service?.Dispose();
            service = null;
            if (pendingCreation != null)
            {
                try { pendingCreation.GetAwaiter().GetResult().Dispose(); }
                catch (OperationCanceledException) { }
                catch (Exception exception) { Debug.LogWarning("[Knowledge] " + exception.Message); }
                pendingCreation = null;
            }
        }

        private void OnDestroy() => Shutdown();
    }
}
