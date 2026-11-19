using System.Reflection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Type and public instance property metadata. Search does not invoke getters.
/// </summary>
internal static class RevitApiMetadataIndex
{
    public const int MaxSearchMatches = 200;

    public const int MaxMatchedMembers = 8;

    public const int MaxListedProperties = 80;

    public static IReadOnlyList<RevitApiMetadataType> Search(string query)
    {
        return Search(RevitApiTypeResolver.Assembly, query);
    }

    public static IReadOnlyList<RevitApiMetadataType> Search(Assembly assembly, string query)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(query);
        var queryTokens = RevitApiTokens.Tokenize(query);
        if (queryTokens.Count == 0 && query.Length < 2)
        {
            throw InvalidQuery("query has no searchable tokens.");
        }

        var matches = new List<RevitApiMetadataType>();
        foreach (var type in ElementTypes(assembly))
        {
            if (TryMatch(type, query, queryTokens, out var summary))
            {
                matches.Add(summary);
            }
        }

        if (matches.Count > MaxSearchMatches)
        {
            throw InvalidQuery("query matches more than 200 types.");
        }

        if (queryTokens.Count == 0 && matches.Count == 0)
        {
            throw InvalidQuery("query has no searchable tokens.");
        }

        return matches;
    }

    public static RevitApiMetadataType DescribeExact(string fullName)
    {
        return DescribeExact(RevitApiTypeResolver.Assembly, fullName);
    }

    public static RevitApiMetadataType DescribeExact(Assembly assembly, string fullName)
    {
        var type = RevitApiTypeResolver.FindPublicType(assembly, fullName);
        if (type is null)
        {
            throw RevitApiTypeResolver.Reject("unknown");
        }

        return Describe(assembly, type);
    }

    public static RevitApiMetadataType Describe(Assembly assembly, Type type)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(type);
        var element = RevitApiTypeResolver.IsElement(type);
        var listed = element
            ? ListElementProperties(assembly, type)
            : (Listed: (IReadOnlyList<RevitApiMetadataProperty>)[], RejectedOmitted: 0);
        return Summary(type, assembly, element, [], 0, listed.Listed, listed.RejectedOmitted);
    }

    public static (IReadOnlyList<string> Members, int Omitted) LimitMembers(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count <= MaxMatchedMembers)
        {
            return (names, 0);
        }

        var kept = new string[MaxMatchedMembers];
        for (var index = 0; index < MaxMatchedMembers; index++)
        {
            kept[index] = names[index];
        }

        return (kept, names.Count - MaxMatchedMembers);
    }

    public static (IReadOnlyList<RevitApiMetadataProperty> Listed, int RejectedOmitted) LimitProperties(
        IReadOnlyList<RevitApiMetadataProperty> projectable,
        IReadOnlyList<RevitApiMetadataProperty> rejected)
    {
        ArgumentNullException.ThrowIfNull(projectable);
        ArgumentNullException.ThrowIfNull(rejected);
        if (projectable.Count > MaxListedProperties)
        {
            throw new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The read operation result is too large.");
        }

        var room = MaxListedProperties - projectable.Count;
        var take = Math.Min(room, rejected.Count);
        var listed = new RevitApiMetadataProperty[projectable.Count + take];
        for (var index = 0; index < projectable.Count; index++)
        {
            listed[index] = projectable[index];
        }

        for (var index = 0; index < take; index++)
        {
            listed[projectable.Count + index] = rejected[index];
        }

        return (listed, rejected.Count - take);
    }

    private static bool TryMatch(
        Type type,
        string query,
        IReadOnlyList<string> queryTokens,
        out RevitApiMetadataType summary)
    {
        var simpleName = type.Name;
        var typeTokens = RevitApiTokens.Tokenize(simpleName);
        var properties = PropertyTokens(type);
        var substring = query.Length >= 2
            && simpleName.Contains(query, StringComparison.OrdinalIgnoreCase);
        var matchedProperties = new List<string>();
        if (queryTokens.Count > 0)
        {
            foreach (var property in properties)
            {
                if (SharesToken(queryTokens, property.Tokens))
                {
                    matchedProperties.Add(property.Name);
                }
            }
        }

        var typeNameHit = RevitApiTokens.MatchesAll(queryTokens, typeTokens);
        var tokenHit = typeNameHit || (queryTokens.Count > 0 && QueryMatches(queryTokens, typeTokens, properties));
        if (!tokenHit && !substring)
        {
            summary = null!;
            return false;
        }

        if (typeNameHit || !tokenHit)
        {
            matchedProperties.Clear();
        }

        matchedProperties.Sort(StringComparer.Ordinal);
        var limited = LimitMembers(matchedProperties);
        summary = Summary(
            type,
            type.Assembly,
            element: true,
            limited.Members,
            limited.Omitted,
            [],
            0);
        return true;
    }

    private static bool QueryMatches(
        IReadOnlyList<string> queryTokens,
        IReadOnlyList<string> typeTokens,
        IReadOnlyList<PropertyNameTokens> properties)
    {
        foreach (var queryToken in queryTokens)
        {
            if (SharesOne(queryToken, typeTokens))
            {
                continue;
            }

            var propertyHit = false;
            foreach (var property in properties)
            {
                if (SharesOne(queryToken, property.Tokens))
                {
                    propertyHit = true;
                    break;
                }
            }

            if (!propertyHit)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SharesToken(IReadOnlyList<string> queryTokens, IReadOnlyList<string> apiTokens)
    {
        foreach (var queryToken in queryTokens)
        {
            if (SharesOne(queryToken, apiTokens))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SharesOne(string queryToken, IReadOnlyList<string> apiTokens)
    {
        foreach (var apiToken in apiTokens)
        {
            if (RevitApiTokens.Matches(queryToken, apiToken))
            {
                return true;
            }
        }

        return false;
    }

    private static (IReadOnlyList<RevitApiMetadataProperty> Listed, int RejectedOmitted) ListElementProperties(
        Assembly assembly,
        Type type)
    {
        var projectable = new List<RevitApiMetadataProperty>();
        var rejected = new List<RevitApiMetadataProperty>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var described = RevitApiPropertyPolicy.Describe(property, assembly);
            var model = new RevitApiMetadataProperty
            {
                Name = property.Name,
                ReturnType = RevitApiPropertyPolicy.Display(property.PropertyType),
                CanProject = described.CanProject,
                Rejection = described.Rejection
            };
            if (described.CanProject)
            {
                projectable.Add(model);
            }
            else
            {
                rejected.Add(model);
            }
        }

        projectable.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        rejected.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return LimitProperties(projectable, rejected);
    }

    private static RevitApiMetadataType Summary(
        Type type,
        Assembly assembly,
        bool element,
        IReadOnlyList<string> matchedMembers,
        int matchedOmitted,
        IReadOnlyList<RevitApiMetadataProperty> properties,
        int rejectedOmitted)
    {
        return new RevitApiMetadataType
        {
            TypeName = type.FullName ?? type.Name,
            BaseTypeName = BaseTypeName(assembly, type),
            IsElement = element,
            IsAbstract = type.IsAbstract,
            MatchedMembers = matchedMembers,
            MatchedMembersOmittedCount = matchedOmitted,
            Properties = properties,
            RejectedOmittedCount = rejectedOmitted
        };
    }

    private static string? BaseTypeName(Assembly assembly, Type type)
    {
        var baseType = type.BaseType;
        if (baseType is null || baseType.Assembly != assembly || string.IsNullOrEmpty(baseType.FullName))
        {
            return null;
        }

        return baseType.FullName;
    }

    private static IReadOnlyList<Type> ElementTypes(Assembly assembly)
    {
        var types = new List<Type>();
        foreach (var type in PublicTypes(assembly))
        {
            if (type.Name.Contains('`', StringComparison.Ordinal))
            {
                continue;
            }

            if (!type.IsPublic && !type.IsNestedPublic)
            {
                continue;
            }

            if (type.Assembly != assembly || !RevitApiTypeResolver.IsElement(type))
            {
                continue;
            }

            types.Add(type);
        }

        types.Sort(static (left, right) => string.CompareOrdinal(left.FullName, right.FullName));
        return types;
    }

    private static Type[] PublicTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var loaded = new List<Type>();
            foreach (var type in exception.Types)
            {
                if (type is not null)
                {
                    loaded.Add(type);
                }
            }

            return loaded.ToArray();
        }
    }

    private static IReadOnlyList<PropertyNameTokens> PropertyTokens(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var tokens = new PropertyNameTokens[properties.Length];
        for (var index = 0; index < properties.Length; index++)
        {
            var name = properties[index].Name;
            tokens[index] = new PropertyNameTokens(name, RevitApiTokens.Tokenize(name));
        }

        return tokens;
    }

    private static BridgeException InvalidQuery(string message)
    {
        return new BridgeException(CapabilityErrorCodes.InvalidReadOperationInput, message);
    }

    private readonly record struct PropertyNameTokens(string Name, IReadOnlyList<string> Tokens);
}
