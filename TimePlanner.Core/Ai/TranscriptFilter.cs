namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //filters low confidence segments and prompt echoes from transcription results
    public static class TranscriptFilter
    {
        private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
        {
            "and", "&", "the", "a", "an", "or", "to", "in", "on", "at", "for", "of", "with", "by"
        };

        //-----------------------------
        //cleans segments dropping silent segments and prompt echoes
        public static string Clean(IReadOnlyList<(string Text, float? NoSpeech)> segments, IReadOnlyList<string> promptWords)
        {
            //empty input returns empty string
            if (segments == null || segments.Count == 0)
            {
                return "";
            }

            var kept = new List<string>();

            //filter segments by no speech probability
            foreach (var (text, noSpeech) in segments)
            {
                //skip segments with high probability of silence
                if (noSpeech.HasValue && noSpeech.Value > 0.6f)
                {
                    continue;
                }

                var trimmed = text?.Trim();
                //add non empty trimmed text
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    kept.Add(trimmed);
                }
            }

            //no segments kept returns empty
            if (kept.Count == 0)
            {
                return "";
            }

            var joined = string.Join(" ", kept).Trim();
            //empty joined text returns empty
            if (string.IsNullOrWhiteSpace(joined))
            {
                return "";
            }

            var promptSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            //extract prompt words
            if (promptWords != null)
            {
                foreach (var pw in promptWords)
                {
                    //split each prompt entry into words
                    if (!string.IsNullOrWhiteSpace(pw))
                    {
                        var tokens = ExtractWords(pw);
                        //add individual words to set
                        foreach (var token in tokens)
                        {
                            promptSet.Add(token);
                        }
                    }
                }
            }

            var words = ExtractWords(joined);
            //if no words found return empty
            if (words.Count == 0)
            {
                return "";
            }

            //check if all words in result are known prompt words or connectors
            var allPromptOrConnectors = words.All(w => promptSet.Contains(w) || Connectors.Contains(w));
            //echo of prompt or connectors returns empty
            if (allPromptOrConnectors)
            {
                return "";
            }

            return joined;
        }

        //-----------------------------
        //extracts alphanumeric words from text
        private static List<string> ExtractWords(string text)
        {
            var words = new List<string>();
            var start = -1;

            //scan characters for word boundaries
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                //check if character is letter or digit
                if (char.IsLetterOrDigit(ch))
                {
                    //mark word start
                    if (start < 0)
                    {
                        start = i;
                    }
                }
                else if (start >= 0)
                {
                    words.Add(text[start..i]);
                    start = -1;
                }
            }

            //add trailing word
            if (start >= 0)
            {
                words.Add(text[start..]);
            }

            return words;
        }
    }
}
//------------------------------EOF-----------------------------\\
