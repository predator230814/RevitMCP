using System.Reflection;
using Autodesk.Revit.DB;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// The Revit API assembly already loaded with this process. Callers cannot name another assembly.
/// </summary>
internal static class TrustedRevitApiTypes
{
    public static Assembly Assembly => typeof(Element).Assembly;

    public static Type? FindPublicType(string? fullName)
    {
        return FindPublicType(Assembly, fullName);
    }

    public static Type? FindPublicType(Assembly assembly, string? fullName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 256 || fullName.Contains('`', StringComparison.Ordinal))
        {
            return null;
        }

        var type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
        if (type is null || type.Assembly != assembly || (!type.IsPublic && !type.IsNestedPublic))
        {
            return null;
        }

        return type;
    }

    public static bool IsConcreteElement(Type type)
    {
        return IsConcreteElement(type, type.Assembly);
    }

    public static bool IsConcreteElement(Type type, Assembly assembly)
    {
        return type.IsClass
            && !type.IsAbstract
            && type.Assembly == assembly
            && Extends(type, "Autodesk.Revit.DB.Element");
    }

    public static bool IsElement(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.FullName == "Autodesk.Revit.DB.Element")
            {
                return current.IsClass;
            }
        }

        return false;
    }

    public static Type RequireConcreteElement(string? fullName)
    {
        return RequireConcreteElement(Assembly, fullName);
    }

    public static Type RequireConcreteElement(Assembly assembly, string? fullName)
    {
        var type = FindPublicType(assembly, fullName);
        if (type is null || !IsConcreteElement(type, assembly))
        {
            throw new BridgeException(
                CapabilityErrorCodes.InvalidReadOperationInput,
                "type_name is not a concrete Element subclass in the loaded Revit API assembly.");
        }

        return type;
    }

    private static bool Extends(Type type, string baseTypeName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == baseTypeName)
            {
                return true;
            }
        }

        return false;
    }
}
