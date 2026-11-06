using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a Set statement (IR lowering S2.6c).
public sealed partial class CompiledActivation
{
    /// <summary>
    /// <c>Set</c> of the object variable in <paramref name="slot"/>, held in <paramref name="target"/>, to the object held in <paramref name="source"/> - or to
    /// <c>Nothing</c> (MS-VBAL §5.4.3.9): as <c>ExecuteSetAssignment</c> assigns it, the value Set-coerced to the variable's declared type, stored, then
    /// counted, and what the variable held before let go of (<c>ObjectReferences.Rebind</c>).
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the interpreter runs the statement: a cell compiled code does not read, a value that does not Set-coerce - the
    /// interpreter raises its error - or an object the variable held that letting go of would destroy, whose <c>Terminate</c> is the interpreter's to run
    /// (MS-VBAL §5.3.1.10).
    /// </returns>
    public bool TrySet(int offset, int slot, object? target, object? source, bool nothing)
    {
        if (FailsAccessAtRandom() || target is not ValueBindingHandle holder
            || Coerced(offset, source, nothing, ((ITypedSymbol)_body.SlotSymbols[slot]).ResolvedType) is not { } coerced)
        {
            return false;
        }

        var before = ObjectReferences.HeldIn(_session, holder);
        if (Destroys(before))
        {
            return false;
        }

        holder.Store(_session.Symbols.Resolver, coerced);
        ObjectReferences.Rebind(_session, holder, before);
        return true;
    }

    /// <summary>
    /// <c>Set</c> of the function result variable to the object held in <paramref name="source"/>, or to <c>Nothing</c> (MS-VBAL §5.3.1, §5.4.3.9): as
    /// <c>ExecuteSetAssignment</c> assigns it, in the frame's result cell (<c>ObjectReferences.AssignResult</c>), which holds it for as long as the activation
    /// runs.
    /// </summary>
    /// <returns><see langword="false"/> when the interpreter runs the statement, as <see cref="TrySet"/>.</returns>
    public bool TrySetResult(int offset, object? source, bool nothing)
    {
        if (FailsAccessAtRandom() || Frame is not { Procedure: ITypedSymbol function } frame
            || Coerced(offset, source, nothing, function.ResolvedType) is not { } coerced
            || Destroys(ObjectReferences.HeldIn(_session, frame.ResultCell)))
        {
            return false;
        }

        frame.ReturnValue = ObjectReferences.AssignResult(_session, frame.ResultCell, coerced);
        return true;
    }

    // the value of the Set at offset Set-coerced to declared (MS-VBAL §5.5.2.2): the object held in source, which is a copy of its reference and not a view of
    // the cell, or Nothing. None when the cell holds no reference compiled code reads, or the value does not Set-coerce.
    private VBTypedValue? Coerced(int offset, object? source, bool nothing, VBType declared)
    {
        VBObjectValue value;
        if (nothing)
        {
            value = VBObjectValue.Nothing;
        }
        else if (ObjectOperations.TryRead(source, out var instance))
        {
            value = new VBObjectValue(instance);
        }
        else
        {
            return null;
        }

        return _executor.Expressions.SetCoercion is { } coercion
            && coercion.EvaluateSetCoercion(_session, ((AssignmentStatementNode)_list.Items[offset].Node!).Value, value, declared) is { IsSuccess: true } coerced
                ? coerced.Result
                : null;
    }

    // whether letting go of what a cell held destroys an object: the cell is the last that holds it.
    private bool Destroys(ObjectReferences.Held held)
    {
        foreach (var (cell, instance) in held)
        {
            if (_session.Objects.IsHeldBy(instance, cell) && _session.Objects.RefCount(instance) <= 1)
            {
                return true;
            }
        }

        return false;
    }
}
