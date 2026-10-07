using System.Buffers.Binary;
using System.Text.Json;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Intents;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ApprovalUiFreshnessTests
{
    [Fact]
    public void WebView_user_data_folder_is_stable_for_one_host_and_unique_across_hosts()
    {
        var first = new ApprovalWebViewUserData();
        var second = new ApprovalWebViewUserData();
        var pidOnly = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitMCP",
            "ApprovalUi",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(first.FolderPath, first.FolderPath);
        Assert.NotEqual(first.FolderPath, second.FolderPath);
        Assert.StartsWith(pidOnly + "-", first.FolderPath, StringComparison.Ordinal);
        Assert.StartsWith(pidOnly + "-", second.FolderPath, StringComparison.Ordinal);
        Assert.NotEqual(pidOnly, first.FolderPath);

        var host = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "ApprovalPaneHost.cs"));
        Assert.Contains("private readonly ApprovalWebViewUserData _userData = new();", host, StringComparison.Ordinal);
        Assert.Contains("_userData.FolderPath", host, StringComparison.Ordinal);
        Assert.DoesNotContain("public string Token", host, StringComparison.Ordinal);
        Assert.DoesNotContain("public string FolderPath", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundled_ui_revision_is_shared_by_navigation_and_html_assets()
    {
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "Ui", "index.html"));
        var host = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "ApprovalPaneHost.cs"));
        var project = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "RevitMCP.Addin.csproj"));
        var revision = ApprovalContent.AssetRevision;

        Assert.False(string.IsNullOrWhiteSpace(revision));
        Assert.Contains("index.html?rev=" + revision, ApprovalContent.IndexUri, StringComparison.Ordinal);
        Assert.True(ApprovalOrigin.IsLocal(ApprovalContent.IndexUri));
        Assert.Contains("_core.Navigate(ApprovalContent.IndexUri)", host, StringComparison.Ordinal);
        Assert.Contains("app.js?rev=" + revision, html, StringComparison.Ordinal);
        Assert.Contains("app.css?rev=" + revision, html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"app.css\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"app.js\"", html, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory=\"Always\"", project, StringComparison.Ordinal);
        Assert.Contains("RefreshApprovalUiAssets", project, StringComparison.Ordinal);
        Assert.Contains("SkipUnchangedFiles=\"false\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval\\Ui\\index.html\" Link=\"ui\\index.html\" CopyToOutputDirectory=\"PreserveNewest\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval\\Ui\\app.js\" Link=\"ui\\app.js\" CopyToOutputDirectory=\"PreserveNewest\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval\\Ui\\app.css\" Link=\"ui\\app.css\" CopyToOutputDirectory=\"PreserveNewest\"", project, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("CoreWebView2HostResourceAccessKind.DenyCors", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Authoritative_render_json_keeps_autodesk_before_and_proposed_value_apart()
    {
        var clock = new ManualTimeProvider();
        var store = new EphemeralWriteIntentStore(clock, () => Bytes(9));
        var provider = new RevitLocalApprovalProviderStateMachine(store, clock, () => "session");
        var controller = new RevitLocalApprovalInteractionController(store, provider);
        var created = store.TryCreate(new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = "doc-a",
            Items =
            [
                new IntentItemDraft
                {
                    RequestPosition = 1,
                    ElementRef = "element-a",
                    ParameterRef = "parameter-a",
                    Source = "instance",
                    IdentityKind = DescribeParameterIdentityKind.BuiltIn,
                    ParameterTypeId = "autodesk.revit.parameter:projectAuthor-1.0.0",
                    StableKey = "built-in:projectAuthor",
                    Status = "ok",
                    ElementName = "Project Information",
                    CategoryName = "Project Information",
                    ParameterName = "Author",
                    DataTypeKind = DescribeParameterDataTypeKind.Spec,
                    ForgeTypeId = "autodesk.spec:spec.string-2.0.0",
                    BeforeHasValue = true,
                    BeforeValue = new IntentTypedValue.StringValue("Autodesk"),
                    Proposed = new IntentTypedValue.StringValue("RevitMCP-PR67"),
                },
            ],
        });
        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);

        var review = controller.BeginReview(created.IntentRef, "doc-a");
        Assert.Equal(ApprovalReviewStatus.Started, review.Status);
        var json = ApprovalReviewJson.SerializeRender(review.RenderModel!);
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("items")[0];
        var before = item.GetProperty("before");
        var proposed = item.GetProperty("proposed");

        Assert.True(before.GetProperty("hasValue").GetBoolean());
        Assert.Equal("string", before.GetProperty("value").GetProperty("kind").GetString());
        Assert.Equal("Autodesk", before.GetProperty("value").GetProperty("value").GetString());
        Assert.Equal("string", proposed.GetProperty("kind").GetString());
        Assert.Equal("RevitMCP-PR67", proposed.GetProperty("value").GetString());
        Assert.False(proposed.TryGetProperty("hasValue", out _));
        Assert.DoesNotContain("intent_ref", json, StringComparison.Ordinal);
        Assert.DoesNotContain("intent_fingerprint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundled_page_renders_current_and_proposed_from_the_authoritative_item()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "Ui", "app.js"));

        Assert.Contains("item.before", script, StringComparison.Ordinal);
        Assert.Contains("item.before.hasValue", script, StringComparison.Ordinal);
        Assert.Contains("item.before.value", script, StringComparison.Ordinal);
        Assert.Contains("item.proposed", script, StringComparison.Ordinal);
        Assert.Contains("textLine(\"Current value\"", script, StringComparison.Ordinal);
        Assert.Contains("textLine(\"Proposed value\", formatValue(item.proposed))", script, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("serviceWorker", script, StringComparison.Ordinal);
    }

    private static byte[] Bytes(byte marker)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes, marker);
        bytes[4] = marker;
        return bytes;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
