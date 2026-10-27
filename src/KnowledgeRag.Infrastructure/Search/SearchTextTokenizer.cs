using System.Text;

namespace KnowledgeRag.Infrastructure.Search;

public static class SearchTextTokenizer
{
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var tokens = new List<string>();
        var word = new StringBuilder();
        var cjkRun = new List<char>();

        void FlushWord()
        {
            if (word.Length == 0) return;
            tokens.Add(word.ToString().ToLowerInvariant());
            word.Clear();
        }

        void FlushCjk()
        {
            if (cjkRun.Count == 0) return;
            for (var i = 0; i < cjkRun.Count; i++)
            {
                tokens.Add(cjkRun[i].ToString());
                if (i + 1 < cjkRun.Count) tokens.Add(new string(new[] { cjkRun[i], cjkRun[i + 1] }));
            }
            cjkRun.Clear();
        }

        foreach (var ch in text.Normalize(NormalizationForm.FormKC))
        {
            if (IsCjk(ch))
            {
                FlushWord();
                cjkRun.Add(ch);
            }
            else if (char.IsLetterOrDigit(ch))
            {
                FlushCjk();
                word.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                FlushWord();
                FlushCjk();
            }
        }

        FlushWord();
        FlushCjk();
        return tokens;
    }

    private static bool IsCjk(char c)
        => c is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';
}
