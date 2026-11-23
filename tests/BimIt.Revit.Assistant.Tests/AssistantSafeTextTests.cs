using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantSafeTextTests
{
    [Fact]
    public void Clean_keeps_markdown_line_breaks()
    {
        var cleaned = AssistantSafeText.Clean("| Name | Elevation |\n|---|---:|\n| NIVEAU 1 | 12,930 mm |\n\n- Existante\n- Nouvelle construction", 16_000);

        Assert.Contains("|---|---:|", cleaned, StringComparison.Ordinal);
        Assert.Contains("\n- Existante\n- Nouvelle construction", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_still_redacts_secrets_around_line_breaks()
    {
        var cleaned = AssistantSafeText.Clean("before\nghp_abcdefghijklmnopqrst\nafter", 16_000);

        Assert.Contains("\n[redacted]\nafter", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", cleaned, StringComparison.Ordinal);
    }
}
