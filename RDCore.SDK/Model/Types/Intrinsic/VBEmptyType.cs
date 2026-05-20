using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Empty</c> data type.
/// </summary>
public sealed record class VBEmptyType() : VBIntrinsicType<int?>(Tokens.Empty)
{
    private static readonly Lazy<VBEmptyType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBEmptyType TypeInfo => _instance.Value;

    private static readonly Lazy<VBEmptyValue> _defaultValue = new(() => VBEmptyValue.Empty, LazyThreadSafetyMode.PublicationOnly);
    public override VBEmptyValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}
