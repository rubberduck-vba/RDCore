using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// Numeric type -> value construction for the runtime-semantics layer. This is the "justified
/// ownership" home (#130) for the dispatch that <c>VBTypedValueFactory.CreateValue(VBType, double)</c>
/// used to do via an infinitely-recursive stub: the zero value of the target numeric type re-bound
/// to <paramref name="value"/>, converted to that type's managed representation by
/// <see cref="VBNumericTypedValue.WithValue{T}(T)"/>.
/// </summary>
internal static class RuntimeNumericValue
{
    public static VBNumericTypedValue Of(VBType numericType, double value)
        => (VBNumericTypedValue)((VBNumericTypedValue)numericType.DefaultValue).WithValue(value);
}
