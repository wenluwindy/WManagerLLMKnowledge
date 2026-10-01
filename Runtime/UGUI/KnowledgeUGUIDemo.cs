using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WManager.Knowledge.Samples
{
    [RequireComponent(typeof(KnowledgeRuntime))]
    public sealed class KnowledgeUGUIDemo : MonoBehaviour
    {
        [SerializeField] private Font chineseFont = null;
        [SerializeField] private Text status;
        [SerializeField] private InputField title, content, filePath, question, modelPath, embeddingPath;
        [SerializeField] private Text output;
        [SerializeField] private Dropdown documents, scope, acceleration;
        [SerializeField] private Button cancel, initialize, save, remove, import, refresh, search, ask, apply;
        [SerializeField] private Button documentTab, answerTab, settingsTab, copy, preview;
        [SerializeField] private GameObject documentPage, answerPage, settingsPage;
        private KnowledgeRuntime runtime;
        private KnowledgeSettings sessionSettings;
        private readonly List<KnowledgeDocument> entries = new List<KnowledgeDocument>();
        private readonly StringBuilder streamed = new StringBuilder();
        private CancellationTokenSource operation;
        private bool busy, alive, confirmingDelete;
        private int revision;
        private Font font;
        private static readonly Color Ink = new Color32(32, 39, 44, 255);
        private static readonly Color Accent = new Color32(21, 116, 97, 255);

        private void Awake()
        {
            alive = true;
            runtime = GetComponent<KnowledgeRuntime>();
            if (status == null) BuildUI();
            EnsureEventSystem();
            if (font == null) font = chineseFont != null ? chineseFont : Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            foreach (var label in GetComponentsInChildren<Text>(true)) label.font = font;
            documentTab.onClick.AddListener(() => ShowPage(documentPage));
            answerTab.onClick.AddListener(() => ShowPage(answerPage));
            settingsTab.onClick.AddListener(() => ShowPage(settingsPage));
            copy.onClick.AddListener(() => GUIUtility.systemCopyBuffer = output.text);
            preview.onClick.AddListener(() => ShowPage(answerPage));
            if (runtime.Settings != null)
            {
                sessionSettings = Instantiate(runtime.Settings);
                runtime.Settings = sessionSettings;
                modelPath.text = sessionSettings.generationModel;
                embeddingPath.text = sessionSettings.embeddingModel;
                acceleration.value = (int)sessionSettings.acceleration;
            }
            initialize.onClick.AddListener(() => Run(async token => { await Ready(token); SetStatus("知识库已就绪"); }));
            cancel.onClick.AddListener(() => { operation?.Cancel(); SetStatus("正在取消…"); });
            refresh.onClick.AddListener(() => Run(async token => { await Ready(token); SetStatus("资料列表已刷新"); }));
            documents.onValueChanged.AddListener(_ => SelectDocument());
            save.onClick.AddListener(() => Run(SaveDocument));
            remove.onClick.AddListener(DeleteDocument);
            import.onClick.AddListener(() => Run(ImportDocument));
            search.onClick.AddListener(() => Run(Search));
            ask.onClick.AddListener(() => Run(Ask));
            apply.onClick.AddListener(ApplySettings);
            SetStatus(runtime.Settings == null ? "请在 Inspector 指定 KnowledgeSettings" : "未初始化");
            UpdateControls();
        }

        private async Task Ready(CancellationToken token)
        {
            SetStatus("正在初始化并读取资料…");
            await runtime.InitializeAsync(token);
            await RefreshDocuments(token);
        }

        private async Task RefreshDocuments(CancellationToken token, string selectedId = null)
        {
            var result = await runtime.Service.ListDocumentsAsync(token);
            if (!alive) return;
            if (selectedId == null) selectedId = SelectedDocument?.Id;
            string scopeId = SelectedScope;
            entries.Clear();
            entries.AddRange(result);
            confirmingDelete = false;
            remove.GetComponentInChildren<Text>().text = "删除资料";
            var options = new List<string> { "新增资料" };
            var scopes = new List<string> { "全部资料" };
            foreach (var entry in entries) { options.Add(entry.Title + " · " + entry.ChunkCount + " 段"); scopes.Add(entry.Title); }
            documents.ClearOptions(); documents.AddOptions(options);
            scope.ClearOptions(); scope.AddOptions(scopes);
            documents.SetValueWithoutNotify(entries.FindIndex(entry => entry.Id == selectedId) + 1);
            scope.SetValueWithoutNotify(entries.FindIndex(entry => entry.Id == scopeId) + 1);
        }

        private KnowledgeDocument SelectedDocument => documents.value > 0 && documents.value <= entries.Count ? entries[documents.value - 1] : null;
        private string SelectedScope => scope.value > 0 && scope.value <= entries.Count ? entries[scope.value - 1].Id : null;

        private void SelectDocument()
        {
            confirmingDelete = false;
            remove.GetComponentInChildren<Text>().text = "删除资料";
            var entry = SelectedDocument;
            if (entry == null) { title.text = ""; content.text = ""; SetStatus("新增资料"); return; }
            title.text = entry.Title;
            // Indexed chunks overlap; rebuilding the original document from them would duplicate text.
            content.text = "";
            Run(async token =>
            {
                var chunks = await runtime.Service.ReadDocumentChunksAsync(entry.Id, token);
                var text = new StringBuilder();
                foreach (var chunk in chunks) text.AppendLine("[" + (chunk.Ordinal + 1) + "] " + chunk.Heading).AppendLine(chunk.Text).AppendLine();
                if (alive) output.text = text.ToString();
                SetStatus(entry.Title + " · " + entry.ChunkCount + " 段 · " + entry.Source);
            });
        }

        private async Task SaveDocument(CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(title.text) || string.IsNullOrWhiteSpace(content.text)) throw new ArgumentException("请输入标题与完整正文");
            var selected = SelectedDocument;
            var request = new ImportRequest { DocumentId = selected?.Id, Title = title.text.Trim(), Text = content.text,
                Source = selected?.Source ?? "uGUI 手动录入" };
            await Ready(token);
            var result = await runtime.Service.ImportAsync(request, ImportProgress(), token);
            await RefreshDocuments(token, result.DocumentId);
            SetStatus(result.Unchanged ? "资料没有变化" : "已保存 " + result.ChunkCount + " 个片段");
        }

        private void DeleteDocument()
        {
            var selected = SelectedDocument;
            if (selected == null) { SetStatus("请先选择资料"); return; }
            if (!confirmingDelete)
            {
                confirmingDelete = true;
                remove.GetComponentInChildren<Text>().text = "确认删除";
                SetStatus("确认删除：" + selected.Title);
                return;
            }
            Run(async token =>
            {
                await runtime.Service.DeleteDocumentAsync(selected.Id, token);
                await RefreshDocuments(token, "");
                title.text = ""; content.text = "";
                confirmingDelete = false;
                remove.GetComponentInChildren<Text>().text = "删除资料";
                SetStatus("资料已删除");
            });
        }

        private async Task ImportDocument(CancellationToken token)
        {
            string path = filePath.text.Trim().Trim('"');
            if (!File.Exists(path)) throw new FileNotFoundException("文件不存在", path);
            await Ready(token);
            SetStatus("正在提取文档…");
            var result = await runtime.ImportFileAsync(path, ImportProgress(), token);
            await RefreshDocuments(token, result.DocumentId);
            SetStatus(result.Unchanged ? "文件内容没有变化" : "导入完成 · " + result.ChunkCount + " 个片段");
        }

        private async Task Search(CancellationToken token)
        {
            string input = ValidateQuestion();
            await Ready(token);
            var settings = runtime.Settings;
            var result = await runtime.Service.SearchAsync(new SearchRequest { Question = input, DocumentId = SelectedScope,
                TopK = settings.topK, MinimumScore = settings.minimumScore }, token);
            var text = new StringBuilder();
            foreach (var hit in result.Hits)
                text.AppendLine(hit.Title + " · " + hit.Heading + " · " + hit.Score.ToString("F3"))
                    .AppendLine(hit.Text).AppendLine(hit.Source).AppendLine();
            if (alive) output.text = result.Hits.Count == 0 ? "没有匹配资料" : text.ToString();
            SetStatus("检索完成 · " + result.Hits.Count + " 个片段");
        }

        private async Task Ask(CancellationToken token)
        {
            string input = ValidateQuestion();
            await Ready(token);
            var settings = runtime.Settings;
            streamed.Clear(); output.text = "";
            int current = revision;
            SetStatus("正在生成回答…");
            var stream = new Progress<AnswerDelta>(delta =>
            {
                if (!alive || current != revision || token.IsCancellationRequested) return;
                streamed.Append(delta.Text); output.text = streamed.ToString();
            });
            var result = await runtime.Service.AskAsync(new AskRequest { Question = input, DocumentId = SelectedScope,
                TopK = settings.topK, MinimumScore = settings.minimumScore, MaximumEvidence = settings.maximumEvidence }, stream, token);
            revision++;
            if (!alive) return;
            var text = new StringBuilder(result.Text);
            foreach (var citation in result.Citations)
                text.AppendLine().Append('[').Append(citation.Id).Append("] ").Append(citation.Hit.Title)
                    .Append(" / ").Append(citation.Hit.Heading).Append(" / ").Append(citation.Hit.Source);
            output.text = text.ToString();
            SetStatus((result.InsufficientEvidence ? "资料不足" : "回答完成") + " · 首字 " + result.FirstTokenMilliseconds
                + " ms · 检索 " + result.RetrievalMilliseconds + " ms · 总计 " + result.ElapsedMilliseconds + " ms");
        }

        private string ValidateQuestion()
        {
            if (string.IsNullOrWhiteSpace(question.text)) throw new ArgumentException("请输入问题");
            return question.text.Trim();
        }

        private IProgress<ImportProgress> ImportProgress()
        {
            int current = revision;
            return new Progress<ImportProgress>(value =>
            {
                if (!alive || revision != current) return;
                SetStatus(value.Stage == "Embedding" ? "正在计算向量 " + value.Completed + "/" + value.Total
                    : value.Stage == "Committed" ? "资料已入库" : "正在分块…");
            });
        }

        private void ApplySettings()
        {
            if (sessionSettings == null) { SetStatus("请在 Inspector 指定 KnowledgeSettings"); return; }
            if (runtime.IsReady) { SetStatus("模型已加载，请停止播放后修改配置"); return; }
            sessionSettings.generationModel = modelPath.text.Trim();
            sessionSettings.embeddingModel = embeddingPath.text.Trim();
            sessionSettings.acceleration = (LlamaAcceleration)acceleration.value;
            SetStatus("本次运行配置已应用");
        }

        private async void Run(Func<CancellationToken, Task> action)
        {
            if (busy || !alive) return;
            busy = true; revision++;
            operation = new CancellationTokenSource();
            UpdateControls();
            try { await action(operation.Token); }
            catch (OperationCanceledException) { SetStatus("已取消"); }
            catch (Exception exception) { SetStatus(exception.Message); if (alive) Debug.LogException(exception, this); }
            finally
            {
                revision++; busy = false;
                operation.Dispose(); operation = null;
                if (alive) UpdateControls();
            }
        }

        private void SetStatus(string value) { if (alive && status != null) status.text = value; }
        private void UpdateControls()
        {
            foreach (var button in new[] { initialize, save, remove, import, refresh, search, ask }) button.interactable = !busy;
            cancel.interactable = busy;
            foreach (var input in new[] { title, content, filePath, question, modelPath, embeddingPath }) input.interactable = !busy;
            documents.interactable = scope.interactable = acceleration.interactable = !busy;
            apply.interactable = !busy && !runtime.IsReady;
        }

        private void OnDestroy()
        {
            alive = false; revision++;
            operation?.Cancel();
            if (sessionSettings != null) ReleaseObject(sessionSettings);
            if (font != null && font != chineseFont) ReleaseObject(font);
        }

        private static void ReleaseObject(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        public void BuildUI()
        {
            if (status != null) return;
            font = chineseFont != null ? chineseFont : Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            var canvas = new GameObject("Knowledge Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1000, 800);
            scaler.matchWidthOrHeight = 1;
            var backdrop = canvas.AddComponent<Image>(); backdrop.color = new Color32(241, 244, 245, 255);
            backdrop.raycastTarget = false;
            var root = Node("Workspace", canvas.transform);
            Stretch(root, 20);
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            layout.childControlWidth = true; layout.childForceExpandWidth = true;
            Label(root, "本地知识库", 26, 38);
            var bar = Row(root);
            initialize = Command(bar, "初始化"); cancel = Command(bar, "取消");
            status = Label(root, "未初始化", 15, 44);
            var tabs = Row(root);
            documentTab = Command(tabs, "资料"); answerTab = Command(tabs, "问答"); settingsTab = Command(tabs, "配置");
            var body = Node("Pages", root);
            body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            documentPage = Page(body, "Documents"); answerPage = Page(body, "Answers"); settingsPage = Page(body, "Settings");
            var docs = documentPage.transform;
            documents = Options(docs, new[] { "新增资料" });
            title = Input(docs, "资料标题", 38);
            content = Input(docs, "完整正文", 160, true);
            var actions = Row(docs); save = Command(actions, "保存资料"); remove = Command(actions, "删除资料"); refresh = Command(actions, "刷新");
            filePath = Input(docs, "本地文档路径", 38);
            import = Command(docs, "导入文件");
            preview = Command(docs, "查看片段");
            var qa = answerPage.transform;
            scope = Options(qa, new[] { "全部资料" });
            question = Input(qa, "输入问题", 76, true);
            var queries = Row(qa); search = Command(queries, "检索"); ask = Command(queries, "提问");
            output = Output(qa);
            copy = Command(qa, "复制内容");
            var config = settingsPage.transform;
            Label(config, "回答模型", 16, 26); modelPath = Input(config, "GGUF 路径", 38);
            Label(config, "向量模型", 16, 26); embeddingPath = Input(config, "GGUF 路径", 38);
            Label(config, "推理设备", 16, 26); acceleration = Options(config, new[] { "Auto", "CPU", "Vulkan GPU" });
            apply = Command(config, "应用配置");
            ShowPage(documentPage);
            // This sample targets the legacy Input Manager or Both. Host apps can supply their own EventSystem.
            EnsureEventSystem();
        }

        private void EnsureEventSystem()
        {
            if ((!Application.isPlaying && GetComponentInChildren<EventSystem>() == null)
                || (Application.isPlaying && FindFirstObjectByType<EventSystem>() == null))
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
        }

        private void ShowPage(GameObject page)
        {
            documentPage.SetActive(page == documentPage); answerPage.SetActive(page == answerPage); settingsPage.SetActive(page == settingsPage);
            SetTab(documentTab, page == documentPage);
            SetTab(answerTab, page == answerPage);
            SetTab(settingsTab, page == settingsPage);
        }

        private static void SetTab(Button tab, bool selected)
        {
            tab.targetGraphic.color = selected ? Accent : new Color32(220, 228, 229, 255);
            tab.GetComponentInChildren<Text>().color = selected ? Color.white : Ink;
        }

        private RectTransform Node(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            node.SetParent(parent, false); return node;
        }
        private static void Stretch(RectTransform rect, float inset = 0, float? verticalInset = null)
        {
            float vertical = verticalInset ?? inset;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, vertical); rect.offsetMax = new Vector2(-inset, -vertical);
        }
        private RectTransform Row(Transform parent)
        {
            var row = Node("Toolbar", parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 8;
            layout.childControlWidth = true; layout.childForceExpandWidth = true;
            layout.childControlHeight = true; layout.childForceExpandHeight = false;
            var size = row.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 38; size.flexibleHeight = 0;
            return row;
        }
        private GameObject Page(Transform parent, string name)
        {
            var page = Node(name, parent); Stretch(page);
            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 10;
            layout.childControlHeight = true; layout.childForceExpandHeight = false;
            layout.childControlWidth = true; layout.childForceExpandWidth = true;
            return page.gameObject;
        }
        private Text Label(Transform parent, string value, int size, float height)
        {
            var node = Node("Label", parent);
            var text = node.gameObject.AddComponent<Text>(); text.font = font;
            text.text = value; text.fontSize = size; text.color = Ink; text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            node.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return text;
        }
        private Button Command(Transform parent, string name)
        {
            var node = Node(name, parent);
            var image = node.gameObject.AddComponent<Image>(); image.color = Accent;
            var button = node.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var text = Label(node, name, 16, 38); Stretch(text.rectTransform, 4); text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
            node.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            return button;
        }
        private InputField Input(Transform parent, string placeholder, float height, bool multiline = false)
        {
            var node = Node("Input " + placeholder, parent);
            var image = node.gameObject.AddComponent<Image>(); image.color = Color.white;
            var field = node.gameObject.AddComponent<InputField>(); field.targetGraphic = image;
            var text = Label(node, "", 17, height); Stretch(text.rectTransform, 10, 4);
            text.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            var hint = Label(node, placeholder, 17, height); Stretch(hint.rectTransform, 10, 4);
            hint.alignment = text.alignment; hint.color = new Color32(113, 121, 125, 255);
            field.textComponent = text; field.placeholder = hint;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            field.ForceLabelUpdate();
            node.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return field;
        }
        private Text Output(Transform parent)
        {
            var node = Node("Results", parent);
            node.gameObject.AddComponent<Image>().color = Color.white;
            var size = node.gameObject.AddComponent<LayoutElement>(); size.preferredHeight = 210; size.flexibleHeight = 1;
            var scroll = node.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            scroll.scrollSensitivity = 24;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Node("Viewport", node); Stretch(viewport, 10);
            viewport.gameObject.AddComponent<RectMask2D>(); scroll.viewport = viewport;
            var text = Label(viewport, "", 17, 0);
            text.GetComponent<LayoutElement>().preferredHeight = -1;
            text.alignment = TextAnchor.UpperLeft; text.verticalOverflow = VerticalWrapMode.Overflow;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1); rect.sizeDelta = Vector2.zero;
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = rect;
            return text;
        }
        private Dropdown Options(Transform parent, string[] values)
        {
            var node = Node("Selection", parent);
            var image = node.gameObject.AddComponent<Image>(); image.color = Color.white;
            var dropdown = node.gameObject.AddComponent<Dropdown>(); dropdown.targetGraphic = image;
            var caption = Label(node, "", 16, 38); Stretch(caption.rectTransform, 10, 4); dropdown.captionText = caption;
            caption.rectTransform.offsetMax = new Vector2(-36, -4);
            var arrow = Label(node, "\u25be", 16, 38);
            arrow.rectTransform.anchorMin = new Vector2(1, 0); arrow.rectTransform.anchorMax = Vector2.one;
            arrow.rectTransform.offsetMin = new Vector2(-30, 4); arrow.rectTransform.offsetMax = new Vector2(-8, -4);
            var template = Node("Options", node);
            template.anchorMin = new Vector2(0, 0); template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(0.5f, 1); template.sizeDelta = new Vector2(0, 200);
            template.gameObject.AddComponent<Image>().color = Color.white;
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = Node("Viewport", template); Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>(); scroll.viewport = viewport;
            var list = Node("Content", viewport);
            list.anchorMin = new Vector2(0, 1); list.anchorMax = Vector2.one; list.pivot = new Vector2(0.5f, 1); list.sizeDelta = new Vector2(0, 38);
            scroll.content = list;
            var item = Node("Item", list);
            item.anchorMin = new Vector2(0, 1); item.anchorMax = Vector2.one;
            item.pivot = new Vector2(0.5f, 1); item.sizeDelta = new Vector2(0, 38);
            var itemImage = item.gameObject.AddComponent<Image>(); itemImage.color = new Color32(223, 237, 231, 255);
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = itemImage;
            var itemText = Label(item, "", 16, 38); Stretch(itemText.rectTransform, 10, 4);
            dropdown.itemText = itemText; dropdown.template = template;
            template.gameObject.SetActive(false);
            dropdown.AddOptions(new List<string>(values));
            dropdown.RefreshShownValue();
            node.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            return dropdown;
        }
    }
}
