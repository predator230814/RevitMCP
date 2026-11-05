using System.Text;
using System.Text.Json;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

/// <summary>
/// CAP-0015 pure query behavior over in-memory binding candidates. The Revit reader is
/// covered structurally in <see cref="ParameterBindingReaderStructureTests"/>.
/// </summary>
public sealed class QueryParameterBindingsTests
{
    private static readonly Guid GuidA = Guid.Parse("B6A72840-0000-0000-0000-00000000000A");
    private static readonly Guid GuidB = Guid.Parse("857E1417-0000-0000-0000-00000000000B");

    private readonly DeclarativeQueryCursorCodec _cursors = new(Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef"));

    private readonly IBuiltInCategoryCatalog _categories = new DefinedCategories(
        "OST_ElectricalFixtures",
        "OST_SecurityDevices",
        "OST_ElectricalEquipment",
        "OST_Doors",
        "OST_Walls");

    [Fact]
    public void Request_defaults_bounds_and_enums()
    {
        var defaults = ParameterBindingQueryValidator.Validate(Request());
        Assert.Equal(50, defaults.PageSize);
        Assert.Equal("all", defaults.IdentityKind);
        Assert.Equal("all", defaults.BindingKind);
        Assert.Null(defaults.CategoryTokens);
        Assert.Null(defaults.NameContains);

        Assert.Equal(1, ParameterBindingQueryValidator.Validate(Request(pageSize: 1)).PageSize);
        Assert.Equal(100, ParameterBindingQueryValidator.Validate(Request(pageSize: 100)).PageSize);
        foreach (var identity in new[] { "all", "shared", "local", "built_in" })
        {
            Assert.Equal(identity, ParameterBindingQueryValidator.Validate(Request(identityKind: identity)).IdentityKind);
        }

        foreach (var binding in new[] { "all", "instance", "type" })
        {
            Assert.Equal(binding, ParameterBindingQueryValidator.Validate(Request(bindingKind: binding)).BindingKind);
        }

        AssertInvalidQuery(Request(documentId: null));
        AssertInvalidQuery(Request(pageSize: 0));
        AssertInvalidQuery(Request(pageSize: 101));
        AssertInvalidQuery(Request(identityKind: "ALL"));
        AssertInvalidQuery(Request(identityKind: "Shared"));
        AssertInvalidQuery(Request(identityKind: "builtin"));
        AssertInvalidQuery(Request(identityKind: ""));
        AssertInvalidQuery(Request(bindingKind: "both"));
        AssertInvalidQuery(Request(bindingKind: "Instance"));
        AssertInvalidQuery(Request(nameContains: ""));
        AssertInvalidQuery(Request(nameContains: " \t"));
        AssertInvalidQuery(Request(nameContains: new string('a', 257)));
        Assert.Equal(256, ParameterBindingQueryValidator.Validate(Request(nameContains: new string('a', 256))).NameContains!.Length);
        Assert.Equal(" Tt_", ParameterBindingQueryValidator.Validate(Request(nameContains: " Tt_")).NameContains);

        AssertInvalidCursor(Request(cursor: ""));
        AssertInvalidCursor(Request(cursor: new string('a', 2049)));
        ParameterBindingQueryValidator.Validate(Request(cursor: new string('a', 2048)));
    }

    [Fact]
    public void Category_token_shape_and_definedness_match_CAP_0014()
    {
        AssertInvalidQuery(Request(tokens: []));
        AssertInvalidQuery(Request(tokens: ["OST-Walls"]));
        AssertInvalidQuery(Request(tokens: ["-2000011"]));
        AssertInvalidQuery(Request(tokens: ["Walls "]));
        AssertInvalidQuery(Request(tokens: [new string('A', 81)]));
        AssertInvalidQuery(Request(tokens: Enumerable.Range(0, 21).Select(index => "OST_" + index).ToArray()));
        ParameterBindingQueryValidator.Validate(Request(tokens: Enumerable.Range(0, 20).Select(index => "OST_" + index).ToArray()));

        // Well-shaped but undefined tokens fail in preparation, before the inventory is read.
        Assert.Equal(CapabilityErrorCodes.InvalidParameterBindingQuery, Error(Request(tokens: ["ost_walls"]), []).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidParameterBindingQuery, Error(Request(tokens: ["unknown"]), []).ErrorCode);
        Assert.Equal(0, Execute(Request(tokens: ["OST_Walls"]), [Row("A", Local("a"), categories: Cat("OST_Doors"))]).ReturnedCount);

        // Same shape decisions as the CAP-0014 validator.
        foreach (var tokens in new[] { new[] { "OST-Walls" }, ["OST_Walls", "OST_Walls"], [new string('A', 81)], [] })
        {
            Assert.Throws<BridgeException>(() => FamilyTypeQueryValidator.Validate(new QueryFamilyTypesRequest { DocumentId = "doc", CategoryTokens = tokens }));
            Assert.Throws<BridgeException>(() => ParameterBindingQueryValidator.Validate(Request(tokens: tokens)));
        }
    }

    [Fact]
    public void Duplicate_category_tokens_are_rejected()
    {
        AssertInvalidQuery(Request(tokens: ["OST_Doors", "OST_Doors"]));
        ParameterBindingQueryValidator.Validate(Request(tokens: ["OST_Doors", "OST_doors"]));
    }

    [Fact]
    public void Name_filter_folds_case_including_accented_capitals_but_not_accents()
    {
        var rows = new[]
        {
            Row("Tt_Capacité_Sectionneur", Shared(GuidA)),
            Row("TT_CAPACITÉ_DISJONCTEUR", Local("b")),
            Row("Tt_Capacite_Sans_Accent", Local("c")),
            Row("CHARGE INTÉGRÉE", Local("d"))
        };

        Assert.Equal(
            ["TT_CAPACITÉ_DISJONCTEUR", "Tt_Capacité_Sectionneur"],
            Names(Execute(Request(nameContains: "capacité"), rows)));
        Assert.Equal(["Tt_Capacite_Sans_Accent"], Names(Execute(Request(nameContains: "capacite"), rows)));
        Assert.Equal(["CHARGE INTÉGRÉE"], Names(Execute(Request(nameContains: "intégrée"), rows)));
        Assert.Empty(Execute(Request(nameContains: "integree"), rows).Bindings);
        Assert.Empty(Execute(Request(nameContains: " Tt_"), rows).Bindings);
    }

    [Fact]
    public void Identity_kind_filters_match_the_classified_identity()
    {
        var rows = new[] { Row("B", BuiltIn("autodesk.revit.parameter:b")), Row("S", Shared(GuidA)), Row("L", Local("l")) };

        Assert.Equal(["B", "L", "S"], Names(Execute(Request(identityKind: "all"), rows)));
        Assert.Equal(["B"], Names(Execute(Request(identityKind: "built_in"), rows)));
        Assert.Equal(["S"], Names(Execute(Request(identityKind: "shared"), rows)));
        Assert.Equal(["L"], Names(Execute(Request(identityKind: "local"), rows)));
    }

    [Fact]
    public void Binding_kind_filters_match_instance_or_type()
    {
        var rows = new[] { Row("I", Local("i")), Row("T", Local("t"), QueryParameterBindingKind.Type) };

        Assert.Equal(["I", "T"], Names(Execute(Request(bindingKind: "all"), rows)));
        Assert.Equal(["I"], Names(Execute(Request(bindingKind: "instance"), rows)));
        Assert.Equal(["T"], Names(Execute(Request(bindingKind: "type"), rows)));
        Assert.Equal(QueryParameterBindingKind.Type, Execute(Request(bindingKind: "type"), rows).Bindings[0].BindingKind);

        // Filters AND together.
        Assert.Empty(Execute(Request(bindingKind: "type", nameContains: "I"), rows).Bindings);
    }

    [Fact]
    public void Category_filter_is_any_match_and_keeps_the_complete_category_set()
    {
        var rows = new[]
        {
            Row("Wide", Shared(GuidA), categories: [Cat("OST_Walls"), Cat("OST_ElectricalFixtures"), Cat("OST_Doors")]),
            Row("Narrow", Local("n"), categories: Cat("OST_SecurityDevices"))
        };

        var page = Execute(Request(tokens: ["OST_ElectricalFixtures"]), rows);
        Assert.Equal(["Wide"], Names(page));
        Assert.Equal(
            ["OST_Doors", "OST_ElectricalFixtures", "OST_Walls"],
            page.Bindings[0].Categories.Select(category => category.CategoryToken).ToArray());
        Assert.Equal(3, page.Summary.DistinctCategoryCount);

        Assert.Equal(["Narrow", "Wide"], Names(Execute(Request(tokens: ["OST_SecurityDevices", "OST_Doors"]), rows)));
    }

    [Fact]
    public void Unknown_categories_never_match_a_filter_and_are_never_dropped()
    {
        var rows = new[]
        {
            Row("OnlyUnknown", Local("u"), categories: Cat("unknown", "Mystery", "id:-1")),
            Row("Mixed", Local("m"), categories: [Cat("unknown", "", "id:-2"), Cat("OST_Doors")]),
            Row("Empty", Local("e"), categories: [])
        };

        var all = Execute(Request(), rows);
        Assert.Equal(["Empty", "Mixed", "OnlyUnknown"], Names(all));
        Assert.Empty(all.Bindings[0].Categories);
        Assert.Equal(["OST_Doors", "unknown"], all.Bindings[1].Categories.Select(category => category.CategoryToken).ToArray());
        Assert.Equal("", all.Bindings[1].Categories[1].CategoryName);
        Assert.Equal("Mystery", all.Bindings[2].Categories[0].CategoryName);
        Assert.Equal(3, all.Summary.DistinctCategoryCount);

        Assert.Equal(["Mixed"], Names(Execute(Request(tokens: ["OST_Doors"]), rows)));
        Assert.Equal(2, Execute(Request(tokens: ["OST_Doors"]), rows).Bindings[0].Categories.Count);
    }

    [Fact]
    public void Two_distinct_unknown_categories_with_the_same_name_stay_distinct()
    {
        var rows = new[]
        {
            Row("A", Local("a"), categories: [Cat("unknown", "Same", "id:-10"), Cat("unknown", "Same", "id:-11")]),
            Row("B", Local("b"), categories: [Cat("unknown", "Same", "id:-10")])
        };

        var page = Execute(Request(), rows);
        Assert.Equal(2, page.Bindings[0].Categories.Count);
        Assert.All(page.Bindings[0].Categories, category => Assert.Equal("Same", category.CategoryName));
        Assert.Equal(2, page.Summary.DistinctCategoryCount);
    }

    [Fact]
    public void Categories_are_deduplicated_and_ordered_by_token_name_then_hidden_identity()
    {
        var normalized = ParameterBindingInventoryQuery.NormalizeCategories(
        [
            Cat("unknown", "b", "id:-3"),
            Cat("OST_Walls", "Walls", "id:-2000011"),
            Cat("unknown", "a", "id:-4"),
            Cat("OST_Doors", "Doors", "id:-2000023"),
            Cat("unknown", "a", "id:-1"),
            Cat("OST_Doors", "Doors", "id:-2000023")
        ]);

        Assert.Equal(
            ["OST_Doors/Doors/id:-2000023", "OST_Walls/Walls/id:-2000011", "unknown/a/id:-1", "unknown/a/id:-4", "unknown/b/id:-3"],
            normalized.Select(category => category.CategoryToken + "/" + category.CategoryName + "/" + category.CategoryKey).ToArray());

        var json = ReadResultBudget.Serialize(Execute(Request(), [Row("A", Local("a"), categories: normalized.ToArray())]));
        Assert.DoesNotContain("id:-", json, StringComparison.Ordinal);
        Assert.DoesNotContain("category_key", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_are_ordered_case_insensitively_then_ordinally_by_name()
    {
        var rows = new[]
        {
            Row("beta", Local("1")),
            Row("alpha", Local("2")),
            Row("ALPHA", Local("3")),
            Row("Alpha", Local("4")),
            Row("Ébène", Local("5")),
            Row("Zeta", Local("6"))
        };

        Assert.Equal(["ALPHA", "Alpha", "alpha", "beta", "Zeta", "Ébène"], Names(Execute(Request(), rows)));
    }

    [Fact]
    public void Identity_kind_then_stable_key_break_name_ties()
    {
        var rows = new[]
        {
            Row("Same", Local("local-b"), parameterRef: "ref-a"),
            Row("Same", Local("local-a"), parameterRef: "ref-b"),
            Row("Same", Shared(GuidB), parameterRef: "ref-c"),
            Row("Same", Shared(GuidA), parameterRef: "ref-d"),
            Row("Same", BuiltIn("autodesk.revit.parameter:x"), parameterRef: "ref-e")
        };

        var page = Execute(Request(), rows);
        Assert.Equal(
            ["built_in", "shared:857e1417", "shared:b6a72840", "local", "local"],
            page.Bindings.Select(row => row.Identity.Kind switch
            {
                DescribeParameterIdentityKind.BuiltIn => "built_in",
                DescribeParameterIdentityKind.Shared => "shared:" + row.Identity.Guid![..8],
                _ => "local"
            }).ToArray());
        Assert.Equal(["ref-e", "ref-c", "ref-d", "ref-b", "ref-a"], page.Bindings.Select(row => row.ParameterRef).ToArray());
    }

    [Fact]
    public void Summary_is_exact_over_the_matched_set_and_repeated_on_every_page()
    {
        var rows = new[]
        {
            Row("A", Shared(GuidA), categories: [Cat("OST_ElectricalFixtures"), Cat("OST_Doors")]),
            Row("B", Shared(GuidB), QueryParameterBindingKind.Type, categories: Cat("OST_ElectricalFixtures")),
            Row("C", Local("c"), categories: Cat("OST_Walls")),
            Row("D", BuiltIn("autodesk.revit.parameter:d"), categories: Cat("unknown", "", "id:-9")),
            Row("E", Local("e"), QueryParameterBindingKind.Type, categories: [])
        };

        var pages = Traverse(Request(pageSize: 2), rows);
        Assert.Equal(3, pages.Count);
        foreach (var page in pages)
        {
            var summary = page.Summary;
            Assert.Equal(5, summary.MatchedDefinitionCount);
            Assert.Equal(2, summary.SharedCount);
            Assert.Equal(2, summary.LocalCount);
            Assert.Equal(1, summary.BuiltInCount);
            Assert.Equal(3, summary.InstanceBoundCount);
            Assert.Equal(2, summary.TypeBoundCount);
            Assert.Equal(4, summary.DistinctCategoryCount);
            Assert.Equal(summary.MatchedDefinitionCount, summary.SharedCount + summary.LocalCount + summary.BuiltInCount);
            Assert.Equal(summary.MatchedDefinitionCount, summary.InstanceBoundCount + summary.TypeBoundCount);
        }

        Assert.Equal(5, pages.Sum(page => page.ReturnedCount));

        var filtered = Execute(Request(identityKind: "shared"), rows).Summary;
        Assert.Equal(2, filtered.MatchedDefinitionCount);
        Assert.Equal(1, filtered.TypeBoundCount);
        Assert.Equal(2, filtered.DistinctCategoryCount);
    }

    [Fact]
    public void An_empty_match_is_complete_with_an_all_zero_summary()
    {
        foreach (var rows in new[] { Array.Empty<ParameterBindingCandidate>(), [Row("A", Local("a"))] })
        {
            var page = Execute(Request(nameContains: "zzz"), rows);
            Assert.True(page.Complete);
            Assert.Null(page.NextCursor);
            Assert.Equal(0, page.ReturnedCount);
            Assert.Empty(page.Bindings);
            var summary = page.Summary;
            Assert.Equal(
                [0, 0, 0, 0, 0, 0, 0],
                new[] { summary.MatchedDefinitionCount, summary.SharedCount, summary.LocalCount, summary.BuiltInCount, summary.InstanceBoundCount, summary.TypeBoundCount, summary.DistinctCategoryCount });
            Assert.Contains("\"next_cursor\":null", ReadResultBudget.Serialize(page), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Page_size_can_change_mid_traversal_and_the_exact_last_page_is_final()
    {
        var rows = Enumerable.Range(0, 7).Select(index => Row("P" + index, Local("k" + index))).ToArray();

        var first = Execute(Request(pageSize: 3), rows);
        Assert.False(first.Complete);
        Assert.NotNull(first.NextCursor);
        var second = Execute(Request(pageSize: 1, cursor: first.NextCursor), rows);
        var third = Execute(Request(pageSize: 100, cursor: second.NextCursor), rows);
        Assert.True(third.Complete);
        Assert.Null(third.NextCursor);
        Assert.Equal(
            rows.Select(row => row.Name).ToArray(),
            first.Bindings.Concat(second.Bindings).Concat(third.Bindings).Select(row => row.Name).ToArray());

        var exact = Execute(Request(pageSize: 7), rows);
        Assert.True(exact.Complete);
        Assert.Null(exact.NextCursor);
        Assert.Equal(7, exact.ReturnedCount);

        var head = Execute(Request(pageSize: 4), rows);
        var tail = Execute(Request(pageSize: 3, cursor: head.NextCursor), rows);
        Assert.True(tail.Complete);
        Assert.Equal(3, tail.ReturnedCount);
    }

    [Fact]
    public void Family_type_and_parameter_binding_cursors_are_not_interchangeable()
    {
        var rows = new[] { Row("A", Local("a")), Row("B", Local("b")) };
        var bindingCursor = Execute(Request(pageSize: 1), rows).NextCursor;

        var familyRows = new[]
        {
            new FamilyTypeCandidate("t1", "f", "OST_Doors", "Doors", "Alpha", "A", "one_level_based", false, 0),
            new FamilyTypeCandidate("t2", "f", "OST_Doors", "Doors", "Alpha", "B", "one_level_based", false, 0)
        };
        var familyCursor = FamilyTypeInventoryQuery.Execute(
            FamilyTypeQueryValidator.Validate(new QueryFamilyTypesRequest { DocumentId = "doc", PageSize = 1 }),
            "instance", "doc", true, false, 0, familyRows, _categories, _cursors).NextCursor;

        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, cursor: familyCursor), rows).ErrorCode);
        var reverse = Assert.Throws<BridgeException>(() => FamilyTypeInventoryQuery.Execute(
            FamilyTypeQueryValidator.Validate(new QueryFamilyTypesRequest { DocumentId = "doc", PageSize = 1, Cursor = bindingCursor }),
            "instance", "doc", true, false, 0, familyRows, _categories, _cursors));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, reverse.ErrorCode);

        Assert.NotEqual(
            ParameterBindingQueryFingerprint.Compute(ParameterBindingQueryValidator.Validate(Request())),
            FamilyTypeQueryFingerprint.Compute(FamilyTypeQueryValidator.Validate(new QueryFamilyTypesRequest { DocumentId = "doc" })));
    }

    [Fact]
    public void Forged_changed_filter_wrong_document_and_wrong_instance_cursors_are_rejected()
    {
        var rows = new[] { Row("A", Local("a")), Row("B", Local("b")) };
        var cursor = Execute(Request(pageSize: 1, identityKind: "local"), rows).NextCursor!;

        var forged = cursor[..^2] + (cursor[^2] == 'A' ? "B" : "A") + cursor[^1];
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, identityKind: "local", cursor: forged), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(cursor: "not a cursor"), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, identityKind: "all", cursor: cursor), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, identityKind: "local", bindingKind: "instance", cursor: cursor), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, identityKind: "local", nameContains: "A", cursor: cursor), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(pageSize: 1, identityKind: "local", tokens: ["OST_Doors"], cursor: cursor), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.DocumentContextChanged, Error(Request(documentId: "other", cursor: cursor), rows).ErrorCode);

        var fingerprint = ParameterBindingQueryFingerprint.Compute(ParameterBindingQueryValidator.Validate(Request(identityKind: "local")));
        Assert.True(_cursors.TryMint(new DeclarativeQueryCursorPayload(DeclarativeQuerySources.ParameterBindings, "instance", "other-doc", fingerprint, 0, 1), out var otherDocument));
        Assert.True(_cursors.TryMint(new DeclarativeQueryCursorPayload(DeclarativeQuerySources.ParameterBindings, "other-instance", "doc", fingerprint, 0, 1), out var otherInstance));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(identityKind: "local", cursor: otherDocument), rows).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, Error(Request(identityKind: "local", cursor: otherInstance), rows).ErrorCode);

        // Token order is not part of the fingerprint, page size is not part of it either.
        Assert.Equal(
            ParameterBindingQueryFingerprint.Compute(ParameterBindingQueryValidator.Validate(Request(tokens: ["OST_Doors", "OST_Walls"], pageSize: 3))),
            ParameterBindingQueryFingerprint.Compute(ParameterBindingQueryValidator.Validate(Request(tokens: ["OST_Walls", "OST_Doors"], pageSize: 99))));
    }

    [Fact]
    public void A_revision_change_makes_a_cursor_stale_without_reading_the_map()
    {
        var rows = new[] { Row("A", Local("a")), Row("B", Local("b")) };
        var cursor = Execute(Request(pageSize: 1), rows, revision: 4).NextCursor;

        Assert.Equal(CapabilityErrorCodes.StaleCursor, Error(Request(pageSize: 1, cursor: cursor), rows, revision: 5).ErrorCode);
        Assert.Equal(0, CountReads(Request(pageSize: 1, cursor: cursor), rows, revision: 5));
        Assert.Equal("A", Execute(Request(pageSize: 1), rows, revision: 5).Bindings[0].Name);
    }

    [Fact]
    public void Invalid_queries_and_document_guards_do_not_read_the_map()
    {
        var rows = new[] { Row("A", Local("a")), Row("B", Local("b")) };
        var cursor = Execute(Request(pageSize: 1), rows).NextCursor;

        Assert.Equal(1, CountReads(Request(pageSize: 1, cursor: cursor), rows));
        Assert.Equal(0, CountReads(Request(cursor: "not a cursor"), rows));
        Assert.Equal(0, CountReads(Request(pageSize: 1, nameContains: "A", cursor: cursor), rows));
        Assert.Equal(0, CountReads(Request(tokens: ["NOT_A_CATEGORY"]), rows));
        Assert.Equal(0, CountReads(Request(documentId: "other"), rows));
        Assert.Equal(0, CountReads(Request(), rows, isFamilyDocument: true));
        Assert.Equal(0, CountReads(Request(), rows, hasActiveDocument: false));

        Assert.Equal(CapabilityErrorCodes.NoActiveDocument, Error(Request(), rows, hasActiveDocument: false).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.UnsupportedDocumentKind, Error(Request(), rows, isFamilyDocument: true).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.DocumentContextChanged, Error(Request(documentId: "other"), rows).ErrorCode);

        // A document mismatch is reported before a bad cursor or an undefined token.
        Assert.Equal(CapabilityErrorCodes.DocumentContextChanged, Error(Request(documentId: "other", tokens: ["NOT_A_CATEGORY"], cursor: "x"), rows).ErrorCode);
        // An undefined token is reported before a bad cursor.
        Assert.Equal(CapabilityErrorCodes.InvalidParameterBindingQuery, Error(Request(tokens: ["NOT_A_CATEGORY"], cursor: "x"), rows).ErrorCode);
    }

    [Fact]
    public void Unreadable_candidates_fail_the_whole_page()
    {
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, Error(Request(), [Row("A", Local("a"), parameterRef: "")]).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, Error(Request(), [Row("A", Local("a"), categories: Cat("OST_Doors", "Doors", ""))]).ErrorCode);
    }

    [Fact]
    public void The_result_budget_shortens_pages_without_losing_or_repeating_rows()
    {
        var wide = Enumerable.Range(0, 8)
            .Select(index => Row(
                "Wide" + index,
                Local("w" + index),
                categories: Enumerable.Range(0, 40).Select(category => Cat("OST_Doors", new string('N', 500) + category, "id:" + index + ":" + category)).ToArray()))
            .ToArray();

        var first = Execute(Request(pageSize: 8), wide);
        Assert.InRange(first.ReturnedCount, 1, 7);
        Assert.False(first.Complete);
        Assert.NotNull(first.NextCursor);
        Assert.True(ReadResultBudget.Measure(first) <= ReadResultBudget.MaxCharacters);

        var seen = Traverse(Request(pageSize: 8), wide).SelectMany(page => page.Bindings.Select(row => row.Name)).ToList();
        Assert.Equal(wide.Select(row => row.Name).ToArray(), seen);
    }

    [Fact]
    public void One_row_that_cannot_fit_is_result_too_large()
    {
        var huge = Row("Huge", Local("h"), categories: Enumerable.Range(0, 300).Select(index => Cat("OST_Doors", new string('X', 500), "id:" + index)).ToArray());
        Assert.Equal(CapabilityErrorCodes.ResultTooLarge, Error(Request(pageSize: 1), [huge]).ErrorCode);
    }

    [Fact]
    public void Unicode_names_survive_serialization()
    {
        var rows = new[]
        {
            Row("Tt_Capacité_Sectionneur", Shared(GuidB), categories: Cat("OST_ElectricalFixtures", "Appareils électriques", "id:-2001060")),
            Row("BOÎTE Ê Â", Local("x"))
        };

        var json = ReadResultBudget.Serialize(Execute(Request(), rows));
        var roundTrip = JsonSerializer.Deserialize<QueryParameterBindingsResult>(json, ReadResultBudget.Options)!;
        Assert.Equal(["BOÎTE Ê Â", "Tt_Capacité_Sectionneur"], roundTrip.Bindings.Select(row => row.Name).ToArray());
        Assert.Equal("Appareils électriques", roundTrip.Bindings[1].Categories[0].CategoryName);
        Assert.Equal("857e1417-0000-0000-0000-00000000000b", roundTrip.Bindings[1].Identity.Guid);
    }

    [Fact]
    public void Ordering_ignores_parameter_ref_and_input_order()
    {
        var rows = new[] { Row("C", Local("c"), parameterRef: "a"), Row("A", Local("a"), parameterRef: "z"), Row("B", Local("b"), parameterRef: "m") };
        var reversedRefs = new[] { Row("C", Local("c"), parameterRef: "z"), Row("A", Local("a"), parameterRef: "a"), Row("B", Local("b"), parameterRef: "m") };

        Assert.Equal(["A", "B", "C"], Names(Execute(Request(), rows)));
        Assert.Equal(["A", "B", "C"], Names(Execute(Request(), reversedRefs)));
        Assert.Equal(["A", "B", "C"], Names(Execute(Request(), Enumerable.Reverse(rows).ToArray())));
        Assert.Equal(["z", "m", "a"], Execute(Request(), rows).Bindings.Select(row => row.ParameterRef).ToArray());
    }

    [Fact]
    public void Rows_use_the_CAP_0004_identity_and_data_type_shapes()
    {
        var dataType = new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.MeasurableSpec, ForgeTypeId = "autodesk.spec.aec.electrical:potential-2.0.0" };
        var page = Execute(Request(), [Row("Tt_Tension", Shared(GuidA), dataType: dataType), Row("Local", Local("uid-123"), parameterRef: "opaque-ref")]);

        var local = page.Bindings[0];
        Assert.Equal(DescribeParameterIdentityKind.Local, local.Identity.Kind);
        Assert.Null(local.Identity.Guid);
        Assert.Null(local.Identity.ParameterTypeId);
        var shared = page.Bindings[1];
        Assert.Equal("b6a72840-0000-0000-0000-00000000000a", shared.Identity.Guid);
        Assert.Same(dataType, shared.DataType);
        Assert.DoesNotContain("uid-123", ReadResultBudget.Serialize(page), StringComparison.Ordinal);
    }

    private List<QueryParameterBindingsResult> Traverse(QueryParameterBindingsRequest request, IReadOnlyList<ParameterBindingCandidate> rows)
    {
        var pages = new List<QueryParameterBindingsResult>();
        string? cursor = null;
        for (var guard = 0; guard < 50; guard++)
        {
            var page = Execute(
                new QueryParameterBindingsRequest
                {
                    DocumentId = request.DocumentId,
                    NameContains = request.NameContains,
                    IdentityKind = request.IdentityKind,
                    BindingKind = request.BindingKind,
                    CategoryTokens = request.CategoryTokens,
                    PageSize = request.PageSize,
                    Cursor = cursor
                },
                rows);
            pages.Add(page);
            if (page.Complete)
            {
                Assert.Null(page.NextCursor);
                return pages;
            }

            cursor = page.NextCursor;
        }

        throw new InvalidOperationException("The traversal did not complete.");
    }

    private QueryParameterBindingsResult Execute(
        QueryParameterBindingsRequest request,
        IReadOnlyList<ParameterBindingCandidate> rows,
        long revision = 0,
        bool hasActiveDocument = true,
        bool isFamilyDocument = false)
    {
        return ParameterBindingInventoryQuery.Execute(
            ParameterBindingQueryValidator.Validate(request),
            "instance",
            "doc",
            hasActiveDocument,
            isFamilyDocument,
            revision,
            () => rows,
            _categories,
            _cursors);
    }

    private BridgeException Error(
        QueryParameterBindingsRequest request,
        IReadOnlyList<ParameterBindingCandidate> rows,
        long revision = 0,
        bool hasActiveDocument = true,
        bool isFamilyDocument = false)
    {
        return Assert.Throws<BridgeException>(() => Execute(request, rows, revision, hasActiveDocument, isFamilyDocument));
    }

    private int CountReads(
        QueryParameterBindingsRequest request,
        IReadOnlyList<ParameterBindingCandidate> rows,
        long revision = 0,
        bool hasActiveDocument = true,
        bool isFamilyDocument = false)
    {
        var reads = 0;
        try
        {
            ParameterBindingInventoryQuery.Execute(
                ParameterBindingQueryValidator.Validate(request),
                "instance",
                "doc",
                hasActiveDocument,
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

    private static void AssertInvalidQuery(QueryParameterBindingsRequest request)
    {
        var error = Assert.Throws<BridgeException>(() => ParameterBindingQueryValidator.Validate(request));
        Assert.Equal(CapabilityErrorCodes.InvalidParameterBindingQuery, error.ErrorCode);
    }

    private static void AssertInvalidCursor(QueryParameterBindingsRequest request)
    {
        var error = Assert.Throws<BridgeException>(() => ParameterBindingQueryValidator.Validate(request));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, error.ErrorCode);
    }

    private static string[] Names(QueryParameterBindingsResult result)
    {
        return result.Bindings.Select(row => row.Name).ToArray();
    }

    private static QueryParameterBindingsRequest Request(
        string? documentId = "doc",
        string? nameContains = null,
        string? identityKind = null,
        string? bindingKind = null,
        string[]? tokens = null,
        int? pageSize = null,
        string? cursor = null)
    {
        return new QueryParameterBindingsRequest
        {
            DocumentId = documentId,
            NameContains = nameContains,
            IdentityKind = identityKind,
            BindingKind = bindingKind,
            CategoryTokens = tokens,
            PageSize = pageSize,
            Cursor = cursor
        };
    }

    private static ClassifiedParameterIdentity BuiltIn(string typeId)
    {
        return ParameterIdentityClassifier.ClassifyDefinition(typeId, () => throw new InvalidOperationException());
    }

    private static ClassifiedParameterIdentity Shared(Guid guid)
    {
        return ParameterIdentityClassifier.ClassifyDefinition(null, () => new DefinitionElementFacts(guid, "unused"));
    }

    private static ClassifiedParameterIdentity Local(string key)
    {
        return ParameterIdentityClassifier.ClassifyDefinition(null, () => new DefinitionElementFacts(null, key));
    }

    private static ParameterBindingCategoryCandidate Cat(string token, string? name = null, string? key = null)
    {
        return new ParameterBindingCategoryCandidate(token, name ?? token.Replace("OST_", string.Empty, StringComparison.Ordinal), key ?? "bic:" + token);
    }

    private static ParameterBindingCandidate Row(
        string name,
        ClassifiedParameterIdentity identity,
        QueryParameterBindingKind binding = QueryParameterBindingKind.Instance,
        string? parameterRef = null,
        DescribeParameterDataType? dataType = null,
        params ParameterBindingCategoryCandidate[] categories)
    {
        return new ParameterBindingCandidate(
            parameterRef ?? "ref:" + name + ":" + identity.StableKey,
            name,
            identity,
            dataType ?? new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.Spec, ForgeTypeId = "autodesk.spec:spec.string-2.0.0" },
            binding,
            categories);
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
