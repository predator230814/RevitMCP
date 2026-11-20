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

        var types = ElementTypes(assembly);
        var typeNameMatches = new List<RevitApiMetadataType>();
        foreach (var type in types)
        {
            if (MatchesTypeName(type, query, queryTokens))
            {
                typeNameMatches.Add(Summary(type, assembly, element: true, [], 0, [], 0));
            }
        }

        if (typeNameMatches.Count > 0)
        {
            return Bound(typeNameMatches);
        }

        if (queryTokens.Count == 0)
        {
            throw InvalidQuery("query has no searchable tokens.");
        }

        var propertyMatches = new List<RevitApiMetadataType>();
        foreach (var type in types)
        {
            if (TryMatchProperties(type, queryTokens, out var summary))
            {
                propertyMatches.Add(summary);
            }
        }

        return Bound(propertyMatches);
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

    private static IReadOnlyList<RevitApiMetadataType> Bound(List<RevitApiMetadataType> matches)
    {
        if (matches.Count > MaxSearchMatches)
        {
            throw InvalidQuery("query matches more than 200 types.");
        }

        return matches;
    }

    private static bool MatchesTypeName(Type type, string query, IReadOnlyList<string> queryTokens)
    {
        var simpleName = type.Name;
        if (query.Length >= 2
            && string.Equals(AsciiLettersAndDigits(query), simpleName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var typeTokens = RevitApiTokens.Tokenize(simpleName);
        if (queryTokens.Count == 0 || queryTokens.Count != typeTokens.Count)
        {
            return false;
        }

        foreach (var typeToken in typeTokens)
        {
            if (!QueryTokenMatches(queryTokens, typeToken))
            {
                return false;
            }
        }

        foreach (var queryToken in queryTokens)
        {
            if (!SharesOne(queryToken, typeTokens))
            {
                return false;
            }
        }

        return true;
    }

    private static bool QueryTokenMatches(IReadOnlyList<string> queryTokens, string typeToken)
    {
        foreach (var queryToken in queryTokens)
        {
            if (RevitApiTokens.Matches(queryToken, typeToken))
            {
                return true;
            }
        }

        return false;
    }

    private static string AsciiLettersAndDigits(string value)
    {
        var kept = new char[value.Length];
        var count = 0;
        foreach (var character in value)
        {
            if (character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                kept[count++] = character;
            }
        }

        return new string(kept, 0, count);
    }

    private static bool TryMatchProperties(
        Type type,
        IReadOnlyList<string> queryTokens,
        out RevitApiMetadataType summary)
    {
        var properties = PropertyTokens(type);
        if (!MatchesEveryToken(queryTokens, properties))
        {
            summary = null!;
            return false;
        }

        var matchedProperties = new List<string>();
        foreach (var property in properties)
        {
            if (SharesToken(queryTokens, property.Tokens))
            {
                matchedProperties.Add(property.Name);
            }
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

    private static bool MatchesEveryToken(IReadOnlyList<string> queryTokens, IReadOnlyList<PropertyNameTokens> properties)
    {
        if (queryTokens.Count == 0)
        {
            return false;
        }

        foreach (var queryToken in queryTokens)
        {
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
