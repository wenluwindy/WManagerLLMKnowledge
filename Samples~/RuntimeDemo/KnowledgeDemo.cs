using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WManager.Knowledge.Samples
{
    [RequireComponent(typeof(KnowledgeRuntime))]
    public sealed class KnowledgeDemo : MonoBehaviour
    {
        private KnowledgeRuntime runtime;
        private CancellationTokenSource operation;
        private bool busy;
        private bool alive;
        private int revision;
        private string title = "新增资料";
        private string content = "";
        private string question = "";
        private string answer = "";
        private string status = "";
        private string documentId;
        private string filePath = "";
        private string documentList = "";
        private Vector2 scroll;

        private void Awake()
        {
            runtime = GetComponent<KnowledgeRuntime>();
            alive = true;
            status = runtime.Settings == null ? "请在 Inspector 指定 KnowledgeSettings" : "未初始化";
        }

        private void OnGUI()
        {
            bool previousEnabled = GUI.enabled;
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Max(1, Screen.width - 32), Mathf.Max(1, Screen.height - 32)));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("本地知识库");
            GUI.enabled = previousEnabled && !busy;
            if (GUILayout.Button("初始化")) Run(async token =>
            {
                await runtime.InitializeAsync(token);
                await RefreshDocuments(token);
            });
            if (GUILayout.Button("刷新资料")) Run(async token =>
            {
                if (!runtime.IsReady) { status = "资料尚未加载，请先初始化知识库"; return; }
                await RefreshDocuments(token);
            });
            GUILayout.Label(documentList);
            if (GUILayout.Button("新建资料")) { documentId = null; title = "新增资料"; content = ""; status = "新增资料"; }
            GUILayout.Label(documentId == null ? "新增资料" : "更新资料：" + documentId);
            title = GUILayout.TextField(title);
            content = GUILayout.TextArea(content, GUILayout.Height(120));
            if (GUILayout.Button("保存资料")) Run(SaveDocument);
            filePath = GUILayout.TextField(filePath);
            if (GUILayout.Button("导入文件")) Run(ImportFile);
            question = GUILayout.TextField(question);
            if (GUILayout.Button("检索")) Run(Search);
            if (GUILayout.Button("提问")) Run(Ask);
            if (GUILayout.Button("复制结果")) GUIUtility.systemCopyBuffer = answer;
            GUI.enabled = previousEnabled && busy;
            if (GUILayout.Button("取消")) operation?.Cancel();
            GUI.enabled = previousEnabled;
            GUILayout.Label(status);
            GUILayout.TextArea(answer, GUILayout.MinHeight(120));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private async Task RefreshDocuments(CancellationToken token)
        {
            var documents = await runtime.Service.ListDocumentsAsync(token);
            if (!alive) return;
            var text = new StringBuilder();
            foreach (var document in documents) text.AppendLine(document.Title + " · " + document.ChunkCount + " 段");
            documentList = text.ToString();
            status = documents.Count == 0 ? "知识库已就绪，暂无资料" : "资料列表已刷新 · " + documents.Count + " 篇";
        }

        private IProgress<ImportProgress> ImportProgress(CancellationToken token)
        {
            int current = revision;
            return new Progress<ImportProgress>(value =>
            {
                if (!alive || current != revision || token.IsCancellationRequested) return;
                status = value.Stage + " " + value.Completed + "/" + value.Total;
            });
        }

        private async Task SaveDocument(CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content)) throw new ArgumentException("请输入标题与完整正文");
            var request = new ImportRequest { DocumentId = documentId, Title = title.Trim(), Source = "Runtime input", Text = content };
            await runtime.InitializeAsync(token);
            var result = await runtime.Service.ImportAsync(request, ImportProgress(token), token);
            if (!alive) return;
            documentId = result.DocumentId;
            await RefreshDocuments(token);
            if (alive) status = result.Unchanged ? "资料没有变化" : "已保存 " + result.ChunkCount + " 个片段";
        }

        private async Task ImportFile(CancellationToken token)
        {
            string path = filePath.Trim().Trim('"');
            if (!File.Exists(path)) throw new FileNotFoundException("文件不存在", path);
            status = "正在初始化并导入文件…";
            var result = await runtime.ImportFileAsync(path, ImportProgress(token), token);
            await RefreshDocuments(token);
            if (alive) status = result.Unchanged ? "文件内容没有变化" : "导入完成 · " + result.ChunkCount + " 个片段";
        }

        private string ValidateQuestion()
        {
            if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("请输入问题");
            return question.Trim();
        }

        private async Task Search(CancellationToken token)
        {
            string input = ValidateQuestion();
            await runtime.InitializeAsync(token);
            var settings = runtime.Settings;
            var result = await runtime.Service.SearchAsync(new SearchRequest { Question = input,
                TopK = settings.topK, MinimumScore = settings.minimumScore }, token);
            if (!alive) return;
            var text = new StringBuilder();
            foreach (var hit in result.Hits)
                text.AppendLine(hit.Title + " / " + hit.Heading + " / " + hit.Score.ToString("F3"))
                    .AppendLine(hit.Text).AppendLine(hit.Source).AppendLine();
            answer = result.Hits.Count == 0 ? "没有匹配资料" : text.ToString();
            status = "检索完成 · " + result.Hits.Count + " 个片段";
        }

        private async Task Ask(CancellationToken token)
        {
            string input = ValidateQuestion();
            await runtime.InitializeAsync(token);
            var settings = runtime.Settings;
            answer = ""; status = "正在生成回答…";
            int current = revision;
            var stream = new Progress<AnswerDelta>(delta =>
            {
                if (alive && current == revision && !token.IsCancellationRequested) answer += delta.Text;
            });
            var result = await runtime.Service.AskAsync(new AskRequest { Question = input, TopK = settings.topK,
                MinimumScore = settings.minimumScore, MaximumEvidence = settings.maximumEvidence }, stream, token);
            revision++;
            if (!alive) return;
            var text = new StringBuilder(result.Text);
            foreach (var citation in result.Citations)
                text.AppendLine().Append('[').Append(citation.Id).Append("] ").Append(citation.Hit.Title)
                    .Append(" / ").Append(citation.Hit.Heading).Append(" / ").Append(citation.Hit.Source);
            answer = text.ToString();
            status = (result.InsufficientEvidence ? "资料不足" : result.MissingCitations ? "引用校验失败" : "回答完成")
                + " · 首字 " + result.FirstTokenMilliseconds + " ms · 检索 " + result.RetrievalMilliseconds
                + " ms · 总计 " + result.ElapsedMilliseconds + " ms";
        }

        private async void Run(Func<CancellationToken, Task> action)
        {
            if (busy || !alive) return;
            busy = true; revision++;
            operation = new CancellationTokenSource();
            try { await action(operation.Token); }
            catch (OperationCanceledException) { if (alive) status = "已取消"; }
            catch (KnowledgeIndexMismatchException exception) { if (alive) status = exception.Message; }
            catch (ArgumentException exception) { if (alive) status = exception.Message; }
            catch (FileNotFoundException exception) { if (alive) status = exception.Message; }
            catch (Exception exception) { if (alive) { status = exception.Message; Debug.LogException(exception, this); } }
            finally { revision++; busy = false; operation.Dispose(); operation = null; }
        }

        private void OnDestroy()
        {
            alive = false; revision++;
            operation?.Cancel();
        }
    }
}
