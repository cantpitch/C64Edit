using C64Edit.Rendering;
using C64Edit.Text;

namespace C64Edit.Syntax;

/// <summary>
/// Tokenizes a document line by line. The only state carried between lines is a small integer
/// (inside a block comment, a multi-line string, an open XML tag or a Markdown code fence), cached
/// per line so only edited lines and the ones after them are re-scanned.
/// </summary>
public sealed class Highlighter
{
    private const int StateNormal = 0;
    private const int StateBlockCommentBase = 1;     // 1 + index into BlockComments
    private const int StateMultiStringBase = 100;    // 100 + index into MultiLineStrings
    private const int StateInTag = 200;
    private const int StateInFence = 300;

    private readonly TextDocument _doc;
    private readonly List<int> _endStates = [];

    public Language Language { get; private set; }

    public Highlighter(TextDocument doc, Language language)
    {
        _doc = doc;
        Language = language;
        _doc.Changed += c =>
        {
            if (_endStates.Count > c.StartLine) _endStates.RemoveRange(c.StartLine, _endStates.Count - c.StartLine);
        };
    }

    public void SetLanguage(Language language)
    {
        Language = language;
        _endStates.Clear();
    }

    private int StateBefore(int line)
    {
        if (line == 0) return StateNormal;
        var scratch = Array.Empty<TokenKind>();
        while (_endStates.Count < line)
        {
            int l = _endStates.Count;
            var text = _doc[l];
            if (scratch.Length < text.Length) scratch = new TokenKind[Math.Max(text.Length, 256)];
            _endStates.Add(Tokenize(text, l == 0 ? StateNormal : _endStates[l - 1], scratch));
        }
        return _endStates[line - 1];
    }

    /// <summary>Fills <paramref name="kinds"/> (at least the line length) with a token kind per character.</summary>
    public void Highlight(int line, TokenKind[] kinds)
    {
        int state = StateBefore(line);
        int end = Tokenize(_doc[line], state, kinds);
        if (_endStates.Count == line) _endStates.Add(end);
    }

    private int Tokenize(string line, int state, TokenKind[] kinds)
    {
        Array.Clear(kinds, 0, line.Length);
        return Language.Style switch
        {
            LexerStyle.Code => TokenizeCode(line, state, kinds),
            LexerStyle.Markup => TokenizeMarkup(line, state, kinds),
            LexerStyle.Markdown => TokenizeMarkdown(line, state, kinds),
            _ => StateNormal,
        };
    }

    private static void Mark(TokenKind[] kinds, int from, int to, TokenKind kind)
    {
        for (int i = from; i < to; i++) kinds[i] = kind;
    }

    private bool At(string line, int i, string token) =>
        string.Compare(line, i, token, 0, token.Length,
            Language.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == 0
        && i + token.Length <= line.Length;

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ---------------------------------------------------------------- code

    private int TokenizeCode(string line, int state, TokenKind[] kinds)
    {
        var lang = Language;
        int n = line.Length;
        int i = 0;

        if (state >= StateBlockCommentBase && state < StateMultiStringBase)
        {
            var close = lang.BlockComments[state - StateBlockCommentBase].Close;
            int idx = line.IndexOf(close, StringComparison.Ordinal);
            if (idx < 0) { Mark(kinds, 0, n, TokenKind.Comment); return state; }
            i = idx + close.Length;
            Mark(kinds, 0, i, TokenKind.Comment);
        }
        else if (state >= StateMultiStringBase && state < StateInTag)
        {
            var delim = lang.MultiLineStrings[state - StateMultiStringBase];
            int idx = FindStringEnd(line, 0, delim);
            if (idx < 0) { Mark(kinds, 0, n, TokenKind.String); return state; }
            i = idx;
            Mark(kinds, 0, i, TokenKind.String);
        }

        int firstNonBlank = 0;
        while (firstNonBlank < n && char.IsWhiteSpace(line[firstNonBlank])) firstNonBlank++;

        if (i == 0 && lang.DirectivePrefix is { } dp && firstNonBlank < n && line[firstNonBlank] == dp)
        {
            int commentAt = lang.LineComments.Length > 0 ? line.IndexOf(lang.LineComments[0], firstNonBlank, StringComparison.Ordinal) : -1;
            int stop = commentAt >= 0 ? commentAt : n;
            Mark(kinds, firstNonBlank, stop, TokenKind.Directive);
            Mark(kinds, stop, n, TokenKind.Comment);
            return StateNormal;
        }

        if (i == 0 && lang.SectionHeaders && firstNonBlank < n && line[firstNonBlank] == '[')
        {
            Mark(kinds, firstNonBlank, n, TokenKind.Keyword);
            return StateNormal;
        }

        if (i == 0 && lang.LineKeys && firstNonBlank < n && !lang.LineComments.Any(c => At(line, firstNonBlank, c)))
        {
            int k = firstNonBlank;
            if (k < n && line[k] == '-') k++; // YAML list item
            while (k < n && line[k] == ' ') k++;
            int keyStart = k;
            while (k < n && line[k] != ':' && line[k] != '=' && line[k] != '"' && line[k] != '\'') k++;
            if (k < n && (line[k] == ':' || line[k] == '=') && k > keyStart)
            {
                Mark(kinds, keyStart, k, TokenKind.Type);
                i = k + 1;
            }
        }

        while (i < n)
        {
            char c = line[i];

            // Line comments. Word-like markers (BASIC's REM) need a word boundary in front.
            bool isComment = false;
            foreach (var lc in lang.LineComments)
            {
                if (!At(line, i, lc)) continue;
                if (char.IsLetter(lc[0]) && i > 0 && char.IsLetter(line[i - 1])) continue;
                isComment = true;
                break;
            }
            if (isComment) { Mark(kinds, i, n, TokenKind.Comment); return StateNormal; }

            bool handled = false;
            for (int b = 0; b < lang.BlockComments.Length; b++)
            {
                var (open, close) = lang.BlockComments[b];
                if (!At(line, i, open)) continue;
                int idx = line.IndexOf(close, i + open.Length, StringComparison.Ordinal);
                if (idx < 0) { Mark(kinds, i, n, TokenKind.Comment); return StateBlockCommentBase + b; }
                Mark(kinds, i, idx + close.Length, TokenKind.Comment);
                i = idx + close.Length;
                handled = true;
                break;
            }
            if (handled) continue;

            for (int s = 0; s < lang.MultiLineStrings.Length; s++)
            {
                var delim = lang.MultiLineStrings[s];
                if (!At(line, i, delim)) continue;
                int end = FindStringEnd(line, i + delim.Length, delim);
                if (end < 0) { Mark(kinds, i, n, TokenKind.String); return StateMultiStringBase + s; }
                Mark(kinds, i, end, TokenKind.String);
                i = end;
                handled = true;
                break;
            }
            if (handled) continue;

            foreach (var delim in lang.Strings)
            {
                if (!At(line, i, delim)) continue;
                // A lone apostrophe inside a word ("don't") is not a string in prose-like languages.
                if (delim == "'" && lang == Languages.Yaml && i > 0 && char.IsLetter(line[i - 1])) continue;
                int end = FindStringEnd(line, i + delim.Length, delim);
                if (end < 0) end = n;
                var kind = TokenKind.String;
                if (lang.StringKeys)
                {
                    int k = end;
                    while (k < n && line[k] == ' ') k++;
                    if (k < n && line[k] == ':') kind = TokenKind.Type;
                }
                Mark(kinds, i, end, kind);
                i = end;
                handled = true;
                break;
            }
            if (handled) continue;

            if (IsNumberStart(line, i))
            {
                int start = i;
                if (line[i] == '#') i++;
                if (i < n && (line[i] == '$' || line[i] == '%')) i++;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '.' || line[i] == '_')) i++;
                Mark(kinds, start, i, TokenKind.Number);
                continue;
            }

            if (lang.DirectiveWordPrefix is { } wp && c == wp && i + 1 < n && (IsIdentStart(line[i + 1]) || (wp == '$' && line[i + 1] == '{')))
            {
                int start = i++;
                if (line[i] == '{')
                {
                    int close = line.IndexOf('}', i);
                    i = close < 0 ? n : close + 1;
                }
                else
                {
                    while (i < n && (IsIdentPart(line[i]) || line[i] == '.')) i++;
                }
                Mark(kinds, start, i, TokenKind.Directive);
                continue;
            }

            if (IsIdentStart(c))
            {
                int start = i;
                while (i < n && IsIdentPart(line[i])) i++;
                var word = line[start..i];
                if (i < n && line[i] is '$' or '#' or '(' && lang.Keywords.Contains(word + line[i])) { word += line[i]; i++; }

                if (lang.Keywords.Contains(word)) Mark(kinds, start, i, TokenKind.Keyword);
                else if (lang.Types.Contains(word)) Mark(kinds, start, i, TokenKind.Type);
                else if (lang.CapitalizedAreTypes && char.IsUpper(word[0]) && word.Any(char.IsLower)) Mark(kinds, start, i, TokenKind.Type);
                // Assembler labels at the start of a line.
                else if (lang == Languages.Asm6502 && start == 0) Mark(kinds, start, i, TokenKind.Attribute);
                continue;
            }

            i++;
        }
        return StateNormal;
    }

    private bool IsNumberStart(string line, int i)
    {
        char c = line[i];
        bool prevIsWord = i > 0 && IsIdentPart(line[i - 1]);
        if (prevIsWord) return false;
        if (char.IsDigit(c)) return true;
        if (c == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1])) return true;
        if (!Language.DollarHex) return false;
        if (c == '#' && i + 1 < line.Length && (char.IsDigit(line[i + 1]) || line[i + 1] is '$' or '%' or '<' or '>')) return true;
        if (c == '$' && i + 1 < line.Length && Uri.IsHexDigit(line[i + 1])) return true;
        if (c == '%' && i + 1 < line.Length && line[i + 1] is '0' or '1') return true;
        return false;
    }

    /// <summary>Returns the index just past the closing delimiter, or -1 if the string runs to the end of the line.</summary>
    private int FindStringEnd(string line, int from, string delim)
    {
        for (int i = from; i < line.Length; i++)
        {
            if (line[i] == '\\' && !Language.RawStrings) { i++; continue; }
            if (string.CompareOrdinal(line, i, delim, 0, delim.Length) == 0) return i + delim.Length;
        }
        return -1;
    }

    // ---------------------------------------------------------------- XML / HTML

    private static int TokenizeMarkup(string line, int state, TokenKind[] kinds)
    {
        int n = line.Length;
        int i = 0;

        if (state == StateBlockCommentBase)
        {
            int idx = line.IndexOf("-->", StringComparison.Ordinal);
            if (idx < 0) { Mark(kinds, 0, n, TokenKind.Comment); return state; }
            i = idx + 3;
            Mark(kinds, 0, i, TokenKind.Comment);
            state = StateNormal;
        }

        while (i < n)
        {
            if (state == StateInTag)
            {
                char c = line[i];
                if (c == '>' || (c == '/' && i + 1 < n && line[i + 1] == '>') || (c == '?' && i + 1 < n && line[i + 1] == '>'))
                {
                    int len = c == '>' ? 1 : 2;
                    Mark(kinds, i, i + len, TokenKind.Tag);
                    i += len;
                    state = StateNormal;
                }
                else if (c == '"' || c == '\'')
                {
                    int end = line.IndexOf(c, i + 1);
                    end = end < 0 ? n : end + 1;
                    Mark(kinds, i, end, TokenKind.String);
                    i = end;
                }
                else if (IsIdentStart(c))
                {
                    int start = i;
                    while (i < n && (IsIdentPart(line[i]) || line[i] is ':' or '-' or '.')) i++;
                    Mark(kinds, start, i, TokenKind.Attribute);
                }
                else i++;
                continue;
            }

            if (string.CompareOrdinal(line, i, "<!--", 0, 4) == 0)
            {
                int idx = line.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (idx < 0) { Mark(kinds, i, n, TokenKind.Comment); return StateBlockCommentBase; }
                Mark(kinds, i, idx + 3, TokenKind.Comment);
                i = idx + 3;
                continue;
            }

            if (line[i] == '<' && i + 1 < n && (IsIdentStart(line[i + 1]) || line[i + 1] is '/' or '!' or '?'))
            {
                int start = i++;
                if (line[i] is '/' or '!' or '?') i++;
                while (i < n && (IsIdentPart(line[i]) || line[i] is ':' or '-' or '.')) i++;
                Mark(kinds, start, i, TokenKind.Tag);
                state = StateInTag;
                continue;
            }

            if (line[i] == '&')
            {
                int semi = line.IndexOf(';', i);
                if (semi > i && semi - i < 10)
                {
                    Mark(kinds, i, semi + 1, TokenKind.Number);
                    i = semi + 1;
                    continue;
                }
            }
            i++;
        }
        return state;
    }

    // ---------------------------------------------------------------- Markdown

    private static int TokenizeMarkdown(string line, int state, TokenKind[] kinds)
    {
        int n = line.Length;
        var trimmed = line.TrimStart();
        bool isFence = trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);

        if (state == StateInFence)
        {
            Mark(kinds, 0, n, isFence ? TokenKind.Directive : TokenKind.String);
            return isFence ? StateNormal : StateInFence;
        }
        if (isFence)
        {
            Mark(kinds, 0, n, TokenKind.Directive);
            return StateInFence;
        }
        if (trimmed.StartsWith('#')) { Mark(kinds, 0, n, TokenKind.Keyword); return StateNormal; }
        if (trimmed.StartsWith('>')) { Mark(kinds, 0, n, TokenKind.Comment); return StateNormal; }
        if (line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t')) { Mark(kinds, 0, n, TokenKind.String); return StateNormal; }

        int i = n - trimmed.Length;
        if (trimmed.Length >= 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
        {
            kinds[i] = TokenKind.Number;
        }
        else
        {
            int k = i;
            while (k < n && char.IsDigit(line[k])) k++;
            if (k > i && k < n && line[k] is '.' or ')') Mark(kinds, i, k + 1, TokenKind.Number);
        }

        while (i < n)
        {
            char c = line[i];
            if (c == '`')
            {
                int end = line.IndexOf('`', i + 1);
                end = end < 0 ? n : end + 1;
                Mark(kinds, i, end, TokenKind.String);
                i = end;
            }
            else if ((c == '*' || c == '_') && i + 1 < n && !char.IsWhiteSpace(line[i + 1]))
            {
                bool strong = i + 1 < n && line[i + 1] == c;
                var marker = strong ? new string(c, 2) : c.ToString();
                int end = line.IndexOf(marker, i + marker.Length, StringComparison.Ordinal);
                if (end < 0) { i++; continue; }
                Mark(kinds, i, end + marker.Length, strong ? TokenKind.Keyword : TokenKind.Type);
                i = end + marker.Length;
            }
            else if (c == '[')
            {
                int close = line.IndexOf(']', i + 1);
                if (close < 0) { i++; continue; }
                Mark(kinds, i, close + 1, TokenKind.Attribute);
                i = close + 1;
                if (i < n && line[i] == '(')
                {
                    int paren = line.IndexOf(')', i);
                    paren = paren < 0 ? n : paren + 1;
                    Mark(kinds, i, paren, TokenKind.String);
                    i = paren;
                }
            }
            else i++;
        }
        return StateNormal;
    }
}
