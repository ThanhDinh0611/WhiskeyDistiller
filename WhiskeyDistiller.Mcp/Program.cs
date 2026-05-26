using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MediatR;
using WhiskeyDistiller.Core;
using ModelContextProtocol.Server;

namespace WhiskeyDistiller.Mcp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--benchmark")
            {
                await RunBenchmarkAsync();
                return;
            }

            if (args.Length > 1 && args[0] == "--search")
            {
                string query = args[1];
                string? customPath = args.Length > 2 ? args[2] : null;
                await RunCliSearchAsync(query, customPath);
                return;
            }

            // Configure host
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Register MediatR for Core assembly
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(WhiskeySearchIndex).Assembly));

                    // Register core search index as a Singleton
                    var index = new WhiskeySearchIndex();
                    services.AddSingleton<IWhiskeySearchIndex>(index);

                    // Add MCP Server and configure it to run over stdio
                    services.AddMcpServer()
                        .WithStdioServerTransport()
                        .WithToolsFromAssembly();
                })
                .Build();

            // Run manual indexing on startup
            var searchIndex = host.Services.GetRequiredService<IWhiskeySearchIndex>();
            string workspacePath = Directory.GetCurrentDirectory();
            
            // Log to stderr to avoid messing up stdio JSON-RPC transport protocol on stdout!
            Console.Error.WriteLine($"Initializing WhiskeyDistiller MCP Server for workspace: {workspacePath}");
            try
            {
                await searchIndex.IndexWorkspaceAsync(workspacePath);
                Console.Error.WriteLine($"Indexing complete! Chunk count: {searchIndex.ChunkCount}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during startup indexing: {ex.Message}");
            }

            // Start the host
            await host.RunAsync();
        }

        static async Task RunBenchmarkAsync()
        {
            Console.WriteLine("====================================================");
            Console.WriteLine("        WHISKEYDISTILLER PERFORMANCE BENCHMARK       ");
            Console.WriteLine("====================================================");

            string workspacePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
            Console.WriteLine($"Workspace Path: {workspacePath}");

            var searchIndex = new WhiskeySearchIndex();

            // Measure Indexing Speed
            var stopwatch = Stopwatch.StartNew();
            Console.WriteLine("Indexing workspace...");
            await searchIndex.IndexWorkspaceAsync(workspacePath);
            stopwatch.Stop();
            long indexingTimeMs = stopwatch.ElapsedMilliseconds;
            Console.WriteLine($"Workspace indexed in: {indexingTimeMs}ms (Total chunks: {searchIndex.ChunkCount})");

            if (searchIndex.ChunkCount == 0)
            {
                Console.WriteLine("No code chunks found to query. Benchmark aborted.");
                return;
            }

            // Test Query
            string query = "Program Main assembly reflection builder";
            Console.WriteLine($"\nRunning Test Query: \"{query}\"");

            // Measure Search Latency
            stopwatch.Restart();
            var results = await searchIndex.SearchAsync(query, topK: 3);
            stopwatch.Stop();
            long searchLatencyMs = stopwatch.ElapsedMilliseconds;

            Console.WriteLine($"Search Latency: {searchLatencyMs}ms\n");

            if (results.Count == 0)
            {
                Console.WriteLine("No search results returned. Unable to compute token savings.");
                return;
            }

            // Compute Token Savings
            int totalChunkTokens = 0;
            int totalFullFileTokens = 0;
            var processedFiles = new HashSet<string>();

            Console.WriteLine("--- Retrieval Details ---");
            for (int i = 0; i < results.Count; i++)
            {
                var res = results[i];
                int chunkTokens = Tokenizer.Tokenize(res.Content).Count;
                totalChunkTokens += chunkTokens;

                Console.WriteLine($"Match {i + 1}: {res.FilePath} (Lines {res.StartLine}-{res.EndLine}) -> Chunk Tokens: {chunkTokens}");

                // Read full file to count naive tokens
                string fullFilePath = Path.Combine(workspacePath, res.FilePath);
                if (File.Exists(fullFilePath) && !processedFiles.Contains(res.FilePath))
                {
                    processedFiles.Add(res.FilePath);
                    string fileContent = File.ReadAllText(fullFilePath);
                    int fullFileTokens = Tokenizer.Tokenize(fileContent).Count;
                    totalFullFileTokens += fullFileTokens;
                }
            }

            double savingsPercent = (1.0 - (double)totalChunkTokens / totalFullFileTokens) * 100;

            Console.WriteLine("\n====================================================");
            Console.WriteLine("                  BENCHMARK SUMMARY                 ");
            Console.WriteLine("====================================================");
            Console.WriteLine($"Indexing Time  : {indexingTimeMs}ms");
            Console.WriteLine($"Query Latency  : {searchLatencyMs}ms");
            Console.WriteLine($"Unique Files   : {processedFiles.Count}");
            Console.WriteLine($"Grep + Read    : {totalFullFileTokens} tokens (Reading full files)");
            Console.WriteLine($"Distilled Chunks: {totalChunkTokens} tokens (WhiskeyDistiller)");
            Console.WriteLine($"Token Savings  : {savingsPercent:F2}%");
            Console.WriteLine("====================================================");
        }

        static async Task RunCliSearchAsync(string query, string? customPath)
        {
            string workspacePath = customPath != null 
                ? Path.GetFullPath(customPath) 
                : Directory.GetCurrentDirectory();

            var searchIndex = new WhiskeySearchIndex();
            
            // Index quietly
            await searchIndex.IndexWorkspaceAsync(workspacePath);

            var results = await searchIndex.SearchAsync(query, topK: 5);
            if (results.Count == 0)
            {
                Console.WriteLine("No matching code chunks found.");
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                var res = results[i];
                Console.WriteLine($"=== Match {i + 1}: {res.FilePath} (Lines {res.StartLine}-{res.EndLine}) [Score: {res.Score:F4}] ===");
                Console.WriteLine(res.Content);
                Console.WriteLine("================================================================================\n");
            }
        }
    }

    public class WhiskeyTools
    {
        private readonly IMediator _mediator;

        public WhiskeyTools(IMediator mediator)
        {
            _mediator = mediator;
        }

        [McpServerTool(Name = "search_whiskey_code")]
        [System.ComponentModel.Description("Search the codebase for relevant snippets using hybrid (ONNX semantic + BM25) search.")]
        public async Task<string> SearchWhiskeyCode(
            [System.ComponentModel.Description("The natural language search query or symbol/variable/method name.")] string query,
            [System.ComponentModel.Description("Number of relevant code chunks to return.")] int topK = 5)
        {
            try
            {
                var response = await _mediator.Send(new SearchQuery { Query = query, TopK = topK });
                if (response.Results.Count == 0)
                {
                    return "No matching code chunks found in the workspace.";
                }

                var outputParts = new List<string>();
                for (int i = 0; i < response.Results.Count; i++)
                {
                    var res = response.Results[i];
                    string header = $"### Match {i + 1}: {res.FilePath} (Lines {res.StartLine}-{res.EndLine}) [Score: {res.Score:F4}]";
                    string codeBlock = $"```\n{res.Content}\n```";
                    outputParts.Add($"{header}\n{codeBlock}");
                }

                return string.Join("\n\n", outputParts);
            }
            catch (Exception ex)
            {
                return $"Error executing code search: {ex.Message}";
            }
        }
    }
}
