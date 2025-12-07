using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SafeWalkers.WakeWord
{
    /// <summary>
    /// Provides fuzzy string matching algorithms for wake word detection.
    /// Supports multiple matching strategies including Levenshtein distance and normalized similarity.
    /// </summary>
    public static class FuzzyMatcher
    {
        /// <summary>
        /// Calculates the Levenshtein distance between two strings.
        /// The Levenshtein distance is the minimum number of single-character edits
        /// (insertions, deletions, or substitutions) required to change one string into the other.
        /// </summary>
        /// <param name="a">First string</param>
        /// <param name="b">Second string</param>
        /// <returns>Edit distance between the strings</returns>
        public static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
                return string.IsNullOrEmpty(b) ? 0 : b.Length;
            if (string.IsNullOrEmpty(b))
                return a.Length;

            int[,] d = new int[a.Length + 1, b.Length + 1];

            // Initialize first column and row
            for (int i = 0; i <= a.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++)
                d[0, j] = j;

            // Calculate distances
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;

                    d[i, j] = Mathf.Min(
                        Mathf.Min(
                            d[i - 1, j] + 1,      // Deletion
                            d[i, j - 1] + 1),      // Insertion
                        d[i - 1, j - 1] + cost);   // Substitution
                }
            }

            return d[a.Length, b.Length];
        }

        /// <summary>
        /// Calculates normalized similarity between two strings based on Levenshtein distance.
        /// Returns a value between 0.0 (completely different) and 1.0 (identical).
        /// </summary>
        /// <param name="a">First string</param>
        /// <param name="b">Second string</param>
        /// <returns>Similarity score (0.0 to 1.0)</returns>
        public static float NormalizedSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
                return 1.0f;

            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0.0f;

            int distance = LevenshteinDistance(a, b);
            int maxLen = Mathf.Max(a.Length, b.Length);

            if (maxLen == 0)
                return 1.0f;

            return 1.0f - ((float)distance / maxLen);
        }

        /// <summary>
        /// Performs fuzzy matching between detected text and reference wake word.
        /// Uses multiple matching strategies for robust detection.
        /// </summary>
        /// <param name="detected">Detected/transcribed text</param>
        /// <param name="reference">Reference wake word to match against</param>
        /// <param name="threshold">Similarity threshold (0.0 to 1.0)</param>
        /// <param name="caseSensitive">Whether to perform case-sensitive matching</param>
        /// <param name="maxEditDistance">Maximum allowed edit distance</param>
        /// <returns>True if the strings match within tolerance</returns>
        public static bool FuzzyMatch(
            string detected,
            string reference,
            float threshold = 0.8f,
            bool caseSensitive = false,
            int maxEditDistance = 2)
        {
            if (string.IsNullOrEmpty(detected) || string.IsNullOrEmpty(reference))
                return false;

            // Normalize text
            string normalizedDetected = NormalizeText(detected, caseSensitive);
            string normalizedReference = NormalizeText(reference, caseSensitive);

            // Strategy 1: Exact match (fastest)
            if (normalizedDetected == normalizedReference)
                return true;

            // Strategy 2: Contains check (for partial matches)
            if (normalizedDetected.Contains(normalizedReference) || normalizedReference.Contains(normalizedDetected))
                return true;

            // Strategy 3: Levenshtein distance threshold
            int distance = LevenshteinDistance(normalizedDetected, normalizedReference);
            if (distance <= maxEditDistance)
                return true;

            // Strategy 4: Normalized similarity threshold
            float similarity = NormalizedSimilarity(normalizedDetected, normalizedReference);
            if (similarity >= threshold)
                return true;

            // Strategy 5: Word-by-word matching (handles word order variations)
            string[] detectedWords = normalizedDetected.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] referenceWords = normalizedReference.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (detectedWords.Length > 0 && referenceWords.Length > 0)
            {
                int matchedWords = 0;

                foreach (string refWord in referenceWords)
                {
                    foreach (string detWord in detectedWords)
                    {
                        float wordSimilarity = NormalizedSimilarity(detWord, refWord);
                        if (wordSimilarity >= 0.85f) // Stricter threshold for individual words
                        {
                            matchedWords++;
                            break;
                        }
                    }
                }

                float wordMatchRatio = (float)matchedWords / referenceWords.Length;
                if (wordMatchRatio >= threshold)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Normalizes text for consistent matching.
        /// Removes punctuation, extra whitespace, and optionally converts to lowercase.
        /// </summary>
        /// <param name="text">Text to normalize</param>
        /// <param name="caseSensitive">Whether to preserve case</param>
        /// <returns>Normalized text</returns>
        public static string NormalizeText(string text, bool caseSensitive = false)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Convert to lowercase unless case sensitive
            if (!caseSensitive)
                text = text.ToLowerInvariant();

            // Remove punctuation
            text = Regex.Replace(text, @"[^\w\s]", "");

            // Normalize whitespace (multiple spaces to single space)
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        /// <summary>
        /// Calculates detailed match information for debugging purposes.
        /// </summary>
        /// <param name="detected">Detected text</param>
        /// <param name="reference">Reference text</param>
        /// <param name="caseSensitive">Whether to perform case-sensitive matching</param>
        /// <returns>Detailed match information</returns>
        public static MatchResult CalculateMatchDetails(string detected, string reference, bool caseSensitive = false)
        {
            if (string.IsNullOrEmpty(detected) || string.IsNullOrEmpty(reference))
            {
                return new MatchResult
                {
                    detected = detected,
                    reference = reference,
                    isExactMatch = false,
                    editDistance = int.MaxValue,
                    similarity = 0f,
                    normalizedDetected = string.Empty,
                    normalizedReference = string.Empty
                };
            }

            string normDetected = NormalizeText(detected, caseSensitive);
            string normReference = NormalizeText(reference, caseSensitive);

            int distance = LevenshteinDistance(normDetected, normReference);
            float similarity = NormalizedSimilarity(normDetected, normReference);

            return new MatchResult
            {
                detected = detected,
                reference = reference,
                normalizedDetected = normDetected,
                normalizedReference = normReference,
                isExactMatch = normDetected == normReference,
                editDistance = distance,
                similarity = similarity
            };
        }

        /// <summary>
        /// Structure containing detailed match information.
        /// </summary>
        public struct MatchResult
        {
            public string detected;
            public string reference;
            public string normalizedDetected;
            public string normalizedReference;
            public bool isExactMatch;
            public int editDistance;
            public float similarity;

            public override string ToString()
            {
                return $"Match Details:\n" +
                       $"  Detected: '{detected}' -> '{normalizedDetected}'\n" +
                       $"  Reference: '{reference}' -> '{normalizedReference}'\n" +
                       $"  Exact Match: {isExactMatch}\n" +
                       $"  Edit Distance: {editDistance}\n" +
                       $"  Similarity: {similarity:F2}";
            }
        }
    }
}
