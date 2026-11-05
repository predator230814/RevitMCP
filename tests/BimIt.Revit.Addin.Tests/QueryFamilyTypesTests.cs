using System.Text;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class QueryFamilyTypesTests
{
    private readonly DeclarativeQueryCursorCodec _cursors = new(Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef"));

    private readonly IBuiltInCategoryCatalog _categories = new DefinedCategories("OST_Doors", "OST_Walls", "OST_ElectricalFixtures");

    [Fact]
    public void Request_bounds_reject_bad_usage_page_size_names_and_tokens()
    {
        AssertInvalid(Request(documentId: null));
        AssertInvalid(Request(usage: "ALL"));
        AssertInvalid(Request(usage: "placed "));
        AssertInvalid(Request(pageSize: 0));
        AssertInvalid(Request(pageSize: 201));
        AssertInvalid(Request(familyName: ""));
        AssertInvalid(Request(familyName: " "));
        AssertInvalid(Request(familyName: new string('a', 257)));
        AssertInvalid(Request(typeName: "\t"));
        AssertInvalid(Request(tokens: []));
        AssertInvalid(Request(tokens: ["OST_Walls", "OST_Walls"]));
        AssertInvalid(Request(tokens: ["OST-Walls"]));
        AssertInvalid(Request(tokens: [new string('A', 81)]));
        AssertInvalid(Request(tokens: Enumerable.Repeat("OST_Walls", 21).ToArray()));
        AssertInvalid(Request(cursor: ""));
        AssertInvalid(Request(cursor: new string('a', 2049)));

        var accepted = FamilyTypeQueryValidator.Validate(Request(pageSize: null, usage: null));
        Assert.Equal(100, accepted.PageSize);
        Assert.Equal("all", accepted.Usage);
        Assert.Equal(1, FamilyTypeQueryValidator.Validate(Request(pageSize: 1)).PageSize);
        Assert.Equal(200, FamilyTypeQueryValidator.Validate(Request(pageSize: 200)).PageSize);
    }

    [Fact]
    public void BuiltInCategory_tokens_accept_any_defined_member_including_outside_the_mep_allowlist()
    {
        Assert.False(MepDeviceCategorySupport.IsSupported("OST_Walls"));
        AssertInvalid(Request(tokens: ["-2000011"]));
        AssertInvalid(Request(tokens: ["Walls "]));
        FamilyTypeQueryValidator.Validate(Request(tokens: ["ost_walls"]));

        var defined = new DefinedCategories("OST_Walls", "OST_Doors");
        var error = Assert.Throws<BridgeException>(() => FamilyTypeQueryValidator.EnsureDefinedCategories(["INVALID"], defined));
        Assert.Equal(CapabilityErrorCodes.InvalidFamilyTypeQuery, error.ErrorCode);
        FamilyTypeQueryValidator.EnsureDefinedCategories(["OST_Walls"], defined);

        var catalog = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Query", "RevitBuiltInCategoryCatalog.cs"));
        Assert.Contains("Enum.TryParse<BuiltInCategory>(token, ignoreCase: false, out var value)", catalog, StringComparison.Ordinal);
        Assert.Contains("value == BuiltInCategory.INVALID", catalog, StringComparison.Ordinal);
        Assert.Contains("Enum.IsDefined(value)", catalog, StringComparison.Ordinal);
        Assert.Contains("Enum.GetName(value)", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void Placement_mapping_covers_every_known_member_and_unknown_does_not_drop_the_row()
    {
        Assert.Equal("one_level_based", FamilyPlacementTokens.FromRevitName("OneLevelBased"));
        Assert.Equal("one_level_based_hosted", FamilyPlacementTokens.FromRevitName("OneLevelBasedHosted"));
        Assert.Equal("two_levels_based", FamilyPlacementTokens.FromRevitName("TwoLevelsBased"));
        Assert.Equal("view_based", FamilyPlacementTokens.FromRevitName("ViewBased"));
        Assert.Equal("work_plane_based", FamilyPlacementTokens.FromRevitName("WorkPlaneBased"));
        Assert.Equal("curve_based", FamilyPlacementTokens.FromRevitName("CurveBased"));
        Assert.Equal("curve_based_detail", FamilyPlacementTokens.FromRevitName("CurveBasedDetail"));
        Assert.Equal("curve_driven_structural", FamilyPlacementTokens.FromRevitName("CurveDrivenStructural"));
        Assert.Equal("adaptive", FamilyPlacementTokens.FromRevitName("Adaptive"));
        Assert.Equal("invalid", FamilyPlacementTokens.FromRevitName("Invalid"));
        Assert.Equal("unknown", FamilyPlacementTokens.FromRevitName("SomethingNew"));
        Assert.Equal("unknown", FamilyPlacementTokens.FromRevitName(null));
        Assert.Equal("unknown", FamilyPlacementTokens.Read(() => throw new InvalidOperationException("boom")));

        var result = Execute(Request(), [Row("t1", "f1", placement: "curve_driven_structural", inPlace: true)]);
        Assert.Equal("curve_driven_structural", result.Types[0].FamilyPlacementType);
        Assert.True(result.Types[0].FamilyIsInPlace);
        Assert.Equal("unknown", Execute(Request(), [Row("t1", "f1", placement: "unknown")]).Types[0].FamilyPlacementType);
    }

    [Fact]
    public void Filters_usage_order_and_summary_follow_the_whole_matched_set()
    {
        var rows = new[]
        {
            Row("t-b", "fam-b", "OST_Walls", "Walls", "Beta", "TypeB", placed: 0),
            Row("t-a2", "fam-a", "OST_Doors", "Doors", "Café", "Leaf", placed: 0),
            Row("t-a1", "fam-a", "OST_Doors", "Doors", "Café", "Frame", placed: 2),
            Row("t-c", "fam-c", "OST_ElectricalFixtures", "Fixtures", "Gamma", "Unused", placed: 0),
            Row("t-star", "fam-star", "OST_Walls", "Walls", "A*B", "A.B", placed: 3)
        };

        var all = Execute(Request(), rows);
        Assert.Equal(["t-a1", "t-a2", "t-c", "t-star", "t-b"], all.Types.Select(row => row.TypeRef).ToArray());
        Assert.Equal(5, all.Summary.MatchedTypeCount);
        Assert.Equal(4, all.Summary.DistinctFamilyCount);
        Assert.Equal(2, all.Summary.PlacedTypeCount);
        Assert.Equal(5, all.Summary.PlacedInstanceCount);
        Assert.Equal(2, all.Summary.UsedFamilyCount);
        Assert.Equal(2, all.Summary.UnusedFamilyCount);
        Assert.Equal(all.Summary.DistinctFamilyCount, all.Summary.UsedFamilyCount + all.Summary.UnusedFamilyCount);
        Assert.True(all.Types[0].IsUsed);
        Assert.False(all.Types[1].IsUsed);

        var placed = Execute(Request(usage: "placed"), rows);
        Assert.Equal(["t-a1", "t-star"], placed.Types.Select(row => row.TypeRef).ToArray());
        Assert.Equal(2, placed.Summary.MatchedTypeCount);
        Assert.Equal(2, placed.Summary.UsedFamilyCount);
        Assert.Equal(0, placed.Summary.UnusedFamilyCount);

        var unused = Execute(Request(usage: "unused"), rows);
        Assert.Equal(["t-a2", "t-c", "t-b"], unused.Types.Select(row => row.TypeRef).ToArray());
        Assert.Equal(0, unused.Summary.UsedFamilyCount);
        Assert.Equal(3, unused.Summary.UnusedFamilyCount);

        var names = Execute(Request(familyName: "café", typeName: "fr"), rows);
        Assert.Equal(["t-a1"], names.Types.Select(row => row.TypeRef).ToArray());
        Assert.Equal(1, names.Summary.DistinctFamilyCount);
        Assert.Empty(Execute(Request(familyName: "cafe"), rows).Types);
        Assert.Equal(["t-star"], Execute(Request(familyName: "*"), rows).Types.Select(row => row.TypeRef).ToArray());
        Assert.Equal(["t-star"], Execute(Request(typeName: "."), rows).Types.Select(row => row.TypeRef).ToArray());
        Assert.Empty(Execute(Request(familyName: "Café "), rows).Types);
        Assert.Equal(["t-a1", "t-a2"], Execute(Request(tokens: ["OST_Walls", "OST_Doors"], familyName: "Café"), rows).Types.Select(row => row.TypeRef).ToArray());

        var serialized = ReadResultBudget.Serialize(names);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<QueryFamilyTypesResult>(serialized, ReadResultBudget.Options);
        Assert.Equal("Café", roundTrip!.Types[0].FamilyName);
        Assert.True(serialized.Length <= ReadResultBudget.MaxCharacters);
    }

    [Fact]
    public void Instance_counts_are_grouped_once_and_ignore_non_symbols()
    {
        var symbols = new HashSet<string>(StringComparer.Ordinal) { "t-a", "t-b" };
        var counts = PlacedInstanceCounts.ByTypeRef(["t-a", "t-a", "other", null, "t-b"], symbols);
        Assert.Equal(2, counts["t-a"]);
        Assert.Equal(1, counts["t-b"]);
        Assert.False(counts.ContainsKey("other"));
    }

    [Fact]
    public void Pagination_can_change_page_size_and_the_exact_last_page_is_complete()
    {
        var rows = new[]
        {
            Row("a", "f", typeName: "A"),
            Row("b", "f", typeName: "B"),
            Row("c", "f", typeName: "C")
        };
        var first = Execute(Request(pageSize: 2), rows);
        Assert.False(first.Complete);
        Assert.Equal(2, first.ReturnedCount);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(3, first.Summary.MatchedTypeCount);

        var rest = Execute(Request(pageSize: 100, cursor: first.NextCursor), rows);
        Assert.True(rest.Complete);
        Assert.Null(rest.NextCursor);
        Assert.Equal(["c"], rest.Types.Select(row => row.TypeRef).ToArray());
        Assert.Equal(3, rest.Summary.MatchedTypeCount);

        var exact = Execute(Request(pageSize: 3), rows);
        Assert.True(exact.Complete);
        Assert.Null(exact.NextCursor);
        Assert.Equal(3, exact.ReturnedCount);

        var one = Execute(Request(pageSize: 1), rows);
        var two = Execute(Request(pageSize: 1, cursor: one.NextCursor), rows);
        var three = Execute(Request(pageSize: 1, cursor: two.NextCursor), rows);
        Assert.Equal(["a", "b", "c"], new[] { one.Types[0].TypeRef, two.Types[0].TypeRef, three.Types[0].TypeRef });
        Assert.True(three.Complete);
    }

    [Fact]
    public void Cursors_reject_forgery_filter_changes_and_the_wrong_document_after_the_document_guard()
    {
        var rows = new[] { Row("a", "f", typeName: "A"), Row("b", "f", typeName: "B") };
        var page = Execute(Request(pageSize: 1), rows);
        var cursor = page.NextCursor!;
        Assert.InRange(cursor.Length, 1, 2048);

        var forged = cursor[..^1] + (cursor[^1] == 'a' ? "b" : "a");
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: forged), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(cursor: "not a cursor"), rows).ErrorCode);
        Assert.Equal(
            CapabilityErrorCodes.InvalidCursor,
            Error(Request(pageSize: 1, cursor: cursor, familyName: "Nope"), rows).ErrorCode);
        Assert.Equal(
            CapabilityErrorCodes.DocumentContextChanged,
            Error(Request(documentId: "other", cursor: cursor), rows, activeDocumentId: "doc").ErrorCode);

        Assert.True(_cursors.TryMint(
            new DeclarativeQueryCursorPayload(
                DeclarativeQuerySources.FamilyTypes,
                "instance",
                "other-doc",
                FamilyTypeQueryFingerprint.Compute(FamilyTypeQueryValidator.Validate(Request())),
                0,
                0),
            out var other));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(cursor: other), rows).ErrorCode);

        var left = FamilyTypeQueryFingerprint.Compute(FamilyTypeQueryValidator.Validate(Request(tokens: ["OST_Walls", "OST_Doors"])));
        var right = FamilyTypeQueryFingerprint.Compute(FamilyTypeQueryValidator.Validate(Request(tokens: ["OST_Doors", "OST_Walls"], pageSize: 7)));
        Assert.Equal(left, right);
    }

    [Fact]
    public void A_revision_change_makes_an_outstanding_cursor_stale_and_a_restart_succeeds()
    {
        var rows = new[] { Row("a", "f"), Row("b", "f", typeName: "B") };
        var page = Execute(Request(pageSize: 1), rows, revision: 4);
        Assert.Equal(
            CapabilityErrorCodes.StaleCursor,
            Error(Request(pageSize: 1, cursor: page.NextCursor), rows, revision: 5).ErrorCode);
        var restarted = Execute(Request(pageSize: 1), rows, revision: 5);
        Assert.Equal("a", restarted.Types[0].TypeRef);
        Assert.False(restarted.Complete);
    }

    [Fact]
    public void Document_and_family_guards_precede_the_inventory()
    {
        var rows = new[] { Row("a", "f") };
        Assert.Equal(CapabilityErrorCodes.NoActiveDocument, Error(Request(), rows, hasActiveDocument: false).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.UnsupportedDocumentKind, Error(Request(), rows, isFamilyDocument: true).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.DocumentContextChanged, Error(Request(documentId: ""), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, Error(Request(), [Row("", "f")]).ErrorCode);
    }

    [Fact]
    public void The_result_budget_shortens_a_page_and_fails_when_one_row_cannot_fit()
    {
        var wide = Enumerable.Range(0, 8)
            .Select(index => Row("t" + index, "f", typeName: new string('X', 20_000) + index))
            .ToArray();
        var page = Execute(Request(pageSize: 8), wide);
        Assert.True(page.ReturnedCount < 8);
        Assert.True(page.ReturnedCount >= 1);
        Assert.False(page.Complete);
        Assert.NotNull(page.NextCursor);
        Assert.Equal(8, page.Summary.MatchedTypeCount);
        Assert.True(ReadResultBudget.Measure(page) <= ReadResultBudget.MaxCharacters);

        var seen = new List<string>();
        string? cursor = null;
        for (var guard = 0; guard < 20; guard++)
        {
            var next = Execute(Request(pageSize: 8, cursor: cursor), wide);
            seen.AddRange(next.Types.Select(row => row.TypeRef));
            if (next.Complete)
            {
                Assert.Null(next.NextCursor);
                break;
            }

            cursor = next.NextCursor;
        }

        Assert.Equal(Enumerable.Range(0, 8).Select(index => "t" + index).ToArray(), seen);

        var huge = new[] { Row("t", "f", familyName: new string('Y', 200_000)) };
        Assert.Equal(CapabilityErrorCodes.ResultTooLarge, Error(Request(pageSize: 1), huge).ErrorCode);
    }

    [Fact]
    public void Revision_service_is_document_scoped_and_thread_safe()
    {
        var revisions = new DocumentQueryRevisionService();
        Assert.Equal(0, revisions.Current("doc"));
        Parallel.For(0, 40, _ => revisions.Advance("doc"));
        Assert.Equal(40, revisions.Current("doc"));
        Assert.Equal(0, revisions.Current("other"));
        revisions.Forget("doc");
        Assert.Equal(0, revisions.Current("doc"));
    }

    [Fact]
    public void Invalid_and_stale_queries_do_not_enumerate_inventory()
    {
        var rows = new[] { Row("a", "f", typeName: "A"), Row("b", "f", typeName: "B") };
        var page = Execute(Request(pageSize: 1), rows);
        Assert.Equal(1, CountReads(Request(pageSize: 1, cursor: page.NextCursor), rows));

        Assert.Equal(0, CountReads(Request(pageSize: 1, cursor: "not a cursor"), rows));
        Assert.Equal(0, CountReads(Request(pageSize: 1, cursor: page.NextCursor, familyName: "Nope"), rows));
        Assert.Equal(0, CountReads(Request(pageSize: 1, cursor: page.NextCursor), rows, revision: 9));
        Assert.Equal(0, CountReads(Request(tokens: ["NOT_A_CATEGORY"]), rows));
        Assert.Equal(0, CountReads(Request(documentId: "other"), rows));
        Assert.Equal(0, CountReads(Request(), rows, isFamilyDocument: true));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, ReadError(Request(cursor: "not a cursor"), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.StaleCursor, ReadError(Request(pageSize: 1, cursor: page.NextCursor), rows, revision: 9).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidFamilyTypeQuery, ReadError(Request(tokens: ["NOT_A_CATEGORY"]), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.DocumentContextChanged, ReadError(Request(documentId: "other"), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.UnsupportedDocumentKind, ReadError(Request(), rows, isFamilyDocument: true).ErrorCode);
    }

    [Fact]
    public void Collector_and_revision_events_match_the_read_contract()
    {
        var root = RepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Capabilities", "RevitQueryFamilyTypesService.cs"));
        Assert.Contains("FamilyTypeQueryValidator.Validate(request)", service, StringComparison.Ordinal);
        Assert.Contains("EnqueueAsync", service, StringComparison.Ordinal);
        var prepare = service.IndexOf("FamilyTypeInventoryQuery.Prepare(", StringComparison.Ordinal);
        var read = service.IndexOf("ReadCandidates(document)", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && prepare < read);
        Assert.Equal(1, Count(service, "OfClass(typeof(FamilySymbol))"));
        Assert.Equal(1, Count(service, "WhereElementIsNotElementType()"));
        Assert.Contains("GetTypeId()", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new Transaction", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SubTransaction", service, StringComparison.Ordinal);
        Assert.DoesNotContain("TransactionGroup", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RevitLinkInstance", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLinkDocument", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementDesignOptionFilter", service, StringComparison.Ordinal);
        Assert.DoesNotContain("FilteredElementCollector(document,", service, StringComparison.Ordinal);

        var events = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Identity", "RevitDocumentQueryChangeEventSource.cs"));
        Assert.Contains("DocumentChanged", events, StringComparison.Ordinal);
        Assert.Contains("DocumentSynchronizedWithCentral", events, StringComparison.Ordinal);
        Assert.Contains("DocumentReloadedLatest", events, StringComparison.Ordinal);
    }

    private int CountReads(
        QueryFamilyTypesRequest request,
        IReadOnlyList<FamilyTypeCandidate> rows,
        long revision = 0,
        bool isFamilyDocument = false)
    {
        var reads = 0;
        try
        {
            FamilyTypeInventoryQuery.Execute(
                FamilyTypeQueryValidator.Validate(request),
                "instance",
                "doc",
                hasActiveDocument: true,
                isFamilyDocument,
                revision,
                () =>
                {
                    reads++;
                    return rows;
                },
                _categories,
                _cursors);
        }
        catch (BridgeException)
        {
        }

        return reads;
    }

    private BridgeException ReadError(
        QueryFamilyTypesRequest request,
        IReadOnlyList<FamilyTypeCandidate> rows,
        long revision = 0,
        bool isFamilyDocument = false)
    {
        return Assert.Throws<BridgeException>(() => FamilyTypeInventoryQuery.Execute(
            FamilyTypeQueryValidator.Validate(request),
            "instance",
            "doc",
            hasActiveDocument: true,
            isFamilyDocument,
            revision,
            () => rows,
            _categories,
            _cursors));
    }

    private QueryFamilyTypesResult Execute(
        QueryFamilyTypesRequest request,
        IReadOnlyList<FamilyTypeCandidate> rows,
        long revision = 0,
        string? activeDocumentId = "doc",
        bool hasActiveDocument = true,
        bool isFamilyDocument = false)
    {
        return FamilyTypeInventoryQuery.Execute(
            FamilyTypeQueryValidator.Validate(request),
            "instance",
            activeDocumentId,
            hasActiveDocument,
            isFamilyDocument,
            revision,
            rows,
            _categories,
            _cursors);
    }

    private BridgeException Error(
        QueryFamilyTypesRequest request,
        IReadOnlyList<FamilyTypeCandidate> rows,
        long revision = 0,
        string? activeDocumentId = "doc",
        bool hasActiveDocument = true,
        bool isFamilyDocument = false)
    {
        return Assert.Throws<BridgeException>(() => Execute(request, rows, revision, activeDocumentId, hasActiveDocument, isFamilyDocument));
    }

    private static void AssertInvalid(QueryFamilyTypesRequest request)
    {
        var error = Assert.Throws<BridgeException>(() => FamilyTypeQueryValidator.Validate(request));
        Assert.Contains(error.ErrorCode, new[] { CapabilityErrorCodes.InvalidFamilyTypeQuery, CapabilityErrorCodes.InvalidCursor });
    }

    private static QueryFamilyTypesRequest Request(
        string? documentId = "doc",
        string[]? tokens = null,
        string? familyName = null,
        string? typeName = null,
        string? usage = null,
        int? pageSize = null,
        string? cursor = null)
    {
        return new QueryFamilyTypesRequest
        {
            DocumentId = documentId,
            CategoryTokens = tokens,
            FamilyNameContains = familyName,
            TypeNameContains = typeName,
            Usage = usage,
            PageSize = pageSize,
            Cursor = cursor
        };
    }

    private static FamilyTypeCandidate Row(
        string typeRef,
        string familyRef,
        string category = "OST_Doors",
        string categoryName = "Doors",
        string familyName = "Alpha",
        string typeName = "A",
        string placement = "one_level_based",
        bool inPlace = false,
        int placed = 0)
    {
        return new FamilyTypeCandidate(typeRef, familyRef, category, categoryName, familyName, typeName, placement, inPlace, placed);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln from the test output directory.");
    }

    private sealed class DefinedCategories : IBuiltInCategoryCatalog
    {
        private readonly HashSet<string> _tokens;

        public DefinedCategories(params string[] tokens)
        {
            _tokens = new HashSet<string>(tokens, StringComparer.Ordinal);
        }

        public bool IsDefinedMember(string token) => _tokens.Contains(token);
    }
}
