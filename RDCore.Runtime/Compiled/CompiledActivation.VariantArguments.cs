using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Compiled;

// what compiled code gives a Variant parameter: of a procedure of the workspace it calls, the address of a Variant variable; of a function or a member of
// the standard library, what a call of the workspace returned.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Gives the argument at <paramref name="index"/> of the call compiled code is about to make, of a <c>Variant</c> parameter passed by reference given a
    /// <c>Variant</c> variable or parameter of the activation: the variable's address, as the interpreter passes it (MS-VBAL §5.3.1.11,
    /// <c>RuntimeExpressionEvaluator.TryReferenceTo</c>), with the type of the variable a reference parameter is bound to. The parameter is a second name for
    /// the variable, so what the callee assigns it is what the variable holds after the call, in the cell compiled code reads it from. The parameter is the
    /// one of the member the call invokes, which <see cref="CanCallMember"/> found for a call of a member of an object.
    /// </summary>
    /// <param name="index">The argument's position in the callee's parameter list, after its <c>Me</c>.</param>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <param name="slot">The slot of the variable given.</param>
    /// <returns><see langword="false"/> when the variable cannot be given an address: the interpreter then makes the call.</returns>
    public bool SetVariantReference(int index, int callee, int slot)
    {
        var called = _body.Callees[callee];
        var parameters = RuntimeProcedureInvoker.GetParameters(called.Site is not null || called.CallsOwnMember ? _member! : called.Procedure);
        var first = parameters is [{ Name: SDK.Model.Tokens.Me }, ..] ? 1 : 0;
        if (FailsAccessAtRandom() || !RuntimeExpressionEvaluator.TryReferenceTo(Frame, _body.SlotSymbols[slot], parameters[first + index], out var reference))
        {
            Array.Clear(_references);
            return false;
        }

        SetReference(index, reference);
        return true;
    }

    /// <summary>
    /// Takes what the call just made returned, of a function or a <c>Property Get</c> that returns a <c>String</c> or a <c>Variant</c>
    /// (<see cref="CompiledCallee.ReturnsValue"/>), the argument at <paramref name="index"/> of the function or the member of the standard library numbered
    /// <paramref name="library"/> of the body: given to its <c>Variant</c> parameter, which takes it by value, as the interpreter gives a parameter what its
    /// argument evaluated to (<c>RuntimeExpressionEvaluator.BindArgumentValue</c>). The statement's temporaries hold what the call returned until the
    /// statement is over, as they hold what a call the interpreter makes returns.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the calls.</param>
    /// <param name="library">The number of the library's call among the body's libraries.</param>
    /// <param name="index">The argument's position in the library's parameter list, after its implicit <c>Me</c>.</param>
    /// <param name="syntax">Where the call is in the instruction's statement (<c>KnownOperands.IndexOf</c>), where an error giving it is raised.</param>
    /// <param name="argument">What the parameter is given; <see langword="null"/> when giving it raised an error, which the interpreter is given.</param>
    /// <returns>What the call returned, which the interpreter is given known when the instruction is handed to it (<see cref="KnowObject"/>).</returns>
    public object? TakeReturnedValue(int pc, int library, int index, int syntax, out IRuntimeValue? argument)
    {
        argument = null;
        var returned = _returnedObject;
        _returnedObject = null;
        if (returned is null || _list.Items[pc].Node is not { } statement || KnownOperands.At(statement, syntax) is not ExpressionNode written)
        {
            _failed = RuntimeExecutionOutcome.InternalError;
            return returned;
        }

        if (_executor.Expressions.BindArgumentValue(_session, ContextAt(pc), _body.Libraries[library].Parameters[index], written, returned, out var bound) is { } error)
        {
            _failed = error.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(error.ErrorInfo!);
            return returned;
        }

        argument = bound;
        return returned;
    }
}
