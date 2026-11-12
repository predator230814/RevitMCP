using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Registry-facing boundary for one compiled read operation.
/// Lookup uses the descriptor captured at registration. Execution is <see cref="IExecutableReadOperation"/>.
/// </summary>
internal interface IRevitReadOperation
{
    ReadOperationDescriptor Descriptor { get; }
}
