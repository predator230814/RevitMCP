using System.Reflection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Bounded public metadata of the loaded Revit API assembly. Members are described, never executed.
/// </summary>
internal static class RevitApiMetadataCatalog
{
    public const int MaxSearchMatches = 1000;

    public const int MaxListedProperties = 80;

    public const int MaxListedMethods = 40;

    public const int MaxMatchedMembers = 8;

    private static Assembly? _cachedAssembly;

    private static Type[]? _cachedTypes;

    public static IReadOnlyList<ApiMetadataType> Search(string query)
    {
        return Search(TrustedRevitApiTypes.Assembly, query);
    }

    public static IReadOnlyList<ApiMetadataType> Search(Assembly assembly, string query)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var matched = new List<ApiMetadataType>();
        foreach (var type in PublicTypes(assembly))
        {
            var nameHit = Contains(type.FullName, query) || Contains(type.Name, query);
            IReadOnlyList<string> members = nameHit ? [] : MatchedMembers(type, query);
            if (!nameHit && members.Count == 0)
            {
                continue;
            }

            matched.Add(Summarize(type, members));
            if (matched.Count > MaxSearchMatches)
            {
                throw new BridgeException(
                    CapabilityErrorCodes.ResultTooLarge,
                    "The metadata search matched more types than the spike bound. Narrow the query.");
            }
        }

        return matched;
    }

    public static ApiMetadataType Describe(Type type)
    {
        return Describe(type.Assembly, type);
    }

    public static ApiMetadataType Describe(Assembly assembly, Type type)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(type);
        var properties = ListProperties(assembly, type);
        var methods = ListMethods(type);
        return new ApiMetadataType
        {
            TypeName = type.FullName ?? type.Name,
            BaseTypeName = type.BaseType?.FullName,
            IsElement = TrustedRevitApiTypes.IsElement(type),
            IsAbstract = type.IsAbstract,
            CanCollect = TrustedRevitApiTypes.IsConcreteElement(type, assembly),
            MatchedMembers = [],
            Properties = properties.Listed,
            PropertiesTruncated = properties.Truncated,
            Methods = methods.Listed,
            MethodsTruncated = methods.Truncated
        };
    }

    private static ApiMetadataType Summarize(Type type, IReadOnlyList<string> matchedMembers)
    {
        return new ApiMetadataType
        {
            TypeName = type.FullName ?? type.Name,
            BaseTypeName = type.BaseType?.FullName,
            IsElement = TrustedRevitApiTypes.IsElement(type),
            IsAbstract = type.IsAbstract,
            CanCollect = TrustedRevitApiTypes.IsConcreteElement(type, type.Assembly),
            MatchedMembers = matchedMembers,
            Properties = [],
            PropertiesTruncated = false,
            Methods = [],
            MethodsTruncated = false
        };
    }

    private static (IReadOnlyList<ApiMetadataProperty> Listed, bool Truncated) ListProperties(Assembly assembly, Type type)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var described = new PropertyProjection[properties.Length];
        for (var index = 0; index < properties.Length; index++)
        {
            described[index] = ScalarPropertyProjection.Describe(properties[index], assembly);
        }

        Array.Sort(described, static (left, right) =>
        {
            var projectable = right.CanProject.CompareTo(left.CanProject);
            if (projectable != 0)
            {
                return projectable;
            }

            var name = string.CompareOrdinal(left.Property.Name, right.Property.Name);
            return name != 0
                ? name
                : string.CompareOrdinal(
                    ScalarPropertyProjection.Display(left.Property.PropertyType),
                    ScalarPropertyProjection.Display(right.Property.PropertyType));
        });
        var truncated = described.Length > MaxListedProperties;
        var count = Math.Min(described.Length, MaxListedProperties);
        var listed = new ApiMetadataProperty[count];
        for (var index = 0; index < count; index++)
        {
            var property = described[index].Property;
            listed[index] = new ApiMetadataProperty
            {
                Name = property.Name,
                ReturnType = ScalarPropertyProjection.Display(property.PropertyType),
                CanProject = described[index].CanProject,
                Rejection = described[index].Rejection
            };
        }

        return (listed, truncated);
    }

    private static (IReadOnlyList<ApiMetadataMethod> Listed, bool Truncated) ListMethods(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
        var visible = new List<MethodInfo>();
        foreach (var method in methods)
        {
            if (method.IsSpecialName || method.DeclaringType == typeof(object))
            {
                continue;
            }

            visible.Add(method);
        }

        visible.Sort(static (left, right) =>
        {
            var name = string.CompareOrdinal(left.Name, right.Name);
            if (name != 0)
            {
                return name;
            }

            var count = left.GetParameters().Length.CompareTo(right.GetParameters().Length);
            return count != 0
                ? count
                : string.CompareOrdinal(ScalarPropertyProjection.Display(left.ReturnType), ScalarPropertyProjection.Display(right.ReturnType));
        });
        var truncated = visible.Count > MaxListedMethods;
        var count = Math.Min(visible.Count, MaxListedMethods);
        var listed = new ApiMetadataMethod[count];
        for (var index = 0; index < count; index++)
        {
            var method = visible[index];
            listed[index] = new ApiMetadataMethod
            {
                Name = method.Name,
                ReturnType = ScalarPropertyProjection.Display(method.ReturnType),
                ParameterCount = method.GetParameters().Length,
                Executable = false
            };
        }

        return (listed, truncated);
    }

    private static IReadOnlyList<string> MatchedMembers(Type type, string query)
    {
        var names = new List<string>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length == 0 && Contains(property.Name, query))
            {
                names.Add(property.Name);
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!method.IsSpecialName && method.DeclaringType != typeof(object) && Contains(method.Name, query))
            {
                names.Add(method.Name);
            }
        }

        if (names.Count == 0)
        {
            return [];
        }

        names.Sort(StringComparer.Ordinal);
        var distinct = new List<string>(Math.Min(names.Count, MaxMatchedMembers));
        foreach (var name in names)
        {
            if (distinct.Count > 0 && string.Equals(distinct[^1], name, StringComparison.Ordinal))
            {
                continue;
            }

            distinct.Add(name);
            if (distinct.Count == MaxMatchedMembers)
            {
                break;
            }
        }

        return distinct;
    }

    private static Type[] PublicTypes(Assembly assembly)
    {
        if (_cachedAssembly == assembly && _cachedTypes is not null)
        {
            return _cachedTypes;
        }

        _cachedTypes = LoadPublicTypes(assembly);
        _cachedAssembly = assembly;
        return _cachedTypes;
    }

    private static Type[] LoadPublicTypes(Assembly assembly)
    {
        Type[] loaded;
        try
        {
            loaded = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            loaded = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }

        var types = new List<Type>();
        foreach (var type in loaded)
        {
            if (type is null || (!type.IsPublic && !type.IsNestedPublic))
            {
                continue;
            }

            var name = type.FullName;
            if (string.IsNullOrEmpty(name) || name.Contains('`', StringComparison.Ordinal) || name.Contains('<', StringComparison.Ordinal))
            {
                continue;
            }

            types.Add(type);
        }

        types.Sort(static (left, right) => string.CompareOrdinal(left.FullName, right.FullName));
        return types.ToArray();
    }

    private static bool Contains(string? text, string query)
    {
        return text is not null && text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
