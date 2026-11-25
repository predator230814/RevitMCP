using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantProposalRegistryTests
{
    private const string InstanceId = "inst-registry";
    private const string DocumentId = "doc-registry";
    private const string TemplateRef = "template-registry";

    [Fact]
    public void First_proposal_becomes_current_and_promotion_keeps_its_generation()
    {
        var (registry, mep, _) = Pair();
        mep.Replace(Placement("artifact-a", 1000));

        var current = registry.Current;
        Assert.NotNull(current);
        Assert.Equal(AssistantProposalKind.MepDevicePlacement, current.Kind);
        Assert.Equal(1, current.Generation);
        Assert.Equal("artifact-a", current.SourceArtifactId);
        Assert.True(registry.IsCurrent(mep.Authority));
        Assert.True(mep.HasCurrentAuthority);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, mep.PresentationState);

        Assert.True(mep.TryPromoteToReady(new TrustedProjectAxisCalibration()));
        Assert.Equal(1, registry.Current!.Generation);
        Assert.Equal(MepDevicePlacementProposalStates.Ready, mep.PresentationState);
        Assert.True(registry.IsCurrent(mep.Authority));
    }

    [Fact]
    public void Same_kind_replacement_invalidates_the_old_generation()
    {
        var (registry, mep, _) = Pair();
        mep.Replace(Placement("artifact-a", 1000));
        var first = mep.Authority;
        mep.RememberPreview("intent-old", "fingerprint-old", DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 1");

        mep.Replace(Placement("artifact-a", 2500));

        Assert.False(registry.IsCurrent(first));
        Assert.True(first.Generation < mep.Authority.Generation);
        Assert.True(registry.IsCurrent(mep.Authority));
        Assert.Equal(mep.Authority.Generation, registry.Current!.Generation);
        Assert.Equal(2500, mep.Current!.Placements[0].OffsetX);
        Assert.Null(mep.Preview);
    }

    [Fact]
    public void Mep_followed_by_room_pattern_invalidates_mep_authority()
    {
        var (registry, mep, patterns) = Pair();
        mep.Replace(Placement("artifact-a", 1000));
        var mepAuthority = mep.Authority;
        patterns.Replace(Pattern("artifact-b"), "{}", Generation());

        Assert.False(registry.IsCurrent(mepAuthority));
        Assert.False(mep.HasCurrentAuthority);
        Assert.Null(mep.PresentationState);
        Assert.True(patterns.HasCurrentAuthority);
        Assert.Equal(AssistantProposalKind.RoomPattern, registry.Current!.Kind);
        Assert.Equal("artifact-b", registry.Current.SourceArtifactId);
    }

    [Fact]
    public void Room_pattern_followed_by_mep_invalidates_room_pattern_authority()
    {
        var (registry, mep, patterns) = Pair();
        patterns.Replace(Pattern("artifact-b"), "{}", Generation());
        var patternAuthority = patterns.Authority;
        mep.Replace(Placement("artifact-a", 1000));

        Assert.False(registry.IsCurrent(patternAuthority));
        Assert.False(patterns.HasCurrentAuthority);
        Assert.True(mep.HasCurrentAuthority);
        Assert.Equal(AssistantProposalKind.MepDevicePlacement, registry.Current!.Kind);
    }

    [Fact]
    public void Displaced_room_pattern_review_is_not_emitted()
    {
        var (registry, mep, patterns) = Pair();
        patterns.Replace(Pattern("artifact-b"), "{\"rooms\":[]}", Generation());
        Assert.Equal("{\"rooms\":[]}", patterns.PeekReviewJson());

        mep.Replace(Placement("artifact-a", 1000));

        Assert.False(registry.IsCurrent(patterns.Authority));
        Assert.Null(patterns.PeekReviewJson());
        Assert.Equal("artifact-b", patterns.CurrentRecord!.SourceArtifactId);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, mep.PresentationState);
    }

    [Fact]
    public async Task Stale_mep_state_cannot_preview_review_or_apply()
    {
        var (registry, mep, patterns) = Pair();
        mep.Replace(Placement("artifact-a", 1000));
        Assert.True(mep.RememberPreview("intent-old", "fingerprint-old", DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 1"));
        patterns.Replace(Pattern("artifact-b"), "{}", Generation());
        Assert.NotNull(mep.Current);
        Assert.NotNull(mep.Preview);
        Assert.False(registry.IsCurrent(mep.Authority));

        var session = new RecordingSession();
        var preview = new ThrowingPreview();
        var review = new CountingReview();
        var apply = new CountingApply();
        var previewOutcome = await new MepDevicePlacementPreviewOrchestrator(session, preview, mep, InstanceId)
            .ConfirmAsync(CancellationToken.None);
        var reviewOutcome = await new MepDevicePlacementReviewApplyOrchestrator(review, apply, mep, InstanceId)
            .ReviewAsync(CancellationToken.None);
        var applyOutcome = await new MepDevicePlacementReviewApplyOrchestrator(review, apply, mep, InstanceId)
            .ApplyAsync(CancellationToken.None);

        Assert.Equal(MepDevicePlacementProposalStore.StaleAuthorityMessage, previewOutcome.Body);
        Assert.Equal(MepDevicePlacementProposalStore.StaleAuthorityMessage, reviewOutcome.Body);
        Assert.Equal(MepDevicePlacementProposalStore.StaleAuthorityMessage, applyOutcome.Body);
        Assert.True(previewOutcome.IsError);
        Assert.True(reviewOutcome.IsError);
        Assert.True(applyOutcome.IsError);
        Assert.Empty(session.Tools);
        Assert.Equal(0, preview.Calls);
        Assert.Empty(review.Calls);
        Assert.Empty(apply.Calls);
        Assert.False(mep.RememberPreview("intent-next", "fingerprint-next", DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 1"));
        Assert.False(mep.TryPromoteToReady(new TrustedProjectAxisCalibration()));
    }

    [Fact]
    public async Task Stale_room_pattern_state_cannot_preview_review_or_apply()
    {
        var (_, mep, patterns) = Pair();
        var generation = Generation();
        patterns.Replace(Pattern("artifact-b"), "{}", generation);
        patterns.RememberPreview(generation.GenerationId, "intent-pattern", 2);
        mep.Replace(Placement("artifact-a", 1000));
        Assert.NotNull(patterns.CurrentRecord);
        Assert.Equal(generation.GenerationId, patterns.CurrentGeneration!.GenerationId);
        Assert.False(patterns.HasCurrentAuthority);

        var preview = new ThrowingPatternPreview();
        var review = new CountingReview();
        var apply = new CountingApply();
        var orchestrator = new RoomPatternPlacementOrchestrator(preview, review, apply, patterns, InstanceId);
        var prepare = await orchestrator.PrepareAsync(null, CancellationToken.None);
        var reviewed = await orchestrator.ReviewAsync(CancellationToken.None);
        var applied = await orchestrator.ApplyAsync(CancellationToken.None);

        Assert.Equal(RoomPatternStore.StaleAuthorityMessage, prepare.Body);
        Assert.Equal(RoomPatternStore.StaleAuthorityMessage, reviewed.Body);
        Assert.Equal(RoomPatternStore.StaleAuthorityMessage, applied.Body);
        Assert.Equal(0, preview.Calls);
        Assert.Empty(review.Calls);
        Assert.Empty(apply.Calls);
        Assert.False(patterns.TryPreview(out _, out _));
        Assert.False(patterns.TryCurrentPreparation(out _, out _, out var failure));
        Assert.Equal(RoomPatternStore.StaleAuthorityMessage, failure);
    }

    [Fact]
    public void Attachment_invalidation_affects_only_a_proposal_bound_to_that_artifact()
    {
        var (registry, mep, patterns) = Pair();
        mep.Replace(Placement("artifact-a", 1000));
        patterns.Replace(Pattern("artifact-b"), "{}", Generation());
        var patternAuthority = patterns.Authority;

        mep.NoteAttachmentIdentityChanged("artifact-a", "artifact-c");
        patterns.NoteAttachmentIdentityChanged("artifact-a", "artifact-c");

        Assert.Null(mep.Current);
        Assert.Equal("artifact-b", patterns.CurrentRecord!.SourceArtifactId);
        Assert.True(registry.IsCurrent(patternAuthority));
        Assert.Equal("artifact-b", registry.Current!.SourceArtifactId);

        mep.Replace(Placement(null, 1000));
        var unbound = mep.Authority;
        mep.NoteAttachmentIdentityChanged("artifact-b", null);
        patterns.NoteAttachmentIdentityChanged("artifact-b", null);
        Assert.True(registry.IsCurrent(unbound));
        Assert.Null(patterns.CurrentRecord);
        Assert.NotNull(mep.Current);

        mep.Replace(Placement("artifact-a", 1000));
        mep.NoteAttachmentIdentityChanged("artifact-a", null);
        patterns.NoteAttachmentIdentityChanged("artifact-a", null);
        Assert.Null(registry.Current);
        Assert.Null(mep.Current);
    }

    [Fact]
    public async Task Reset_clears_current_authority()
    {
        var (registry, mep, patterns) = Pair();
        mep.Replace(Placement("artifact-a", 1000));
        var stale = mep.Authority;
        await RunHostAsync(registry, mep, patterns, AssistantIpcKind.ResetConversation);

        Assert.Null(registry.Current);
        Assert.Null(mep.Current);
        Assert.False(patterns.HasDomainPayload);
        Assert.False(registry.IsCurrent(stale));
        mep.Replace(Placement("artifact-a", 1000));
        Assert.Equal(3, mep.Authority.Generation);
        Assert.True(registry.IsCurrent(mep.Authority));
    }

    [Fact]
    public async Task Shutdown_clearing_is_deterministic()
    {
        var first = ShutdownGeneration();
        var second = ShutdownGeneration();
        Assert.Equal(first, second);
        Assert.Equal(4, first);

        var (registry, mep, patterns) = Pair();
        mep.Replace(Placement("artifact-a", 1000));
        var stale = mep.Authority;
        await RunHostAsync(registry, mep, patterns, AssistantIpcKind.Shutdown);

        Assert.Null(registry.Current);
        Assert.Null(mep.Current);
        Assert.False(patterns.HasDomainPayload);
        Assert.False(registry.IsCurrent(stale));
        mep.Clear();
        patterns.Clear();
        Assert.Null(registry.Current);
        mep.Replace(Placement("artifact-a", 1000));
        Assert.Equal(3, mep.Authority.Generation);
    }

    [Fact]
    public void Domain_payloads_stay_in_their_stores_and_the_registry_has_no_intent_authority()
    {
        var (registry, mep, patterns) = Pair();
        mep.Replace(Placement("artifact-a", 1000));
        patterns.Replace(Pattern("artifact-b"), "{\"page\":1}", Generation());

        Assert.Equal(1000, mep.Current!.Placements[0].OffsetX);
        Assert.Equal(DocumentId, mep.Current.DocumentId);
        Assert.Equal(TemplateRef, mep.Current.TemplateElementRef);
        Assert.Equal("artifact-b", patterns.CurrentRecord!.SourceArtifactId);
        Assert.Equal(2, patterns.CurrentRecord.DeviceCount);
        Assert.NotNull(patterns.PeekReviewJson());

        var registrationNames = typeof(AssistantProposalRegistration).GetProperties().Select(item => item.Name).ToArray();
        Assert.Equal(
            ["Kind", "Generation", "SourceArtifactId"],
            registrationNames);
        var surface = string.Join(
            " ",
            new[] { typeof(AssistantProposalRegistry), typeof(AssistantProposalRegistration), typeof(AssistantProposalAuthority) }
                .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                .Select(member => member.Name));
        foreach (var banned in new[] { "Intent", "Approval", "Audit", "Document", "Element", "Fingerprint", "Ephemeral", "Presentation" })
        {
            Assert.DoesNotContain(banned, surface, StringComparison.Ordinal);
        }

        var model = mep.Current.ToModelResult();
        Assert.DoesNotContain(model.Keys, key => key.Contains("generation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Keys, key => key.Contains("authority", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Keys, key => key.Contains("intent", StringComparison.OrdinalIgnoreCase));
        var proposal = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(model["proposal"]);
        Assert.DoesNotContain(proposal.Keys, key => key.Contains("generation", StringComparison.OrdinalIgnoreCase));
        var json = JsonSerializer.Serialize(model);
        Assert.DoesNotContain(registry.Current!.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
        Assert.False(AssistantIpc.GrantsApprovalAuthority);
    }

    [Fact]
    public void Mcp_bridge_and_tool_count_surfaces_are_unchanged()
    {
        Assert.Equal(26, BridgeProtocol.CurrentVersion);
        Assert.Equal(26, BridgeProtocol.SupportedVersions[0]);
        Assert.DoesNotContain(27, BridgeProtocol.SupportedVersions);
        Assert.Equal(3, AssistantModelTools.LocalNames.Length);
        Assert.DoesNotContain("bimit_propose_parameter_updates", AssistantModelTools.LocalNames);

        var root = SkillRegistryTests.RepoRoot();
        var serverHost = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "ServerHost.cs"));
        Assert.Equal(22, Regex.Matches(serverHost, @"\.With[A-Za-z]+Tool\(\)").Count);
        var assistant = string.Join(
            '\n',
            Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Assistant"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotContain("bimit_propose_parameter_updates", assistant, StringComparison.Ordinal);
        Assert.DoesNotContain("bimit_propose_parameter_updates", serverHost, StringComparison.Ordinal);
        Assert.Contains("var proposalRegistry = new AssistantProposalRegistry();", assistant, StringComparison.Ordinal);
        Assert.Contains("new MepDevicePlacementProposalStore(proposalRegistry)", assistant, StringComparison.Ordinal);
        Assert.Contains("new RoomPatternStore(proposalRegistry)", assistant, StringComparison.Ordinal);
    }

    private static long ShutdownGeneration()
    {
        var (registry, mep, patterns) = Pair();
        mep.Replace(Placement("artifact-a", 1000));
        patterns.Replace(Pattern("artifact-b"), "{}", Generation());
        mep.Clear();
        patterns.Clear();
        mep.Clear();
        patterns.Clear();
        Assert.Null(registry.Current);
        mep.Replace(Placement("artifact-a", 1000));
        return mep.Authority.Generation;
    }

    private static async Task RunHostAsync(
        AssistantProposalRegistry registry,
        MepDevicePlacementProposalStore mep,
        RoomPatternStore patterns,
        AssistantIpcKind signal)
    {
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            new IdleSession(),
            new IdleRuntime(),
            providerTimeouts: new AssistantProviderTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)),
            proposals: mep,
            roomPatterns: patterns);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Hello, InstanceId, "2026", "26.5.0.55", true, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(signal, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        if (signal == AssistantIpcKind.ResetConversation)
        {
            _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);
            await AssistantPipeFrames.WriteAsync(
                client,
                AssistantIpc.Serialize(new AssistantIpcMessage(
                    AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
                timeout.Token);
        }

        Assert.Equal(0, await host);
        Assert.NotNull(registry);
    }

    private static (AssistantProposalRegistry Registry, MepDevicePlacementProposalStore Mep, RoomPatternStore Patterns) Pair()
    {
        var registry = new AssistantProposalRegistry();
        return (registry, new MepDevicePlacementProposalStore(registry), new RoomPatternStore(registry));
    }

    private static MepDevicePlacementProposal Placement(string? artifactId, double offsetX)
    {
        return MepDevicePlacementProposal.Create(
            "mm",
            [new MepDevicePlacementProposalItem(offsetX, 0, 0, null, null)],
            MepDevicePlacementProposalReasons.CoordinateBasisUnestablished,
            InstanceId,
            DocumentId,
            TemplateRef,
            artifactId);
    }

    private static RoomPatternRecord Pattern(string artifactId)
    {
        return new RoomPatternRecord(artifactId, 1, 2);
    }

    private static RoomPatternGeneration Generation()
    {
        return new RoomPatternGeneration
        {
            GenerationId = "0123456789abcdef0123456789abcdef",
            InstanceId = InstanceId,
            DocumentId = DocumentId,
            ArtifactId = "artifact-b",
            PageNumber = 1,
            Source = new SourceRoomPattern([], null, []),
            Rooms = [],
            Options = new Dictionary<string, LoadedMepTypeOption>(),
        };
    }

    private sealed class RecordingSession : IAssistantToolSession
    {
        public List<string> Tools { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 22, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Tools.Add(toolName);
            return Task.FromResult(new AssistantToolCall(true, "unused", "unused", null));
        }
    }

    private sealed class ThrowingPreview : IMepDevicePlacementPreviewClient
    {
        public int Calls { get; private set; }

        public Task<AssistantToolCall> PreviewAsync(PreviewMepDevicePlacementsRequest request, string instanceId, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Stale MEP preview must not call Revit.");
        }
    }

    private sealed class ThrowingPatternPreview : IRoomPatternPreviewClient
    {
        public int Calls { get; private set; }

        public Task<AssistantToolCall> PreviewAsync(string instanceId, PreviewRoomPatternPlacementsRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Stale pattern preview must not call Revit.");
        }
    }

    private sealed class CountingReview : IMepDevicePlacementReviewClient
    {
        public List<string> Calls { get; } = [];

        public Task<AssistantToolCall> ReviewAsync(string instanceId, string intentRef, CancellationToken cancellationToken)
        {
            Calls.Add(intentRef);
            throw new InvalidOperationException("Stale review must not call Revit.");
        }
    }

    private sealed class CountingApply : IMepDevicePlacementApplyClient
    {
        public List<string> Calls { get; } = [];

        public Task<AssistantToolCall> ApplyAsync(string instanceId, string intentRef, CancellationToken cancellationToken)
        {
            Calls.Add(intentRef);
            throw new InvalidOperationException("Stale apply must not call Revit.");
        }
    }

    private sealed class IdleSession : IAssistantToolSession
    {
        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 22, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantToolCall(true, "unused", "unused", null));
        }
    }

    private sealed class IdleRuntime : IAssistantAgentRuntime
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
