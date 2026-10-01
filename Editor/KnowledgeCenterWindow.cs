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
        private readonly KnowledgeEditorUI configurationUI = new KnowledgeEditorUI();
        [SerializeField] private string documentFilter = "";
        [SerializeField] private string searchDocumentId = "";

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
            configurationUI.Dispose();
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
            hits = Array.Empty<SearchHit>();
            lastAnswer = null;
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
            EditorGUILayout.LabelField("知识中心", EditorStyles.largeLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(busy || service != null))
                settings = (KnowledgeSettings)EditorGUILayout.ObjectField("配置", settings, typeof(KnowledgeSettings), false);
            using (new EditorGUI.DisabledScope(busy))
                if (GUILayout.Button("新建配置", GUILayout.Width(80))) CreateSettings();
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(busy))
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.Label("资料 " + documents.Count + "  |  " + (service == null ? "模型未加载" : "模型已加载"));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("大模型聊天", EditorStyles.toolbarButton)) LlamaChatWindow.Open();
                if (GUILayout.Button("保存配置", EditorStyles.toolbarButton)) AssetDatabase.SaveAssets();
                if (GUILayout.Button("释放模型", EditorStyles.toolbarButton) && service != null) { Stop(); status = "模型已释放，可修改配置"; }
                EditorGUILayout.EndHorizontal();
            }
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
            if (settings == null) { EditorGUILayout.HelpBox("请选择或新建知识库配置。", MessageType.Info); return; }
            KnowledgeEditorUI.BeginSection("模型、生成、分块与检索参数");
            using (new EditorGUI.DisabledScope(service != null))
            {
                configurationUI.DrawSettings(settings);
                DrawModelSelection();
            }
            KnowledgeEditorUI.EndSection();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("模型目录", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(Path.Combine(Application.streamingAssetsPath, "Knowledge/Models"), GUILayout.Height(20));
            EditorGUILayout.LabelField("编辑器工作库", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(EditorDatabaseDirectory, GUILayout.Height(20));
            EditorGUILayout.LabelField("推理后端", service == null ? "LLamaSharp " + LlamaKnowledgeFactory.BackendVersion + " / " + settings.acceleration : service.BackendDescription);
            if (GUILayout.Button(service == null ? "初始化知识库" : "释放模型"))
            {
                if (service != null) { Stop(); status = "模型已释放"; }
                else Run(async token => { status = "正在加载模型并预热…"; await EnsureService(token); status = "模型和知识库已就绪 / " + service.BackendDescription; });
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

        private void DrawModelSelection()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("选择回答模型…")) ChooseModel(false);
            if (GUILayout.Button("选择向量模型…")) ChooseModel(true);
            EditorGUILayout.EndHorizontal();
            DrawModelArchitecture("回答模型架构", settings.generationModel);
            DrawModelArchitecture("向量模型架构", settings.embeddingModel);
        }

        private void ChooseModel(bool embedding)
        {
            string path = EditorUtility.OpenFilePanel("选择 GGUF 模型", Path.Combine(Application.streamingAssetsPath, "Knowledge/Models"), "gguf");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                GgufModelInfo.Read(path);
                string root = Path.GetFullPath(Application.streamingAssetsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(path);
                string configured = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length).Replace('\\', '/') : full;
                Undo.RecordObject(settings, "Change knowledge model");
                if (embedding) settings.embeddingModel = configured;
                else settings.generationModel = configured;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                error = "";
            }
            catch (Exception exception) { error = exception.Message; }
        }

        private void DrawModelArchitecture(string label, string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath)) return;
            try
            {
                string path = settings.ResolveModelPath(configuredPath);
                if (File.Exists(path)) EditorGUILayout.LabelField(label, GgufModelInfo.Read(path).Architecture);
            }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }
        }

        private void DrawDocuments()
        {
            if (settings == null) return;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导入文档"))
            {
                string path = EditorUtility.OpenFilePanelWithFilters("选择资料", "", new[]
                {
                    "支持的文档", "txt,md,docx,docm,pptx,pptm,xls,xlsx,xlsm,pdf",
                    "Word", "docx,docm", "PowerPoint", "pptx,pptm", "Excel", "xls,xlsx,xlsm",
                    "PDF", "pdf", "Text / Markdown", "txt,md"
                });
                if (!string.IsNullOrEmpty(path)) Run(async token =>
                {
                    await EnsureService(token);
                    status = "正在提取文档内容…";
                    var request = await Task.Run(() => ImportRequest.FromFile(path, token), token);
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
            documentFilter = EditorGUILayout.TextField("筛选标题 / 来源", documentFilter);
            EditorGUILayout.LabelField("资料列表 · " + documents.Count, EditorStyles.boldLabel);
            foreach (var document in documents)
            {
                if (!string.IsNullOrWhiteSpace(documentFilter)
                    && (document.Title ?? "").IndexOf(documentFilter, StringComparison.OrdinalIgnoreCase) < 0
                    && (document.Source ?? "").IndexOf(documentFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                KnowledgeEditorUI.BeginSection(document.Title);
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
                EditorGUILayout.LabelField("更新时间", document.UpdatedUtc, EditorStyles.miniLabel);
                KnowledgeEditorUI.EndSection();
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
            KnowledgeEditorUI.BeginSection("检索范围");
            var labels = new List<string> { "全部资料" };
            int selected = 0;
            for (int i = 0; i < documents.Count; i++)
            {
                labels.Add(documents[i].Title);
                if (documents[i].Id == searchDocumentId) selected = i + 1;
            }
            selected = EditorGUILayout.Popup("资料", selected, labels.ToArray());
            searchDocumentId = selected == 0 ? "" : documents[selected - 1].Id;
            EditorGUILayout.LabelField("召回数量 / 最低分数", settings.topK + " / " + settings.minimumScore.ToString("F2"));
            KnowledgeEditorUI.EndSection();
            EditorGUILayout.LabelField("问题", EditorStyles.boldLabel);
            question = EditorGUILayout.TextArea(question, GUILayout.MinHeight(70));
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(question)))
            {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("检索")) Run(async token =>
            {
                await EnsureService(token);
                var result = await service.SearchAsync(new SearchRequest { Question = question, TopK = settings.topK, MinimumScore = settings.minimumScore, DocumentId = string.IsNullOrEmpty(searchDocumentId) ? null : searchDocumentId }, token);
                hits = result.Hits;
                status = "命中 " + hits.Count + " 个片段";
            });
            if (GUILayout.Button("提问")) Run(async token =>
            {
                await EnsureService(token);
                answer = string.Empty; lastAnswer = null;
                status = "正在检索和处理资料…";
                bool receiving = true;
                var progress = new Progress<AnswerDelta>(delta => { if (!closing && receiving) { answer += delta.Text; status = "正在回答…"; Repaint(); } });
                try
                {
                    lastAnswer = await service.AskAsync(new AskRequest { Question = question, TopK = settings.topK, MinimumScore = settings.minimumScore,
                        MaximumEvidence = settings.maximumEvidence, DocumentId = string.IsNullOrEmpty(searchDocumentId) ? null : searchDocumentId }, progress, token);
                }
                finally { receiving = false; }
                answer = lastAnswer.Text;
                status = "完成 " + lastAnswer.ElapsedMilliseconds + " ms；首字 " + lastAnswer.FirstTokenMilliseconds
                    + " ms；检索 " + lastAnswer.RetrievalMilliseconds + " ms；生成 " + lastAnswer.GenerationMilliseconds + " ms";
            });
            EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("回答", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(answer, GUILayout.MinHeight(100));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(answer)))
                if (GUILayout.Button("复制回答")) EditorGUIUtility.systemCopyBuffer = answer;
            if (lastAnswer != null)
                foreach (var citation in lastAnswer.Citations)
                {
                    EditorGUILayout.LabelField("[" + citation.Id + "] " + citation.Hit.Title + " / " + citation.Hit.Heading, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(citation.Hit.Source, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.TextArea(citation.Hit.Text, GUILayout.MinHeight(65));
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
