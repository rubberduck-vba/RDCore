namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// Declares the element type of a standard-library array parameter, which its C# signature cannot say on
/// its own.
/// </summary>
/// <remarks>
/// An array parameter is declared as a <see cref="Model.Values.Intrinsic.VBResizableArrayValue"/>, which
/// on its own reads as <c>Variant()</c>: the only array type a value type names. A member whose
/// specification declares an array of something else, such as <c>ValueArray() As Double</c>
/// (<strong>MS-VBAL §6.1.2.6.1.4</strong>), states the element type here, and the call site then
/// Let-coerces an argument to an array of it, the way it would to any other declared type.
/// </remarks>
/// <param name="elementType">
/// The element type: a <see cref="Model.Values.Abstract.VBTypedValue"/> implementation, the way a signature
/// names an intrinsic type.
/// </param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibArrayAttribute(Type elementType) : Attribute
{
    /// <summary>
    /// The value type of each element of the array.
    /// </summary>
    public Type ElementType { get; } = elementType;
}
