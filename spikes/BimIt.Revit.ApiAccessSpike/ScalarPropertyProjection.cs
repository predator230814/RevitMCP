using System.Globalization;
using System.Reflection;
using Autodesk.Revit.DB;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal readonly record struct PropertyProjection(PropertyInfo Property, bool CanProject, string? Rejection);

internal readonly record struct ResolvedElementProperties(Type Type, IReadOnlyList<PropertyInfo> Properties);

/// <summary>
/// Projects JSON scalars from public instance getters. Setters and methods are never invoked.
/// </summary>
internal static class ScalarPropertyProjection
{
    public const string NotReadable = "not_readable";

    public const string Indexer = "indexer";

    public const string StaticProperty = "static";

    public const string UnsupportedReturn = "unsupported_return_type";

    public const string OutsideAssembly = "outside_trusted_assembly";

    public static PropertyProjection Describe(PropertyInfo property, Assembly trustedAssembly)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(trustedAssembly);
        if (property.GetIndexParameters().Length > 0)
        {
            return new PropertyProjection(property, false, Indexer);
        }

        var getter = property.GetMethod;
        if (getter is null || !getter.IsPublic || getter.GetParameters().Length != 0)
        {
            return new PropertyProjection(property, false, NotReadable);
        }

        if (getter.IsStatic)
        {
            return new PropertyProjection(property, false, StaticProperty);
        }

        if (property.DeclaringType?.Assembly != trustedAssembly)
        {
            return new PropertyProjection(property, false, OutsideAssembly);
        }

        if (!IsSupportedScalar(property.PropertyType))
        {
            return new PropertyProjection(property, false, UnsupportedReturn);
        }

        return new PropertyProjection(property, true, null);
    }

    public static ResolvedElementProperties RequireProjectable(Type elementType, IReadOnlyList<string> propertyNames)
    {
        return RequireProjectable(TrustedRevitApiTypes.Assembly, elementType, propertyNames);
    }

    public static ResolvedElementProperties RequireProjectable(
        Assembly trustedAssembly,
        Type elementType,
        IReadOnlyList<string> propertyNames)
    {
        ArgumentNullException.ThrowIfNull(trustedAssembly);
        ArgumentNullException.ThrowIfNull(elementType);
        ArgumentNullException.ThrowIfNull(propertyNames);
        if (elementType.Assembly != trustedAssembly || !TrustedRevitApiTypes.IsConcreteElement(elementType, trustedAssembly))
        {
            throw Invalid("type_name is not a concrete Element subclass in the loaded Revit API assembly.");
        }

        var resolved = new PropertyInfo[propertyNames.Count];
        for (var index = 0; index < propertyNames.Count; index++)
        {
            var name = propertyNames[index];
            PropertyInfo? property;
            try
            {
                property = elementType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            }
            catch (AmbiguousMatchException)
            {
                throw Invalid("Property '" + name + "' is ambiguous.");
            }

            if (property is null)
            {
                throw Invalid("Property '" + name + "' is not a public instance property of this type.");
            }

            var described = Describe(property, trustedAssembly);
            if (!described.CanProject)
            {
                throw Invalid("Property '" + name + "' cannot be projected (" + described.Rejection + ").");
            }

            resolved[index] = property;
        }

        return new ResolvedElementProperties(elementType, resolved);
    }

    public static ApiScalarValue Read(PropertyInfo property, Element target)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(target);
        var getter = property.GetMethod
            ?? throw Invalid("Property '" + property.Name + "' is not readable.");
        object? value;
        try
        {
            value = getter.Invoke(target, null);
        }
        catch (TargetInvocationException)
        {
            throw Invalid("Property '" + property.Name + "' could not be read.");
        }

        return Encode(property.Name, property.PropertyType, value);
    }

    public static ApiScalarValue Encode(string name, Type declaredType, object? value)
    {
        if (value is null)
        {
            return new ApiScalarValue { Name = name, Kind = "null" };
        }

        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type == typeof(string))
        {
            if (value is not string text)
            {
                throw Mismatch(name);
            }

            return new ApiScalarValue { Name = name, Kind = "string", Text = text };
        }

        if (type == typeof(bool))
        {
            if (value is not bool boolean)
            {
                throw Mismatch(name);
            }

            return new ApiScalarValue { Name = name, Kind = "boolean", Boolean = boolean };
        }

        if (type.IsEnum)
        {
            if (value.GetType() != type)
            {
                throw Mismatch(name);
            }

            var enumName = Enum.GetName(type, value);
            if (string.IsNullOrEmpty(enumName))
            {
                throw Invalid("Property '" + name + "' returned an unnamed enum value.");
            }

            return new ApiScalarValue { Name = name, Kind = "enum", Text = enumName };
        }

        if (IsIntegral(type))
        {
            long integer;
            try
            {
                integer = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
                throw Mismatch(name);
            }

            return new ApiScalarValue { Name = name, Kind = "integer", Integer = integer };
        }

        if (type == typeof(float) || type == typeof(double))
        {
            double number;
            try
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
                throw Mismatch(name);
            }

            if (!double.IsFinite(number))
            {
                throw Invalid("Property '" + name + "' returned a non-finite number.");
            }

            return new ApiScalarValue { Name = name, Kind = "number", Number = number };
        }

        throw Invalid("Property '" + name + "' cannot be projected (" + UnsupportedReturn + ").");
    }

    public static bool IsSupportedScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return FullNameIs(underlying, "System.String")
            || FullNameIs(underlying, "System.Boolean")
            || underlying.IsEnum
            || IsIntegral(underlying)
            || FullNameIs(underlying, "System.Single")
            || FullNameIs(underlying, "System.Double");
    }

    private static bool FullNameIs(Type type, string fullName)
    {
        return type.FullName == fullName;
    }

    public static string Display(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return (underlying.FullName ?? underlying.Name) + "?";
        }

        return type.FullName ?? type.Name;
    }

    private static bool IsIntegral(Type type)
    {
        return FullNameIs(type, "System.Byte")
            || FullNameIs(type, "System.SByte")
            || FullNameIs(type, "System.Int16")
            || FullNameIs(type, "System.UInt16")
            || FullNameIs(type, "System.Int32")
            || FullNameIs(type, "System.UInt32")
            || FullNameIs(type, "System.Int64");
    }

    private static BridgeException Mismatch(string name)
    {
        return Invalid("Property '" + name + "' returned a value that does not match its declared type.");
    }

    private static BridgeException Invalid(string message)
    {
        return new BridgeException(CapabilityErrorCodes.InvalidReadOperationInput, message);
    }
}
