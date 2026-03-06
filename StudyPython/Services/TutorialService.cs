using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using StudyPython.Models;

namespace StudyPython.Services;

public static class TutorialService
{
    private static readonly HttpClient _httpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StudyPython/1.0" }
        }
    };
    private static readonly string BaseUrl = "https://076923.github.io";
    private static string _imageCacheFolder = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "image");

    /// <summary>이미지 저장 폴더 설정 (실행파일 위치/image)</summary>
    public static void SetImageFolder(string? workingFolder)
    {
        // 항상 실행파일 위치의 \image 폴더 사용 (작업폴더 아님)
        _imageCacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image");
    }

    public static async Task<ObservableCollection<TreeNodeItem>> GetCourseListAsync()
    {
        var tree = new ObservableCollection<TreeNodeItem>();

        try
        {
            var html = await _httpClient.GetStringAsync($"{BaseUrl}/posts/Python-5/");
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var navLinks = doc.DocumentNode.SelectNodes("//nav//a[@href]");
            var courses = new List<CourseItem>();

            if (navLinks != null)
            {
                foreach (var link in navLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (href.Contains("/posts/Python-"))
                    {
                        var title = link.InnerText.Trim();
                        if (string.IsNullOrEmpty(title)) continue;

                        var numStr = href.Replace("/posts/Python-", "").TrimEnd('/');
                        if (int.TryParse(numStr, out var num))
                        {
                            if (!courses.Any(c => c.LessonNumber == num))
                            {
                                courses.Add(new CourseItem
                                {
                                    Title = title,
                                    Url = $"{BaseUrl}{href}",
                                    LessonNumber = num
                                });
                            }
                        }
                    }
                }
            }

            if (courses.Count == 0)
            {
                courses = GenerateDefaultCourseList();
            }

            courses = courses.OrderBy(c => c.LessonNumber).ToList();

            foreach (var course in courses)
            {
                tree.Add(new TreeNodeItem
                {
                    Name = course.Title,
                    Tag = course
                });
            }
        }
        catch
        {
            foreach (var course in GenerateDefaultCourseList())
            {
                tree.Add(new TreeNodeItem
                {
                    Name = course.Title,
                    Tag = course
                });
            }
        }

        return tree;
    }

    public static async Task<CourseContent> GetCourseContentAsync(CourseItem course)
    {
        var content = new CourseContent();

        try
        {
            var html = await _httpClient.GetStringAsync(course.Url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var articleNode = doc.DocumentNode.SelectSingleNode("//article")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'post-content')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'page__content')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'entry-content')]")
                ?? doc.DocumentNode.SelectSingleNode("//main");

            if (articleNode != null)
            {
                // 목차(TOC) 제거
                RemoveNodes(articleNode, ".//*[contains(@class,'toc') or contains(@id,'toc') or contains(@class,'table-of-contents')]");

                // 네비게이션 링크 제거
                RemoveNodes(articleNode, ".//nav | .//*[contains(@class,'pagination') or contains(@class,'page__nav') or contains(@class,'nav-previous') or contains(@class,'nav-next')]");

                // 공유하기/소셜 버튼 제거
                RemoveNodes(articleNode, ".//*[contains(@class,'share') or contains(@class,'social') or contains(@class,'sns') or contains(@class,'page__share')]");

                // 댓글 섹션 제거
                RemoveNodes(articleNode, ".//*[contains(@class,'comment') or contains(@class,'disqus') or contains(@class,'utterances') or contains(@class,'giscus')]");

                // 태그/카테고리/메타 섹션 제거
                RemoveNodes(articleNode, ".//*[contains(@class,'page__taxonomy') or contains(@class,'page__tags') or contains(@class,'page__meta') or contains(@class,'page__date')]");

                // footer 제거
                RemoveNodes(articleNode, ".//footer");

                // "최근 포스트" 섹션 제거
                RemoveNodes(articleNode, ".//*[contains(@class,'recent') or contains(@class,'related')]");

                // script 태그 제거 (Kakao, Utterances, MathJax 등)
                RemoveNodes(articleNode, ".//script");

                // 이미지 다운로드 및 로컬 캐싱 (src + data-src lazy loading 모두 처리)
                var imgNodes = articleNode.SelectNodes(".//img[@src or @data-src]");
                if (imgNodes != null)
                {
                    Directory.CreateDirectory(_imageCacheFolder);
                    foreach (var img in imgNodes)
                    {
                        // data-src (lazy loading)를 우선 사용, 없으면 src
                        var src = img.GetAttributeValue("data-src", "");
                        if (string.IsNullOrEmpty(src))
                            src = img.GetAttributeValue("src", "");
                        if (string.IsNullOrEmpty(src)) continue;

                        // 프로토콜 없는 URL 처리 (//developers.kakao.com/...)
                        if (src.StartsWith("//"))
                            src = "https:" + src;

                        var absoluteUrl = src.StartsWith("http") ? src
                            : $"{BaseUrl}{(src.StartsWith("/") ? "" : "/")}{src}";

                        try
                        {
                            var dataUri = await CacheImageAsync(absoluteUrl, course.LessonNumber);
                            if (!string.IsNullOrEmpty(dataUri))
                            {
                                img.SetAttributeValue("src", dataUri);
                                // data-src 제거 (이미 src에 설정)
                                img.Attributes.Remove("data-src");
                            }
                        }
                        catch
                        {
                            // 실패 시 원본 URL 유지
                            if (!img.GetAttributeValue("src", "").StartsWith("http"))
                                img.SetAttributeValue("src", absoluteUrl);
                        }
                    }
                }

                // href 절대 경로 변환
                var linkNodes = articleNode.SelectNodes(".//a[@href]");
                if (linkNodes != null)
                {
                    foreach (var link in linkNodes)
                    {
                        var href = link.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(href) && !href.StartsWith("http") && !href.StartsWith("#") && !href.StartsWith("javascript"))
                        {
                            link.SetAttributeValue("href", $"{BaseUrl}{(href.StartsWith("/") ? "" : "/")}{href}");
                        }
                    }
                }

                // 코드 예제 추출 (본문 HTML 생성 전에 수행 - heading 참조 필요)
                var codeBlocks = articleNode.SelectNodes(".//figure[contains(@class,'highlight')]//pre/code | .//code[contains(@class,'language-python')] | .//pre[contains(@class,'highlight')]//code | .//div[contains(@class,'highlight')]//pre");
                if (codeBlocks != null)
                {
                    int idx = 1;
                    int exampleIndex = 0;
                    foreach (var codeBlock in codeBlocks)
                    {
                        var code = System.Net.WebUtility.HtmlDecode(codeBlock.InnerText.Trim());
                        if (!string.IsNullOrWhiteSpace(code) && code.Length > 5)
                        {
                            // 예제 위의 가장 가까운 h2/h3/h4 타이틀 찾기
                            var title = FindNearestHeading(codeBlock) ?? $"예제 {idx}";

                            // figure 부모에 data-example-idx 속성 추가 (스크롤 자동 선택용)
                            var figure = codeBlock;
                            while (figure != null && figure.Name != "figure")
                                figure = figure.ParentNode;
                            figure?.SetAttributeValue("data-example-idx", exampleIndex.ToString());

                            content.CodeExamples.Add(new CodeExample
                            {
                                Title = title,
                                Code = code
                            });
                            exampleIndex++;
                            idx++;
                        }
                    }
                }

                // 본문에서 예제 실행 결과(결과 dl 태그) 제거
                var resultDls = articleNode.SelectNodes(".//dl[dt/strong[text()='결과']]");
                if (resultDls != null)
                {
                    foreach (var dl in resultDls.ToList())
                    {
                        try { dl.Remove(); } catch { }
                    }
                }

                // 본문 HTML 가져오기
                var bodyHtml = articleNode.InnerHtml;

                // "Python 강좌 : " 접두사 제거 (h1, h2 등의 타이틀에서)
                bodyHtml = Regex.Replace(bodyHtml, @"Python\s*강좌\s*:\s*", "");

                // 불필요한 텍스트 블록 제거
                bodyHtml = RemoveUnnecessaryTextBlocks(bodyHtml);

                content.HtmlContent = WrapInHtmlTemplate(bodyHtml);
            }
            else
            {
                content.HtmlContent = WrapInHtmlTemplate("<p>강좌 내용을 불러올 수 없습니다.</p>");
            }
        }
        catch (Exception ex)
        {
            content.HtmlContent = WrapInHtmlTemplate($"<p>강좌 로딩 오류: {ex.Message}</p>");
        }

        return content;
    }

    /// <summary>코드 블록 위의 가장 가까운 h2/h3/h4 제목 찾기</summary>
    private static string? FindNearestHeading(HtmlNode codeNode)
    {
        // figure 부모까지 올라감
        var current = codeNode;
        while (current != null && current.Name != "figure")
            current = current.ParentNode;

        // figure를 못 찾으면 pre 부모 사용
        if (current == null || current.Name != "figure")
        {
            current = codeNode;
            while (current != null && current.Name != "pre")
                current = current.ParentNode;
        }

        if (current == null) return null;

        // 이전 형제 노드를 순회하며 h2/h3/h4 찾기
        var sibling = current.PreviousSibling;
        while (sibling != null)
        {
            if (sibling.Name is "h2" or "h3" or "h4")
            {
                var text = System.Net.WebUtility.HtmlDecode(sibling.InnerText.Trim());
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
            sibling = sibling.PreviousSibling;
        }

        return null;
    }

    private static void RemoveNodes(HtmlNode parent, string xpath)
    {
        var nodes = parent.SelectNodes(xpath);
        if (nodes != null)
        {
            foreach (var node in nodes.ToList())
            {
                try { node.Remove(); } catch { }
            }
        }
    }

    private static string RemoveUnnecessaryTextBlocks(string html)
    {
        // "최근 포스트" 이하 삭제
        var recentIdx = html.IndexOf("최근 포스트", StringComparison.Ordinal);
        if (recentIdx > 0)
            html = html[..recentIdx];

        // "공유하기" 이하 삭제
        var shareIdx = html.IndexOf("공유하기", StringComparison.Ordinal);
        if (shareIdx > 0)
            html = html[..shareIdx];

        // "Copyright" 이하 삭제
        var copyrightIdx = html.IndexOf("Copyright", StringComparison.OrdinalIgnoreCase);
        if (copyrightIdx > 0)
            html = html[..copyrightIdx];

        return html;
    }

    private static async Task<string?> CacheImageAsync(string imageUrl, int lessonNumber)
    {
        Directory.CreateDirectory(_imageCacheFolder);

        var fileName = Path.GetFileName(new Uri(imageUrl).AbsolutePath);
        if (string.IsNullOrEmpty(fileName) || fileName == "/")
            fileName = $"img_{imageUrl.GetHashCode():X8}.png";

        // 파일명 충돌 방지: 레슨 번호를 접두사로 추가
        fileName = $"{lessonNumber}_{fileName}";

        var localPath = Path.Combine(_imageCacheFolder, fileName);

        // WebP → PNG 변환 파일 경로
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var pngPath = ext == ".webp"
            ? Path.Combine(_imageCacheFolder, Path.ChangeExtension(fileName, ".png"))
            : localPath;

        // 이미 PNG 변환 파일이 있으면 바로 사용
        if (File.Exists(pngPath) && ext == ".webp")
        {
            var pngBytes = await File.ReadAllBytesAsync(pngPath);
            return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
        }

        byte[] imageBytes;
        if (File.Exists(localPath))
        {
            imageBytes = await File.ReadAllBytesAsync(localPath);
        }
        else
        {
            imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
            await File.WriteAllBytesAsync(localPath, imageBytes);
        }

        // WebP인 경우 PNG로 변환 (IE WebBrowser는 WebP 미지원)
        if (ext == ".webp")
        {
            using var ms = new MemoryStream(imageBytes);
            using var image = await SixLabors.ImageSharp.Image.LoadAsync(ms);
            using var outMs = new MemoryStream();
            await image.SaveAsPngAsync(outMs);
            var pngBytes = outMs.ToArray();
            await File.WriteAllBytesAsync(pngPath, pngBytes);
            return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
        }

        // 기타 형식은 그대로 base64
        var mime = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
        return $"data:{mime};base64,{Convert.ToBase64String(imageBytes)}";
    }

    private static string WrapInHtmlTemplate(string bodyContent)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>
    * {{ margin: 0; padding: 0; box-sizing: border-box; }}
    body {{ font-family: 'Malgun Gothic', 'Segoe UI', 'Arial', sans-serif; padding: 20px 25px; line-height: 1.9; background: #fff; color: #2c3e50; font-size: 32px; }}
    p {{ margin: 8px 0; font-size: 32px; }}
    li {{ font-size: 32px; margin: 4px 0; }}
    h1 {{ color: #1a5276; font-size: 48px; margin: 20px 0 12px 0; padding-bottom: 8px; border-bottom: 2px solid #3498db; }}
    h2 {{ color: #1a5276; font-size: 40px; margin: 18px 0 10px 0; padding-bottom: 5px; border-bottom: 1px solid #bdc3c7; }}
    h3 {{ color: #2471a3; font-size: 36px; margin: 14px 0 8px 0; }}
    h4 {{ color: #2e86c1; font-size: 32px; margin: 10px 0 6px 0; }}
    pre {{ background: #1e1e1e; padding: 14px 16px; border-radius: 6px; overflow-x: auto; border: 1px solid #444; margin: 10px 0; }}
    code {{ font-family: 'Consolas', 'D2Coding', 'Courier New', monospace; font-size: 28px; color: #d4d4d4; }}
    p > code, li > code {{ background: #eaf2f8; padding: 2px 6px; border-radius: 3px; color: #c0392b; font-size: 28px; }}
    img {{ max-width: 100%; height: auto; margin: 12px 0; border-radius: 4px; }}
    table {{ border-collapse: collapse; width: 100%; margin: 12px 0; }}
    th {{ background: #2c3e50; color: #fff; padding: 10px 12px; text-align: left; font-size: 30px; }}
    td {{ border: 1px solid #d5dbdb; padding: 8px 12px; text-align: left; font-size: 30px; }}
    tr:nth-child(even) {{ background: #f8f9fa; }}
    blockquote {{ border-left: 4px solid #3498db; padding: 10px 15px; color: #555; margin: 12px 0; background: #f8f9fa; font-size: 32px; }}
    ul, ol {{ padding-left: 25px; margin: 8px 0; }}
    a {{ color: #2980b9; text-decoration: none; }}
    a:hover {{ text-decoration: underline; }}
    hr {{ border: none; border-top: 1px solid #d5dbdb; margin: 15px 0; }}
    br + br {{ display: none; }}

    /* 파이썬 구문 하이라이트 (VS Code 다크 테마) */
    .highlight pre {{ background: #1e1e1e; color: #d4d4d4; }}
    .k, .kn, .kd, .kp, .kr, .kt {{ color: #569cd6; font-weight: bold; }}  /* keyword */
    .ow {{ color: #569cd6; font-weight: bold; }}                           /* operator.word */
    .nb, .bp {{ color: #dcdcaa; }}                                         /* builtin */
    .nf, .fm {{ color: #dcdcaa; }}                                         /* function */
    .nc {{ color: #4ec9b0; }}                                              /* class */
    .nn {{ color: #4ec9b0; }}                                              /* namespace */
    .n {{ color: #d4d4d4; }}                                               /* name */
    .o {{ color: #d4d4d4; }}                                               /* operator */
    .p {{ color: #d4d4d4; }}                                               /* punctuation */
    .s, .s1, .s2, .sb, .sc, .sd, .sh, .sx {{ color: #ce9178; }}           /* string */
    .si {{ color: #d7ba7d; }}                                              /* string interpolation */
    .mi, .mf, .mh, .mo {{ color: #b5cea8; }}                              /* number */
    .c, .c1, .cm, .cs, .cp {{ color: #6a9955; font-style: italic; }}      /* comment */
    .se {{ color: #d7ba7d; }}                                              /* string escape */
    .err {{ color: #f44747; }}                                             /* error */
</style>
</head>
<body>
{bodyContent}
<script>
(function() {{
    var allFigures = document.getElementsByTagName('figure');
    var figures = [];
    for (var i = 0; i < allFigures.length; i++) {{
        if (allFigures[i].getAttribute('data-example-idx') !== null) {{
            figures.push(allFigures[i]);
        }}
    }}
    if (figures.length === 0) return;
    window._lastExIdx = -1;
    window.onscroll = function() {{
        var vh = document.documentElement.clientHeight || document.body.clientHeight;
        for (var i = figures.length - 1; i >= 0; i--) {{
            var rect = figures[i].getBoundingClientRect();
            if (rect.top < vh * 0.6) {{
                var idx = parseInt(figures[i].getAttribute('data-example-idx'), 10);
                if (idx !== window._lastExIdx) {{
                    window._lastExIdx = idx;
                    try {{ window.external.NotifyExampleVisible(idx); }} catch(e) {{}}
                }}
                break;
            }}
        }}
    }};
}})();
</script>
</body>
</html>";
    }

    private static List<CourseItem> GenerateDefaultCourseList()
    {
        var titles = new Dictionary<int, string>
        {
            {1, "제 1강 - 소개 및 설치"}, {2, "제 2강 - 기초 문법"}, {3, "제 3강 - 출력"}, {4, "제 4강 - 기초 연산 (1)"},
            {5, "제 5강 - 기초 연산 (2)"}, {6, "제 6강 - 문자열"}, {7, "제 7강 - 리스트"}, {8, "제 8강 - 튜플"},
            {9, "제 9강 - 딕셔너리"}, {10, "제 10강 - 집합"}, {11, "제 11강 - 조건문"}, {12, "제 12강 - 반복문"},
            {13, "제 13강 - 함수"}, {14, "제 14강 - 클래스 (1)"}, {15, "제 15강 - 클래스 (2)"}, {16, "제 16강 - 모듈"},
            {17, "제 17강 - 패키지"}, {18, "제 18강 - 예외 처리"}, {19, "제 19강 - 파일 입출력"}, {20, "제 20강 - 정규 표현식"},
            {21, "제 21강 - 람다"}, {22, "제 22강 - 맵"}, {23, "제 23강 - 필터"}, {24, "제 24강 - 데코레이터"},
            {25, "제 25강 - 이터레이터"}, {26, "제 26강 - 제너레이터"}, {27, "제 27강 - 가변 인수"}, {28, "제 28강 - 고정 인수"},
            {29, "제 29강 - 형식 지정자"}, {30, "제 30강 - 내장 함수 (1)"}, {31, "제 31강 - 내장 함수 (2)"}, {32, "제 32강 - 외장 함수"},
            {33, "제 33강 - 날짜와 시간"}, {34, "제 34강 - 배열"}, {35, "제 35강 - 연결 리스트"}, {36, "제 36강 - 스택"},
            {37, "제 37강 - 큐"}, {38, "제 38강 - 트리"}, {39, "제 39강 - 그래프"}, {40, "제 40강 - 정렬"},
            {41, "제 41강 - 탐색"}, {42, "제 42강 - 스레드"}, {43, "제 43강 - 프로세스"}, {44, "제 44강 - 비동기"},
            {45, "제 45강 - 디스패치"}, {46, "제 46강 - 병렬 처리"}
        };

        return titles.Select(kvp => new CourseItem
        {
            Title = kvp.Value,
            Url = $"{BaseUrl}/posts/Python-{kvp.Key}/",
            LessonNumber = kvp.Key
        }).ToList();
    }
}
