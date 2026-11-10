using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The value of a declared type that a binding holds, as <c>CreateValue</c> of the type makes it, except for a <c>LongPtr</c> of a 32-bit environment: the
/// value the 32-bit type makes of a binding is of the 64-bit type, so it is made here from the integer the binding holds, a <c>Long</c>'s (MS-VBAL §2.2).
/// </summary>
internal static class DeclaredValues
{
    /// <summary>
    /// The value of <paramref name="type"/> that <paramref name="handle"/> holds.
    /// </summary>
    /// <param name="type">The declared type.</param>
    /// <param name="handle">The binding: a variable's cell, or one made of a runtime value.</param>
    /// <returns>A value of the type; a 32-bit <c>LongPtr</c> whose value is not known is made by the type, as any other value is.</returns>
    public static VBTypedValue Of(VBType type, IBindingHandle handle)
        => type is VBLongPtrType_x86 && handle is not IKnowledgeBinding { IsKnown: false } && handle.Value is VBRuntimeValue<int> pointer
            ? new VBLongPtrValue(pointer.Value)
            : type.CreateValue(handle);

    /// <summary>
    /// The value of <paramref name="type"/> that <paramref name="value"/> is.
    /// </summary>
    /// <param name="type">The declared type.</param>
    /// <param name="value">The runtime value.</param>
    /// <returns>A value of the type, as <see cref="Of(VBType, IBindingHandle)"/> makes it of a binding to <paramref name="value"/>.</returns>
    public static VBTypedValue Of(VBType type, IRuntimeValue value) => Of(type, new ValueBindingHandle(value));
}
