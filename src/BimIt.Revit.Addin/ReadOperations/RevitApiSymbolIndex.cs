using System.Globalization;
using System.Reflection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Public non-generic types in the Revit API assembly already loaded by this process.
/// Descriptions are reflection metadata. Members are not invoked.
/// </summary>
internal static class RevitApiSymbolIndex
{
    public const int MaxSearchMatches = 40;

    public const int MaxMatchedMembers = 8;

    public const int MaxConstructors = 8;

    public const int MaxProperties = 24;

    public const int MaxFields = 24;

    public const int MaxMethodGroups = 12;

    public const int MaxOverloadsPerGroup = 4;

    public const int MaxMemberOverloads = 12;

    public const int MaxFactories = 8;

    public const int MaxObsoleteMessage = 160;

    public const int MaxSignatureLength = 400;

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<Assembly, IndexedType[]> Caches = new();

    private static readonly string[] EmptyNames = [];

    private static readonly RevitApiSymbolSignature[] EmptySignatures = [];

    private static readonly RevitApiSymbolGroup[] EmptyGroups = [];

    public static IReadOnlyList<RevitApiSymbolType> Search(string query)
    {
        return Search(RevitApiTypeResolver.Assembly, query);
    }

    public static IReadOnlyList<RevitApiSymbolType> Search(Assembly assembly, string query)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length is < 2 or > 128)
        {
            throw Invalid("query must be between 2 and 128 characters.");
        }

        var indexed = Types(assembly);
        var byName = new List<IndexedType>();
        foreach (var type in indexed)
        {
            if (NameHit(type.Type, query))
            {
                byName.Add(type);
            }
        }

        if (byName.Count > 0)
        {
            RequirePopulation(byName.Count);
            var summaries = new RevitApiSymbolType[byName.Count];
            for (var index = 0; index < byName.Count; index++)
            {
                summaries[index] = Summarize(byName[index].Type, EmptyNames);
            }

            return summaries;
        }

        var byMember = new List<(Type Type, string[] Members)>();
        foreach (var type in indexed)
        {
            var members = MatchingMembers(type, query);
            if (members.Length > 0)
            {
                byMember.Add((type.Type, members));
            }
        }

        RequirePopulation(byMember.Count);
        var matches = new RevitApiSymbolType[byMember.Count];
        for (var index = 0; index < byMember.Count; index++)
        {
            matches[index] = Summarize(byMember[index].Type, byMember[index].Members);
        }

        return matches;
    }

    public static RevitApiSymbolType DescribeExact(string typeName, string? member)
    {
        return DescribeExact(RevitApiTypeResolver.Assembly, typeName, member);
    }

    public static RevitApiSymbolType DescribeExact(Assembly assembly, string typeName, string? member)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (member is not null && member.Length is < 1 or > 128)
        {
            throw Invalid("member must be between 1 and 128 characters.");
        }

        var type = RevitApiTypeResolver.FindPublicType(assembly, typeName);
        if (type is null || !IncludeType(type))
        {
            throw Invalid("type_name was not found.");
        }

        return member is null ? Describe(type) : DescribeMember(type, member);
    }

    private static void RequirePopulation(int count)
    {
        if (count > MaxSearchMatches)
        {
            throw Invalid(
                "The query matched "
                + count.ToString(CultureInfo.InvariantCulture)
                + " types, which exceeds the maximum of "
                + MaxSearchMatches.ToString(CultureInfo.InvariantCulture)
                + ".");
        }
    }

    private static bool NameHit(Type type, string query)
    {
        return type.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (type.FullName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static string[] MatchingMembers(IndexedType type, string query)
    {
        var names = type.Members;
        var matched = new List<string>();
        foreach (var name in names)
        {
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(name);
            }
        }

        matched.Sort(StringComparer.Ordinal);
        return matched.ToArray();
    }

    private static RevitApiSymbolType Summarize(Type type, IReadOnlyList<string> matchedMembers)
    {
        var (kept, omitted) = Limit(matchedMembers, MaxMatchedMembers);
        return Identity(type, kept, omitted, EmptySignatures, 0, EmptySignatures, 0, EmptySignatures, 0, EmptyGroups, 0, EmptySignatures, 0, null);
    }

    private static RevitApiSymbolType Describe(Type type)
    {
        var constructors = Limit(Signatures(Constructors(type).Select(ctor => (ConstructorText(ctor), (MemberInfo)ctor))), MaxConstructors);
        var properties = Limit(Signatures(Properties(type).Where(HasPublicAccessor).Select(PropertyText)), MaxProperties);
        var fields = Limit(Signatures(Fields(type).Select(FieldText)), MaxFields);
        var methods = Methods(type).Where(IncludeMethod).ToArray();
        var factories = new List<MethodInfo>();
        var ordinary = new List<MethodInfo>();
        foreach (var method in methods)
        {
            if (IsFactory(method, type.Assembly))
            {
                factories.Add(method);
            }
            else
            {
                ordinary.Add(method);
            }
        }

        var factorySignatures = Limit(
            Signatures(factories.Select(method => (MethodText(method), (MemberInfo)method)).OrderBy(item => item.Item1, StringComparer.Ordinal)),
            MaxFactories);
        var groups = MethodGroups(ordinary, MaxOverloadsPerGroup);
        return Identity(
            type,
            EmptyNames,
            0,
            constructors.Kept,
            constructors.Omitted,
            properties.Kept,
            properties.Omitted,
            fields.Kept,
            fields.Omitted,
            groups.Kept,
            groups.Omitted,
            factorySignatures.Kept,
            factorySignatures.Omitted,
            null);
    }

    private static RevitApiSymbolType DescribeMember(Type type, string member)
    {
        var constructors = string.Equals(member, type.Name, StringComparison.Ordinal)
            ? Constructors(type)
            : Array.Empty<ConstructorInfo>();
        var methods = Methods(type).Where(method => IncludeMethod(method) && string.Equals(method.Name, member, StringComparison.Ordinal)).ToArray();
        var properties = Properties(type).Where(property => HasPublicAccessor(property) && string.Equals(property.Name, member, StringComparison.Ordinal)).ToArray();
        var fields = Fields(type).Where(field => string.Equals(field.Name, member, StringComparison.Ordinal)).ToArray();
        var categories = 0;
        if (constructors.Length > 0)
        {
            categories++;
        }

        if (methods.Length > 0)
        {
            categories++;
        }

        if (properties.Length > 0)
        {
            categories++;
        }

        if (fields.Length > 0)
        {
            categories++;
        }

        if (categories == 0)
        {
            throw Invalid("member was not found.");
        }

        if (categories > 1)
        {
            throw Invalid("member is ambiguous.");
        }

        RevitApiSymbolMember selected;
        if (constructors.Length > 0)
        {
            var signatures = Limit(Signatures(constructors.Select(ctor => (ConstructorText(ctor), (MemberInfo)ctor))), MaxMemberOverloads);
            selected = Member(member, "constructor", false, signatures.Kept, signatures.Omitted);
        }
        else if (methods.Length > 0)
        {
            var signatures = Limit(
                Signatures(methods.Select(method => (MethodText(method), (MemberInfo)method)).OrderBy(item => item.Item1, StringComparer.Ordinal)),
                MaxMemberOverloads);
            selected = Member(member, "method", methods.All(method => method.IsStatic), signatures.Kept, signatures.Omitted);
        }
        else if (properties.Length > 0)
        {
            var signatures = Limit(Signatures(properties.Select(PropertyText)), MaxMemberOverloads);
            selected = Member(member, "property", properties.All(IsStaticProperty), signatures.Kept, signatures.Omitted);
        }
        else
        {
            var signatures = Limit(Signatures(fields.Select(FieldText)), MaxMemberOverloads);
            selected = Member(member, "field", fields.All(field => field.IsStatic), signatures.Kept, signatures.Omitted);
        }

        return Identity(type, EmptyNames, 0, EmptySignatures, 0, EmptySignatures, 0, EmptySignatures, 0, EmptyGroups, 0, EmptySignatures, 0, selected);
    }

    private static (RevitApiSymbolGroup[] Kept, int Omitted) MethodGroups(IReadOnlyList<MethodInfo> methods, int overloadCap)
    {
        var groups = methods
            .GroupBy(method => (method.Name, method.IsStatic))
            .OrderBy(group => group.Key.Name, StringComparer.Ordinal)
            .ThenBy(group => group.Key.IsStatic)
            .Select(group =>
            {
                var signatures = Limit(
                    Signatures(group.Select(method => (MethodText(method), (MemberInfo)method)).OrderBy(item => item.Item1, StringComparer.Ordinal)),
                    overloadCap);
                return new RevitApiSymbolGroup
                {
                    Name = group.Key.Name,
                    IsStatic = group.Key.IsStatic,
                    Signatures = signatures.Kept,
                    SignaturesOmittedCount = signatures.Omitted
                };
            })
            .ToArray();
        return Limit(groups, MaxMethodGroups);
    }

    private static RevitApiSymbolType Identity(
        Type type,
        IReadOnlyList<string> matchedMembers,
        int matchedMembersOmitted,
        IReadOnlyList<RevitApiSymbolSignature> constructors,
        int constructorsOmitted,
        IReadOnlyList<RevitApiSymbolSignature> properties,
        int propertiesOmitted,
        IReadOnlyList<RevitApiSymbolSignature> fields,
        int fieldsOmitted,
        IReadOnlyList<RevitApiSymbolGroup> methods,
        int methodsOmitted,
        IReadOnlyList<RevitApiSymbolSignature> factories,
        int factoriesOmitted,
        RevitApiSymbolMember? member)
    {
        var obsolete = ReadObsolete(type);
        var assemblyName = type.Assembly.GetName();
        return new RevitApiSymbolType
        {
            FullName = type.FullName ?? type.Name,
            SimpleName = type.Name,
            Namespace = type.Namespace ?? string.Empty,
            Kind = Kind(type),
            BaseTypeName = BaseTypeName(type),
            AssemblyName = assemblyName.Name ?? string.Empty,
            AssemblyVersion = assemblyName.Version?.ToString() ?? string.Empty,
            Obsolete = obsolete.Obsolete,
            ObsoleteMessage = obsolete.Message,
            ObsoleteMessageTruncated = obsolete.Truncated,
            MatchedMembers = matchedMembers,
            MatchedMembersOmittedCount = matchedMembersOmitted,
            Constructors = constructors,
            ConstructorsOmittedCount = constructorsOmitted,
            Properties = properties,
            PropertiesOmittedCount = propertiesOmitted,
            Fields = fields,
            FieldsOmittedCount = fieldsOmitted,
            Methods = methods,
            MethodsOmittedCount = methodsOmitted,
            Factories = factories,
            FactoriesOmittedCount = factoriesOmitted,
            Member = member
        };
    }

    private static RevitApiSymbolMember Member(
        string name,
        string kind,
        bool isStatic,
        IReadOnlyList<RevitApiSymbolSignature> signatures,
        int omitted)
    {
        return new RevitApiSymbolMember
        {
            Name = name,
            Kind = kind,
            IsStatic = isStatic,
            Signatures = signatures,
            SignaturesOmittedCount = omitted
        };
    }

    private static string? BaseTypeName(Type type)
    {
        var baseType = type.BaseType;
        if (baseType?.FullName is not string name)
        {
            return null;
        }

        if (name is "System.Object" or "System.ValueType" or "System.Enum" or "System.MulticastDelegate" or "System.Delegate")
        {
            return null;
        }

        return name;
    }

    private static string Kind(Type type)
    {
        if (type.IsEnum)
        {
            return "enum";
        }

        if (type.BaseType?.FullName == "System.MulticastDelegate")
        {
            return "delegate";
        }

        if (type.IsInterface)
        {
            return "interface";
        }

        if (type.IsValueType)
        {
            return "struct";
        }

        if (type.IsAbstract && type.IsSealed)
        {
            return "static class";
        }

        if (type.IsAbstract)
        {
            return "abstract class";
        }

        return "class";
    }

    private static bool IsFactory(MethodInfo method, Assembly assembly)
    {
        if (!method.IsStatic)
        {
            return false;
        }

        if (!method.Name.StartsWith("Create", StringComparison.Ordinal)
            && !method.Name.StartsWith("New", StringComparison.Ordinal))
        {
            return false;
        }

        var returnType = method.ReturnType;
        return returnType.FullName != "System.Void" && returnType.Assembly == assembly;
    }

    private static bool IsStaticProperty(PropertyInfo property)
    {
        return (property.GetMethod ?? property.SetMethod)?.IsStatic == true;
    }

    private static bool HasPublicAccessor(PropertyInfo property)
    {
        return property.GetMethod is { IsPublic: true } || property.SetMethod is { IsPublic: true };
    }

    private static bool IncludeMethod(MethodInfo method)
    {
        return !method.IsSpecialName && IncludeName(method.Name);
    }

    private static bool IncludeName(string? name)
    {
        return !string.IsNullOrEmpty(name) && !name.Contains('<', StringComparison.Ordinal);
    }

    private static bool IncludeType(Type type)
    {
        var name = type.FullName;
        return name is not null
            && !name.Contains('`', StringComparison.Ordinal)
            && !name.Contains('<', StringComparison.Ordinal)
            && (type.IsPublic || type.IsNestedPublic);
    }

    private static IndexedType[] Types(Assembly assembly)
    {
        lock (Caches)
        {
            if (Caches.TryGetValue(assembly, out var cached))
            {
                return cached;
            }

            var built = Build(assembly);
            Caches.Add(assembly, built);
            return built;
        }
    }

    private static IndexedType[] Build(Assembly assembly)
    {
        Type[] loaded;
        try
        {
            loaded = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            loaded = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }

        return loaded
            .Where(type => type.Assembly == assembly && IncludeType(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new IndexedType(type))
            .ToArray();
    }

    private static string[] MemberNames(Type type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in Methods(type))
        {
            if (IncludeMethod(method))
            {
                names.Add(method.Name);
            }
        }

        foreach (var property in Properties(type))
        {
            if (HasPublicAccessor(property) && IncludeName(property.Name))
            {
                names.Add(property.Name);
            }
        }

        foreach (var field in Fields(type))
        {
            if (IncludeName(field.Name))
            {
                names.Add(field.Name);
            }
        }

        return names.ToArray();
    }

    private static ConstructorInfo[] Constructors(Type type)
    {
        try
        {
            return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        }
        catch (Exception exception) when (IsLoadFailure(exception))
        {
            throw Unreadable();
        }
    }

    private static MethodInfo[] Methods(Type type)
    {
        try
        {
            return type.GetMethods(Declared);
        }
        catch (Exception exception) when (IsLoadFailure(exception))
        {
            throw Unreadable();
        }
    }

    private static PropertyInfo[] Properties(Type type)
    {
        try
        {
            return type.GetProperties(Declared);
        }
        catch (Exception exception) when (IsLoadFailure(exception))
        {
            throw Unreadable();
        }
    }

    private static FieldInfo[] Fields(Type type)
    {
        try
        {
            return type.GetFields(Declared).Where(field => IncludeName(field.Name)).ToArray();
        }
        catch (Exception exception) when (IsLoadFailure(exception))
        {
            throw Unreadable();
        }
    }

    private static IEnumerable<(string Text, MemberInfo Member)> Signatures(IEnumerable<(string Text, MemberInfo Member)> items)
    {
        return items.OrderBy(item => item.Text, StringComparer.Ordinal);
    }

    private static RevitApiSymbolSignature[] Signatures(IEnumerable<(string Text, MemberInfo Member)> items, bool ordered)
    {
        var source = ordered ? items : items.OrderBy(item => item.Text, StringComparer.Ordinal);
        return source.Select(item => ToSignature(item.Text, item.Member)).ToArray();
    }

    private static (RevitApiSymbolSignature[] Kept, int Omitted) Limit(IEnumerable<(string Text, MemberInfo Member)> items, int maximum)
    {
        return Limit(Signatures(items, ordered: true), maximum);
    }

    private static (T[] Kept, int Omitted) Limit<T>(IReadOnlyList<T> items, int maximum)
    {
        if (items.Count <= maximum)
        {
            var copy = new T[items.Count];
            for (var index = 0; index < items.Count; index++)
            {
                copy[index] = items[index];
            }

            return (copy, 0);
        }

        var kept = new T[maximum];
        for (var index = 0; index < maximum; index++)
        {
            kept[index] = items[index];
        }

        return (kept, items.Count - maximum);
    }

    private static RevitApiSymbolSignature ToSignature(string text, MemberInfo member)
    {
        var truncated = false;
        if (text.Length > MaxSignatureLength)
        {
            text = text[..MaxSignatureLength];
            truncated = true;
        }

        var obsolete = ReadObsolete(member);
        return new RevitApiSymbolSignature
        {
            Text = text,
            Obsolete = obsolete.Obsolete,
            ObsoleteMessage = obsolete.Message,
            ObsoleteMessageTruncated = obsolete.Truncated,
            TextTruncated = truncated
        };
    }

    private static (bool Obsolete, string? Message, bool Truncated) ReadObsolete(MemberInfo member)
    {
        try
        {
            foreach (var attribute in member.GetCustomAttributesData())
            {
                if (!string.Equals(attribute.AttributeType.Name, "ObsoleteAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                string? message = null;
                if (attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is string text && text.Length > 0)
                {
                    message = text;
                }

                var truncated = false;
                if (message is not null && message.Length > MaxObsoleteMessage)
                {
                    message = message[..MaxObsoleteMessage];
                    truncated = true;
                }

                return (true, message, truncated);
            }
        }
        catch (Exception exception) when (IsLoadFailure(exception))
        {
            return (false, null, false);
        }

        return (false, null, false);
    }

    private static string ConstructorText(ConstructorInfo constructor)
    {
        return TypeText(constructor.DeclaringType!) + "(" + Parameters(constructor) + ")";
    }

    private static string MethodText(MethodInfo method)
    {
        var generic = string.Empty;
        if (method.IsGenericMethodDefinition)
        {
            generic = "<" + string.Join(", ", method.GetGenericArguments().Select(TypeText)) + ">";
        }

        var head = method.IsStatic ? "static " : string.Empty;
        var byRef = method.ReturnType.IsByRef ? "ref " : string.Empty;
        return head + byRef + TypeText(method.ReturnType) + " " + method.Name + generic + "(" + Parameters(method) + ")";
    }

    private static (string Text, MemberInfo Member) PropertyText(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod is { IsPublic: true })
        {
            accessors.Add("get");
        }

        if (property.SetMethod is { IsPublic: true })
        {
            accessors.Add("set");
        }

        var head = IsStaticProperty(property) ? "static " : string.Empty;
        var indexes = property.GetIndexParameters();
        var name = indexes.Length == 0
            ? property.Name
            : "this[" + string.Join(", ", indexes.Select(ParameterText)) + "]";
        var text = head + TypeText(property.PropertyType) + " " + name + " { " + string.Join("; ", accessors) + "; }";
        return (text, property);
    }

    private static (string Text, MemberInfo Member) FieldText(FieldInfo field)
    {
        var prefix = field.IsLiteral ? "const " : field.IsStatic ? "static " : string.Empty;
        var text = prefix + TypeText(field.FieldType) + " " + field.Name;
        if (field.IsLiteral)
        {
            try
            {
                var raw = field.GetRawConstantValue();
                if (raw is not null && FormatConstant(raw) is { } formatted)
                {
                    text += " = " + formatted;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or FormatException)
            {
            }
        }

        return (text, field);
    }

    private static string? FormatConstant(object raw)
    {
        return raw switch
        {
            bool value => value ? "true" : "false",
            string value => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
            char value => "'" + value + "'",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string Parameters(MethodBase method)
    {
        return string.Join(", ", method.GetParameters().Select(ParameterText));
    }

    private static string ParameterText(ParameterInfo parameter)
    {
        var prefix = parameter.IsOut
            ? "out "
            : parameter.IsIn
                ? "in "
                : parameter.ParameterType.IsByRef
                    ? "ref "
                    : string.Empty;
        var name = string.IsNullOrEmpty(parameter.Name) ? "_" : parameter.Name;
        return prefix + TypeText(parameter.ParameterType) + " " + name;
    }

    private static string TypeText(Type type)
    {
        if (type.IsByRef || type.IsPointer)
        {
            return TypeText(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return TypeText(type.GetElementType()!) + "[" + new string(',', Math.Max(0, type.GetArrayRank() - 1)) + "]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsGenericType)
        {
            var name = type.Name;
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                name = name[..tick];
            }

            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeText)) + ">";
        }

        return type.FullName switch
        {
            "System.Void" => "void",
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Decimal" => "decimal",
            "System.Char" => "char",
            "System.String" => "string",
            "System.Object" => "object",
            _ => type.Name
        };
    }

    private static bool IsLoadFailure(Exception exception)
    {
        return exception is TypeLoadException or System.IO.FileNotFoundException or System.IO.FileLoadException or ReflectionTypeLoadException;
    }

    private static BridgeException Invalid(string message)
    {
        return new BridgeException(CapabilityErrorCodes.InvalidReadOperationInput, message);
    }

    private static BridgeException Unreadable()
    {
        return new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The Revit API symbol could not be read.");
    }

    private sealed class IndexedType
    {
        private readonly Type _type;
        private string[]? _members;

        public IndexedType(Type type)
        {
            _type = type;
        }

        public Type Type => _type;

        public string[] Members => _members ??= MemberNames(_type);
    }
}
