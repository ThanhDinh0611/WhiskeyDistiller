using System;
using System.Collections.Generic;
using System.IO;

namespace WhiskeyDistiller.Core
{
    public class CodeChunk
    {
        public string FilePath { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Content { get; set; } = string.Empty;
        public List<string> Tokens { get; set; } = new();
    }

    public static class Chunker
    {
        private const int ChunkSize = 30;
        private const int Overlap = 10;
        private const int Step = ChunkSize - Overlap; // 20

        private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "bin", "obj", "node_modules", "dist", "build", ".venv", "temp"
        };

        private static readonly HashSet<string> IncludedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".ts", ".js", ".py", ".json", ".md", ".html", ".css", ".txt", ".yml", ".yaml", ".xml", ".sh", ".ps1"
        };

        public static List<CodeChunk> ChunkWorkspace(string workspacePath)
        {
            var allChunks = new List<CodeChunk>();
            if (!Directory.Exists(workspacePath)) return allChunks;

            var files = Directory.GetFiles(workspacePath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                // Check if file is in excluded directory
                if (IsExcluded(file, workspacePath)) continue;

                // Check extension
                var ext = Path.GetExtension(file);
                if (!IncludedExtensions.Contains(ext)) continue;

                try
                {
                    var relativePath = Path.GetRelativePath(workspacePath, file).Replace('\\', '/');
                    var content = File.ReadAllText(file);
                    var chunks = ChunkFile(relativePath, content);
                    allChunks.AddRange(chunks);
                }
                catch (Exception ex)
                {
                    // Log error and continue
                    Console.Error.WriteLine($"Error chunking file {file}: {ex.Message}");
                }
            }

            return allChunks;
        }

        private static bool IsExcluded(string filePath, string workspacePath)
        {
            var relativePath = Path.GetRelativePath(workspacePath, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            foreach (var part in parts)
            {
                if (ExcludedDirs.Contains(part)) return true;
            }
            return false;
        }

        public static List<CodeChunk> ChunkFile(string relativePath, string fileContent)
        {
            var chunks = new List<CodeChunk>();
            if (string.IsNullOrWhiteSpace(fileContent)) return chunks;

            // Split into lines
            var lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int lineCount = lines.Length;

            if (lineCount <= ChunkSize)
            {
                // Create a single chunk
                var chunkContent = string.Join("\n", lines);
                chunks.Add(new CodeChunk
                {
                    FilePath = relativePath,
                    StartLine = 1,
                    EndLine = lineCount,
                    Content = chunkContent,
                    Tokens = Tokenizer.Tokenize(chunkContent)
                });
                return chunks;
            }

            for (int start = 0; start < lineCount; start += Step)
            {
                int end = Math.Min(start + ChunkSize, lineCount);
                var chunkLines = new List<string>();
                for (int i = start; i < end; i++)
                {
                    chunkLines.Add(lines[i]);
                }

                var chunkContent = string.Join("\n", chunkLines);
                chunks.Add(new CodeChunk
                {
                    FilePath = relativePath,
                    StartLine = start + 1,
                    EndLine = end,
                    Content = chunkContent,
                    Tokens = Tokenizer.Tokenize(chunkContent)
                });

                // If we reached the end of the file, stop
                if (end == lineCount) break;
            }

            return chunks;
        }
    }
}
