using System.Net;
using System.Text;
using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;

namespace DevPath.Web.Lessons;

/// <summary>
/// Destaque de sintaxe no servidor (sem JS, sem CDN), com ColorCode. Só SQL e C# são coloridos;
/// outras linguagens saem como texto puro (escapado). Cada trecho vira
/// <c>&lt;span class="tok-..."&gt;</c>; as cores ficam em Styles/app.css.
/// </summary>
public static class CodeHighlighter
{
    private static readonly Dictionary<string, (ILanguage Language, string Label)> Supported =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sql"] = (Languages.Sql, "SQL"),
            ["postgresql"] = (Languages.Sql, "SQL"),
            ["postgres"] = (Languages.Sql, "SQL"),
            ["csharp"] = (Languages.CSharp, "C#"),
            ["cs"] = (Languages.CSharp, "C#"),
            ["c#"] = (Languages.CSharp, "C#"),
        };

    /// <summary>Nome da linguagem para mostrar ao aluno ("SQL", "C#"), ou null se desconhecida.</summary>
    public static string? LabelFor(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        return Supported.TryGetValue(language.Trim(), out var entry) ? entry.Label : language.Trim();
    }

    /// <summary>Classe CSS normalizada ("sql", "csharp") ou null.</summary>
    public static string? CssLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var key = language.Trim();
        if (Supported.TryGetValue(key, out var entry))
            return entry.Language == Languages.CSharp ? "csharp" : "sql";
        var safe = new string(key.ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '+').ToArray());
        return safe.Length == 0 ? null : safe;
    }

    /// <summary>Devolve o HTML (já escapado) do código, com spans de destaque se a linguagem for suportada.</summary>
    public static string ToHtml(string code, string? language)
    {
        if (!string.IsNullOrWhiteSpace(language) && Supported.TryGetValue(language.Trim(), out var entry))
            return new SpanFormatter().Format(code, entry.Language);

        return WebUtility.HtmlEncode(code);
    }

    /// <summary>Escreve spans com classes nossas (tok-keyword etc.), sem estilos inline.</summary>
    private sealed class SpanFormatter() : CodeColorizerBase(null, null)
    {
        private readonly StringBuilder _html = new();

        public string Format(string code, ILanguage language)
        {
            languageParser.Parse(code, language, Write);
            return _html.ToString();
        }

        protected override void Write(string parsedSourceCode, IList<Scope> scopes) =>
            WriteRange(parsedSourceCode, 0, parsedSourceCode.Length, scopes);

        private void WriteRange(string source, int start, int end, IEnumerable<Scope> scopes)
        {
            var position = start;
            foreach (var scope in scopes.OrderBy(s => s.Index))
            {
                if (scope.Index < position || scope.Index + scope.Length > end) continue; // sobreposição: ignora

                Append(source, position, scope.Index);
                var css = TokenClass(scope.Name);
                if (css is not null) _html.Append("<span class=\"").Append(css).Append("\">");
                WriteRange(source, scope.Index, scope.Index + scope.Length, scope.Children);
                if (css is not null) _html.Append("</span>");
                position = scope.Index + scope.Length;
            }

            Append(source, position, end);
        }

        private void Append(string source, int from, int to)
        {
            if (to > from) _html.Append(WebUtility.HtmlEncode(source[from..to]));
        }

        private static string? TokenClass(string scopeName) => scopeName switch
        {
            ScopeName.Keyword or ScopeName.PreprocessorKeyword or ScopeName.ControlKeyword
                or ScopeName.PseudoKeyword or ScopeName.Predefined => "tok-keyword",
            ScopeName.SqlSystemFunction or ScopeName.BuiltinFunction => "tok-function",
            ScopeName.Comment or ScopeName.XmlDocComment or ScopeName.XmlDocTag => "tok-comment",
            ScopeName.String or ScopeName.StringCSharpVerbatim => "tok-string",
            ScopeName.Number => "tok-number",
            ScopeName.ClassName or ScopeName.Type or ScopeName.TypeVariable or ScopeName.NameSpace => "tok-type",
            ScopeName.Operator => "tok-operator",
            _ => null,
        };
    }
}
