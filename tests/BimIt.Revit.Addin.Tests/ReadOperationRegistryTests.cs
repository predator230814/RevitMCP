using System.Text.Json;
using BimIt.Revit.Addin.ReadOperations;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class ReadOperationRegistryTests
{
    private const string ClosedObject = """{"type":"object","additionalProperties":false}""";

    [Fact]
    public void One_valid_operation_registers_and_search_omits_schemas()
    {
        var operation = new ProjectUserWorksetsReadOperation();
        var registry = new ReadOperationRegistry([operation]);
        var page = registry.Search(null, 0, 25);

        Assert.Equal("search", page.Mode);
        Assert.Equal(1, page.Total);
        Assert.Equal(1, page.ReturnedCount);
        Assert.Null(page.NextOffset);
        var descriptor = Assert.Single(page.Operations);
        Assert.Equal(ProjectUserWorksetsReadOperation.OperationId, descriptor.Id);
        Assert.Null(descriptor.InputSchema);
        Assert.Null(descriptor.OutputSchema);
        Assert.NotSame(operation.Descriptor, descriptor);
    }

    [Fact]
    public void Versions_sort_by_id_then_version_regardless_of_registration_order()
    {
        var registry = new ReadOperationRegistry(
        [
            Operation("z.catalog", 1, "Zed"),
            Operation("a.catalog", 2, "Second"),
            Operation(ProjectUserWorksetsReadOperation.OperationId, 9, "Later"),
            Operation("a.catalog", 1, "First")
        ]);

        var page = registry.Search(null, 0, 10);
        Assert.Equal(
            ["a.catalog:1", "a.catalog:2", ProjectUserWorksetsReadOperation.OperationId + ":9", "z.catalog:1"],
            page.Operations.Select(item => item.Id + ":" + item.ContractVersion).ToArray());
    }

    [Fact]
    public void Search_matches_id_title_and_description_without_fuzzy_matching()
    {
        var registry = new ReadOperationRegistry(
        [
            Operation("alpha.beta", 1, "Plain title", "plain words"),
            Operation("other.item", 1, "Workset inventory", "nothing here"),
            Operation("third.item", 1, "Third", "a   b")
        ]);

        Assert.Equal(["alpha.beta"], Ids(registry.Search("ALPHA", 0, 10)));
        Assert.Equal(["other.item"], Ids(registry.Search("workset", 0, 10)));
        Assert.Equal(["alpha.beta"], Ids(registry.Search("PLAIN WORDS", 0, 10)));
        Assert.Empty(Ids(registry.Search("wrkset", 0, 10)));
        Assert.Equal(["third.item"], Ids(registry.Search("   ", 0, 10)));
        Assert.Equal(3, registry.Search(null, 0, 10).Total);
    }

    [Fact]
    public void Offset_past_the_end_returns_an_empty_page_and_keeps_total()
    {
        var registry = new ReadOperationRegistry(
        [
            Operation("a.one", 1, "One"),
            Operation("b.two", 1, "Two"),
            Operation("c.three", 1, "Three")
        ]);

        var middle = registry.Search(null, 1, 1);
        Assert.Equal(3, middle.Total);
        Assert.Equal(1, middle.ReturnedCount);
        Assert.Equal(2, middle.NextOffset);
        Assert.Equal("b.two", Assert.Single(middle.Operations).Id);

        var tail = registry.Search(null, 2, 2);
        Assert.Equal(3, tail.Total);
        Assert.Equal(1, tail.ReturnedCount);
        Assert.Null(tail.NextOffset);

        var past = registry.Search(null, 5, 25);
        Assert.Equal(3, past.Total);
        Assert.Equal(0, past.ReturnedCount);
        Assert.Null(past.NextOffset);
        Assert.Empty(past.Operations);
    }

    [Fact]
    public void Exact_lookup_includes_schemas_and_returns_versions_ascending()
    {
        var first = Operation("project.sample", 1, "Sample");
        var later = Operation("project.sample", 3, "Sample later");
        var registry = new ReadOperationRegistry([later, Operation("other.sample", 1, "Other"), first]);

        var all = registry.LookupExact("project.sample", null);
        Assert.Equal(ReadOperationResolution.Found, all.Resolution);
        Assert.NotNull(all.Result);
        Assert.Equal("exact", all.Result.Mode);
        Assert.Equal(2, all.Result.Total);
        Assert.Equal(2, all.Result.ReturnedCount);
        Assert.Null(all.Result.NextOffset);
        Assert.Equal([1, 3], all.Result.Operations.Select(item => item.ContractVersion).ToArray());
        Assert.Same(first.Descriptor, all.Result.Operations[0]);
        Assert.Equal(JsonValueKind.Object, all.Result.Operations[0].InputSchema!.Value.ValueKind);
        Assert.Equal(JsonValueKind.Object, all.Result.Operations[0].OutputSchema!.Value.ValueKind);
        Assert.Empty(all.RegisteredVersions);

        var one = registry.LookupExact("project.sample", 3);
        Assert.Equal(ReadOperationResolution.Found, one.Resolution);
        Assert.Equal(3, Assert.Single(one.Result!.Operations).ContractVersion);
        Assert.NotNull(one.Result.Operations[0].InputSchema);
    }

    [Fact]
    public void Unknown_id_is_distinct_from_a_known_id_with_an_unsupported_version()
    {
        var registry = new ReadOperationRegistry(
        [
            Operation("project.sample", 3, "Later"),
            Operation("project.sample", 1, "Sample")
        ]);

        var unknown = registry.LookupExact("Project.sample", 1);
        Assert.Equal(ReadOperationResolution.UnknownOperation, unknown.Resolution);
        Assert.Null(unknown.Result);
        Assert.Empty(unknown.RegisteredVersions);

        var wrongCaseAndVersion = registry.LookupExact("PROJECT.SAMPLE", 9);
        Assert.Equal(ReadOperationResolution.UnknownOperation, wrongCaseAndVersion.Resolution);

        var unsupported = registry.LookupExact("project.sample", 2);
        Assert.Equal(ReadOperationResolution.UnsupportedVersion, unsupported.Resolution);
        Assert.Null(unsupported.Result);
        Assert.Equal([1, 3], unsupported.RegisteredVersions);
    }

    [Fact]
    public void Registry_keeps_the_descriptor_captured_at_registration()
    {
        var first = new OneShotOperation(Operation("project.sample", 1, "First").Descriptor);
        var later = new OneShotOperation(Operation("project.sample", 2, "Later").Descriptor);
        var registry = new ReadOperationRegistry([later, first]);

        var search = registry.Search(null, 0, 10);
        Assert.Equal(["First", "Later"], search.Operations.Select(item => item.Title).ToArray());
        Assert.Equal([1, 2], search.Operations.Select(item => item.ContractVersion).ToArray());
        Assert.Null(search.Operations[0].InputSchema);

        var exact = registry.LookupExact("project.sample", null);
        Assert.Same(first.Registered, exact.Result!.Operations[0]);
        Assert.Same(later.Registered, exact.Result.Operations[1]);

        var unsupported = registry.LookupExact("project.sample", 9);
        Assert.Equal(ReadOperationResolution.UnsupportedVersion, unsupported.Resolution);
        Assert.Equal([1, 2], unsupported.RegisteredVersions);

        Assert.Equal(1, first.Reads);
        Assert.Equal(1, later.Reads);
    }

    [Fact]
    public void A_typed_enum_property_remains_a_valid_nested_definition()
    {
        var registry = new ReadOperationRegistry(
        [
            Operation(
                "project.sample",
                input: """
                    {"type":"object","additionalProperties":false,"properties":{"kind":{"type":"string","enum":["a","b"]}}}
                    """)
        ]);

        var exact = registry.LookupExact("project.sample", 1);
        Assert.Equal(ReadOperationResolution.Found, exact.Resolution);
    }

    [Fact]
    public void A_sixty_four_character_id_registers_and_sixty_five_does_not()
    {
        var accepted = "a." + new string('b', 62);
        Assert.Equal(64, accepted.Length);
        var registry = new ReadOperationRegistry([Operation(accepted)]);
        Assert.Equal(accepted, Assert.Single(registry.Search(null, 0, 1).Operations).Id);

        var rejected = "a." + new string('b', 63);
        Assert.Equal(65, rejected.Length);
        Assert.Throws<ReadOperationRegistrationException>(() => new ReadOperationRegistry([Operation(rejected)]));
    }

    [Theory]
    [MemberData(nameof(InvalidRegistrations))]
    public void Invalid_registration_rejects_the_whole_list(string name, object invalid)
    {
        var rejected = Assert.IsAssignableFrom<IRevitReadOperation>(invalid);
        var valid = Operation("kept.operation");
        var alone = new ReadOperationRegistry([valid]);
        Assert.Equal("kept.operation", Assert.Single(alone.Search(null, 0, 1).Operations).Id);

        var exception = Assert.Throws<ReadOperationRegistrationException>(
            () => new ReadOperationRegistry([valid, rejected]));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void Duplicate_id_and_version_rejects_the_whole_list()
    {
        var valid = Operation("kept.operation");
        Assert.Throws<ReadOperationRegistrationException>(() => new ReadOperationRegistry(
        [
            valid,
            Operation("project.sample", 1),
            Operation("project.sample", 1)
        ]));
    }

    public static IEnumerable<object[]> InvalidRegistrations()
    {
        yield return Case("one segment", Operation("project"));
        yield return Case("uppercase id", Operation("Project.worksets"));
        yield return Case("digit segment", Operation("project.1worksets"));
        yield return Case("version zero", Operation("project.sample", version: 0));
        yield return Case("effects", Operation("project.sample", effects: "write"));
        yield return Case("context", Operation("project.sample", context: "view"));
        yield return Case("pagination", Operation("project.sample", pagination: "offset"));
        yield return Case("missing input schema", Operation("project.sample", omitInput: true));
        yield return Case("missing output schema", Operation("project.sample", omitOutput: true));
        yield return Case("non-object schema root", Operation("project.sample", input: """{"type":"string"}"""));
        yield return Case("open root object", Operation("project.sample", input: """{"type":"object","additionalProperties":true}"""));
        yield return Case("empty object without closure", Operation("project.sample", input: """{"type":"object"}"""));
        yield return Case(
            "open nested object",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"child":{"type":"object"}}}"""));
        yield return Case(
            "open object array item",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"rows":{"type":"array","items":{"type":"object"}}}}"""));
        yield return Case(
            "nullable object without closure",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"child":{"type":["object","null"]}}}"""));
        yield return Case(
            "nested empty schema",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"child":{}}}"""));
        yield return Case(
            "enum without type",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"child":{"enum":["a","b"]}}}"""));
        yield return Case(
            "unknown type",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"child":{"type":"date"}}}"""));
        yield return Case(
            "required names an undeclared property",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}},"required":["missing"]}"""));
        yield return Case(
            "minLength greater than maxLength",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"name":{"type":"string","minLength":3,"maxLength":1}}}"""));
        yield return Case(
            "items as an array",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"rows":{"type":"array","items":[{"type":"string"}]}}}"""));
        yield return Case(
            "type-inapplicable keyword",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"name":{"type":"number","minLength":1}}}"""));
        yield return Case(
            "pattern",
            Operation("project.sample", input: """{"type":"object","additionalProperties":false,"properties":{"name":{"type":"string","pattern":"a"}}}"""));
        yield return Case(
            "oneOf",
            Operation("project.sample", output: """{"type":"object","additionalProperties":false,"oneOf":[{"type":"object"}]}"""));
    }

    private static object[] Case(string name, IRevitReadOperation operation)
    {
        return [name, operation];
    }

    private static IReadOnlyList<string> Ids(LookupReadOperationsResult result)
    {
        return result.Operations.Select(item => item.Id).ToArray();
    }

    private static FixtureOperation Operation(
        string id,
        int version = 1,
        string title = "Title",
        string description = "Description",
        string context = "document",
        string effects = "read",
        string pagination = "none",
        string? input = ClosedObject,
        string? output = ClosedObject,
        bool omitInput = false,
        bool omitOutput = false)
    {
        return new FixtureOperation(new ReadOperationDescriptor
        {
            Id = id,
            ContractVersion = version,
            Title = title,
            Description = description,
            Context = context,
            Effects = effects,
            Pagination = pagination,
            InputSchema = omitInput ? null : Parse(input!),
            OutputSchema = omitOutput ? null : Parse(output!)
        });
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FixtureOperation(ReadOperationDescriptor descriptor) : IRevitReadOperation
    {
        public ReadOperationDescriptor Descriptor { get; } = descriptor;
    }

    private sealed class OneShotOperation(ReadOperationDescriptor registered) : IRevitReadOperation
    {
        private int _reads;

        public ReadOperationDescriptor Registered { get; } = registered;

        public int Reads => _reads;

        public ReadOperationDescriptor Descriptor
        {
            get
            {
                if (_reads++ > 0)
                {
                    throw new InvalidOperationException("Descriptor was read again after registration.");
                }

                return Registered;
            }
        }
    }
}
