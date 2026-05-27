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
public record class VBTypeDescValue(VBType Target, Symbol Symbol) : VBTypedValue(VBTypeDesc.TypeInfo, Symbol)
{
    private static readonly Lazy<VBTypeDescValue> _defaultValue = new(() => new(GlobalSymbols.StaticSymbols.VBUnknown.ResolvedType, GlobalSymbols.StaticSymbols.VBUnknown), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets a <c>VBTypeDescValue</c> describing a <c>VBUnknownType</c>.
    /// </summary>
    public static VBTypedValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);

    public override object BoxedValue => 0;
}

/// <summary>
/// Creates a new <c>VBTypeDescValue</c> describing the specified <em>deferred type</em> at the specified symbol.
/// </summary>
/// <param name="deferredTypeInfo">The described <em>deferred type</em>.</param>
/// <param name="symbol">The symbol associated with this value.</param>
public record class VBDeferredTypeDescValue(VBDeferredType DeferredTarget, Symbol Symbol) : VBTypeDescValue(DeferredTarget, Symbol) 
{
}
