using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Any</c> type, which is specified but without any semantics.
/// </summary>
public sealed record class VBAnyType() : VBIntrinsicType<object?>(Tokens.Any)
{
    private static readonly Lazy<VBAnyType> _instance = new Lazy<VBAnyType>(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBAnyType TypeInfo => _instance.Value;

    private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBVariantType.TypeInfo.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}
