namespace C64Edit.Syntax;

public static class Languages
{
    private static HashSet<string> Words(string list, bool ignoreCase = false) =>
        new(list.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static readonly Language PlainText = new() { Name = "TEXT", Style = LexerStyle.Plain };

    public static readonly Language CSharp = new()
    {
        Name = "C#",
        Extensions = [".cs", ".csx"],
        LineComments = ["//"],
        BlockComments = [("/*", "*/")],
        DirectivePrefix = '#',
        Keywords = Words("abstract as async await base break case catch checked class const continue default delegate do " +
            "else enum event explicit extern false finally fixed for foreach get goto if implicit in init interface internal is " +
            "lock namespace new null operator out override params partial private protected public readonly record ref " +
            "required return sealed set sizeof stackalloc static struct switch this throw true try typeof unchecked unsafe " +
            "using value var virtual void volatile when where while with yield file global not and or"),
        Types = Words("bool byte char decimal double float int long nint nuint object sbyte short string uint ulong ushort dynamic " +
            "Task List Dictionary HashSet IEnumerable String Int32 Exception Action Func Span"),
        CapitalizedAreTypes = true,
    };

    public static readonly Language CFamily = new()
    {
        Name = "C/C++",
        Extensions = [".c", ".h", ".cpp", ".hpp", ".cc", ".cxx", ".hh", ".m", ".mm", ".ino"],
        LineComments = ["//"],
        BlockComments = [("/*", "*/")],
        DirectivePrefix = '#',
        Keywords = Words("auto break case catch class const constexpr continue default delete do else enum explicit export extern " +
            "false for friend goto if inline mutable namespace new noexcept nullptr operator private protected public " +
            "register return sizeof static static_cast dynamic_cast reinterpret_cast const_cast struct switch template this " +
            "throw true try typedef typename union using virtual volatile while NULL override final"),
        Types = Words("bool char double float int long short signed unsigned void size_t int8_t int16_t int32_t int64_t " +
            "uint8_t uint16_t uint32_t uint64_t wchar_t std string vector map"),
    };

    public static readonly Language Java = new()
    {
        Name = "JAVA",
        Extensions = [".java", ".kt", ".kts", ".scala", ".groovy", ".gradle"],
        LineComments = ["//"],
        BlockComments = [("/*", "*/")],
        DirectiveWordPrefix = '@',
        Keywords = Words("abstract assert break case catch class const continue default do else enum extends final finally for " +
            "goto if implements import instanceof interface native new package private protected public return static " +
            "strictfp super switch synchronized this throw throws transient try volatile while true false null var val fun " +
            "object when is in override data sealed open companion record yield"),
        Types = Words("boolean byte char double float int long short void String Integer Object List Map Unit Any"),
        CapitalizedAreTypes = true,
    };

    public static readonly Language JavaScript = new()
    {
        Name = "JS/TS",
        Extensions = [".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx"],
        LineComments = ["//"],
        BlockComments = [("/*", "*/")],
        MultiLineStrings = ["`"],
        DirectiveWordPrefix = '@',
        Keywords = Words("async await break case catch class const continue debugger default delete do else export extends " +
            "false finally for from function get if import in instanceof let new null of return set static super switch " +
            "this throw true try typeof undefined var void while with yield as interface type enum implements private " +
            "public protected readonly declare namespace abstract keyof"),
        Types = Words("string number boolean any unknown never object symbol bigint Array Promise Map Set Record"),
        CapitalizedAreTypes = true,
    };

    public static readonly Language Python = new()
    {
        Name = "PYTHON",
        Extensions = [".py", ".pyw", ".pyi"],
        LineComments = ["#"],
        MultiLineStrings = ["\"\"\"", "'''"],
        DirectiveWordPrefix = '@',
        Keywords = Words("and as assert async await break class continue def del elif else except False finally for from global " +
            "if import in is lambda None nonlocal not or pass raise return True try while with yield match case self"),
        Types = Words("int float str bool list dict set tuple bytes object type Exception"),
    };

    public static readonly Language Rust = new()
    {
        Name = "RUST",
        Extensions = [".rs"],
        LineComments = ["//"],
        BlockComments = [("/*", "*/")],
        DirectiveWordPrefix = '#',
        Strings = ["\""],
        Keywords = Words("as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod " +
            "move mut pub ref return self Self static struct super trait true type unsafe use where while"),
        Types = Words("i8 i16 i32 i64 i128 isize u8 u16 u32 u64 u128 usize f32 f64 bool char str String Vec Option Result Box"),
        CapitalizedAreTypes = true,
    };

    public static readonly Language Go = new()
    {
        Name = "GO",
        Extensions = [".go"],
        LineComments = ["//"],
        BlockComments = [("/*", "*/")],
        MultiLineStrings = ["`"],
        Keywords = Words("break case chan const continue default defer else fallthrough for func go goto if import interface map " +
            "package range return select struct switch type var true false nil iota"),
        Types = Words("bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 " +
            "uint16 uint32 uint64 uintptr any"),
    };

    public static readonly Language Json = new()
    {
        Name = "JSON",
        Extensions = [".json", ".jsonc", ".json5"],
        LineComments = ["//"],
        BlockComments = [("/*", "*/")],
        Strings = ["\""],
        StringKeys = true,
        Keywords = Words("true false null"),
    };

    public static readonly Language Markup = new()
    {
        Name = "XML/HTML",
        Extensions = [".xml", ".html", ".htm", ".xhtml", ".xaml", ".axaml", ".svg", ".csproj", ".props", ".targets",
            ".config", ".plist", ".resx", ".vue", ".nuspec", ".fsproj", ".vbproj", ".slnx"],
        Style = LexerStyle.Markup,
        BlockComments = [("<!--", "-->")],
    };

    public static readonly Language Css = new()
    {
        Name = "CSS",
        Extensions = [".css", ".scss", ".less"],
        LineComments = ["//"],
        BlockComments = [("/*", "*/")],
        DirectiveWordPrefix = '@',
        Keywords = Words("important inherit initial none auto px em rem vh vw solid flex grid block inline absolute relative fixed"),
    };

    public static readonly Language Shell = new()
    {
        Name = "SHELL",
        Extensions = [".sh", ".bash", ".zsh", ".fish", ".command"],
        FileNames = [".bashrc", ".zshrc", ".profile", ".bash_profile", "Dockerfile", "Makefile", "makefile"],
        LineComments = ["#"],
        RawStrings = false,
        DirectiveWordPrefix = '$',
        Keywords = Words("if then else elif fi case esac for while until do done in function return exit local export " +
            "readonly declare source alias unset shift break continue echo cd set eval exec trap FROM RUN CMD COPY ADD " +
            "ENV WORKDIR ENTRYPOINT EXPOSE ARG LABEL USER VOLUME"),
    };

    public static readonly Language PowerShell = new()
    {
        Name = "POWERSHELL",
        Extensions = [".ps1", ".psm1", ".psd1"],
        LineComments = ["#"],
        BlockComments = [("<#", "#>")],
        CaseInsensitive = true,
        DirectiveWordPrefix = '$',
        Keywords = Words("begin break catch class continue data do dynamicparam else elseif end exit filter finally for foreach " +
            "from function if in param process return switch throw trap try until using var while", true),
    };

    public static readonly Language Sql = new()
    {
        Name = "SQL",
        Extensions = [".sql"],
        LineComments = ["--"],
        BlockComments = [("/*", "*/")],
        CaseInsensitive = true,
        Keywords = Words("select from where and or not insert into values update set delete create table alter drop index view " +
            "join inner left right outer full on group by order having limit offset as distinct union all case when then else " +
            "end null is in like between exists primary key foreign references default begin commit rollback transaction " +
            "with returning desc asc", true),
        Types = Words("int integer bigint smallint varchar char text date datetime timestamp boolean real float decimal numeric blob", true),
    };

    public static readonly Language Yaml = new()
    {
        Name = "YAML",
        Extensions = [".yml", ".yaml"],
        LineComments = ["#"],
        LineKeys = true,
        Keywords = Words("true false null yes no on off"),
    };

    public static readonly Language Ini = new()
    {
        Name = "INI",
        Extensions = [".ini", ".cfg", ".conf", ".toml", ".properties", ".editorconfig", ".gitconfig", ".env"],
        FileNames = [".editorconfig", ".gitconfig", ".gitignore", ".gitattributes"],
        LineComments = ["#", ";"],
        LineKeys = true,
        SectionHeaders = true,
        Keywords = Words("true false"),
    };

    public static readonly Language Markdown = new()
    {
        Name = "MARKDOWN",
        Extensions = [".md", ".markdown", ".mdx"],
        Style = LexerStyle.Markdown,
    };

    /// <summary>6502/6510 assembly as written for ACME, KickAssembler, ca65, 64tass and friends.</summary>
    public static readonly Language Asm6502 = new()
    {
        Name = "6502 ASM",
        Extensions = [".asm", ".s", ".a65", ".a", ".inc", ".65s", ".tas"],
        LineComments = [";", "//"],
        BlockComments = [("/*", "*/")],
        CaseInsensitive = true,
        DollarHex = true,
        DirectiveWordPrefix = '.',
        Strings = ["\""],
        Keywords = Words("adc and asl bcc bcs beq bit bmi bne bpl brk bvc bvs clc cld cli clv cmp cpx cpy dec dex dey eor inc " +
            "inx iny jmp jsr lda ldx ldy lsr nop ora pha php pla plp rol ror rti rts sbc sec sed sei sta stx sty tax tay " +
            "tsx txa txs tya lax sax dcp isc slo rla sre rra anc alr arr sbx", true),
        Types = Words("a x y", true),
    };

    /// <summary>Commodore BASIC V2 (C64) and V7 (C128).</summary>
    public static readonly Language CbmBasic = new()
    {
        Name = "CBM BASIC",
        Extensions = [".bas", ".bpp", ".lst"],
        LineComments = ["rem"],
        CaseInsensitive = true,
        DollarHex = true,
        Strings = ["\""],
        RawStrings = true,
        Keywords = Words("end for next data input# input dim read let goto run if restore gosub return rem stop on wait load " +
            "save verify def poke print# print cont list clr cmd sys open close get new tab( to fn spc( then not step and or " +
            "sgn int abs usr fre pos sqr rnd log exp cos sin tan atn peek len str$ val asc chr$ left$ right$ mid$ go " +
            "rgr rclr rlum joy rdot dec hex$ err$ instr else resume trap tron troff sound vol auto pudef graphic paint char " +
            "box circle gshape sshape draw locate color scnclr scale stash fetch swap do loop while until exit directory " +
            "dsave dload header scratch collect copy rename backup delete renumber key monitor using bank fast slow sleep " +
            "window boot dverify dclear sprite movspr sprcolor sprsav collision begin bend envelope filter play tempo", true),
        Types = Words("ti ti$ st ds ds$ el er", true),
    };

    public static readonly IReadOnlyList<Language> All =
    [
        PlainText, CSharp, CFamily, Java, JavaScript, Python, Rust, Go, Json, Markup, Css, Shell, PowerShell, Sql,
        Yaml, Ini, Markdown, Asm6502, CbmBasic,
    ];

    public static Language ForPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return PlainText;
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        foreach (var lang in All)
            if (lang.FileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                return lang;
        foreach (var lang in All)
            if (lang.Extensions.Contains(ext))
                return lang;
        return PlainText;
    }
}
