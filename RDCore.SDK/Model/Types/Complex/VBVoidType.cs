using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Types.Complex;

/// <summary>
/// Represents the <c>void</c> semantic data type.
/// </summary>
public record class VBVoidType() : VBType(typeof(void), VBTypeNames.VBVoid, isHidden: true)
{
    private static readonly Lazy<VBVoidType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Void</c> semantic data type.
    /// </summary>
    /// <remarks>
    /// Represents the absence of data type semantics, expressed in a valid <c>VBType</c> representation.
    /// </remarks>
    public static VBType TypeInfo => _instance.Value;

    private readonly Lazy<VBVoidValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override int Size => 0;
}