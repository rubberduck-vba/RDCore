using Microsoft.VisualBasic;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic
{
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
    /// <remarks>
    /// 👉 This type is intended to eventually interface with a COM <c>VT_Variant</c>
    /// </remarks>
    /// <param name="ValueType">The variant <em>value type</em>.</param>
    /// <param name="ValueAlloc">The allocation scope of the value.</param>
    /// <param name="ValuePtr">A pointer to the value in the specified memory space.</param>
    public readonly record struct VBVariantInteropValue(VBVariantValueType ValueType, ScopeKind ValueAlloc, long ValuePtr);
}