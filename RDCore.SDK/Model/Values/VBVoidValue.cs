using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values;

/// <summary>
/// Represents the data type returned by non-returning member procedures.
/// </summary>
/// <remarks>
/// This data type has no attached or specified semantics: as far as the language goes, there is no value. Under that, the runtime
/// has what every call has: a result code, the <c>HRESULT</c> <c>S_OK</c> (<see cref="VBRuntimeHResult.Ok"/>).
/// </remarks>
public sealed record class VBVoidValue : VBTypedValue
{
    private VBVoidValue() : base(VBVoidType.TypeInfo)
    {
        Handle = new ValueBindingHandle(VBRuntimeHResult.Ok);
    }

    private static readonly Lazy<VBVoidValue> _void = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// A meta-value representing the absence of value semantics, expressed in a valid <c>VBTypedValue</c>.
    /// </summary>
    public static VBVoidValue Void => _void.Value;

    public override int Size => 0;
}