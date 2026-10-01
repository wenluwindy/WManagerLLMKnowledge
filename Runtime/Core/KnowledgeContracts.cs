using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WManager.Knowledge
{
    public interface IKnowledgeService : IDisposable
    {
        Task InitializeAsync(CancellationToken ct = default);
        Task<ImportResult> ImportAsync(ImportRequest request, IProgress<ImportProgress> progress = null, CancellationToken ct = default);
        Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(CancellationToken ct = default);
        Task<IReadOnlyList<SearchHit>> ReadDocumentChunksAsync(string documentId, CancellationToken ct = default);
        Task DeleteDocumentAsync(string documentId, CancellationToken ct = default);
        Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken ct = default);
        Task<AnswerResult> AskAsync(AskRequest request, IProgress<AnswerDelta> stream = null, CancellationToken ct = default);
        Task<KnowledgeExport> ExportAsync(string destinationPath, CancellationToken ct = default);
    }

    public interface IEmbeddingProvider : IDisposable
    {
        string Fingerprint { get; }
        int Dimensions { get; }
        int MaxInputTokens { get; }
        int CountTokens(string text);
        Task<float[]> EmbedAsync(string text, bool isQuery, CancellationToken ct);
    }

    public interface IAnswerGenerator : IDisposable
    {
        int ContextTokens { get; }
        int MaxOutputTokens { get; }
        int CountTokens(string prompt);
        string BuildPrompt(string question, IReadOnlyList<KnowledgeCitation> evidence);
        Task<string> GenerateAsync(string prompt, IProgress<AnswerDelta> stream, CancellationToken ct);
    }

    [Serializable]
    public sealed class KnowledgeOptions
    {
        public string DatabasePath;
        public int ChunkTokens = 320;
        public int OverlapTokens = 48;
        public int MaximumDocumentCharacters = 2000000;
    }

    public sealed class ImportRequest
    {
        public string DocumentId;
        public string Title;
        public string Source;
        public string Text;

        public static ImportRequest FromFile(string path)
            => FromFile(path, CancellationToken.None);

        public static ImportRequest FromFile(string path, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
            string text = KnowledgeDocumentReader.Read(path, ct);
            string canonicalPath = System.IO.Path.GetFullPath(path);
            return new ImportRequest
            {
                DocumentId = KnowledgeHash.Text(canonicalPath.ToUpperInvariant()),
                Title = System.IO.Path.GetFileNameWithoutExtension(path),
                Source = canonicalPath,
                Text = text
            };
        }
    }

    public sealed class ImportProgress
    {
        public string Stage;
        public int Completed;
        public int Total;
        public float Fraction => Total > 0 ? (float)Completed / Total : 0f;
    }

    public sealed class ImportResult
    {
        public string DocumentId;
        public int ChunkCount;
        public bool Unchanged;
    }

    [Serializable]
    public sealed class KnowledgeDocument
    {
        public string Id;
        public string Title;
        public string Source;
        public string ContentHash;
        public string UpdatedUtc;
        public int ChunkCount;
    }

    public sealed class SearchRequest
    {
        public string Question;
        public int TopK = 8;
        public float MinimumScore = 0.35f;
        public string DocumentId;
    }

    public sealed class AskRequest
    {
        public string Question;
        public int TopK = 8;
        public float MinimumScore = 0.35f;
        public string DocumentId;
        public int MaximumEvidence = 5;
    }

    public sealed class SearchHit
    {
        public string ChunkId;
        public string DocumentId;
        public string Title;
        public string Source;
        public string Heading;
        public string Text;
        public int Ordinal;
        public float Score;
    }

    public sealed class SearchResult
    {
        public IReadOnlyList<SearchHit> Hits = Array.Empty<SearchHit>();
    }

    public sealed class KnowledgeCitation
    {
        public string Id;
        public SearchHit Hit;
    }

    public sealed class AnswerDelta
    {
        public string Text;
    }

    public sealed class AnswerResult
    {
        public string Text;
        public bool InsufficientEvidence;
        public IReadOnlyList<KnowledgeCitation> Citations = Array.Empty<KnowledgeCitation>();
        public IReadOnlyList<KnowledgeCitation> RetrievedEvidence = Array.Empty<KnowledgeCitation>();
        public long ElapsedMilliseconds;
        public long RetrievalMilliseconds;
        public long GenerationMilliseconds;
        public long FirstTokenMilliseconds = -1;
        public int PromptTokens;
    }

    [Serializable]
    public sealed class KnowledgeExport
    {
        public int schemaVersion = 1;
        public string databaseFile;
        public string databaseSha256;
        public string indexFingerprint;
        public string embeddingFingerprint;
        public int dimensions;
        public int chunkTokens;
        public int overlapTokens;
        public int documentCount;
        public string createdUtc;
    }

    internal sealed class StoredChunk
    {
        public string Id;
        public string DocumentId;
        public int Ordinal;
        public string Heading;
        public string Text;
        public float[] Vector;
    }
}
