namespace C64Edit.Syntax;

public enum LexerStyle
{
    Code,
    Markup,
    Markdown,
    Plain,
}

/// <summary>Declarative description of a language for the generic tokenizer.</summary>
public sealed class Language
{
    public required string Name { get; init; }
    public string[] Extensions { get; init; } = [];
    public string[] FileNames { get; init; } = [];
    public LexerStyle Style { get; init; } = LexerStyle.Code;

    public string[] LineComments { get; init; } = [];
    public (string Open, string Close)[] BlockComments { get; init; } = [];
    /// <summary>Single-line strings, closed by the same delimiter, with backslash escapes unless <see cref="RawStrings"/>.</summary>
    public string[] Strings { get; init; } = ["\"", "'"];
    /// <summary>Strings that may span lines (Python triple quotes, JS template literals ...).</summary>
    public string[] MultiLineStrings { get; init; } = [];
    public bool RawStrings { get; init; }

    public bool CaseInsensitive { get; init; }
    public HashSet<string> Keywords { get; init; } = [];
    public HashSet<string> Types { get; init; } = [];

    /// <summary>Lines whose first non-blank character is this are directives (#include, #region ...).</summary>
    public char? DirectivePrefix { get; init; }
    /// <summary>Words starting with this character are directives/decorators (@attr, .byte ...).</summary>
    public char? DirectiveWordPrefix { get; init; }
    /// <summary>Identifiers that start with an uppercase letter are colored as types.</summary>
    public bool CapitalizedAreTypes { get; init; }
    /// <summary>$ff and %1010 style numbers (6502 assemblers, Commodore BASIC).</summary>
    public bool DollarHex { get; init; }
    /// <summary>Strings immediately followed by ':' are keys (JSON, YAML-ish).</summary>
    public bool StringKeys { get; init; }
    /// <summary>Text up to '=' or ':' at the start of a line is a key (INI, YAML).</summary>
    public bool LineKeys { get; init; }
    /// <summary>Lines like [section] are headers (INI/TOML).</summary>
    public bool SectionHeaders { get; init; }

    public override string ToString() => Name;
}
