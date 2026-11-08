using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types.Meta;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Meta;

/// <summary>
/// Describes a specified <see cref="VBType"/> for a specified symbol.
/// </summary>
/// <param name="Target">The described type.</param>
/// <param name="Symbol">The symbol associated with this value.</param>
public record class VBTypeDescValue(VBType Target) : VBTypedValue(VBTypeDesc.TypeInfo)
{
    private static readonly Lazy<VBTypeDescValue> _defaultValue = new(() => new(VBVoidType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets a <c>VBTypeDescValue</c> describing a <c>VBUnknownType</c>.
    /// </summary>
    public static VBTypedValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}

/// <summary>
/// Creates a new <c>VBTypeDescValue</c> describing the specified <em>deferred type</em> at the specified symbol.
/// </summary>
/// <param name="deferredTypeInfo">The described <em>deferred type</em>.</param>
internal record class VBDeferredTypeDescValue(VBDeferredType DeferredTarget) : VBTypeDescValue(DeferredTarget) 
{
}
