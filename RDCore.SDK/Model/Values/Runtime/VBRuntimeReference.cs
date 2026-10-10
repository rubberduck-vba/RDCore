using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Shared;
namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// The address of a variable, passed where the variable itself is: the argument of a <c>ByRef</c> parameter that names one.
/// </summary>
/// <param name="Value">The address of the variable.</param>
/// <param name="DeclaredType">
/// The declared type of the variable, when the parameter it is passed to does not have it: a <c>Variant</c> parameter given a variable of another type
/// (<strong>MS-VBAL §5.3.1.11</strong>), which reads as a <c>Variant</c> but is assigned as what the variable is declared to be. <see langword="null"/>
/// when the parameter's own type is the variable's.
/// </param>
public readonly record struct VBRuntimeReference(MemoryAddress Value, VBType? DeclaredType = null) : IRuntimeValue
{
    public static readonly VBRuntimeReference NullRef = new(MemoryAddress.Zero);

    public object BoxedValue => Value;
}
