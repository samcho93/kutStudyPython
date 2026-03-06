using System.IO;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StudyPython.Models;

namespace StudyPython.Services;

public static class PdfExportService
{
    /// <summary>단일 탭 출력 (하위 호환)</summary>
    public static string Export(UserProfile profile, string pythonCode, string executionResult, string? outputFolder = null)
    {
        var tabCodes = new List<(string Title, string Code)> { ("Python 코드", pythonCode) };
        return Export(profile, tabCodes, executionResult, outputFolder);
    }

    /// <summary>여러 탭의 코드를 모두 출력</summary>
    public static string Export(UserProfile profile, List<(string Title, string Code)> tabCodes, string executionResult, string? outputFolder = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var dateStr = DateTime.Now.ToString("yyyyMMdd");
        var fileName = $"{profile.StudentId}_{profile.Name}_{dateStr}.pdf";
        var folder = outputFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StudyPython");
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, fileName);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Malgun Gothic"));

                page.Header().Column(col =>
                {
                    col.Item().Text("학습 과제").FontSize(18).Bold().AlignCenter();
                    col.Item().PaddingVertical(5).LineHorizontal(1);
                    col.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().Text($"학과: {profile.Department}");
                        row.RelativeItem().Text($"학번: {profile.StudentId}");
                        row.RelativeItem().Text($"학년: {profile.Grade}");
                        row.RelativeItem().Text($"이름: {profile.Name}");
                    });
                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text($"소속 회사: {profile.Company}");
                        row.RelativeItem().Text($"제출일: {DateTime.Now:yyyy-MM-dd}");
                    });
                    col.Item().PaddingVertical(5).LineHorizontal(1);
                });

                page.Content().Column(col =>
                {
                    foreach (var (title, code) in tabCodes)
                    {
                        col.Item().PaddingTop(10).Text(title).FontSize(14).Bold();
                        col.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2)
                            .Background("#FAFAFA").Padding(10)
                            .Column(codeCol => RenderHighlightedCode(codeCol, code));
                    }

                    col.Item().PaddingTop(15).Text("실행 결과").FontSize(14).Bold();
                    col.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2)
                        .Background("#F0F8FF").Padding(10)
                        .Text(string.IsNullOrEmpty(executionResult) ? "(실행 결과 없음)" : executionResult)
                        .FontSize(9).FontFamily("Consolas");
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    private static void RenderHighlightedCode(ColumnDescriptor col, string code)
    {
        var lines = code.Split('\n');
        foreach (var line in lines)
        {
            col.Item().Text(text =>
            {
                RenderHighlightedLine(text, line);
            });
        }
    }

    private static void RenderHighlightedLine(TextDescriptor text, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            text.Span(" ").FontSize(9).FontFamily("Consolas");
            return;
        }

        // 토큰 분석
        var tokens = TokenizePython(line);
        foreach (var token in tokens)
        {
            var span = text.Span(token.Text).FontSize(9).FontFamily("Consolas");
            switch (token.Type)
            {
                case TokenType.Keyword:
                    span.FontColor("#0000FF").Bold();
                    break;
                case TokenType.String:
                    span.FontColor("#A31515");
                    break;
                case TokenType.Comment:
                    span.FontColor("#008000").Italic();
                    break;
                case TokenType.BuiltinFunction:
                    span.FontColor("#8B008B");
                    break;
                case TokenType.Number:
                    span.FontColor("#FF4500");
                    break;
                case TokenType.Decorator:
                    span.FontColor("#CC7832");
                    break;
                case TokenType.Boolean:
                    span.FontColor("#0000FF").Bold();
                    break;
                case TokenType.Self:
                    span.FontColor("#D2691E").Italic();
                    break;
                default:
                    span.FontColor("#333333");
                    break;
            }
        }
    }

    private static readonly HashSet<string> Keywords = new()
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue",
        "def", "del", "elif", "else", "except", "finally", "for", "from",
        "global", "if", "import", "in", "is", "lambda", "nonlocal", "not",
        "or", "pass", "raise", "return", "try", "while", "with", "yield"
    };

    private static readonly HashSet<string> Builtins = new()
    {
        "print", "input", "len", "range", "type", "int", "float", "str",
        "list", "dict", "tuple", "set", "bool", "abs", "max", "min",
        "sum", "sorted", "reversed", "enumerate", "zip", "map", "filter",
        "open", "isinstance", "super", "round", "pow", "hex", "oct",
        "bin", "ord", "chr", "format", "repr", "id", "dir", "vars",
        "callable", "iter", "next", "all", "any", "eval", "exec"
    };

    private static readonly HashSet<string> Booleans = new() { "True", "False", "None" };

    private enum TokenType { Normal, Keyword, String, Comment, BuiltinFunction, Number, Decorator, Boolean, Self }

    private record struct Token(string Text, TokenType Type);

    private static List<Token> TokenizePython(string line)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < line.Length)
        {
            // 공백
            if (char.IsWhiteSpace(line[i]))
            {
                int start = i;
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                tokens.Add(new Token(line[start..i], TokenType.Normal));
                continue;
            }

            // 주석
            if (line[i] == '#')
            {
                tokens.Add(new Token(line[i..], TokenType.Comment));
                break;
            }

            // 데코레이터
            if (line[i] == '@' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                int start = i;
                i++;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                tokens.Add(new Token(line[start..i], TokenType.Decorator));
                continue;
            }

            // 문자열 (', ", ''', """)
            if (line[i] == '\'' || line[i] == '"')
            {
                char quote = line[i];
                int start = i;
                // 삼중 따옴표 체크
                if (i + 2 < line.Length && line[i + 1] == quote && line[i + 2] == quote)
                {
                    i += 3;
                    var tripleEnd = line.IndexOf(new string(quote, 3), i);
                    if (tripleEnd >= 0) i = tripleEnd + 3;
                    else i = line.Length;
                }
                else
                {
                    i++;
                    while (i < line.Length && line[i] != quote)
                    {
                        if (line[i] == '\\') i++;
                        i++;
                    }
                    if (i < line.Length) i++;
                }
                tokens.Add(new Token(line[start..i], TokenType.String));
                continue;
            }

            // 숫자
            if (char.IsDigit(line[i]) || (line[i] == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
            {
                int start = i;
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == 'x' || line[i] == 'X'
                    || line[i] == 'o' || line[i] == 'O' || line[i] == 'b' || line[i] == 'B'
                    || line[i] == 'e' || line[i] == 'E' || line[i] == '+' || line[i] == '-'
                    || (line[i] >= 'a' && line[i] <= 'f') || (line[i] >= 'A' && line[i] <= 'F')
                    || line[i] == '_'))
                    i++;
                tokens.Add(new Token(line[start..i], TokenType.Number));
                continue;
            }

            // 식별자/키워드
            if (char.IsLetter(line[i]) || line[i] == '_')
            {
                int start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                var word = line[start..i];

                if (word == "self" || word == "cls")
                    tokens.Add(new Token(word, TokenType.Self));
                else if (Booleans.Contains(word))
                    tokens.Add(new Token(word, TokenType.Boolean));
                else if (Keywords.Contains(word))
                    tokens.Add(new Token(word, TokenType.Keyword));
                else if (Builtins.Contains(word))
                    tokens.Add(new Token(word, TokenType.BuiltinFunction));
                else
                    tokens.Add(new Token(word, TokenType.Normal));
                continue;
            }

            // 기타 연산자/기호
            tokens.Add(new Token(line[i].ToString(), TokenType.Normal));
            i++;
        }

        if (tokens.Count == 0)
            tokens.Add(new Token(" ", TokenType.Normal));

        return tokens;
    }
}
