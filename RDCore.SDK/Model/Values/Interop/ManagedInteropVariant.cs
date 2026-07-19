using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values.Interop;

public enum VBVariantValueType
{
    Empty,
    Integer,
    Dispatch,
    BString,
}

/// <summary>
/// Represents the <em>managed value</em> of a <see cref="VBVariantValue"/>
/// </summary>
/// <param name="ValueType">The variant <em>value type</em>.</param>
/// <param name="ValueAlloc">The allocation scope of the value.</param>
/// <param name="Handle">A handle to the value in the specified memory space.</param>
public readonly record struct ManagedInteropVariant(VBVariantValueType ValueType, ScopeKind ValueAlloc, IBindingHandle Handle);
