using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;
using BimIt.Revit.Contracts;
using Microsoft.Extensions.AI;
using SkiaSharp;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class DocumentPageCalibrationTests
{
    private const string InstanceId = "11111111-2222-3333-4444-555555555555";

    private const string DocumentId = "doc-hidden-calibration";

    private const string TemplateRef = "tmpl-hidden-calibration";

    [Fact]
    public void Model_surface_stays_six_tools_and_names_both_coordinate_spaces()
    {
        Assert.Equal(6, AssistantModelTools.Names.Length);
        Assert.False(AssistantModelTools.IsExposed("calibratePlanPreview"));
        Assert.False(AssistantIpc.IsModelToolName("beginDocumentCalibration"));
        Assert.Contains("document_page", GitHubCopilotSessionBlueprint.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("project_offsets", GitHubCopilotSessionBlueprint.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("You cannot establish the drawing calibration.", GitHubCopilotSessionBlueprint.SystemPrompt, StringComparison.Ordinal);

        var tools = GitHubCopilotSessionBlueprint.CreateTools(
            new BimItAgentReadTools(new IdleSession(), InstanceId),
            new MepDevicePlacementProposalTool(new IdleSession(), InstanceId, new MepDevicePlacementProposalStore(), static () => AssistantAttachmentView.None));
        Assert.Equal(AssistantModelTools.Names, tools.Select(tool => tool.Name).ToArray());
        var schema = tools.Single(tool => tool.Name == MepDevicePlacementProposalTool.Name).JsonSchema.GetRawText();
        Assert.Contains("\"coordinate_space\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"page_number\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"page_x\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"page_y\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"offset_x\"", schema, StringComparison.Ordinal);
        foreach (var token in new[] { "instance_id", "document_id", "template_element_ref", "intent_ref", "element_ref", "artifact", "attachment", "\"state\"", "ready", "calibrated" })
        {
            Assert.DoesNotContain(token, schema, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Document_and_project_modes_reject_mixed_fields_and_a_missing_page()
    {
        var documents = Drawing(120, 60);
        var page = documents.Current!.VisualPages[0];
        var (tool, store, _) = Tool(documents);
        var accepted = await tool.InvokeAsync(Json(DocumentProposal(0.25, 0.5, 1)), CancellationToken.None);
        var proposal = Assert.IsType<Dictionary<string, object?>>(accepted["proposal"]);
        Assert.Equal("document_page", proposal["coordinate_space"]);
        Assert.Equal(1, proposal["page_number"]);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, proposal["state"]);
        Assert.Equal(MepDevicePlacementProposalReasons.DocumentCalibrationRequired, proposal["reason"]);
        var row = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object?>>(proposal["placements"]).Cast<Dictionary<string, object?>>());
        Assert.Equal(0.25, row["page_x"]);
        Assert.False(row.ContainsKey("offset_x"));
        Assert.Equal(page.RasterWidth, store.Current!.RasterWidth);
        AssertHidden(accepted);

        await Reject(tool, ProjectProposal(), MepDevicePlacementProposalFailures.CoordinateSpace);
        await Reject(tool, "{\"coordinate_space\":\"sheet\",\"length_unit\":\"m\",\"placements\":[{\"offset_x\":1,\"offset_y\":0,\"vertical_offset\":0}]}", MepDevicePlacementProposalFailures.CoordinateSpace);
        await Reject(tool, "{\"coordinate_space\":\"project_offsets\",\"length_unit\":\"m\",\"page_number\":1,\"placements\":[{\"offset_x\":1,\"offset_y\":0,\"vertical_offset\":0}]}", MepDevicePlacementProposalFailures.Mixed);
        await Reject(tool, "{\"coordinate_space\":\"document_page\",\"length_unit\":\"m\",\"page_number\":1,\"placements\":[{\"offset_x\":1,\"page_y\":0,\"vertical_offset\":0}]}", MepDevicePlacementProposalFailures.Mixed);
        await Reject(tool, "{\"coordinate_space\":\"document_page\",\"length_unit\":\"m\",\"page_number\":1,\"placements\":[{\"page_x\":1,\"page_y\":0,\"vertical_offset\":0,\"offset_y\":2}]}", MepDevicePlacementProposalFailures.Mixed);
        await Reject(tool, "{\"coordinate_space\":\"project_offsets\",\"length_unit\":\"m\",\"placements\":[{\"offset_x\":1,\"offset_y\":0,\"vertical_offset\":0,\"page_x\":0.2}]}", MepDevicePlacementProposalFailures.Mixed);
        await Reject(tool, "{\"coordinate_space\":\"document_page\",\"length_unit\":\"m\",\"page_number\":9,\"placements\":[{\"page_x\":0.2,\"page_y\":0.2,\"vertical_offset\":0}]}", MepDevicePlacementProposalFailures.Page);
        await Reject(tool, "{\"coordinate_space\":\"document_page\",\"length_unit\":\"m\",\"page_number\":1,\"placements\":[{\"page_x\":1.1,\"page_y\":0,\"vertical_offset\":0}]}", MepDevicePlacementProposalFailures.Range);
        await Reject(tool, "{\"coordinate_space\":\"document_page\",\"length_unit\":\"m\",\"placements\":[{\"page_x\":0.2,\"page_y\":0.2,\"vertical_offset\":0}]}", MepDevicePlacementProposalFailures.Shape);
        await Reject(tool, "{\"coordinate_space\":\"project_offsets\",\"length_unit\":\"m\",\"placements\":[{\"offset_x\":1,\"offset_y\":0,\"vertical_offset\":0}],\"note\":true}", MepDevicePlacementProposalFailures.Shape);
        Assert.Equal(page.PageNumber, store.Current.PageNumber);
    }

    [Fact]
    public async Task Attachment_replacement_invalidates_a_document_proposal_and_its_session()
    {
        var documents = Drawing(80, 40);
        var (tool, store, _) = Tool(documents);
        Assert.True((bool)(await tool.InvokeAsync(Json(DocumentProposal(0.2, 0.2, 0)), CancellationToken.None))["ok"]!);
        var session = store.OpenCalibrationSession(new string('a', 32), DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.NotNull(session);

        var previous = documents.Current!.ArtifactId;
        documents.Clear();
        store.NoteAttachmentIdentityChanged(previous, null);
        Assert.Null(store.Current);
        Assert.Null(store.Calibration);
    }

    [Fact]
    public void Similarity_covers_identity_scale_rotation_aspect_bounds_and_degenerates()
    {
        Assert.True(DocumentPageSimilarity.TryPagePixel(0, 1, 200, 100, out var origin));
        Assert.Equal(0, origin.X);
        Assert.Equal(0, origin.Y);
        Assert.True(DocumentPageSimilarity.TryPagePixel(0, 0, 200, 100, out var top));
        Assert.Equal(100, top.Y);
        Assert.True(DocumentPageSimilarity.TryPagePixel(1, 0, 200, 100, out var landscape));
        Assert.Equal(200, landscape.X);
        Assert.Equal(100, landscape.Y);
        Assert.True(DocumentPageSimilarity.TryPagePixel(1, 0, 100, 200, out var portrait));
        Assert.Equal(100, portrait.X);
        Assert.Equal(200, portrait.Y);
        Assert.False(DocumentPageSimilarity.TryPagePixel(-0.0000001, 0, 10, 10, out _));
        Assert.False(DocumentPageSimilarity.TryPagePixel(1.0000001, 0, 10, 10, out _));
        Assert.False(DocumentPageSimilarity.TryPagePixel(double.NaN, 0, 10, 10, out _));
        Assert.False(DocumentPageSimilarity.TryPagePixel(0, 0, 0, 10, out _));

        const int raster = 100;
        var anchor = new PagePixel(0, 0);
        var east = new PagePixel(8, 0);
        Assert.True(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, 8, 0, out var identity));
        Assert.True(identity.TryApply(3, 4, out var ix, out var iy));
        Assert.Equal(3, ix, 9);
        Assert.Equal(4, iy, 9);
        Assert.True(identity.ScaleSquared > 0);

        var shifted = new PagePixel(10, 5);
        Assert.True(DocumentPageSimilarity.TryCreate(shifted, new PagePixel(18, 5), raster, raster, 8, 0, out var translated));
        Assert.True(translated.TryApply(10, 5, out var zx, out var zy));
        Assert.Equal(0, zx, 9);
        Assert.Equal(0, zy, 9);
        Assert.True(translated.TryApply(11, 5, out var oneX, out var oneY));
        Assert.Equal(1, oneX, 9);
        Assert.Equal(0, oneY, 9);

        Assert.True(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, 16, 0, out var scaled));
        Assert.True(scaled.TryApply(1, 0, out var sx, out var sy));
        Assert.Equal(2, sx, 9);
        Assert.Equal(0, sy, 9);

        Assert.True(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, 0, 8, out var plus));
        Assert.True(plus.TryApply(1, 0, out var px, out var py));
        Assert.Equal(0, px, 9);
        Assert.Equal(1, py, 9);
        Assert.True(plus.TryApply(0, 1, out var leftX, out var leftY));
        Assert.Equal(-1, leftX, 9);
        Assert.Equal(0, leftY, 9);

        Assert.True(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, 0, -8, out var minus));
        Assert.True(minus.TryApply(1, 0, out var mx, out var my));
        Assert.Equal(0, mx, 9);
        Assert.Equal(-1, my, 9);

        Assert.True(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, 0, 16, out var combined));
        Assert.True(combined.TryApply(2, 0, out var cx, out var cy));
        Assert.Equal(0, cx, 9);
        Assert.Equal(4, cy, 9);
        Assert.True(combined.TryApply(0, 2, out var c2x, out var c2y));
        Assert.Equal(-4, c2x, 9);
        Assert.Equal(0, c2y, 9);

        Assert.False(DocumentPageSimilarity.TryCreate(anchor, anchor, raster, raster, 1, 0, out _));
        Assert.False(DocumentPageSimilarity.TryCreate(anchor, new PagePixel(0.5, 0), raster, raster, 1, 0, out _));
        Assert.False(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, 0, 0, out _));
        Assert.False(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, double.PositiveInfinity, 0, out _));
        Assert.False(DocumentPageSimilarity.TryCreate(new PagePixel(double.NaN, 0), east, raster, raster, 1, 0, out _));

        Assert.True(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, 800, 0, out var wide));
        var outside = new List<MepDevicePlacementProposalItem>
        {
            new(0, 0, 0, null, null, 0.02, 1),
        };
        Assert.False(DocumentPagePlacementTransform.TryTransform(wide, raster, raster, "m", outside, out _, out var range));
        Assert.Equal(MepDevicePlacementProposalFailures.Range, range);

        Assert.True(DocumentPageSimilarity.TryCreate(anchor, east, raster, raster, 8, 0, out var unit));
        var duplicate = new List<MepDevicePlacementProposalItem>
        {
            new(0, 0, 0, null, null, 0.5, 1),
            new(0, 0, 0, null, null, 0.5, 1),
        };
        Assert.False(DocumentPagePlacementTransform.TryTransform(unit, raster, raster, "m", duplicate, out _, out var duplicateCode));
        Assert.Equal(MepDevicePlacementProposalFailures.Duplicate, duplicateCode);
    }

    [Fact]
    public void Separation_rejects_under_eight_pixels_or_one_percent_of_the_long_edge()
    {
        Assert.Equal(8d, DocumentPageSimilarity.MinimumSeparation(100, 40));
        Assert.False(DocumentPageSimilarity.TryCreate(new PagePixel(0, 0), new PagePixel(Math.BitDecrement(8d), 0), 100, 40, 1, 0, out _));
        Assert.True(DocumentPageSimilarity.TryCreate(new PagePixel(0, 0), new PagePixel(8, 0), 100, 40, 1, 0, out _));

        var largeMinimum = DocumentPageSimilarity.MinimumSeparation(2_000, 800);
        Assert.True(largeMinimum > DocumentPageSimilarity.AbsoluteMinimumSeparationPixels);
        Assert.Equal(2_000 * DocumentPageSimilarity.LongEdgeSeparationFraction, largeMinimum);
        Assert.False(DocumentPageSimilarity.TryCreate(new PagePixel(0, 0), new PagePixel(Math.BitDecrement(largeMinimum), 0), 2_000, 800, 1, 0, out _));
        Assert.True(DocumentPageSimilarity.TryCreate(new PagePixel(0, 0), new PagePixel(largeMinimum, 0), 2_000, 800, 1, 0, out var boundary));
        Assert.True(boundary.TryApply(largeMinimum, 0, out var boundaryX, out var boundaryY));
        Assert.Equal(1, boundaryX, 9);
        Assert.Equal(0, boundaryY, 9);
    }

    [Fact]
    public async Task Close_source_points_leave_the_proposal_uncalibrated_and_create_no_preview()
    {
        var documents = Drawing(100, 50);
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var (tool, store, session) = Tool(documents);
        Assert.True((bool)(await tool.InvokeAsync(Json(DocumentProposal(0.5, 1, 0)), CancellationToken.None))["ok"]!);
        var preview = new EchoPreview();
        var orchestrator = new DocumentPageCalibrationOrchestrator(session, preview, store, documents, InstanceId, () => now);
        var began = await orchestrator.BeginAsync(CancellationToken.None);
        Assert.NotNull(began.Page);

        var outcome = await orchestrator.CompleteAsync(began.Page.SessionRef, 0, 1, 0.001, 1, 10, 0, false, CancellationToken.None);
        Assert.True(outcome.IsError);
        Assert.Contains("too close", outcome.Body, StringComparison.Ordinal);
        Assert.Empty(preview.Requests);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current!.State);
        Assert.Null(store.Calibration);
    }

    [Fact]
    public async Task Successful_calibration_promotes_and_previews_once()
    {
        var documents = Drawing(100, 50);
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var (tool, store, session) = Tool(documents);
        Assert.True((bool)(await tool.InvokeAsync(Json(DocumentProposal(0.5, 1, 0)), CancellationToken.None))["ok"]!);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current!.State);
        var preview = new EchoPreview();
        var orchestrator = new DocumentPageCalibrationOrchestrator(session, preview, store, documents, InstanceId, () => now);
        var began = await orchestrator.BeginAsync(CancellationToken.None);
        Assert.NotNull(began.Page);
        Assert.Equal(DocumentId, began.Page.DocumentId);
        Assert.DoesNotContain(DocumentId, began.Page.ImageBase64, StringComparison.Ordinal);
        Assert.True(Convert.FromBase64String(began.Page.ImageBase64).Length <= DocumentCalibrationPreview.MaxPreviewBytes);
        var web = AssistantCalibrationPage(began.Page);
        Assert.DoesNotContain(DocumentId, web, StringComparison.Ordinal);
        Assert.DoesNotContain(TemplateRef, web, StringComparison.Ordinal);
        Assert.DoesNotContain(InstanceId, web, StringComparison.Ordinal);

        var projectPreview = new MepDevicePlacementPreviewOrchestrator(session, preview, store, InstanceId, () => now);
        var wrong = await projectPreview.ConfirmAsync(CancellationToken.None);
        Assert.True(wrong.IsError);
        Assert.Empty(preview.Requests);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current.State);

        var outcome = await orchestrator.CompleteAsync(began.Page.SessionRef, 0, 1, 1, 1, 10, 0, false, CancellationToken.None);
        Assert.False(outcome.IsError);
        Assert.Contains("Preview ready: yes", outcome.Body, StringComparison.Ordinal);
        Assert.Contains("Drawing page calibrated.", outcome.Body, StringComparison.Ordinal);
        AssertHidden(outcome.Body);
        var request = Assert.Single(preview.Requests);
        Assert.Equal(5, request.Placements[0].OffsetX, 6);
        Assert.Equal(0, request.Placements[0].OffsetY, 6);
        Assert.Equal(MepDevicePlacementProposalStates.Ready, store.Current.State);
        Assert.Null(store.Calibration);

        var again = await orchestrator.BeginAsync(CancellationToken.None);
        Assert.Null(again.Page);
        Assert.Contains("Preview ready: yes", again.Outcome.GetValueOrDefault().Body, StringComparison.Ordinal);
        Assert.Single(preview.Requests);

        store.RememberTerminalApply(store.Preview!.IntentRef, "Applied 1 placements. Revit verification passed.", false);
        var blocked = await orchestrator.BeginAsync(CancellationToken.None);
        Assert.True(blocked.Outcome.GetValueOrDefault().IsError);
        Assert.Contains(MepDevicePlacementPreviewOrchestrator.TerminalGenerationMessage, blocked.Outcome.GetValueOrDefault().Body, StringComparison.Ordinal);
        Assert.Single(preview.Requests);
        var blockedPreview = await projectPreview.ConfirmAsync(CancellationToken.None);
        Assert.True(blockedPreview.IsError);
        Assert.Single(preview.Requests);
    }

    [Fact]
    public async Task Expired_mismatched_cancelled_and_unbound_calibration_does_not_preview()
    {
        var documents = Drawing(100, 50);
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var clock = now;
        var (tool, store, session) = Tool(documents);
        Assert.True((bool)(await tool.InvokeAsync(Json(DocumentProposal(0.2, 1, 0)), CancellationToken.None))["ok"]!);
        var preview = new EchoPreview();
        var orchestrator = new DocumentPageCalibrationOrchestrator(session, preview, store, documents, InstanceId, () => clock);
        var began = await orchestrator.BeginAsync(CancellationToken.None);
        clock = now.AddMinutes(6);
        var expired = await orchestrator.CompleteAsync(began.Page!.SessionRef, 0, 1, 1, 1, 1, 0, false, CancellationToken.None);
        Assert.True(expired.IsError);
        Assert.Empty(preview.Requests);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current!.State);

        clock = now;
        began = await orchestrator.BeginAsync(CancellationToken.None);
        var mismatch = await orchestrator.CompleteAsync(new string('b', 32), 0, 1, 1, 1, 1, 0, false, CancellationToken.None);
        Assert.True(mismatch.IsError);
        Assert.Empty(preview.Requests);

        began = await orchestrator.BeginAsync(CancellationToken.None);
        var cancelled = await orchestrator.CompleteAsync(began.Page!.SessionRef, 0, 1, 1, 1, 4, 0, true, CancellationToken.None);
        Assert.True(cancelled.IsError);
        Assert.Contains("No preview was created.", cancelled.Body, StringComparison.Ordinal);
        Assert.Empty(preview.Requests);
        Assert.Null(store.Calibration);

        session.SelectionJson = Selection(0, false, "[]");
        began = await orchestrator.BeginAsync(CancellationToken.None);
        Assert.True(began.Outcome.GetValueOrDefault().IsError);
        Assert.Null(began.Page);
        Assert.Empty(preview.Requests);
    }

    [Fact]
    public async Task New_chat_and_shutdown_clear_document_calibration()
    {
        var store = new MepDevicePlacementProposalStore();
        store.Replace(DocumentProposalState("artifact-1"));
        Assert.NotNull(store.OpenCalibrationSession(new string('c', 32), DateTimeOffset.UtcNow.AddMinutes(5)));
        var runtime = new QuietRuntime();
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            new IdleSession(),
            runtime,
            providerTimeouts: new AssistantProviderTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)),
            proposals: store);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(AssistantIpcKind.Hello, InstanceId, "2026", "26.5.0.55", true, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(AssistantIpcKind.ResetConversation, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);
        Assert.Null(store.Current);
        Assert.Null(store.Calibration);

        store.Replace(DocumentProposalState("artifact-2"));
        Assert.NotNull(store.OpenCalibrationSession(new string('d', 32), DateTimeOffset.UtcNow.AddMinutes(5)));
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.Equal(0, await host);
        Assert.Null(store.Current);
        Assert.Null(store.Calibration);
    }

    [Fact]
    public void Calibration_ipc_rejects_identity_on_the_completion_and_paths_on_the_page()
    {
        const string session = "0123456789abcdef0123456789abcdef";
        var page = AssistantIpc.Parse(
            "{\"type\":\"calibrationPage\",\"instanceId\":\"" + InstanceId + "\",\"calibrationSession\":\"" + session + "\",\"calibrationMime\":\"image/png\",\"calibrationImage\":\"qqqq\",\"rasterWidth\":10,\"rasterHeight\":5,\"boundDocumentId\":\"doc-a\",\"boundTemplateRef\":\"tmpl-a\"}");
        Assert.True(page.Accepted);
        Assert.Equal(AssistantIpcKind.CalibrationPage, page.Message!.Kind);
        Assert.False(AssistantIpc.Parse(
            "{\"type\":\"calibrationPage\",\"instanceId\":\"" + InstanceId + "\",\"calibrationSession\":\"" + session + "\",\"calibrationMime\":\"image/png\",\"calibrationImage\":\"qqqq\",\"rasterWidth\":10,\"rasterHeight\":5,\"boundDocumentId\":\"doc-a\",\"boundTemplateRef\":\"tmpl-a\",\"path\":\"C:\\\\secret\\\\plan.pdf\"}").Accepted);
        Assert.False(AssistantIpc.Parse(
            "{\"type\":\"completeDocumentCalibration\",\"calibrationSession\":\"" + session + "\",\"sourceAX\":0,\"sourceAY\":1,\"sourceBX\":1,\"sourceBY\":1,\"targetDeltaXMetres\":1,\"targetDeltaYMetres\":0,\"boundDocumentId\":\"doc-a\"}").Accepted);
        Assert.False(AssistantIpc.IsModelToolName("revit_preview_mep_device_placements"));
    }

    private static async Task Reject(MepDevicePlacementProposalTool tool, string json, string reason)
    {
        var result = await tool.InvokeAsync(Json(json), CancellationToken.None);
        Assert.False((bool)result["ok"]!);
        Assert.Equal(reason, result["reason"]);
    }

    private static string DocumentProposal(double x, double y, double vertical)
    {
        return FormattableString.Invariant(
            $"{{\"coordinate_space\":\"document_page\",\"length_unit\":\"m\",\"page_number\":1,\"placements\":[{{\"page_x\":{x},\"page_y\":{y},\"vertical_offset\":{vertical}}}]}}");
    }

    private static string ProjectProposal()
    {
        return "{\"length_unit\":\"m\",\"placements\":[{\"offset_x\":1,\"offset_y\":0,\"vertical_offset\":0}]}";
    }

    private static MepDevicePlacementProposal DocumentProposalState(string artifactId)
    {
        return MepDevicePlacementProposal.Create(
            "m",
            [new MepDevicePlacementProposalItem(0, 0, 0, null, null, 0.5, 1)],
            MepDevicePlacementProposalReasons.DocumentCalibrationRequired,
            InstanceId,
            DocumentId,
            TemplateRef,
            artifactId,
            MepDevicePlacementCoordinateSpaces.DocumentPage,
            1,
            100,
            50);
    }

    private static (MepDevicePlacementProposalTool Tool, MepDevicePlacementProposalStore Store, SelectionSession Session) Tool(AssistantDocumentStore documents)
    {
        var session = new SelectionSession(Selection(1, false, "[\"" + TemplateRef + "\"]"));
        var store = new MepDevicePlacementProposalStore();
        var tool = new MepDevicePlacementProposalTool(session, InstanceId, store, () => AssistantAttachmentView.From(documents.Current));
        return (tool, store, session);
    }

    private static AssistantDocumentStore Drawing(int width, int height)
    {
        var directory = Directory.CreateTempSubdirectory("bimit-doc-cal-").FullName;
        var path = Path.Combine(directory, "plan.png");
        using (var bitmap = new SKBitmap(width, height))
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }

        var store = new AssistantDocumentStore();
        var read = store.Attach(path);
        Assert.NotNull(read.Artifact);
        return store;
    }

    private static string Selection(int count, bool truncated, string refs)
    {
        return "{\"context\":{\"document_id\":\"" + DocumentId + "\",\"instance_id\":\"wire-instance\"},\"selected_count\":" + count + ",\"truncated\":" + (truncated ? "true" : "false") + ",\"element_refs\":" + refs + "}";
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void AssertHidden(object value)
    {
        var json = value as string ?? JsonSerializer.Serialize(value);
        Assert.DoesNotContain(DocumentId, json, StringComparison.Ordinal);
        Assert.DoesNotContain(TemplateRef, json, StringComparison.Ordinal);
        Assert.DoesNotContain(InstanceId, json, StringComparison.Ordinal);
        Assert.DoesNotContain("intent", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\", json, StringComparison.Ordinal);
    }

    private static string AssistantCalibrationPage(CalibrationPagePayload page)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "calibrationPage",
            ["sessionRef"] = page.SessionRef,
            ["mimeType"] = page.MimeType,
            ["image"] = page.ImageBase64,
            ["rasterWidth"] = page.RasterWidth,
            ["rasterHeight"] = page.RasterHeight,
        });
    }

    private sealed class SelectionSession(string selectionJson) : IAssistantToolSession
    {
        public string SelectionJson { get; set; } = selectionJson;

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 14, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Assert.Equal(AssistantTools.GetSelection, toolName);
            using var document = JsonDocument.Parse(SelectionJson);
            return Task.FromResult(new AssistantToolCall(false, null, null, document.RootElement.Clone()));
        }
    }

    private sealed class EchoPreview : IMepDevicePlacementPreviewClient
    {
        public List<PreviewMepDevicePlacementsRequest> Requests { get; } = [];

        public Task<AssistantToolCall> PreviewAsync(PreviewMepDevicePlacementsRequest request, string instanceId, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var result = new PreviewMepDevicePlacementsResult
            {
                Context = new PreviewMepDevicePlacementsContext { InstanceId = instanceId, DocumentId = request.DocumentId },
                Ready = true,
                Template = new PreviewMepDeviceTemplate
                {
                    ElementRef = request.TemplateElementRef,
                    Status = PreviewMepDeviceTemplateStatus.Ok,
                    PlacementMode = PreviewMepDevicePlacementMode.LevelBased,
                    FamilyName = "HeatRecoveryUnit",
                    TypeName = "Heat Recovery Unit (HRU)",
                    CategoryName = "Mechanical Equipment",
                    LevelName = "L2",
                },
                Items = request.Placements.Select(item => new PreviewMepDevicePlacementItem
                {
                    OffsetX = item.OffsetX,
                    OffsetY = item.OffsetY,
                    VerticalOffset = item.VerticalOffset,
                    Status = PreviewMepDevicePlacementItemStatus.Ok,
                }).ToArray(),
                IntentRef = "intent-hidden",
                IntentFingerprint = "fingerprint-hidden",
                ExpiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            };
            var json = JsonSerializer.Serialize(result, ContractJson.Options);
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(new AssistantToolCall(false, null, null, document.RootElement.Clone()));
        }
    }

    private sealed class IdleSession : IAssistantToolSession
    {
        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 14, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantToolCall(true, "unused", "unused", null));
        }
    }

    private sealed class QuietRuntime : IAssistantAgentRuntime
    {
        public AssistantProviderStatus Status { get; } = new("GitHub Copilot", AssistantProviderState.Ready, "auto", null);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", "Noted.", false));
        }

        public Task ResetConversationAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
