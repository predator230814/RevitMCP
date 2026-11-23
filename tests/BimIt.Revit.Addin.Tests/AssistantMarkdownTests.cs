using System.Text.RegularExpressions;
using Jint;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class AssistantMarkdownTests
{
    private static readonly string[] AllowedTags =
    [
        "p", "h4", "h5", "h6", "ul", "ol", "li", "strong", "em", "code", "pre",
        "div", "table", "thead", "tbody", "tr", "th", "td", "br"
    ];

    [Fact]
    public void Headings_bold_and_lists_render()
    {
        var html = Render("""
            # Levels
            ## Detail
            **primary** and *available*

            - Existante
            - Nouvelle construction

            1. First
            2. Second
            """);

        Assert.Contains("<h4>Levels</h4>", html, StringComparison.Ordinal);
        Assert.Contains("<h5>Detail</h5>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>primary</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>available</em>", html, StringComparison.Ordinal);
        Assert.Contains("<ul><li>Existante</li><li>Nouvelle construction</li></ul>", html, StringComparison.Ordinal);
        Assert.Contains("<ol><li>First</li><li>Second</li></ol>", html, StringComparison.Ordinal);
        AssertOnlyAllowedTags(html);
    }

    [Fact]
    public void A_pipe_table_renders_as_a_table()
    {
        var html = Render("""
            **Levels:**

            | Name | Elevation (m) | Elevation (ft) |
            | --- | --- | --- |
            | NIVEAU 1 | 12.930 | 42.421 |
            | NIVEAU 2 | 16.880 | 55.381 |

            **Phases:**

            - Existante
            - Nouvelle construction
            """);

        Assert.Contains("<div class=\"table-scroll\"><table><thead><tr><th>Name</th><th>Elevation (m)</th><th>Elevation (ft)</th></tr></thead><tbody><tr><td>NIVEAU 1</td><td>12.930</td><td>42.421</td></tr><tr><td>NIVEAU 2</td><td>16.880</td><td>55.381</td></tr></tbody></table></div>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Levels:</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<li>Existante</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("| --- |", html, StringComparison.Ordinal);
        AssertOnlyAllowedTags(html);
    }

    [Fact]
    public void Inline_and_fenced_code_are_escaped()
    {
        var html = Render("""
            Use `a < b` in the reply.

            ```
            </code></pre><script>alert(1)</script>
            ```
            """);

        Assert.Contains("<code>a &lt; b</code>", html, StringComparison.Ordinal);
        Assert.Contains("<pre><code>&lt;/code&gt;&lt;/pre&gt;&lt;script&gt;alert(1)&lt;/script&gt;</code></pre>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        AssertOnlyAllowedTags(html);
    }

    [Fact]
    public void Raw_script_remains_text()
    {
        var html = Render("<script>alert(1)</script>");

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        AssertOnlyAllowedTags(html);
    }

    [Fact]
    public void Active_markup_and_handlers_cannot_be_created()
    {
        var html = Render("""
            <img src=x onerror="alert(1)">
            <iframe src="https://example.invalid"></iframe>
            <form action="https://example.invalid"></form>
            <a href="javascript:alert(1)">open</a>
            [open](javascript:alert(1))
            ![chart](https://example.invalid/a.png)
            """);

        Assert.Contains("&lt;img src=x onerror=&quot;alert(1)&quot;&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;iframe src=&quot;https://example.invalid&quot;&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;form action=&quot;https://example.invalid&quot;&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", html, StringComparison.OrdinalIgnoreCase);
        var link = Render("[open](https://example.invalid/path)\n![chart](https://example.invalid/a.png)");
        Assert.Equal("<p>open<br>chart</p>", link);
        Assert.Contains("[open](javascript:alert(1))", Render("[open](javascript:alert(1))"), StringComparison.Ordinal);
        AssertOnlyAllowedTags(html);
    }

    [Fact]
    public void Malformed_markup_stays_readable()
    {
        var html = Render("**Levels\nNIVEAU_1 stays intact");

        Assert.Contains("**Levels", html, StringComparison.Ordinal);
        Assert.Contains("NIVEAU_1 stays intact", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<em>", html, StringComparison.Ordinal);
        Assert.Contains("<p>", html, StringComparison.Ordinal);
        AssertOnlyAllowedTags(html);
    }

    [Fact]
    public void Shell_policy_and_activity_surface_stay_unchanged()
    {
        var root = RepoRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Assistant", "Ui", "index.html"));
        var script = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Assistant", "Ui", "app.js"));
        var approval = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Approval", "Ui", "index.html"));
        var policy = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Assistant", "AssistantShellPolicy.cs"));

        Assert.Contains(
            "default-src 'none'; style-src 'self'; script-src 'self'; img-src 'self' data:",
            page,
            StringComparison.Ordinal);
        Assert.Contains("<script src=\"assistant-markdown.js\"></script>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("assistant-markdown.js", approval, StringComparison.Ordinal);
        Assert.Contains("public const bool HostObjectsAllowed = false;", policy, StringComparison.Ordinal);
        Assert.Contains("public const bool DevToolsEnabled = false;", policy, StringComparison.Ordinal);
        Assert.Contains("public static bool BlockNewWindow => true;", policy, StringComparison.Ordinal);
        Assert.Contains("timeline.insertAdjacentHTML(\"beforeend\", activityCard(status.activities));", script, StringComparison.Ordinal);
        Assert.Contains("<strong>Working</strong>", script, StringComparison.Ordinal);
        Assert.Contains("<p class=\"used\">${escapeHtml(message.used)}</p>", script, StringComparison.Ordinal);
        Assert.Contains("renderAssistantMarkdown(message.body || \"\")", script, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(script, "renderAssistantMarkdown\\("));
        Assert.Contains("<p class=\"plain\">${escapeHtml(message.body || \"\")}</p>", script, StringComparison.Ordinal);
        Assert.Contains("id=\"approval-approve\"", script, StringComparison.Ordinal);
        Assert.Contains("id=\"approval-reject\"", script, StringComparison.Ordinal);
        Assert.Contains("id=\"approval-details-toggle\"", script, StringComparison.Ordinal);
        Assert.Contains("escapeHtml(summary)", script, StringComparison.Ordinal);
        Assert.Contains("function renderPlacement", script, StringComparison.Ordinal);
        Assert.Contains("function renderPattern", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML = message.body", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_text_cannot_manufacture_an_approval_card()
    {
        var html = Render("<section class=\"approval\"><button id=\"approval-approve\">Approve</button></section>\n**Approve**");

        Assert.DoesNotContain("<button", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"approval\"", html, StringComparison.Ordinal);
        Assert.Contains("&lt;button id=&quot;approval-approve&quot;&gt;Approve&lt;/button&gt;", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Approve</strong>", html, StringComparison.Ordinal);
        AssertOnlyAllowedTags(html);
    }

    private static void AssertOnlyAllowedTags(string html)
    {
        foreach (Match match in Regex.Matches(html, @"<([A-Za-z0-9]+)([^>]*)>", RegexOptions.CultureInvariant))
        {
            var tag = match.Groups[1].Value;
            Assert.Contains(tag, AllowedTags, StringComparer.OrdinalIgnoreCase);
            var attributes = match.Groups[2].Value;
            if (string.Equals(tag, "div", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(" class=\"table-scroll\"", attributes, StringComparer.Ordinal);
            }
            else
            {
                Assert.Equal("", attributes, StringComparer.Ordinal);
            }
        }
    }

    private static string Render(string source)
    {
        var engine = new Engine(options => options.LimitRecursion(64).MaxStatements(100_000));
        engine.Execute(File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "Ui", "assistant-markdown.js")));
        return engine.Invoke("renderAssistantMarkdown", source).AsString();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
