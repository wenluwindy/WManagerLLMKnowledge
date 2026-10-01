using System;
using System.Collections.Generic;
using System.Threading;

namespace WManager.Knowledge
{
    internal sealed class TokenTextSplitter
    {
        private readonly Func<string, int> countTokens;
        private readonly int chunkTokens;
        private readonly int overlapTokens;

        public TokenTextSplitter(Func<string, int> countTokens, int chunkTokens, int overlapTokens)
        {
            this.countTokens = countTokens ?? throw new ArgumentNullException(nameof(countTokens));
            if (chunkTokens < 8) throw new ArgumentOutOfRangeException(nameof(chunkTokens));
            if (overlapTokens < 0 || overlapTokens >= chunkTokens) throw new ArgumentOutOfRangeException(nameof(overlapTokens));
            this.chunkTokens = chunkTokens;
            this.overlapTokens = overlapTokens;
        }

        public List<StoredChunk> Split(string documentId, string text, CancellationToken ct)
        {
            var result = new List<StoredChunk>();
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            int start = 0;
            string heading = string.Empty;
            while (start < text.Length)
            {
                ct.ThrowIfCancellationRequested();
                int length = FindPrefix(text, start, Math.Min(text.Length - start, chunkTokens * 16), chunkTokens);
                if (length == 0) throw new InvalidOperationException("The token budget cannot fit a text character. Increase ChunkTokens.");
                int end = start + length;
                if (end < text.Length)
                {
                    int paragraph = text.LastIndexOf("\n\n", end - 1, length, StringComparison.Ordinal);
                    if (paragraph > start + length / 2) end = paragraph + 2;
                }
                string body = text.Substring(start, end - start).Trim();
                foreach (string line in body.Split('\n'))
                {
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        string candidate = line.TrimStart('#', ' ', '\t');
                        if (candidate.Length > 0) heading = candidate;
                        break;
                    }
                }
                if (body.Length > 0)
                {
                    int ordinal = result.Count;
                    result.Add(new StoredChunk
                    {
                        Id = KnowledgeHash.Text(documentId + ":" + ordinal), DocumentId = documentId,
                        Ordinal = ordinal, Heading = heading, Text = body
                    });
                }
                if (end == text.Length) break;
                int overlap = 0;
                if (overlapTokens > 0)
                {
                    int low = 0;
                    int high = end - start - 1;
                    while (low < high)
                    {
                        int mid = low + (high - low + 1) / 2;
                        int tailStart = SafeBoundary(text, end - mid);
                        if (countTokens(text.Substring(tailStart, end - tailStart)) <= overlapTokens) low = mid;
                        else high = mid - 1;
                    }
                    overlap = low;
                }
                start = SafeBoundary(text, Math.Max(start + 1, end - overlap));
                if (start <= end - length) start = end;
            }
            return result;
        }

        private int FindPrefix(string text, int start, int limit, int budget)
        {
            int low = 0;
            int high = limit;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                int end = SafeBoundary(text, start + mid);
                if (countTokens(text.Substring(start, end - start)) <= budget) low = mid;
                else high = mid - 1;
            }
            int length = SafeBoundary(text, start + low) - start;
            // Token counts are not strictly monotonic, so validate the chosen prefix.
            while (length > 0 && countTokens(text.Substring(start, length)) > budget)
                length = SafeBoundary(text, start + length - 1) - start;
            return length;
        }

        private static int SafeBoundary(string text, int index)
        {
            if (index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
                return index - 1;
            return index;
        }
    }
}
