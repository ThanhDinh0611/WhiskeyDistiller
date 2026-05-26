using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WhiskeyDistiller.Core
{
    public static class Tokenizer
    {
        private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Programming language keywords (C#, TS, Python, JS, etc.)
            "public", "private", "protected", "internal", "class", "interface", "struct", "void",
            "string", "int", "double", "float", "bool", "object", "var", "return", "if", "else",
            "for", "foreach", "while", "do", "switch", "case", "default", "break", "continue",
            "using", "namespace", "import", "from", "function", "def", "async", "await",
            "try", "catch", "finally", "throw", "new", "this", "null", "true", "false",
            // Common English stopwords
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "with", "by",
            "of", "is", "are", "was", "were", "be", "been", "being", "have", "has", "had",
            "do", "does", "did", "as", "if", "that", "this", "these", "those"
        };

        private static readonly Regex AlphanumericRegex = new(@"[a-zA-Z0-9]+", RegexOptions.Compiled);
        private static readonly Regex CamelCaseRegex = new(@"([a-z0-9])([A-Z])", RegexOptions.Compiled);

        public static List<string> Tokenize(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            // Find all alphanumeric words
            var matches = AlphanumericRegex.Matches(text);
            foreach (Match match in matches)
            {
                var originalWord = match.Value;

                // 1. Process the word as-is (lowercased)
                var lowerWord = originalWord.ToLower();
                if (!Stopwords.Contains(lowerWord) && lowerWord.Length > 1)
                {
                    result.Add(lowerWord);
                }

                // 2. Split camelCase identifiers
                var splitCamel = CamelCaseRegex.Replace(originalWord, "$1 $2");
                if (splitCamel != originalWord)
                {
                    var subTokens = splitCamel.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var subToken in subTokens)
                    {
                        var lowerSub = subToken.ToLower();
                        if (!Stopwords.Contains(lowerSub) && lowerSub.Length > 1 && lowerSub != lowerWord)
                        {
                            result.Add(lowerSub);
                        }
                    }
                }

                // 3. Split snake_case identifiers
                if (originalWord.Contains('_'))
                {
                    var subTokens = originalWord.Split('_', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var subToken in subTokens)
                    {
                        var lowerSub = subToken.ToLower();
                        if (!Stopwords.Contains(lowerSub) && lowerSub.Length > 1 && lowerSub != lowerWord)
                        {
                            result.Add(lowerSub);
                        }
                    }
                }
            }

            return result;
        }
    }
}
