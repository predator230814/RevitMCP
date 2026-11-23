using System.Reflection;
using Autodesk.Revit.DB;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Public types from the Revit API assembly already loaded in this process.
/// The parameterless members use <c>typeof(Element).Assembly</c> and run inside Revit.
/// </summary>
internal static class RevitApiTypeResolver
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

    public static bool IsElement(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.FullName == "Autodesk.Revit.DB.Element")
            {
                return current.IsClass;
            }
        }

        return false;
    }

    public static Type RequireForRead(string? fullName)
    {
        return RequireForRead(Assembly, fullName);
    }

    public static Type RequireForRead(Assembly assembly, string? fullName)
    {
        var type = FindPublicType(assembly, fullName);
        if (type is null)
        {
            throw Reject("unknown");
        }

        if (!IsElement(type))
        {
            throw Reject("not_element");
        }

        if (!type.IsClass || type.IsAbstract)
        {
            throw Reject("abstract");
        }

        return type;
    }

    public static BridgeException Reject(string reason)
    {
        return new BridgeException(
            CapabilityErrorCodes.RevitApiTypeRejected,
            "type_name is not available (" + reason + ").");
    }
}
