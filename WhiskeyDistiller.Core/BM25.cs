using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WhiskeyDistiller.Core
{
    public class BM25
    {
        private readonly List<CodeChunk> _chunks;
        private readonly double _avgDocLength;
        private readonly int _numDocs;
        
        // Parameter configuration
        private const double K1 = 1.2;
        private const double B = 0.75;

        // In-memory indexing structures
        private readonly Dictionary<string, int> _docFrequencies = new(); // Term -> number of docs containing term
        private readonly List<Dictionary<string, int>> _termFrequencies = new(); // Index of doc -> term frequencies
        private readonly List<int> _docLengths = new(); // Index of doc -> doc length in tokens

        public BM25(List<CodeChunk> chunks)
        {
            _chunks = chunks;
            _numDocs = chunks.Count;

            if (_numDocs == 0)
            {
                _avgDocLength = 0;
                return;
            }

            double totalLength = 0;
            for (int i = 0; i < _numDocs; i++)
            {
                var chunk = chunks[i];
                var tf = new Dictionary<string, int>();
                
                foreach (var token in chunk.Tokens)
                {
                    tf[token] = tf.TryGetValue(token, out int val) ? val + 1 : 1;
                }

                _termFrequencies.Add(tf);
                _docLengths.Add(chunk.Tokens.Count);
                totalLength += chunk.Tokens.Count;

                // Update document frequency
                foreach (var term in tf.Keys)
                {
                    _docFrequencies[term] = _docFrequencies.TryGetValue(term, out int df) ? df + 1 : 1;
                }
            }

            _avgDocLength = totalLength / _numDocs;
        }

        public List<(CodeChunk Chunk, double Score)> Search(List<string> queryTokens, int topK)
        {
            var results = new List<(CodeChunk Chunk, double Score)>();
            if (_numDocs == 0 || queryTokens.Count == 0) return results;

            // Score each chunk
            for (int i = 0; i < _numDocs; i++)
            {
                double score = 0;
                var tf = _termFrequencies[i];
                int docLen = _docLengths[i];

                foreach (var qToken in queryTokens)
                {
                    if (!_docFrequencies.TryGetValue(qToken, out int df)) continue; // Term not in vocabulary

                    // Calculate TF for this document
                    tf.TryGetValue(qToken, out int f);
                    if (f == 0) continue;

                    // Calculate IDF
                    double idf = Math.Log(1 + (_numDocs - df + 0.5) / (df + 0.5));

                    // Calculate term score
                    double termScore = idf * (f * (K1 + 1)) / (f + K1 * (1 - B + B * (docLen / _avgDocLength)));
                    score += termScore;
                }

                // Definition Boost: Boost the score if this chunk contains a definition of a search term (e.g. class or method declaration)
                if (score > 0)
                {
                    double boost = CalculateDefinitionBoost(_chunks[i], queryTokens);
                    score *= boost;
                }

                if (score > 0)
                {
                    results.Add((_chunks[i], score));
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();
        }

        private double CalculateDefinitionBoost(CodeChunk chunk, List<string> queryTokens)
        {
            double boost = 1.0;
            var content = chunk.Content;

            // Check if chunk contains definition keywords matching query terms
            // Simple heuristics: "class MyTerm", "void MyTerm", "def MyTerm", "function MyTerm"
            foreach (var token in queryTokens)
            {
                // Regex check for definition of the token in the code
                // Example: matching class, interface, method signature
                string patternClass = $@"\b(class|interface|struct|record)\b\s+\b{token}\b";
                string patternMethod = $@"\b(void|string|int|async|Task|def|fn|function)\b\s+\b{token}\b";

                if (Regex.IsMatch(content, patternClass, RegexOptions.IgnoreCase))
                {
                    boost += 0.5; // 50% boost for class definition
                }
                else if (Regex.IsMatch(content, patternMethod, RegexOptions.IgnoreCase))
                {
                    boost += 0.3; // 30% boost for method definition
                }
            }

            return boost;
        }
    }
}
