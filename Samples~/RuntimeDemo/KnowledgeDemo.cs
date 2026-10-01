using System;
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
        private string title = "新增资料";
        private string content = "";
        private string question = "";
        private string answer = "";
        private string status = "";
        private string documentId;
        private Vector2 scroll;

        private void Awake() => runtime = GetComponent<KnowledgeRuntime>();

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Max(280, Screen.width - 32), Mathf.Max(300, Screen.height - 32)));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("本地知识库");
            GUI.enabled = !busy;
            if (GUILayout.Button("初始化")) Run(async token => { await runtime.InitializeAsync(token); status = "知识库已就绪"; });
            title = GUILayout.TextField(title);
            content = GUILayout.TextArea(content, GUILayout.Height(120));
            if (GUILayout.Button("保存资料")) Run(async token =>
            {
                await runtime.InitializeAsync(token);
                var result = await runtime.Service.ImportAsync(new ImportRequest { DocumentId = documentId, Title = title, Source = "Runtime input", Text = content }, null, token);
                documentId = result.DocumentId;
                status = "已保存 " + result.ChunkCount + " 个片段";
            });
            question = GUILayout.TextField(question);
            if (GUILayout.Button("提问")) Run(async token =>
            {
                answer = string.Empty;
                await runtime.InitializeAsync(token);
                var settings = runtime.Settings;
                var result = await runtime.Service.AskAsync(new AskRequest { Question = question, TopK = settings.topK, MinimumScore = settings.minimumScore }, new Progress<AnswerDelta>(delta => answer += delta.Text), token);
                answer = result.Text;
                foreach (var citation in result.Citations) answer += "\n[" + citation.Id + "] " + citation.Hit.Title + " / " + citation.Hit.Heading;
            });
            GUI.enabled = busy;
            if (GUILayout.Button("取消")) operation?.Cancel();
            GUI.enabled = true;
            GUILayout.Label(status);
            GUILayout.Label(runtime.LastError ?? string.Empty);
            GUILayout.TextArea(answer, GUILayout.MinHeight(120));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private async void Run(Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true;
            operation = new CancellationTokenSource();
            try { await action(operation.Token); }
            catch (OperationCanceledException) { status = "已取消"; }
            catch (Exception exception) { status = exception.Message; Debug.LogException(exception); }
            finally { busy = false; operation.Dispose(); operation = null; }
        }

        private void OnDestroy() => operation?.Cancel();
    }
}
