using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace WhiskeyDistiller.Core
{
    #region Models
    
    public class SearchResult
    {
        public string FilePath { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
    }

    #endregion

    #region Search Index Service Interface and Implementation

    public interface IWhiskeySearchIndex
    {
        bool IsIndexed { get; }
        int ChunkCount { get; }
        string WorkspacePath { get; }
        Task IndexWorkspaceAsync(string workspacePath);
        Task<List<SearchResult>> SearchAsync(string query, int topK);
    }

    public class WhiskeySearchIndex : IWhiskeySearchIndex, IDisposable
    {
        private List<CodeChunk> _chunks = new();
        private BM25? _bm25;
        private OnnxEmbedder? _embedder;
        private List<float[]>? _chunkEmbeddings;
        private readonly string _modelDir;

        public bool IsIndexed => _bm25 != null;
        public int ChunkCount => _chunks.Count;
        public string WorkspacePath { get; private set; } = string.Empty;

        public WhiskeySearchIndex(string? modelDir = null)
        {
            _modelDir = modelDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model");
        }

        public async Task IndexWorkspaceAsync(string workspacePath)
        {
            WorkspacePath = workspacePath;
            
            // 1. Chunk workspace
            _chunks = Chunker.ChunkWorkspace(workspacePath);

            if (_chunks.Count == 0)
            {
                _bm25 = new BM25(_chunks);
                _chunkEmbeddings = new List<float[]>();
                return;
            }

            // 2. Build BM25 index
            _bm25 = new BM25(_chunks);

            // 3. Initialize ONNX embedder
            if (_embedder == null)
            {
                _embedder = new OnnxEmbedder(_modelDir);
                await _embedder.InitializeAsync();
            }

            // 4. Generate embeddings in parallel (InferenceSession is thread-safe)
            var embeddingsArray = new float[_chunks.Count][];
            Parallel.For(0, _chunks.Count, i =>
            {
                embeddingsArray[i] = _embedder.GenerateEmbedding(_chunks[i].Content);
            });

            _chunkEmbeddings = embeddingsArray.ToList();
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int topK)
        {
            if (_bm25 == null || _embedder == null || _chunkEmbeddings == null || _chunks.Count == 0)
            {
                return new List<SearchResult>();
            }

            // 1. Tokenize query for BM25
            var queryTokens = Tokenizer.Tokenize(query);
            
            // 2. Run BM25 search (request extra candidates for fusion)
            var bm25Results = _bm25.Search(queryTokens, topK * 3);

            // 3. Run Semantic search
            var queryEmbedding = _embedder.GenerateEmbedding(query);
            var semanticResults = new List<(CodeChunk Chunk, double Score)>();

            for (int i = 0; i < _chunks.Count; i++)
            {
                float sim = OnnxEmbedder.ComputeCosineSimilarity(queryEmbedding, _chunkEmbeddings[i]);
                if (sim > 0.1) // Minimum similarity threshold
                {
                    semanticResults.Add((_chunks[i], sim));
                }
            }

            semanticResults = semanticResults
                .OrderByDescending(r => r.Score)
                .Take(topK * 3)
                .ToList();

            // 4. Reciprocal Rank Fusion (RRF)
            var fused = RrfRanker.Fuse(bm25Results, semanticResults, topK);

            // 5. Map to output model
            return fused.Select(f => new SearchResult
            {
                FilePath = f.Chunk.FilePath,
                StartLine = f.Chunk.StartLine,
                EndLine = f.Chunk.EndLine,
                Content = f.Chunk.Content,
                Score = f.Score
            }).ToList();
        }

        public void Dispose()
        {
            _embedder?.Dispose();
            _embedder = null;
        }
    }

    #endregion

    #region MediatR Requests and Handlers

    // Index Command
    public class IndexCommand : IRequest<IndexResponse>
    {
        public string WorkspacePath { get; set; } = string.Empty;
    }

    public class IndexResponse
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int ChunkCount { get; set; }
    }

    public class IndexCommandHandler : IRequestHandler<IndexCommand, IndexResponse>
    {
        private readonly IWhiskeySearchIndex _searchIndex;

        public IndexCommandHandler(IWhiskeySearchIndex searchIndex)
        {
            _searchIndex = searchIndex;
        }

        public async Task<IndexResponse> Handle(IndexCommand request, CancellationToken cancellationToken)
        {
            try
            {
                await _searchIndex.IndexWorkspaceAsync(request.WorkspacePath);
                return new IndexResponse
                {
                    Status = "success",
                    Message = $"Indexed successfully. Total chunks: {_searchIndex.ChunkCount}",
                    ChunkCount = _searchIndex.ChunkCount
                };
            }
            catch (Exception ex)
            {
                return new IndexResponse
                {
                    Status = "error",
                    Message = ex.Message,
                    ChunkCount = 0
                };
            }
        }
    }

    // Search Query
    public class SearchQuery : IRequest<SearchResponse>
    {
        public string Query { get; set; } = string.Empty;
        public int TopK { get; set; } = 5;
    }

    public class SearchResponse
    {
        public string Query { get; set; } = string.Empty;
        public List<SearchResult> Results { get; set; } = new();
    }

    public class SearchQueryHandler : IRequestHandler<SearchQuery, SearchResponse>
    {
        private readonly IWhiskeySearchIndex _searchIndex;

        public SearchQueryHandler(IWhiskeySearchIndex searchIndex)
        {
            _searchIndex = searchIndex;
        }

        public async Task<SearchResponse> Handle(SearchQuery request, CancellationToken cancellationToken)
        {
            var results = await _searchIndex.SearchAsync(request.Query, request.TopK);
            return new SearchResponse
            {
                Query = request.Query,
                Results = results
            };
        }
    }

    #endregion
}
