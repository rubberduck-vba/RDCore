using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// What the environment the code is compiled for decides about its static semantics, which no module and no scope says: how wide a pointer is.
/// </summary>
/// <remarks>
/// The declarations of a module are resolved with it already - a variable declared <c>As LongPtr</c> has the <c>LongPtr</c> of its environment - so a
/// rule only needs it for a value whose type is the environment's own: the address an <c>AddressOf</c> expression gives (<strong>MS-VBAL §5.6.16.8</strong>).
/// </remarks>
/// <param name="Is64Bit">Whether the environment is a 64-bit one, whose <c>LongPtr</c> is a <c>LongLong</c> (<strong>MS-VBAL §2.2</strong>); otherwise it is a <c>Long</c>.</param>
public sealed record class StaticEnvironment(bool Is64Bit)
{
    /// <summary>
    /// The <c>LongPtr</c> of the environment: the declared type of a pointer, or of a handle.
    /// </summary>
    public VBType PointerType => Is64Bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo;
}
