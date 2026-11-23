using System.Globalization;
using System.Reflection;
using Autodesk.Revit.DB;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal readonly record struct RevitApiPropertyProjection(PropertyInfo Property, bool CanProject, string? Rejection);

/// <summary>
/// Public zero-argument instance getters that return an accepted scalar.
/// </summary>
internal static class RevitApiPropertyPolicy
{
    public const string NotReadable = "not_readable";

    public const string Indexer = "indexer";

    public const string StaticProperty = "static";

    public const string UnsupportedReturn = "unsupported_return_type";

    public const string OutsideAssembly = "outside_trusted_assembly";

    public const int MaxStringLength = 4096;

    public const long MinSafeInteger = -9007199254740991L;

    public const long MaxSafeInteger = 9007199254740991L;

    public static RevitApiPropertyProjection Describe(PropertyInfo property, Assembly trustedAssembly)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(trustedAssembly);
        if (property.GetIndexParameters().Length > 0)
        {
            return new RevitApiPropertyProjection(property, false, Indexer);
        }

        var getter = property.GetMethod;
        if (getter is null || !getter.IsPublic || getter.GetParameters().Length != 0)
        {
            return new RevitApiPropertyProjection(property, false, NotReadable);
        }

        if (getter.IsStatic)
        {
            return new RevitApiPropertyProjection(property, false, StaticProperty);
        }

        if (property.DeclaringType?.Assembly != trustedAssembly)
        {
            return new RevitApiPropertyProjection(property, false, OutsideAssembly);
        }

        if (!IsSupportedScalar(property.PropertyType))
        {
            return new RevitApiPropertyProjection(property, false, UnsupportedReturn);
        }

        return new RevitApiPropertyProjection(property, true, null);
    }

    public static IReadOnlyList<PropertyInfo> RequireProjectable(
        Assembly trustedAssembly,
        Type elementType,
        IReadOnlyList<string> propertyNames)
    {
        ArgumentNullException.ThrowIfNull(trustedAssembly);
        ArgumentNullException.ThrowIfNull(elementType);
        ArgumentNullException.ThrowIfNull(propertyNames);
        if (elementType.Assembly != trustedAssembly)
        {
            throw Invalid("type_name is not in the loaded Revit API assembly.");
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

        return resolved;
    }

    public static RevitApiScalar Read(PropertyInfo property, Element target)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(target);
        var getter = property.GetMethod
            ?? throw Failed(property.Name, "getter_failed");
        object? value;
        try
        {
            value = getter.Invoke(target, null);
        }
        catch (Exception exception) when (exception is TargetInvocationException or TargetException)
        {
            throw Failed(property.Name, "getter_failed");
        }

        return Encode(property.Name, property.PropertyType, value);
    }

    public static RevitApiScalar Encode(string name, Type declaredType, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(declaredType);
        if (value is null)
        {
            if (declaredType == typeof(string) || Nullable.GetUnderlyingType(declaredType) is not null)
            {
                return new RevitApiScalar { Name = name, Kind = "null" };
            }

            throw Failed(name, "getter_failed");
        }

        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (FullNameIs(type, "System.String"))
        {
            if (value is not string text)
            {
                throw Failed(name, "getter_failed");
            }

            if (text.Length > MaxStringLength)
            {
                throw new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The read operation result is too large.");
            }

            return new RevitApiScalar { Name = name, Kind = "string", Text = text };
        }

        if (FullNameIs(type, "System.Boolean"))
        {
            if (value is not bool boolean)
            {
                throw Failed(name, "getter_failed");
            }

            return new RevitApiScalar { Name = name, Kind = "boolean", Boolean = boolean };
        }

        if (type.IsEnum)
        {
            if (IsFlags(type) || value.GetType() != type)
            {
                throw Failed(name, "getter_failed");
            }

            var enumName = Enum.GetName(type, value);
            if (string.IsNullOrEmpty(enumName))
            {
                throw Failed(name, "unnamed_enum");
            }

            return new RevitApiScalar { Name = name, Kind = "enum", Text = enumName };
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
                throw Failed(name, "getter_failed");
            }

            if (integer < MinSafeInteger || integer > MaxSafeInteger)
            {
                throw Failed(name, "integer_out_of_range");
            }

            return new RevitApiScalar { Name = name, Kind = "integer", Integer = integer };
        }

        if (FullNameIs(type, "System.Single") || FullNameIs(type, "System.Double"))
        {
            double number;
            try
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
                throw Failed(name, "getter_failed");
            }

            if (!double.IsFinite(number))
            {
                throw Failed(name, "non_finite_number");
            }

            return new RevitApiScalar { Name = name, Kind = "number", Number = number };
        }

        throw Failed(name, "getter_failed");
    }

    public static bool IsSupportedScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (IsFlags(underlying))
        {
            return false;
        }

        return FullNameIs(underlying, "System.String")
            || FullNameIs(underlying, "System.Boolean")
            || underlying.IsEnum
            || IsIntegral(underlying)
            || FullNameIs(underlying, "System.Single")
            || FullNameIs(underlying, "System.Double");
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

    private static bool IsFlags(Type type)
    {
        if (!type.IsEnum)
        {
            return false;
        }

        foreach (var attribute in type.GetCustomAttributesData())
        {
            if (attribute.AttributeType.FullName == "System.FlagsAttribute")
            {
                return true;
            }
        }

        return false;
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

    private static bool FullNameIs(Type type, string fullName)
    {
        return type.FullName == fullName;
    }

    private static BridgeException Failed(string name, string reason)
    {
        return new BridgeException(
            CapabilityErrorCodes.ExecutionFailed,
            "Property '" + name + "' could not be read (" + reason + ").");
    }

    private static BridgeException Invalid(string message)
    {
        return new BridgeException(CapabilityErrorCodes.InvalidReadOperationInput, message);
    }
}
