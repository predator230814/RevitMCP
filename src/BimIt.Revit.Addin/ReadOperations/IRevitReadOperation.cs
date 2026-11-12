using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Registry-facing boundary for one compiled read operation.
/// Execution stays off this interface until the operation is implemented.
/// </summary>
internal interface IRevitReadOperation
{
    ReadOperationDescriptor Descriptor { get; }
}
