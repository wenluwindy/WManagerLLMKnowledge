using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WManager.Knowledge
{
    public sealed class LlamaChatService : IDisposable
    {
        private readonly LlamaAnswerGenerator generator;
        private readonly string systemPrompt;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private List<ChatMessage> history = new List<ChatMessage>();
        private int disposed;
        public string BackendDescription => generator.BackendDescription;

        private LlamaChatService(LlamaAnswerGenerator generator, string systemPrompt)
        { this.generator = generator; this.systemPrompt = systemPrompt ?? string.Empty; }

        public static Task<LlamaChatService> CreateAsync(LlamaBackendOptions options, string systemPrompt, CancellationToken ct = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            LlamaKnowledgeFactory.Validate(options, false);
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                LlamaNativeLoader.Configure(options.NativeLibraryDirectory, options.Acceleration != LlamaAcceleration.Cpu);
                var generator = new LlamaAnswerGenerator(options);
                try
                {
                    if (options.WarmupGpu) await generator.WarmupAsync(ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    return new LlamaChatService(generator, systemPrompt);
                }
                catch { generator.Dispose(); throw; }
            }, ct);
        }

        public Task RestoreHistoryAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
        {
            var snapshot = ChatHistoryBudget.CopyAndValidate(messages);
            return RunAsync(token => { history = snapshot; return Task.FromResult(0); }, ct);
        }

        public Task<ChatReply> SendAsync(string question, IProgress<AnswerDelta> stream = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("A message is required.", nameof(question));
            if (question.Length > 2000000) throw new ArgumentException("Message is too large.", nameof(question));
            return RunAsync(async token =>
            {
                var timer = Stopwatch.StartNew();
                var fitted = ChatHistoryBudget.Fit(history, question,
                    (messages, input) => generator.BuildChatPrompt(systemPrompt, messages, input), generator.CountTokens,
                    generator.ContextTokens - generator.MaxOutputTokens - 64);
                long firstToken = -1;
                var forwarding = new InlineProgress(delta =>
                {
                    if (firstToken < 0 && !string.IsNullOrWhiteSpace(delta.Text)) firstToken = timer.ElapsedMilliseconds;
                    stream?.Report(delta);
                });
                string answer = await generator.GenerateAsync(fitted.Prompt, forwarding, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("模型未返回可显示的正文。请检查聊天模板和最大输出设置；修改参数后释放并重新加载模型。");
                var committed = fitted.History;
                committed.Add(new ChatMessage { Role = ChatRole.User, Text = question, CreatedUtc = DateTime.UtcNow.ToString("O") });
                committed.Add(new ChatMessage { Role = ChatRole.Assistant, Text = answer, CreatedUtc = DateTime.UtcNow.ToString("O") });
                history = committed;
                return new ChatReply { Text = answer, PromptTokens = fitted.Tokens, DroppedTurns = fitted.DroppedTurns,
                    FirstTokenMilliseconds = firstToken, ElapsedMilliseconds = timer.ElapsedMilliseconds, History = ChatHistoryBudget.CopyAndValidate(history) };
            }, ct);
        }

        private Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(LlamaChatService));
            return Task.Run(async () =>
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token))
                {
                    await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                    try { linked.Token.ThrowIfCancellationRequested(); return await action(linked.Token).ConfigureAwait(false); }
                    finally { gate.Release(); }
                }
            });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            gate.Wait();
            try { generator.Dispose(); }
            finally { gate.Release(); }
        }

        private sealed class InlineProgress : IProgress<AnswerDelta>
        {
            private readonly Action<AnswerDelta> report;
            public InlineProgress(Action<AnswerDelta> report) => this.report = report;
            public void Report(AnswerDelta value) => report(value);
        }
    }

    internal static class ChatHistoryBudget
    {
        internal sealed class Selection
        {
            internal List<ChatMessage> History;
            internal string Prompt;
            internal int Tokens;
            internal int DroppedTurns;
        }

        internal static List<ChatMessage> CopyAndValidate(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (messages.Count > 1000 || messages.Count % 2 != 0) throw new ArgumentException("Chat history must contain complete user/assistant pairs (maximum 500 turns).");
            var copy = new List<ChatMessage>();
            long characters = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                var item = messages[i];
                if (item == null || item.Role != (i % 2 == 0 ? ChatRole.User : ChatRole.Assistant) || string.IsNullOrWhiteSpace(item.Text))
                    throw new ArgumentException("Chat history contains an invalid or incomplete message.");
                characters += item.Text.Length;
                if (characters > 2000000) throw new ArgumentException("Chat history is too large.");
                copy.Add(new ChatMessage { Role = item.Role, Text = item.Text, CreatedUtc = item.CreatedUtc });
            }
            return copy;
        }

        internal static Selection Fit(IReadOnlyList<ChatMessage> history, string question,
            Func<IReadOnlyList<ChatMessage>, string, string> build, Func<string, int> count, int budget)
        {
            var retained = CopyAndValidate(history);
            int dropped = 0;
            while (true)
            {
                string prompt = build(retained, question);
                int tokens = count(prompt);
                if (tokens <= budget) return new Selection { History = retained, Prompt = prompt, Tokens = tokens, DroppedTurns = dropped };
                if (retained.Count == 0) throw new ArgumentException("The message and system prompt exceed the model context budget.");
                retained.RemoveRange(0, 2);
                dropped++;
            }
        }
    }
}
