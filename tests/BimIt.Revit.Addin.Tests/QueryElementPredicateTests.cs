using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class QueryElementPredicateTests
{
    [Fact]
    public void Missing_present_and_empty_are_distinct()
    {
        var missing = Surface(present: false);
        var noValue = Surface(present: true, hasValue: false);
        var blank = Surface(present: true, hasValue: true, storage: QueryParameterStorage.String, text: "");
        var spaces = Surface(present: true, hasValue: true, storage: QueryParameterStorage.String, text: " ");
        var zero = Surface(present: true, hasValue: true, storage: QueryParameterStorage.Integer, integer: 0);
        var elementId = Surface(present: true, hasValue: true, storage: QueryParameterStorage.ElementId);

        Assert.True(Match(Exists("is_missing"), missing));
        Assert.False(Match(Exists("is_present"), missing));
        Assert.False(Match(Exists("is_empty"), missing));
        Assert.False(Match(Exists("is_not_empty"), missing));

        Assert.False(Match(Exists("is_missing"), noValue));
        Assert.True(Match(Exists("is_present"), noValue));
        Assert.True(Match(Exists("is_empty"), noValue));
        Assert.False(Match(Exists("is_not_empty"), noValue));

        Assert.True(Match(Exists("is_empty"), blank));
        Assert.False(Match(Exists("is_empty"), spaces));
        Assert.True(Match(Exists("is_not_empty"), spaces));
        Assert.False(Match(Exists("is_empty"), zero));
        Assert.True(Match(Exists("is_not_empty"), zero));
        Assert.False(Match(Exists("is_empty"), elementId));
    }

    [Fact]
    public void Missing_does_not_satisfy_not_equals()
    {
        Assert.False(Match(StringPredicate("not_equals", "Mark"), Surface(present: false)));
        Assert.False(Match(
            IntegerPredicate("not_equals", 0),
            Surface(present: true, hasValue: false)));
    }

    [Fact]
    public void Instance_and_type_sources_read_different_surfaces()
    {
        var instanceReads = 0;
        var typeReads = 0;
        var instance = QueryParameterSurfaceResolver.Resolve(
            GetElementParameterSource.Instance,
            () =>
            {
                instanceReads++;
                return Surface(present: true, hasValue: true, storage: QueryParameterStorage.String, text: "Mark");
            },
            () =>
            {
                typeReads++;
                return Surface(present: true, hasValue: true, storage: QueryParameterStorage.String, text: "Type");
            });
        var type = QueryParameterSurfaceResolver.Resolve(
            GetElementParameterSource.Type,
            () => Surface(present: true, hasValue: true, storage: QueryParameterStorage.Integer, integer: 1),
            () => Surface(present: true, hasValue: true, storage: QueryParameterStorage.Integer, integer: 4));
        var missingType = QueryParameterSurfaceResolver.Resolve(
            GetElementParameterSource.Type,
            () => Surface(present: true, hasValue: true, storage: QueryParameterStorage.String, text: "Instance"),
            () => null);

        Assert.Equal(1, instanceReads);
        Assert.Equal(0, typeReads);
        Assert.True(Match(StringPredicate("equals", "Mark"), instance));
        Assert.False(Match(StringPredicate("equals", "Type"), instance));
        Assert.True(Match(IntegerPredicate("equals", 4), type));
        Assert.False(Match(IntegerPredicate("equals", 1), type));
        Assert.True(Match(Exists("is_missing"), missingType));
        Assert.False(Match(Exists("is_empty"), missingType));
    }

    [Fact]
    public void String_equality_and_contains_are_ordinal_and_untrimmed()
    {
        var surface = Surface(present: true, hasValue: true, storage: QueryParameterStorage.String, text: "VAV Box");

        Assert.True(Match(StringPredicate("equals", "VAV Box"), surface));
        Assert.False(Match(StringPredicate("equals", "vav box"), surface));
        Assert.True(Match(StringPredicate("contains", "VAV"), surface));
        Assert.False(Match(StringPredicate("contains", "vav"), surface));
        Assert.False(Match(StringPredicate("equals", " VAV Box"), surface));
        Assert.True(Match(StringPredicate("not_equals", "Other"), surface));
        Assert.False(Match(
            StringPredicate("equals", "VAV Box"),
            Surface(present: true, hasValue: true, storage: QueryParameterStorage.Integer, integer: 1)));
    }

    [Fact]
    public void Integer_comparisons_use_the_stored_integer()
    {
        var surface = Surface(present: true, hasValue: true, storage: QueryParameterStorage.Integer, integer: 5);

        Assert.True(Match(IntegerPredicate("equals", 5), surface));
        Assert.True(Match(IntegerPredicate("not_equals", 4), surface));
        Assert.False(Match(IntegerPredicate("not_equals", 5), surface));
        Assert.True(Match(IntegerPredicate("greater_than", 4), surface));
        Assert.True(Match(IntegerPredicate("greater_or_equal", 5), surface));
        Assert.True(Match(IntegerPredicate("less_than", 6), surface));
        Assert.True(Match(IntegerPredicate("less_or_equal", 5), surface));
        Assert.False(Match(IntegerPredicate("greater_than", 5), surface));
    }

    [Fact]
    public void Quantity_comparison_uses_the_converted_internal_value()
    {
        var quantities = new RecordingQuantityConverter { Converted = 3 };
        var predicate = Prepare(QuantityPredicate("greater_than", 1, "unit:feet"), quantities);
        var surface = Surface(
            present: true,
            hasValue: true,
            storage: QueryParameterStorage.Double,
            number: 2,
            spec: "spec:length",
            measurable: true);

        Assert.False(new QueryPredicateEvaluator(quantities).Matches(predicate, surface));
        Assert.Equal(1, quantities.IsValidUnitCalls);
        Assert.Equal(1, quantities.ConvertCalls);
        Assert.True(new QueryPredicateEvaluator(quantities).Matches(
            Prepare(QuantityPredicate("less_than", 1, "unit:feet"), quantities),
            surface));
    }

    [Fact]
    public void Incompatible_quantity_unit_is_rejected_before_conversion()
    {
        var quantities = new RecordingQuantityConverter { Valid = false, ConvertThrows = true };
        var predicate = Prepare(QuantityPredicate("less_or_equal", 2, "unit:celsius"), quantities);
        var surface = Surface(
            present: true,
            hasValue: true,
            storage: QueryParameterStorage.Double,
            number: 1,
            spec: "spec:length",
            measurable: true);

        var exception = Assert.Throws<BridgeException>(() =>
            new QueryPredicateEvaluator(quantities).Matches(predicate, surface));

        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
        Assert.Equal(1, quantities.IsValidUnitCalls);
        Assert.Equal(0, quantities.ConvertCalls);
    }

    [Fact]
    public void Non_measurable_double_is_rejected_before_unit_validation()
    {
        var quantities = new RecordingQuantityConverter { ConvertThrows = true };
        var predicate = Prepare(QuantityPredicate("greater_or_equal", 1, "unit:feet"), quantities);
        var surface = Surface(
            present: true,
            hasValue: true,
            storage: QueryParameterStorage.Double,
            number: 1,
            spec: "",
            measurable: false);

        var exception = Assert.Throws<BridgeException>(() =>
            new QueryPredicateEvaluator(quantities).Matches(predicate, surface));

        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
        Assert.Equal(0, quantities.IsValidUnitCalls);
        Assert.Equal(0, quantities.ConvertCalls);
    }

    [Fact]
    public void Non_finite_quantity_is_rejected()
    {
        var quantities = new RecordingQuantityConverter();
        AssertInvalid(Request(QuantityPredicate("greater_than", double.NaN, "unit:feet")));
        AssertInvalid(Request(QuantityPredicate("greater_than", double.PositiveInfinity, "unit:feet")));
        Assert.Equal(0, quantities.IsUnitCalls);

        var predicate = Prepare(QuantityPredicate("less_than", 1, "unit:feet"), quantities);
        quantities.Converted = double.NaN;
        var exception = Assert.Throws<BridgeException>(() =>
            new QueryPredicateEvaluator(quantities).Matches(
                predicate,
                Surface(present: true, hasValue: true, storage: QueryParameterStorage.Double, number: 1, spec: "spec:length", measurable: true)));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
        Assert.Equal(1, quantities.ConvertCalls);
    }

    [Fact]
    public void Unknown_unit_token_is_rejected_before_scanning_or_conversion()
    {
        var quantities = new RecordingQuantityConverter { Unit = false, ConvertThrows = true };
        var exception = Assert.Throws<BridgeException>(() =>
            QueryPredicatePreparer.Prepare(
                [QuantityPredicate("greater_than", 1, "not-a-unit")],
                _ => Binding(GetElementParameterSource.Instance),
                quantities));

        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
        Assert.Equal(1, quantities.IsUnitCalls);
        Assert.Equal(0, quantities.IsValidUnitCalls);
        Assert.Equal(0, quantities.ConvertCalls);
    }

    [Fact]
    public void Unknown_parameter_ref_is_an_invalid_query()
    {
        var quantities = new RecordingQuantityConverter();
        var exception = Assert.Throws<BridgeException>(() =>
            QueryPredicatePreparer.Prepare(
                [Exists("is_present")],
                _ => null,
                quantities));

        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
        Assert.Equal(0, quantities.IsUnitCalls);
    }

    [Fact]
    public void Multiple_predicates_are_anded_and_invalid_quantity_is_not_hidden()
    {
        var quantities = new RecordingQuantityConverter { Valid = false, ConvertThrows = true };
        var binding = Binding(GetElementParameterSource.Instance);
        var resolved = QueryPredicatePreparer.Prepare(
            [
                StringPredicate("equals", "Other"),
                QuantityPredicate("greater_than", 1, "unit:feet")
            ],
            _ => binding,
            quantities);
        var evaluator = new QueryPredicateEvaluator(quantities);

        Assert.False(evaluator.MatchesAll(
            [resolved[0]],
            _ => Surface(present: true, hasValue: true, storage: QueryParameterStorage.String, text: "Mark")));
        Assert.Throws<BridgeException>(() => evaluator.MatchesAll(
            resolved,
            _ => Surface(
                present: true,
                hasValue: true,
                storage: QueryParameterStorage.Double,
                number: 4,
                spec: "spec:length",
                measurable: true)));
        Assert.Equal(0, quantities.ConvertCalls);
    }

    [Fact]
    public void Predicate_bounds_and_applicability_are_rejected()
    {
        AssertInvalid(Request(predicates: []));
        AssertInvalid(Request(Enumerable.Range(0, 9).Select(_ => Exists("is_present")).ToArray()));
        QueryElementsRequestValidator.Validate(Request(Enumerable.Range(0, 8).Select(_ => Exists("is_present")).ToArray()));

        AssertInvalid(Request(StringPredicate("greater_than", "A")));
        AssertInvalid(Request(IntegerPredicate("contains", 1)));
        AssertInvalid(Request(QuantityPredicate("equals", 1, "unit:feet")));
        AssertInvalid(Request(Exists("is_empty", new QueryPredicateStringValue { Value = "" })));
        AssertInvalid(Request(new QueryElementPredicate
        {
            Source = "parameter",
            ParameterRef = "pref",
            Operator = "equals"
        }));
        AssertInvalid(Request(new QueryElementPredicate
        {
            Source = "api_property",
            ParameterRef = "pref",
            Operator = "is_present"
        }));
    }

    [Fact]
    public void Document_scope_accepts_predicates_without_coarse_filters()
    {
        QueryElementsRequestValidator.Validate(new QueryElementsRequest
        {
            Scope = QueryScope.Document,
            Filters = new QueryElementFilters(),
            Predicates = [Exists("is_present")],
            Limit = 50
        });
        AssertInvalid(new QueryElementsRequest
        {
            Scope = QueryScope.Document,
            Filters = new QueryElementFilters(),
            Limit = 50
        });
        QueryElementsRequestValidator.Validate(new QueryElementsRequest
        {
            Scope = QueryScope.ActiveView,
            Filters = new QueryElementFilters(),
            Limit = 25
        });
    }

    [Fact]
    public void Candidate_budget_is_counted_after_coarse_filtering_and_overflow_returns_nothing()
    {
        var evaluated = 0;
        var withinBudget = QueryElementsMatchPlan.Match(
            Enumerable.Range(0, 30),
            value => value < 4,
            value => value.ToString(),
            value =>
            {
                evaluated++;
                return value % 2 == 0;
            },
            limit: 10,
            maxPredicateCandidates: 10);

        Assert.Equal(2, withinBudget.MatchedCount);
        Assert.Equal(4, evaluated);
        Assert.Equal(["0", "2"], withinBudget.ElementRefs);

        evaluated = 0;
        var overflow = Assert.Throws<BridgeException>(() => QueryElementsMatchPlan.Match(
            Enumerable.Range(0, 12),
            _ => true,
            value => value.ToString(),
            _ =>
            {
                evaluated++;
                return true;
            },
            limit: 50,
            maxPredicateCandidates: QueryPredicateLimits.MaxPredicateCandidates == 10_000 ? 10 : 10));

        Assert.Equal(CapabilityErrorCodes.QueryCandidateLimit, overflow.ErrorCode);
        Assert.Equal(0, evaluated);
        Assert.Equal(10_000, QueryPredicateLimits.MaxPredicateCandidates);
    }

    [Fact]
    public void Legacy_queries_do_not_use_the_candidate_budget()
    {
        var evaluated = 0;
        var match = QueryElementsMatchPlan.Match(
            Enumerable.Range(0, 20_000),
            _ => true,
            value => value.ToString("D5"),
            predicateMatch: null,
            limit: 50,
            maxPredicateCandidates: 10);

        Assert.Equal(20_000, match.MatchedCount);
        Assert.True(match.Truncated);
        Assert.Equal(50, match.ElementRefs.Count);
        Assert.Equal("00000", match.ElementRefs[0]);
        Assert.Equal(0, evaluated);
    }

    [Fact]
    public void Query_candidate_does_not_carry_parameter_fields()
    {
        Assert.DoesNotContain(
            typeof(QueryElementCandidate).GetProperties(),
            property => property.Name.Contains("Parameter", StringComparison.Ordinal));
    }

    [Fact]
    public void Revit_quantity_converter_checks_the_unit_before_converting()
    {
        var converter = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Addin", "Query", "RevitQueryQuantityConverter.cs"));
        var isUnit = converter.IndexOf("UnitUtils.IsUnit(", StringComparison.Ordinal);
        var isValid = converter.IndexOf("UnitUtils.IsValidUnit(", StringComparison.Ordinal);
        var convert = converter.IndexOf("UnitUtils.ConvertToInternalUnits(", StringComparison.Ordinal);
        Assert.True(isUnit >= 0 && isValid > isUnit && convert > isValid);

        var evaluator = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Addin", "Query", "QueryPredicateEvaluator.cs"));
        var validCall = evaluator.IndexOf(".IsValidUnit(", StringComparison.Ordinal);
        var convertCall = evaluator.IndexOf(".ConvertToInternalUnits(", StringComparison.Ordinal);
        Assert.True(validCall >= 0 && convertCall > validCall);

        var reader = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Addin", "Query", "RevitQueryParameterSurfaceReader.cs"));
        Assert.Contains("RevitParameterIdentity.Classify", reader, StringComparison.Ordinal);
        Assert.Contains("metadata.ResolveType", reader, StringComparison.Ordinal);
        var resolver = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Addin", "Query", "QueryParameterSurfaceResolver.cs"));
        Assert.Contains("GetElementParameterSource.Type", resolver, StringComparison.Ordinal);
        Assert.Contains("GetOrderedParameters", reader, StringComparison.Ordinal);
        Assert.Contains("StorageType.Double", reader, StringComparison.Ordinal);
        Assert.Contains("UnitUtils.IsMeasurableSpec", reader, StringComparison.Ordinal);
    }

    private static bool Match(QueryElementPredicate predicate, QueryParameterSurface surface)
    {
        var resolved = QueryPredicatePreparer.Prepare(
            [predicate],
            _ => Binding(GetElementParameterSource.Instance),
            new RecordingQuantityConverter());
        return new QueryPredicateEvaluator(new RecordingQuantityConverter()).Matches(resolved[0], surface);
    }

    private static ResolvedParameterPredicate Prepare(QueryElementPredicate predicate, RecordingQuantityConverter quantities)
    {
        return QueryPredicatePreparer.Prepare(
            [predicate],
            _ => Binding(GetElementParameterSource.Instance),
            quantities)[0];
    }

    private static QueryElementsRequest Request(params QueryElementPredicate[] predicates)
    {
        return new QueryElementsRequest
        {
            Scope = QueryScope.Document,
            Filters = new QueryElementFilters { CategoryNames = ["Walls"] },
            Predicates = predicates,
            Limit = 50
        };
    }

    private static void AssertInvalid(QueryElementsRequest request)
    {
        var exception = Assert.Throws<BridgeException>(() => QueryElementsRequestValidator.Validate(request));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, exception.ErrorCode);
    }

    private static QueryElementPredicate Exists(string op, QueryPredicateValue? value = null)
    {
        return new QueryElementPredicate
        {
            Source = "parameter",
            ParameterRef = "pref",
            Operator = op,
            Value = value
        };
    }

    private static QueryElementPredicate StringPredicate(string op, string value)
    {
        return Exists(op, new QueryPredicateStringValue { Value = value });
    }

    private static QueryElementPredicate IntegerPredicate(string op, int value)
    {
        return Exists(op, new QueryPredicateIntegerValue { Value = value });
    }

    private static QueryElementPredicate QuantityPredicate(string op, double value, string unit)
    {
        return Exists(op, new QueryPredicateQuantityValue { Value = value, UnitTypeId = unit });
    }

    private static ParameterIdentityBinding Binding(GetElementParameterSource source)
    {
        return new ParameterIdentityBinding(
            source,
            new ClassifiedParameterIdentity(
                DescribeParameterIdentityKind.BuiltIn,
                "autodesk.parameter:mark",
                null,
                "autodesk.parameter:mark"));
    }

    private static QueryParameterSurface Surface(
        bool present,
        bool hasValue = false,
        QueryParameterStorage storage = QueryParameterStorage.None,
        string? text = null,
        int integer = 0,
        double number = 0,
        string? spec = null,
        bool measurable = false)
    {
        if (!present)
        {
            return QueryParameterSurface.Missing;
        }

        return new QueryParameterSurface
        {
            Present = true,
            HasValue = hasValue,
            Storage = storage,
            StringValue = text,
            IntegerValue = integer,
            DoubleValue = number,
            SpecTypeId = spec,
            MeasurableSpec = measurable
        };
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln.");
    }

    private sealed class RecordingQuantityConverter : IQueryQuantityConverter
    {
        public int IsUnitCalls { get; private set; }

        public int IsValidUnitCalls { get; private set; }

        public int ConvertCalls { get; private set; }

        public bool Unit { get; set; } = true;

        public bool Valid { get; set; } = true;

        public bool ConvertThrows { get; set; }

        public double Converted { get; set; } = 1;

        public bool IsUnit(string unitTypeId)
        {
            IsUnitCalls++;
            return Unit;
        }

        public bool IsValidUnit(string specTypeId, string unitTypeId)
        {
            IsValidUnitCalls++;
            return Valid;
        }

        public double ConvertToInternalUnits(double value, string unitTypeId)
        {
            ConvertCalls++;
            if (ConvertThrows)
            {
                throw new InvalidOperationException("Conversion ran before validation.");
            }

            return Converted;
        }
    }
}
