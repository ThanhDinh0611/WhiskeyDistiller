using System;
using System.Collections.Generic;
using System.Linq;

namespace WhiskeyDistiller.Core
{
    public static class RrfRanker
    {
        private const int K = 60; // Standard RRF parameter

        public class FusedResult
        {
            public CodeChunk Chunk { get; set; } = null!;
            public double Score { get; set; }
            public int Bm25Rank { get; set; } = -1;
            public int SemanticRank { get; set; } = -1;
        }

        public static List<FusedResult> Fuse(
            List<(CodeChunk Chunk, double Score)> bm25Results,
            List<(CodeChunk Chunk, double Score)> semanticResults,
            int topK)
        {
            var fusedScores = new Dictionary<CodeChunk, FusedResult>();

            // 1. Process BM25 ranks
            for (int rank = 0; rank < bm25Results.Count; rank++)
            {
                var item = bm25Results[rank];
                if (!fusedScores.TryGetValue(item.Chunk, out var result))
                {
                    result = new FusedResult { Chunk = item.Chunk };
                    fusedScores[item.Chunk] = result;
                }
                result.Bm25Rank = rank + 1;
                result.Score += 1.0 / (K + rank + 1);
            }

            // 2. Process Semantic ranks
            for (int rank = 0; rank < semanticResults.Count; rank++)
            {
                var item = semanticResults[rank];
                if (!fusedScores.TryGetValue(item.Chunk, out var result))
                {
                    result = new FusedResult { Chunk = item.Chunk };
                    fusedScores[item.Chunk] = result;
                }
                result.SemanticRank = rank + 1;
                result.Score += 1.0 / (K + rank + 1);
            }

            // 3. Return topK results sorted by score descending
            return fusedScores.Values
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();
        }
    }
}
