using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Variant/Missing</c> value: what an optional <c>Variant</c> parameter holds when no argument was supplied for it.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §6.1.2.7.1.6</strong> calls it "the Missing data value", and §6.1.2.7.1.12 and §6.1.2.7.1.13 report it the same as
/// an Error. It is one: the <c>Variant</c> COM passes for an omitted argument is a <c>VT_ERROR</c> carrying <c>DISP_E_PARAMNOTFOUND</c>,
/// which VBA reads as error <see cref="VBRuntimeErrorId.NamedArgumentNotFound"/> (448) everywhere but in <c>IsMissing</c>:
/// <c>CStr</c> of it is <c>"Error 448"</c>, <c>CLng</c> of it is <c>448</c>, and an operator given it raises Type mismatch. So it is an
/// <see cref="VBErrorValue"/> with that code, and every operation that handles an error value handles it unchanged.
/// <para>
/// 👉 Copying it keeps it Missing: a <c>Variant</c> assigned from an omitted parameter, or an optional parameter it is passed on to,
/// is Missing too.
/// </para>
/// </remarks>
public sealed record class VBMissingValue : VBErrorValue
{
    public VBMissingValue()
        : this(new ValueBindingHandle(new VBRuntimeValue<int>((int)VBRuntimeErrorId.NamedArgumentNotFound)))
    {
    }

    public VBMissingValue(IBindingHandle handle) : base(VBMissingType.TypeInfo, handle) { }

    private static readonly Lazy<VBMissingValue> _missing = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static VBMissingValue Missing => _missing.Value;
}
