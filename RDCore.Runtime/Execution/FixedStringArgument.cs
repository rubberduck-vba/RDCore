using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// A fixed-length <c>String</c> variable given to a <c>ByRef String</c> parameter (<strong>MS-VBAL §5.3.1.11</strong>): the parameter is bound to a <c>String</c>
/// of its own, made of the variable's characters, which the variable is let to when the call returns, padded with spaces or cut to its length.
/// </summary>
/// <remarks>
/// The parameter is not a second name for the variable while the call runs: what the callee assigns it is as long as it is, and the variable keeps what it
/// held until the call returns. A call that raises an error leaves the variable as it was (MS-VBA, observed in Excel for a variable, a field of a record
/// and an element of an array alike).
/// </remarks>
/// <param name="variable">The cell of the fixed-length <c>String</c> variable.</param>
/// <param name="length">The variable's length.</param>
/// <param name="text">What the variable holds when the call is made.</param>
internal sealed class FixedStringArgument(IBindingHandle variable, int length, string text) : IBindingHandle
{
    private IRuntimeValue _value = new VBStringValue(text).RuntimeValue;

    public IRuntimeValue Value => _value;

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    public IRuntimeValue GetValue(IRuntimeSymbolResolver resolver) => _value;

    public void SetValue(IRuntimeSymbolResolver resolver, IRuntimeValue value) => _value = value;

    public IRuntimeValue Invoke(IRuntimeSymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();

    /// <summary>
    /// Lets the variable to the <c>String</c> the parameter holds, fitted to its length.
    /// </summary>
    public void WriteBack(IRuntimeSymbolResolver resolver)
        => variable.SetValue(resolver, new VBStringValue(VBFixedStringLetCoercionRuntimeSemantics.Fit(_value.BoxedValue as string ?? string.Empty, length)).RuntimeValue);
}
