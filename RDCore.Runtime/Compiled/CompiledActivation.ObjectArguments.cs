using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Compiled;

// what compiled code gives an object parameter of a procedure it calls.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Gives the argument at <paramref name="index"/> of the call numbered <paramref name="callee"/> compiled code is about to make, of an object parameter
    /// passed by value, or passed by reference and given no variable: the object held in <paramref name="cell"/>, or <c>Nothing</c>, Set-coerced to the
    /// parameter's declared type as the interpreter coerces it (MS-VBAL §5.3.1.11, §5.5.2.2). The callee's parameter holds it, and counts it, for as long
    /// as the callee runs.
    /// </summary>
    /// <param name="index">The argument's position in the callee's parameter list.</param>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <param name="cell">The cell of the object variable, field or <c>Me</c> given.</param>
    /// <param name="nothing">Whether <c>Nothing</c> is given, and no cell.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter makes the call: a cell compiled code does not read, or an object that does not Set-coerce to the
    /// parameter's type, whose error the interpreter raises at the call, before anything is called.
    /// </returns>
    public bool SetObject(int index, int callee, object? cell, bool nothing)
    {
        VBObjectValue value;
        if (FailsAccessAtRandom())
        {
            Array.Clear(_references);
            return false;
        }

        if (nothing)
        {
            value = VBObjectValue.Nothing;
        }
        else if (ObjectOperations.TryRead(cell, out var instance))
        {
            value = new VBObjectValue(instance);
        }
        else
        {
            Array.Clear(_references);
            return false;
        }

        var parameter = _body.Callees[callee].ObjectParameters[index]!;
        if (_executor.Expressions.SetCoercion is not { } coercion
            || coercion.EvaluateSetCoercion(_session, parameter.Argument, value, parameter.Type) is not { IsSuccess: true, Result: { } coerced })
        {
            Array.Clear(_references);
            return false;
        }

        SetReference(index, coerced.RuntimeValue);
        return true;
    }

    /// <summary>
    /// Gives the argument at <paramref name="index"/> of the call compiled code is about to make, of an object parameter passed by reference given an object
    /// variable of the activation of exactly its type: the variable's address, as the interpreter passes it (MS-VBAL §5.3.1.11). The parameter is a second name
    /// for the variable, so what the callee Sets it to is what the variable holds after the call, in the cell compiled code reads it from.
    /// </summary>
    /// <param name="index">The argument's position in the callee's parameter list.</param>
    /// <param name="slot">The slot of the variable given.</param>
    /// <returns><see langword="false"/> when the variable cannot be given an address: the interpreter then makes the call.</returns>
    public bool SetObjectReference(int index, int slot)
    {
        if (FailsAccessAtRandom() || !Frame.TryGetAddress(_body.SlotSymbols[slot], out var address))
        {
            Array.Clear(_references);
            return false;
        }

        SetReference(index, new VBRuntimeReference(address));
        return true;
    }
}
