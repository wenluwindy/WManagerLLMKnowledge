using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WManager.Knowledge
{
    public sealed class KnowledgeService : IKnowledgeService
    {
        private readonly KnowledgeOptions options;
        private readonly IEmbeddingProvider embeddings;
        private readonly IAnswerGenerator generator;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly string fingerprint;
        private SqliteKnowledgeStore store;
        private int disposed;
        private static readonly Regex CitationPattern = new Regex(@"\[S[0-9]+\]", RegexOptions.CultureInvariant);

        public KnowledgeService(KnowledgeOptions options, IEmbeddingProvider embeddings, IAnswerGenerator generator)
        {
            if (options == null || string.IsNullOrWhiteSpace(options.DatabasePath)) throw new ArgumentException("DatabasePath is required.", nameof(options));
            this.options = new KnowledgeOptions
            {
                DatabasePath = System.IO.Path.GetFullPath(options.DatabasePath), ChunkTokens = options.ChunkTokens,
                OverlapTokens = options.OverlapTokens, MaximumDocumentCharacters = options.MaximumDocumentCharacters
            };
            this.embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            if (options.ChunkTokens < 8 || options.ChunkTokens > embeddings.MaxInputTokens || options.OverlapTokens < 0 || options.OverlapTokens >= options.ChunkTokens)
                throw new ArgumentException("Invalid chunk size or overlap for this embedding model.", nameof(options));
            fingerprint = KnowledgeHash.Text("schema=1;splitter=1;normalize=l2;" + embeddings.Fingerprint + ";dimensions=" + embeddings.Dimensions + ";chunk=" + options.ChunkTokens + ";overlap=" + options.OverlapTokens);
        }

        public Task InitializeAsync(CancellationToken ct = default) => RunAsync<object>(token =>
        {
            if (store == null) store = new SqliteKnowledgeStore(options.DatabasePath, fingerprint);
            return Task.FromResult<object>(null);
        }, ct, false);

        public Task<ImportResult> ImportAsync(ImportRequest request, IProgress<ImportProgress> progress = null, CancellationToken ct = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Document text is required.", nameof(request));
            if (request.Text.Length > options.MaximumDocumentCharacters) throw new ArgumentException("Document exceeds the configured size limit.", nameof(request));
            string text = request.Text;
            string id = string.IsNullOrWhiteSpace(request.DocumentId) ? Guid.NewGuid().ToString("N") : request.DocumentId;
            string title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled" : request.Title;
            string source = request.Source ?? string.Empty;
            return RunAsync(async token =>
            {
                string hash = KnowledgeHash.Text(text);
                var previous = store.ListDocuments().FirstOrDefault(x => x.Id == id);
                if (previous != null && previous.ContentHash == hash && previous.Title == title && previous.Source == source)
                    return new ImportResult { DocumentId = id, ChunkCount = previous.ChunkCount, Unchanged = true };
                var splitter = new TokenTextSplitter(embeddings.CountTokens, options.ChunkTokens, options.OverlapTokens);
                progress?.Report(new ImportProgress { Stage = "Splitting" });
                var chunks = splitter.Split(id, text, token);
                if (chunks.Count == 0) throw new InvalidOperationException("The document contains no usable text.");
                for (int i = 0; i < chunks.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    chunks[i].Vector = VectorMath.Normalize(await embeddings.EmbedAsync(chunks[i].Text, false, token).ConfigureAwait(false), embeddings.Dimensions);
                    progress?.Report(new ImportProgress { Stage = "Embedding", Completed = i + 1, Total = chunks.Count });
                }
                token.ThrowIfCancellationRequested();
                store.ReplaceDocument(new KnowledgeDocument
                {
                    Id = id, Title = title, Source = source, ContentHash = hash,
                    UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), ChunkCount = chunks.Count
                }, chunks, token);
                progress?.Report(new ImportProgress { Stage = "Committed", Completed = chunks.Count, Total = chunks.Count });
                return new ImportResult { DocumentId = id, ChunkCount = chunks.Count };
            }, ct);
        }

        public Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(CancellationToken ct = default) =>
            RunAsync(token => Task.FromResult<IReadOnlyList<KnowledgeDocument>>(store.ListDocuments()), ct);

        public Task DeleteDocumentAsync(string documentId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document ID is required.", nameof(documentId));
            return RunAsync<object>(token => { token.ThrowIfCancellationRequested(); store.DeleteDocument(documentId); return Task.FromResult<object>(null); }, ct);
        }

        public Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken ct = default)
        {
            ValidateSearch(request);
            var snapshot = new SearchRequest { Question = request.Question, TopK = request.TopK, MinimumScore = request.MinimumScore, DocumentId = request.DocumentId };
            return RunAsync(token => SearchInternalAsync(snapshot, token), ct);
        }

        private async Task<SearchResult> SearchInternalAsync(SearchRequest request, CancellationToken ct)
        {
            var documents = store.ListDocuments().ToDictionary(x => x.Id);
            if (documents.Count == 0) return new SearchResult();
            float[] query = VectorMath.Normalize(await embeddings.EmbedAsync(request.Question, true, ct).ConfigureAwait(false), embeddings.Dimensions);
            var hits = new List<SearchHit>();
            foreach (var chunk in store.ReadChunks(request.DocumentId, ct))
            {
                ct.ThrowIfCancellationRequested();
                float score = VectorMath.Dot(query, chunk.Vector);
                if (float.IsNaN(score) || score < request.MinimumScore) continue;
                var document = documents[chunk.DocumentId];
                hits.Add(new SearchHit
                {
                    ChunkId = chunk.Id, DocumentId = chunk.DocumentId, Title = document.Title, Source = document.Source,
                    Heading = chunk.Heading, Text = chunk.Text, Ordinal = chunk.Ordinal, Score = score
                });
            }
            return new SearchResult { Hits = hits.OrderByDescending(x => x.Score).ThenBy(x => x.ChunkId, StringComparer.Ordinal).Take(request.TopK).ToArray() };
        }

        public Task<AnswerResult> AskAsync(AskRequest request, IProgress<AnswerDelta> stream = null, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var search = new SearchRequest { Question = request.Question, TopK = request.TopK, MinimumScore = request.MinimumScore, DocumentId = request.DocumentId };
            ValidateSearch(search);
            if (request.MaximumEvidence < 1 || request.MaximumEvidence > 20) throw new ArgumentOutOfRangeException(nameof(request.MaximumEvidence));
            int maximumEvidence = request.MaximumEvidence;
            return RunAsync(async token =>
            {
                var timer = Stopwatch.StartNew();
                var result = await SearchInternalAsync(search, token).ConfigureAwait(false);
                var evidence = new List<KnowledgeCitation>();
                int promptBudget = generator.ContextTokens - generator.MaxOutputTokens - 64;
                if (generator.CountTokens(generator.BuildPrompt(search.Question, evidence)) > promptBudget)
                    throw new ArgumentException("The question exceeds the model context budget.");
                var bodies = new HashSet<string>(StringComparer.Ordinal);
                foreach (var hit in result.Hits)
                {
                    if (!bodies.Add(hit.Text)) continue;
                    var citation = new KnowledgeCitation { Id = "S" + (evidence.Count + 1), Hit = hit };
                    evidence.Add(citation);
                    if (generator.CountTokens(generator.BuildPrompt(search.Question, evidence)) > promptBudget) evidence.RemoveAt(evidence.Count - 1);
                    if (evidence.Count == maximumEvidence) break;
                }
                if (evidence.Count == 0)
                {
                    const string missing = "现有资料中没有找到足够的依据。";
                    stream?.Report(new AnswerDelta { Text = missing });
                    return new AnswerResult { Text = missing, InsufficientEvidence = true, ElapsedMilliseconds = timer.ElapsedMilliseconds };
                }
                string answer = await generator.GenerateAsync(generator.BuildPrompt(search.Question, evidence), stream, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("The model returned no visible answer. Check its chat template and output budget.");
                var used = new HashSet<string>(CitationPattern.Matches(answer).Cast<Match>().Select(x => x.Value.Trim('[', ']')), StringComparer.Ordinal);
                var validIds = new HashSet<string>(evidence.Select(x => x.Id), StringComparer.Ordinal);
                string cleaned = CitationPattern.Replace(answer, match => validIds.Contains(match.Value.Trim('[', ']')) ? match.Value : string.Empty);
                return new AnswerResult
                {
                    Text = cleaned, Citations = evidence.Where(x => used.Contains(x.Id)).ToArray(), RetrievedEvidence = evidence.ToArray(),
                    InsufficientEvidence = false, ElapsedMilliseconds = timer.ElapsedMilliseconds
                };
            }, ct);
        }

        public Task<KnowledgeExport> ExportAsync(string destinationPath, CancellationToken ct = default) => RunAsync(token =>
        {
            store.Export(destinationPath, token);
            return Task.FromResult(new KnowledgeExport
            {
                databaseFile = System.IO.Path.GetFileName(destinationPath), databaseSha256 = KnowledgeHash.File(destinationPath),
                indexFingerprint = fingerprint, embeddingFingerprint = embeddings.Fingerprint, dimensions = embeddings.Dimensions,
                chunkTokens = options.ChunkTokens, overlapTokens = options.OverlapTokens, documentCount = store.ListDocuments().Count,
                createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            });
        }, ct);

        private Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct, bool requireInitialized = true)
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(KnowledgeService));
            return Task.Run(async () =>
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token))
                {
                    await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                    try
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (requireInitialized && store == null) throw new InvalidOperationException("Initialize the knowledge service first.");
                        return await operation(linked.Token).ConfigureAwait(false);
                    }
                    finally { gate.Release(); }
                }
            });
        }

        private static void ValidateSearch(SearchRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Question)) throw new ArgumentException("A question is required.", nameof(request));
            if (request.TopK < 1 || request.TopK > 100) throw new ArgumentOutOfRangeException(nameof(request.TopK));
            if (float.IsNaN(request.MinimumScore) || request.MinimumScore < -1 || request.MinimumScore > 1) throw new ArgumentOutOfRangeException(nameof(request.MinimumScore));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            gate.Wait();
            try { store?.Dispose(); generator.Dispose(); embeddings.Dispose(); }
            finally { gate.Release(); }
            // Keep synchronization primitives alive for already queued operations to observe cancellation.
        }
    }
}
