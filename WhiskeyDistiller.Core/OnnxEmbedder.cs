using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace WhiskeyDistiller.Core
{
    public class OnnxEmbedder : IDisposable
    {
        private InferenceSession? _session;
        private Microsoft.ML.Tokenizers.Tokenizer? _tokenizer;
        private readonly string _modelDir;
        private readonly string _modelPath;
        private readonly string _vocabPath;

        private const string ModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
        private const string VocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";

        public OnnxEmbedder(string? customModelDir = null)
        {
            // Default to app base directory if no directory provided
            _modelDir = customModelDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model");
            _modelPath = Path.Combine(_modelDir, "model.onnx");
            _vocabPath = Path.Combine(_modelDir, "vocab.txt");
        }

        public async Task InitializeAsync()
        {
            if (_session != null) return;

            // Ensure directory exists
            Directory.CreateDirectory(_modelDir);

            // Download model and vocab if they do not exist
            await EnsureFileDownloadedAsync(ModelUrl, _modelPath, "ONNX Model");
            await EnsureFileDownloadedAsync(VocabUrl, _vocabPath, "Vocabulary File");

            // Load ONNX session
            _session = new InferenceSession(_modelPath);

            // Load WordPiece tokenizer
            _tokenizer = WordPieceTokenizer.Create(_vocabPath);
        }

        private async Task EnsureFileDownloadedAsync(string url, string path, string name)
        {
            if (File.Exists(path))
            {
                // Check if the file is not empty/corrupted
                var info = new FileInfo(path);
                if (info.Length > 1024) return; // File exists and is not empty
            }

            Console.WriteLine($"Downloading {name} from {url} to {path}...");
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5); // Give enough time for model download

            try
            {
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream);
                Console.WriteLine($"Successfully downloaded {name}.");
            }
            catch (Exception ex)
            {
                if (File.Exists(path)) File.Delete(path); // Clean up partial download
                throw new Exception($"Failed to download required search model asset {name}: {ex.Message}", ex);
            }
        }

        public float[] GenerateEmbedding(string text)
        {
            if (_session == null || _tokenizer == null)
            {
                throw new InvalidOperationException("Embedder has not been initialized. Call InitializeAsync() first.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new float[384]; // Model outputs 384 dimensions
            }

            // WordPiece tokenization
            var ids = _tokenizer.EncodeToIds(text);

            // Prepare BERT inputs: [CLS] + tokens + [SEP]
            // BERT vocabulary: [CLS] = 101, [SEP] = 102. Max sequence length is 512.
            var inputIds = new List<long> { 101 };
            var truncatedIds = ids.Take(510);
            foreach (var id in truncatedIds) inputIds.Add(id);
            inputIds.Add(102);

            int seqLen = inputIds.Count;
            var attentionMask = Enumerable.Repeat(1L, seqLen).ToList();
            var tokenTypeIds = Enumerable.Repeat(0L, seqLen).ToList();

            // Run ONNX inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds.ToArray(), new[] { 1, seqLen })),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask.ToArray(), new[] { 1, seqLen })),
                NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds.ToArray(), new[] { 1, seqLen }))
            };

            using var results = _session.Run(inputs);
            
            // Output "last_hidden_state" tensor is of shape [batch_size, sequence_length, hidden_size]
            var outputValue = results.FirstOrDefault(r => r.Name == "last_hidden_state");
            if (outputValue == null)
            {
                throw new Exception("ONNX model output 'last_hidden_state' not found.");
            }

            var outputTensor = outputValue.AsTensor<float>();
            int hiddenSize = 384; // hidden size for all-MiniLM-L6-v2

            // Mean Pooling: average the token embeddings
            float[] meanVector = new float[hiddenSize];
            for (int i = 0; i < seqLen; i++)
            {
                for (int d = 0; d < hiddenSize; d++)
                {
                    // DenseTensor indices for shape [1, seqLen, hiddenSize]
                    meanVector[d] += outputTensor[0, i, d];
                }
            }

            for (int d = 0; d < hiddenSize; d++)
            {
                meanVector[d] /= seqLen;
            }

            // L2 Normalization
            double sumSq = 0;
            for (int d = 0; d < hiddenSize; d++)
            {
                sumSq += meanVector[d] * meanVector[d];
            }

            double norm = Math.Sqrt(sumSq);
            if (norm > 0)
            {
                for (int d = 0; d < hiddenSize; d++)
                {
                    meanVector[d] = (float)(meanVector[d] / norm);
                }
            }

            return meanVector;
        }

        public static float ComputeCosineSimilarity(float[] vectorA, float[] vectorB)
        {
            if (vectorA.Length != vectorB.Length) return 0;
            // Since vectors are normalized, cosine similarity is just the dot product
            float dotProduct = 0;
            for (int i = 0; i < vectorA.Length; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
            }
            return dotProduct;
        }

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
            GC.SuppressFinalize(this);
        }
    }
}
