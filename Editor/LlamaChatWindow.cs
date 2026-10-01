using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace WManager.Knowledge.Editor
{
    public sealed class LlamaChatWindow : EditorWindow
    {
        [SerializeField] private LlamaChatSettings settings;
        [SerializeField] private int tab;
        [SerializeField] private string input = "";
        [SerializeField] private List<ChatMessage> history = new List<ChatMessage>();
        private readonly KnowledgeEditorUI configurationUI = new KnowledgeEditorUI();
        private LlamaChatService service;
        private Task<LlamaChatService> pendingCreation;
        private CancellationTokenSource operation;
        private Vector2 scroll;
        private string streaming = "";
        private string pendingQuestion = "";
        private string status = "";
        private string error = "";
        private bool busy;
        private bool closing;
        private int revision;

        [Serializable]
        private sealed class Conversation
        {
            public List<ChatMessage> messages = new List<ChatMessage>();
        }

        [MenuItem("Tools/WManager/LLM Chat")]
        public static void Open()
        {
            var window = GetWindow<LlamaChatWindow>("LLM Chat");
            window.minSize = new Vector2(620, 520);
        }

        private void OnEnable()
        {
            closing = false;
            if (settings == null)
            {
                var assets = AssetDatabase.FindAssets("t:LlamaChatSettings");
                if (assets.Length > 0) settings = AssetDatabase.LoadAssetAtPath<LlamaChatSettings>(AssetDatabase.GUIDToAssetPath(assets[0]));
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
            revision++;
            operation?.Cancel();
            service?.Dispose();
            service = null;
            if (pendingCreation != null)
            {
                try { pendingCreation.GetAwaiter().GetResult().Dispose(); }
                catch (OperationCanceledException) { }
                catch (Exception exception) { Debug.LogWarning("[LLM Chat] " + exception.Message); }
                pendingCreation = null;
            }
            streaming = "";
            pendingQuestion = "";
        }

        private void OnInspectorUpdate() { if (busy) Repaint(); }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("大模型聊天", EditorStyles.largeLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(busy || service != null))
                settings = (LlamaChatSettings)EditorGUILayout.ObjectField("配置", settings, typeof(LlamaChatSettings), false);
            using (new EditorGUI.DisabledScope(busy))
                if (GUILayout.Button("新建配置", GUILayout.Width(80))) CreateSettings();
            EditorGUILayout.EndHorizontal();
            tab = GUILayout.Toolbar(tab, new[] { "对话", "模型与参数" });
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawConversation();
            else DrawConfiguration();
            EditorGUILayout.EndScrollView();
            if (tab == 0) DrawComposer();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(string.IsNullOrEmpty(status) ? (service == null ? "模型未加载" : service.BackendDescription) : status, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!busy))
                if (GUILayout.Button("取消", EditorStyles.toolbarButton, GUILayout.Width(50))) operation?.Cancel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfiguration()
        {
            if (settings == null) { EditorGUILayout.HelpBox("请选择或新建聊天配置。", MessageType.Info); return; }
            KnowledgeEditorUI.BeginSection("模型、采样、加速与系统提示词");
            using (new EditorGUI.DisabledScope(busy || service != null))
            {
                configurationUI.DrawSettings(settings);
                if (GUILayout.Button("选择 GGUF 模型"))
                {
                    string path = EditorUtility.OpenFilePanel("选择聊天模型", Application.streamingAssetsPath, "gguf");
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            GgufModelInfo.Read(path);
                            Undo.RecordObject(settings, "Select chat model");
                            string root = Path.GetFullPath(Application.streamingAssetsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                            string full = Path.GetFullPath(path);
                            settings.generationModel = full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                                ? full.Substring(root.Length).Replace('\\', '/') : full;
                            EditorUtility.SetDirty(settings);
                            AssetDatabase.SaveAssets();
                            error = "";
                        }
                        catch (Exception exception) { error = exception.Message; }
                    }
                }
            }
            KnowledgeEditorUI.EndSection();
            using (new EditorGUI.DisabledScope(busy))
            {
                if (GUILayout.Button(service == null ? "加载模型" : "释放模型"))
                {
                    if (service != null) { Stop(); status = "模型已释放，可修改参数"; }
                    else Run(async token => { await EnsureService(token); status = "已就绪 / " + service.BackendDescription; });
                }
                if (GUILayout.Button("保存配置")) AssetDatabase.SaveAssets();
            }
        }

        private void DrawConversation()
        {
            using (new EditorGUI.DisabledScope(busy))
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.Label(history.Count / 2 + " 轮对话", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("新会话", EditorStyles.toolbarButton)
                    && (history.Count == 0 || EditorUtility.DisplayDialog("新会话", "清空当前对话？", "清空", "取消")))
                    Run(async token => { if (service != null) await service.RestoreHistoryAsync(Array.Empty<ChatMessage>(), token); history.Clear(); streaming = ""; status = "已创建新会话"; });
                if (GUILayout.Button("导入", EditorStyles.toolbarButton)) ImportConversation();
                using (new EditorGUI.DisabledScope(history.Count == 0))
                    if (GUILayout.Button("导出", EditorStyles.toolbarButton)) ExportConversation();
                EditorGUILayout.EndHorizontal();
            }
            foreach (var message in history) DrawMessage(message.Role == ChatRole.User ? "你" : "助手", message.Text);
            if (!string.IsNullOrEmpty(pendingQuestion))
            {
                DrawMessage("你", pendingQuestion);
                DrawMessage("助手", string.IsNullOrEmpty(streaming) ? "正在生成…" : streaming);
            }
        }

        private static void DrawMessage(string role, string text)
        {
            KnowledgeEditorUI.BeginSection(role);
            EditorGUILayout.SelectableLabel(text, EditorStyles.wordWrappedLabel,
                GUILayout.Height(Mathf.Max(36, EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(text), Mathf.Max(200, EditorGUIUtility.currentViewWidth - 64)))));
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("复制", EditorStyles.miniButton, GUILayout.Width(48))) EditorGUIUtility.systemCopyBuffer = text;
            EditorGUILayout.EndHorizontal();
            KnowledgeEditorUI.EndSection();
        }

        private void DrawComposer()
        {
            using (new EditorGUI.DisabledScope(busy))
            {
                input = EditorGUILayout.TextArea(input, GUILayout.Height(76));
                using (new EditorGUI.DisabledScope(settings == null || string.IsNullOrWhiteSpace(input)))
                    if (GUILayout.Button("发送", GUILayout.Height(28))) Send();
            }
        }

        private void Send()
        {
            string question = input;
            int current = revision;
            Run(async token =>
            {
                await EnsureService(token);
                pendingQuestion = question;
                streaming = "";
                bool receiving = true;
                var progress = new Progress<AnswerDelta>(delta =>
                {
                    if (!closing && receiving && current == revision)
                    { streaming += delta.Text; scroll.y = float.MaxValue; Repaint(); }
                });
                ChatReply reply;
                try { reply = await service.SendAsync(question, progress, token); }
                finally { receiving = false; }
                token.ThrowIfCancellationRequested();
                if (current != revision) throw new OperationCanceledException();
                history = new List<ChatMessage>(reply.History);
                input = "";
                pendingQuestion = "";
                streaming = "";
                scroll.y = float.MaxValue;
                status = "完成 " + reply.ElapsedMilliseconds + " ms / 首字 " + reply.FirstTokenMilliseconds
                    + " ms / 输入 " + reply.PromptTokens + " tokens / 裁剪 " + reply.DroppedTurns + " 轮";
            });
        }

        private async Task EnsureService(CancellationToken token)
        {
            if (service != null) return;
            if (settings == null) throw new InvalidOperationException("请选择聊天配置。");
            status = "正在加载模型…";
            int current = revision;
            pendingCreation = LlamaChatService.CreateAsync(settings.CreateBackendOptions(
                Path.Combine(KnowledgeCenterWindow.PackageDirectory, "Plugins/Windows/x86_64")), settings.systemPrompt, token);
            var created = await pendingCreation;
            pendingCreation = null;
            if (closing || token.IsCancellationRequested || current != revision)
            { created.Dispose(); throw new OperationCanceledException(); }
            service = created;
            await service.RestoreHistoryAsync(history, token);
        }

        private async void Run(Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true;
            error = "";
            var cancellation = new CancellationTokenSource();
            operation = cancellation;
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) { status = "已取消，输入已保留"; pendingQuestion = ""; streaming = ""; }
            catch (Exception exception) { error = exception.Message; pendingQuestion = ""; streaming = ""; Debug.LogException(exception); }
            finally { busy = false; cancellation.Dispose(); operation = null; if (!closing) Repaint(); }
        }

        private void CreateSettings()
        {
            string path = EditorUtility.SaveFilePanelInProject("创建聊天配置", "LlamaChatSettings", "asset", "选择保存目录");
            if (string.IsNullOrEmpty(path)) return;
            Stop();
            settings = CreateInstance<LlamaChatSettings>();
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
        }

        private void ExportConversation()
        {
            string path = EditorUtility.SaveFilePanel("导出对话", "", "conversation.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            try { File.WriteAllText(path, JsonUtility.ToJson(new Conversation { messages = history }, true)); status = "对话已导出"; }
            catch (Exception exception) { error = exception.Message; }
        }

        private void ImportConversation()
        {
            string path = EditorUtility.OpenFilePanel("导入对话", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            Run(async token =>
            {
                if (new FileInfo(path).Length > 16000000) throw new InvalidOperationException("对话文件过大。");
                var data = JsonUtility.FromJson<Conversation>(File.ReadAllText(path));
                if (data?.messages == null) throw new InvalidOperationException("对话文件格式无效。");
                ValidateConversation(data.messages);
                if (service != null) await service.RestoreHistoryAsync(data.messages, token);
                history = data.messages;
                streaming = "";
                pendingQuestion = "";
                status = "对话已导入";
            });
        }

        private static void ValidateConversation(List<ChatMessage> messages)
        {
            if (messages.Count > 1000 || messages.Count % 2 != 0) throw new InvalidOperationException("对话必须包含完整问答，最多 500 轮。");
            long size = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                if (message == null || message.Role != (i % 2 == 0 ? ChatRole.User : ChatRole.Assistant) || string.IsNullOrWhiteSpace(message.Text))
                    throw new InvalidOperationException("对话包含无效或不完整的消息。");
                size += message.Text.Length;
            }
            if (size > 2000000) throw new InvalidOperationException("对话内容过大。");
        }
    }
}
