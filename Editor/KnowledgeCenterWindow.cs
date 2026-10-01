using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace WManager.Knowledge.Editor
{
    public sealed class KnowledgeCenterWindow : EditorWindow
    {
        [SerializeField] private KnowledgeSettings settings;
        [SerializeField] private int tab;
        [SerializeField] private string documentTitle = "新增资料";
        [SerializeField] private string body = "";
        [SerializeField] private string source = "Manual input";
        [SerializeField] private string question = "";
        private string documentId;
        private string answer = "";
        private string status = "";
        private string error = "";
        private bool busy;
        private Vector2 scroll;
        private KnowledgeService service;
        private Task<KnowledgeService> pendingCreation;
        private CancellationTokenSource operation;
        private IReadOnlyList<KnowledgeDocument> documents = Array.Empty<KnowledgeDocument>();
        private IReadOnlyList<SearchHit> hits = Array.Empty<SearchHit>();
        private AnswerResult lastAnswer;
        private bool closing;

        [MenuItem("Tools/WManager/Knowledge Center")]
        public static void Open()
        {
            var window = GetWindow<KnowledgeCenterWindow>("Knowledge Center");
            window.minSize = new Vector2(660, 520);
        }

        private void OnEnable()
        {
            closing = false;
            if (settings == null)
            {
                string[] assets = AssetDatabase.FindAssets("t:KnowledgeSettings");
                if (assets.Length > 0) settings = AssetDatabase.LoadAssetAtPath<KnowledgeSettings>(AssetDatabase.GUIDToAssetPath(assets[0]));
            }
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            closing = true;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Stop();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) Stop();
        }

        private void Stop()
        {
            operation?.Cancel();
            service?.Dispose();
            service = null;
            documents = Array.Empty<KnowledgeDocument>();
            if (pendingCreation != null)
            {
                try { pendingCreation.GetAwaiter().GetResult().Dispose(); }
                catch (OperationCanceledException) { }
                catch (Exception exception) { Debug.LogWarning("[Knowledge] " + exception.Message); }
                pendingCreation = null;
            }
        }

        private void OnInspectorUpdate() { if (busy) Repaint(); }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(busy || service != null))
                settings = (KnowledgeSettings)EditorGUILayout.ObjectField("配置", settings, typeof(KnowledgeSettings), false);
            using (new EditorGUI.DisabledScope(busy))
                if (GUILayout.Button("新建配置", GUILayout.Width(80))) CreateSettings();
            EditorGUILayout.EndHorizontal();
            tab = GUILayout.Toolbar(tab, new[] { "配置", "资料", "问答与检索" });
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(busy))
            {
                if (tab == 0) DrawConfiguration();
                else if (tab == 1) DrawDocuments();
                else DrawChat();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(string.IsNullOrEmpty(status) ? (service == null ? "未初始化" : "已就绪") : status, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!busy))
                if (GUILayout.Button("取消", EditorStyles.toolbarButton, GUILayout.Width(50))) operation?.Cancel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfiguration()
        {
            if (settings == null) return;
            using (new EditorGUI.DisabledScope(service != null))
            {
                var serialized = new SerializedObject(settings);
                serialized.Update();
                var iterator = serialized.GetIterator();
                bool enter = true;
                while (iterator.NextVisible(enter))
                {
                    enter = false;
                    if (iterator.name != "m_Script") EditorGUILayout.PropertyField(iterator, true);
                }
                serialized.ApplyModifiedProperties();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("模型目录", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(Path.Combine(Application.streamingAssetsPath, "Knowledge/Models"), GUILayout.Height(20));
            EditorGUILayout.LabelField("编辑器工作库", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(EditorDatabaseDirectory, GUILayout.Height(20));
            if (GUILayout.Button(service == null ? "初始化知识库" : "释放模型"))
            {
                if (service != null) { Stop(); status = "模型已释放"; }
                else Run(async token => { await EnsureService(token); status = "模型和知识库已就绪"; });
            }
            if (GUILayout.Button("打开模型目录"))
            { Directory.CreateDirectory(Path.Combine(Application.streamingAssetsPath, "Knowledge/Models")); EditorUtility.RevealInFinder(Path.Combine(Application.streamingAssetsPath, "Knowledge/Models")); }
            if (GUILayout.Button("查看开发方案"))
                Application.OpenURL(new Uri(Path.Combine(PackageDirectory, "Documentation~/DevelopmentPlan.md")).AbsoluteUri);
            if (GUILayout.Button("导出独立 UPM 包"))
            {
                string parent = EditorUtility.OpenFolderPanel("选择导出包的父目录", "", "");
                if (!string.IsNullOrEmpty(parent))
                {
                    try { string exported = KnowledgePackageExporter.Export(parent); status = "已导出：" + exported; EditorUtility.RevealInFinder(exported); }
                    catch (Exception exception) { error = exception.Message; }
                }
            }
        }

        private void DrawDocuments()
        {
            if (settings == null) return;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导入 TXT / Markdown"))
            {
                string path = EditorUtility.OpenFilePanelWithFilters("选择资料", "", new[] { "Text / Markdown", "txt,md" });
                if (!string.IsNullOrEmpty(path)) Run(async token =>
                {
                    await EnsureService(token);
                    var request = await Task.Run(() => ImportRequest.FromFile(path), token);
                    var result = await service.ImportAsync(request, ImportProgress(), token);
                    documents = await service.ListDocumentsAsync(token);
                    status = result.Unchanged ? "资料没有变化" : "已导入 " + result.ChunkCount + " 个片段";
                });
            }
            if (GUILayout.Button("刷新列表")) Run(async token => { await EnsureService(token); documents = await service.ListDocumentsAsync(token); });
            if (GUILayout.Button("导出基础知识库"))
            {
                string folder = EditorUtility.OpenFolderPanel("选择导出目录", Path.Combine(Application.streamingAssetsPath, "Knowledge"), "");
                if (!string.IsNullOrEmpty(folder)) Run(async token =>
                {
                    await EnsureService(token);
                    string destination = Path.Combine(folder, settings.databaseName);
                    var manifest = await service.ExportAsync(destination, token);
                    KnowledgeDeployment.WriteManifest(destination, manifest);
                    AssetDatabase.Refresh();
                    status = "已导出数据库与清单：" + destination;
                });
            }
            EditorGUILayout.EndHorizontal();
            foreach (var document in documents)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(document.Title + "  (" + document.ChunkCount + " 片段)");
                if (GUILayout.Button("替换", GUILayout.Width(48)))
                { documentId = document.Id; documentTitle = document.Title; source = document.Source; body = ""; status = "编辑新正文后保存，将替换所选资料"; }
                if (GUILayout.Button("删除", GUILayout.Width(48)))
                {
                    string id = document.Id;
                    if (EditorUtility.DisplayDialog("删除资料", document.Title, "删除", "取消")) Run(async token =>
                    { await EnsureService(token); await service.DeleteDocumentAsync(id, token); documents = await service.ListDocumentsAsync(token); status = "已删除"; });
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(document.Source, EditorStyles.miniLabel);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(string.IsNullOrEmpty(documentId) ? "新资料" : "替换所选资料", EditorStyles.boldLabel);
            documentTitle = EditorGUILayout.TextField("标题", documentTitle);
            source = EditorGUILayout.TextField("来源", source);
            body = EditorGUILayout.TextArea(body, GUILayout.MinHeight(140));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存资料")) Run(async token =>
            {
                await EnsureService(token);
                var result = await service.ImportAsync(new ImportRequest { DocumentId = documentId, Title = documentTitle, Source = source, Text = body }, ImportProgress(), token);
                documentId = result.DocumentId;
                documents = await service.ListDocumentsAsync(token);
                status = "已保存 " + result.ChunkCount + " 个片段";
            });
            if (GUILayout.Button("新建下一条")) { documentId = null; body = ""; documentTitle = "新增资料"; source = "Manual input"; }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawChat()
        {
            if (settings == null) return;
            question = EditorGUILayout.TextArea(question, GUILayout.MinHeight(70));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("检索")) Run(async token =>
            {
                await EnsureService(token);
                var result = await service.SearchAsync(new SearchRequest { Question = question, TopK = settings.topK, MinimumScore = settings.minimumScore }, token);
                hits = result.Hits;
                status = "命中 " + hits.Count + " 个片段";
            });
            if (GUILayout.Button("提问")) Run(async token =>
            {
                await EnsureService(token);
                answer = string.Empty; lastAnswer = null;
                bool receiving = true;
                var progress = new Progress<AnswerDelta>(delta => { if (!closing && receiving) { answer += delta.Text; Repaint(); } });
                lastAnswer = await service.AskAsync(new AskRequest { Question = question, TopK = settings.topK, MinimumScore = settings.minimumScore }, progress, token);
                receiving = false;
                answer = lastAnswer.Text;
                status = "完成，用时 " + lastAnswer.ElapsedMilliseconds + " ms";
            });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("回答", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(answer, GUILayout.MinHeight(100));
            if (lastAnswer != null)
                foreach (var citation in lastAnswer.Citations)
                {
                    EditorGUILayout.LabelField("[" + citation.Id + "] " + citation.Hit.Title + " / " + citation.Hit.Heading, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(citation.Hit.Source, EditorStyles.wordWrappedMiniLabel);
                }
            foreach (var hit in hits)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(hit.Title + " / " + hit.Heading + "  " + hit.Score.ToString("F3"), EditorStyles.boldLabel);
                EditorGUILayout.TextArea(hit.Text, GUILayout.MinHeight(65));
            }
        }

        private IProgress<ImportProgress> ImportProgress() => new Progress<ImportProgress>(value =>
        { if (!closing) { status = value.Stage + " " + value.Completed + "/" + value.Total; Repaint(); } });

        private async Task EnsureService(CancellationToken token)
        {
            if (service != null) return;
            if (settings == null) throw new InvalidOperationException("先创建或选择配置。");
            var options = settings.CreateKnowledgeOptions(EditorDatabaseDirectory);
            var backend = settings.CreateBackendOptions(Path.Combine(PackageDirectory, "Plugins/Windows/x86_64"));
            pendingCreation = LlamaKnowledgeFactory.CreateAsync(options, backend, token);
            var created = await pendingCreation;
            pendingCreation = null;
            if (token.IsCancellationRequested || closing) { created.Dispose(); token.ThrowIfCancellationRequested(); throw new OperationCanceledException(); }
            service = created;
            documents = await service.ListDocumentsAsync(token);
        }

        private async void Run(Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true; error = "";
            operation = new CancellationTokenSource();
            try { await action(operation.Token); }
            catch (OperationCanceledException) { status = "已取消"; }
            catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
            finally { busy = false; operation.Dispose(); operation = null; if (!closing) Repaint(); }
        }

        private void CreateSettings()
        {
            Stop();
            string path = EditorUtility.SaveFilePanelInProject("创建配置", "KnowledgeSettings", "asset", "选择保存目录");
            if (string.IsNullOrEmpty(path)) return;
            settings = CreateInstance<KnowledgeSettings>();
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
        }

        internal static string PackageDirectory => PackageInfo.FindForAssembly(typeof(KnowledgeCenterWindow).Assembly).resolvedPath;
        private static string EditorDatabaseDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../UserSettings/Knowledge/Databases"));
    }
}
