using System.Reflection;

namespace BimIt.Revit.QueryRuntimeSpike;

public sealed record ApiMethodDescription(string Name, bool IsStatic, bool Obsolete, IReadOnlyList<string> Overloads);

public sealed record ApiPropertyDescription(string Name, string Type, bool IsStatic, bool Obsolete);

public sealed record ApiTypeDescription(
    string FullName,
    string Namespace,
    string? BaseType,
    string Kind,
    bool Obsolete,
    bool MembersTruncated,
    IReadOnlyList<string> Constructors,
    IReadOnlyList<ApiMethodDescription> Methods,
    IReadOnlyList<ApiPropertyDescription> Properties,
    IReadOnlyList<string> EnumValues,
    int EnumValueCount,
    bool EnumValuesTruncated,
    IReadOnlyList<string> Factories);

/// <summary>
/// Descriptive index over a loaded RevitAPI assembly. It does not expose callable methods.
/// </summary>
public sealed class RevitApiSymbolIndex
{
    public const int MaxMethodGroups = 16;

    public const int MaxOverloads = 8;

    public const int MaxProperties = 24;

    public const int MaxConstructors = 8;

    public const int MaxEnumValues = 32;

    private static readonly string[] PreferredEnumValues =
    [
        "OST_Walls",
        "OST_Doors",
        "OST_Windows",
        "OST_Floors",
        "OST_Rooms",
        "OST_Stairs"
    ];

    public ApiTypeDescription? Describe(Assembly assembly, string typeName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (string.IsNullOrWhiteSpace(typeName) || typeName.Length > 200)
        {
            return null;
        }

        var match = Types(assembly)
            .Where(type => type.IsPublic && !type.IsNested && (type.FullName == typeName || type.Name == typeName))
            .OrderBy(type => type.FullName == typeName ? 0 : 1)
            .ThenBy(type => type.Namespace == "Autodesk.Revit.DB" ? 0 : 1)
            .ThenBy(type => type.FullName, StringComparer.Ordinal)
            .FirstOrDefault();
        return match is null ? null : DescribeType(match);
    }

    private static ApiTypeDescription DescribeType(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(Signature)
            .Order(StringComparer.Ordinal)
            .ToList();
        var constructorTruncated = constructors.Count > MaxConstructors;
        if (constructorTruncated)
        {
            constructors = constructors.Take(MaxConstructors).ToList();
        }

        var groups = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .GroupBy(method => method.Name, StringComparer.Ordinal)
            .Select(group =>
            {
                var methods = group.OrderBy(method => Signature(method), StringComparer.Ordinal).ToList();
                var overloads = methods.Select(Signature).Take(MaxOverloads).ToList();
                return new MethodGroup(
                    group.Key,
                    methods.Any(method => method.IsStatic),
                    methods.Any(IsObsolete),
                    overloads,
                    methods.Count > MaxOverloads,
                    IsFactory(methods[0]));
            })
            .ToList();

        var ordered = groups
            .OrderBy(group => group.Overloads.Count > 1 ? 0 : 1)
            .ThenBy(group => group.Factory ? 0 : 1)
            .ThenBy(group => group.Name, StringComparer.Ordinal)
            .ToList();
        var methodsTruncated = ordered.Count > MaxMethodGroups || ordered.Any(group => group.OverloadTruncated);
        var methods = ordered
            .Take(MaxMethodGroups)
            .Select(group => new ApiMethodDescription(group.Name, group.IsStatic, group.Obsolete, group.Overloads))
            .ToList();
        var factories = groups
            .Where(group => group.Factory)
            .SelectMany(group => group.Overloads)
            .Order(StringComparer.Ordinal)
            .Take(MaxMethodGroups)
            .ToList();

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => new ApiPropertyDescription(property.Name, TypeName(property.PropertyType), property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false, IsObsolete(property)))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();
        var propertiesTruncated = properties.Count > MaxProperties;
        if (propertiesTruncated)
        {
            properties = properties.Take(MaxProperties).ToList();
        }

        var enumValues = Array.Empty<string>();
        var enumCount = 0;
        var enumTruncated = false;
        if (type.IsEnum)
        {
            var names = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral)
                .Select(field => field.Name)
                .Order(StringComparer.Ordinal)
                .ToList();
            enumCount = names.Count;
            enumTruncated = enumCount > MaxEnumValues;
            var preferred = PreferredEnumValues.Where(names.Contains).ToList();
            enumValues = preferred
                .Concat(names.Where(name => !preferred.Contains(name, StringComparer.Ordinal)))
                .Take(MaxEnumValues)
                .ToArray();
        }

        return new ApiTypeDescription(
            type.FullName ?? type.Name,
            type.Namespace ?? "",
            type.BaseType?.FullName,
            type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : "class",
            IsObsolete(type),
            constructorTruncated || methodsTruncated || propertiesTruncated,
            constructors,
            methods,
            properties,
            enumValues,
            enumCount,
            enumTruncated,
            factories);
    }

    private static bool IsFactory(MethodInfo method)
    {
        var space = method.ReturnType.Namespace ?? "";
        return method.IsStatic
            && space.StartsWith("Autodesk.Revit.DB", StringComparison.Ordinal)
            && (method.Name.StartsWith("Create", StringComparison.Ordinal) || method.Name.StartsWith("New", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private static bool IsObsolete(MemberInfo member)
    {
        try
        {
            return member.GetCustomAttributesData().Any(attribute =>
                attribute.AttributeType.FullName == "System.ObsoleteAttribute"
                || attribute.AttributeType.Name == "ObsoleteAttribute");
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException or FileLoadException)
        {
            return false;
        }
    }

    private static string Signature(MethodBase method)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter =>
        {
            var prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "";
            return prefix + TypeName(parameter.ParameterType) + " " + parameter.Name;
        }));
        if (method.IsConstructor)
        {
            return TypeName(method.DeclaringType!) + "(" + parameters + ")";
        }

        var info = (MethodInfo)method;
        var stat = info.IsStatic ? "static " : "";
        return stat + TypeName(info.ReturnType) + " " + info.Name + "(" + parameters + ")";
    }

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
        {
            return TypeName(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericType)
        {
            var name = type.Name;
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                name = name[..tick];
            }

            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
        }

        return type.Name;
    }

    private sealed record MethodGroup(
        string Name,
        bool IsStatic,
        bool Obsolete,
        IReadOnlyList<string> Overloads,
        bool OverloadTruncated,
        bool Factory);
}
