using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Compiled;

// the cells an activation without a frame holds its object parameters and variables in (CompiledBody.ObjectSlots).
public sealed partial class CompiledActivation
{
    // the cells of a call made in the activation again: those no frame was given held Nothing since the last call let go of what they held (LetGoOfObjects),
    // and are this call's; those a frame was given are the frame's, and this call makes its own.
    private void BeginObjects()
    {
        if (_objectsGiven)
        {
            Array.Clear(_objects);
            _objectsGiven = false;
        }

        // a parameter that was a second name for its caller's variable held the caller's cell, which is not the activation's to keep.
        for (var number = 0; number < _objectAliases.Length; number++)
        {
            if (_objectAliases[number])
            {
                (_objects[number], _objectAliases[number]) = (null, false);
            }
        }

        (_objectResult, _resultLetGo) = (null, false);
    }

    /// <summary>
    /// Gives the object parameter in <paramref name="slot"/> of a call compiled code makes directly (<see cref="EnterDirectly"/>) the argument
    /// <paramref name="caller"/> Set-coerced for it (<see cref="SetObject"/>), at <paramref name="index"/>: a cell of the activation's own holds it, and counts
    /// it for as long as the callee runs, as the interpreter's callee holds an object given by value, or a value given to a parameter passed by reference
    /// (MS-VBAL §5.3.1.11).
    /// </summary>
    /// <param name="slot">The parameter's slot (<see cref="CompiledCallee.ObjectSlots"/>).</param>
    /// <param name="caller">The activation that makes the call.</param>
    /// <param name="index">The argument's position in the callee's parameter list.</param>
    public void TakeObject(int slot, CompiledActivation caller, int index)
    {
        var number = Array.IndexOf(_body.ObjectSlots, slot);
        var cell = OwnObjectCell(number);
        var given = (IRuntimeValue)caller._references[index]!;
        caller._references[index] = null;
        cell.Store(given, isKnown: true);
        if (given is VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var instance })
        {
            ObjectReferences.HoldIn(_session, cell, instance);
        }
    }

    /// <summary>
    /// Gives the object parameter passed by reference in <paramref name="slot"/> of a call compiled code makes directly (<see cref="EnterDirectly"/>) the
    /// cell of the caller's variable, which the parameter is a second name for (MS-VBAL §5.3.1.11): what the callee <c>Set</c>s it to is what the variable
    /// holds, counted in the variable's cell, and the callee lets go of nothing of it when it returns.
    /// </summary>
    /// <param name="slot">The parameter's slot (<see cref="CompiledCallee.ObjectSlots"/>).</param>
    /// <param name="cell">The cell of the caller's variable, as <see cref="ObjectCellOf"/> found it, one <see cref="IsObjectCell"/> says it is.</param>
    public void TakeObjectReference(int slot, object cell)
    {
        var number = Array.IndexOf(_body.ObjectSlots, slot);
        (_objects[number], _objectAliases[number]) = ((ValueBindingHandle)cell, true);
    }

    /// <summary>
    /// Whether the cell of an object variable is one a call made directly gives a parameter passed by reference (<see cref="TakeObjectReference"/>): one
    /// compiled code reads the reference in. A variable held in no such cell is given its address, through the invoker (<see cref="SetObjectReference"/>).
    /// </summary>
    /// <param name="cell">The cell of the variable given, as <see cref="ObjectCellOf"/> found it.</param>
    public static bool IsObjectCell(object? cell) => cell is ValueBindingHandle;

    // whether each object parameter of the activation is a second name for its caller's variable (TakeObjectReference), by its number.
    private readonly bool[] _objectAliases;

    // the function result of an activation without a frame, of a function that returns an object (CompiledBody.ResultIsObject): what it was last Set to,
    // a copy of the reference, and the cell that holds that reference while the activation runs, as the frame's result cell holds it
    // (CallStackFrame.ResultCell).
    // The cell holds Nothing once the activation returned, or gave its result to its frame, and is the next call's.
    private VBTypedValue? _objectResult;
    private ValueBindingHandle? _resultCell;

    // Set of the function result of an activation without a frame (TrySetResult), as ObjectReferences.AssignResult assigns the frame's.
    private bool TrySetOwnResult(int offset, object? source, bool nothing)
    {
        var cell = _resultCell ??= new ValueBindingHandle(VBObjectValue.Nothing.RuntimeValue);
        if (ValueOf(source, nothing) is not { } value || Coerced(offset, value, ((ITypedSymbol)_procedure!).ResolvedType) is not { } coerced
            || Destroys(ObjectReferences.HeldIn(_session, cell)))
        {
            return false;
        }

        _objectResult = ObjectReferences.AssignResult(_session, cell, coerced);
        return true;
    }

    // what the function returns: what its result was Set to, or Nothing, of its type, when it was Set to nothing (MS-VBAL §5.3.1).
    private VBTypedValue OwnResult() => _objectResult ?? ((ITypedSymbol)_procedure!).ResolvedType.DefaultValue;

    // the frame given the function result, as it is made (MakeFrame): the frame's result cell holds the reference, then the activation's lets go of it.
    private void GiveResult(CallStackFrame frame)
    {
        if (!_body.ResultIsObject)
        {
            return;
        }

        // a result the activation let go of as it returned (LetGoOfResult) is held by the caller's temporary. A frame made after that, for a Class_Terminate
        // that runs as its objects are let go of, is told what the function returns, and holds nothing of it.
        var value = OwnResult();
        if (_resultLetGo)
        {
            (frame.ReturnValue, _objectResult) = (value, null);
            return;
        }

        frame.ReturnValue = ObjectReferences.AssignResult(_session, frame.ResultCell, value);
        if (_resultCell is { } cell)
        {
            LetGoOf(cell);
        }

        _objectResult = null;
    }

    // what the function returns held as a temporary of the caller's statement, then let go of by the result, as the frame does it
    // (RuntimeProcedureInvoker.ReleaseLocals): what it returns outlives the callee's variables, which let go of it after.
    private void LetGoOfResult()
    {
        if (_body.ResultIsObject)
        {
            ObjectReferences.HoldTemporarily(_session, _objectResult);
            if (_resultCell is { } cell)
            {
                LetGoOf(cell);
            }

            _resultLetGo = true;
        }
    }

    // whether the activation let go of its function result as it returned (LetGoOfResult): a frame given it after holds nothing of it (GiveResult).
    private bool _resultLetGo;

    // the cell of the object numbered number, made holding what the interpreter declares the variable with (RuntimeProcedureInvoker.DefaultValueOf): Nothing.
    private ValueBindingHandle OwnObjectCell(int number)
        => _objects[number] ??= new ValueBindingHandle(_body.SlotSymbols[_body.ObjectSlots[number]] is VBLocalVariableSymbol variable
            ? RuntimeProcedureInvoker.DefaultValueOf(_session, variable).RuntimeValue
            : VBObjectValue.Nothing.RuntimeValue);

    // the frame given every object's cell, as it is made (MakeFrame): what compiled code Set the variables to, counted in the cells, is what the frame holds,
    // and the frame lets go of it when the activation returns (RuntimeProcedureInvoker.ReleaseLocals).
    private void GiveObjects(CallStackFrame frame)
    {
        for (var number = 0; number < _objects.Length; number++)
        {
            _ = GiveObject(frame, number);
        }
    }

    // the frame given the cell of the object numbered number, when it holds none in its slot.
    private ValueBindingHandle GiveObject(CallStackFrame frame, int number)
    {
        var cell = OwnObjectCell(number);
        var slot = _body.ObjectSlots[number];
        if (!frame.IsSlotFilled(slot))
        {
            var symbol = _body.SlotSymbols[slot];
            if (_objectAliases[number])
            {
                frame.PushAlias(symbol, cell, slot);
            }
            else
            {
                frame.PushObject(symbol, ((ITypedSymbol)symbol).ResolvedType, cell, slot);
            }
        }

        _objectsGiven = true;
        return cell;
    }

    // what the object variables and parameters of an activation that returns let go of, as the frame lets go of them (RuntimeProcedureInvoker.ReleaseLocals):
    // the variables in the order they are declared, then Me, of a member, then the parameters in theirs, but those that were their caller's variable
    // (MS-VBAL §5.3.1.10: an object only they held is destroyed with them, which is where its Terminate runs). Each cell holds Nothing after, for the next
    // call made in the activation.
    private void LetGoOfObjects(ValueBindingHandle? me)
    {
        for (var number = 0; number < _objects.Length; number++)
        {
            if (_objects[number] is { } cell && _body.SlotSymbols[_body.ObjectSlots[number]] is VBLocalVariableSymbol)
            {
                LetGoOf(cell);
            }
        }

        if (me is not null)
        {
            ObjectReferences.Release(_session, me);
        }

        for (var number = 0; number < _objects.Length; number++)
        {
            if (_objects[number] is { } cell && !_objectAliases[number] && _body.SlotSymbols[_body.ObjectSlots[number]] is VBParameterSymbol)
            {
                LetGoOf(cell);
            }
        }
    }

    // the arguments the frame's end reads which parameters were their caller's variable from (RuntimeProcedureInvoker.ReleaseLocals): the cell of each that
    // was, for a reference to it; none when no parameter is an object.
    private IRuntimeValue[]? ArgumentsAsGiven()
    {
        if (_objectAliases.Length == 0 || Array.IndexOf(_objectAliases, true) < 0)
        {
            return null;
        }

        var procedure = _procedure ?? _frame!.Procedure!;
        var slots = LanguageCore.Binding.FrameLayout.For(procedure).ParameterSlots;
        var arguments = new IRuntimeValue[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var number = Array.IndexOf(_body.ObjectSlots, slots[i]);
            arguments[i] = number >= 0 && _objectAliases[number] ? new CellReference(_objects[number]!) : VBObjectValue.Nothing.RuntimeValue;
        }

        return arguments;
    }

    // the reference cell holds let go of, as ObjectReferences.Release lets go of it, and Nothing stored in it.
    private void LetGoOf(ValueBindingHandle cell)
    {
        if (cell.Value is VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var held } && !held.Equals(VBObjectValue.Nothing.Value))
        {
            cell.Store(VBObjectValue.Nothing.RuntimeValue, isKnown: true);
            if (_session.Objects.IsHeldBy(held, cell))
            {
                _session.ReleaseReference(held, cell);
            }
        }
    }

    // the end of an activation of a procedure compiled code called directly (EnterDirectly), as the invoker ends one: in its frame, when it has one, which
    // lets go of what its variables hold as the frame of any call does, unless the program was stopped or ended in it
    // (RuntimeProcedureInvoker.PopMemberActivation); without one, the activation lets go of them itself, and is taken off the stack
    // (RuntimeProcedureInvoker.PopCompiledActivation).
    private RuntimeExecutionOutcome EndProcedure(CompiledActivation callee, RuntimeExecutionOutcome outcome)
    {
        if (callee._body.ObjectSlots.Length == 0 && !callee._body.ResultIsObject)
        {
            return _invoker.PopCompiledActivation(outcome, light: callee._frame is null);
        }

        if (callee._frame is { Procedure: { } procedure } frame)
        {
            return _invoker.PopMemberActivation(frame, procedure, outcome, callee.ArgumentsAsGiven());
        }

        if (outcome.Kind is not RuntimeExecutionOutcomeKind.ExitProcedure)
        {
            // what stopped or failed the call is the frame's to say, which lets go of nothing when the program is over
            // (RuntimeProcedureInvoker.EndsTheProgram).
            return _invoker.PopMemberActivation(callee.Frame, callee._procedure!, outcome, callee.ArgumentsAsGiven());
        }

        callee.LetGoOfResult();
        callee.LetGoOfObjects(me: null);
        return _invoker.PopCompiledActivation(outcome, light: callee._frame is null);
    }

    /// <summary>
    /// Whether the activation holds an object of its own while it has no frame: an object parameter or variable (<see cref="CompiledBody.ObjectSlots"/>),
    /// or the result of a function that returns an object.
    /// </summary>
    internal bool HoldsObjects => _body.ObjectSlots.Length > 0 || _body.ResultIsObject;

    /// <summary>
    /// Ends an activation the invoker made without a frame (<see cref="EnterThroughInvoker"/>), which returned without one. The object its function
    /// returns is held as a temporary of the caller's statement first; then what its result and its object parameters and variables hold is let go of, as
    /// the frame of any call lets go of it (MS-VBAL §5.3.1.10). A <c>Class_Terminate</c> that runs then may give the activation its frame, as any call does.
    /// </summary>
    /// <returns>
    /// What the function returns: the object it was <c>Set</c> to (<c>Nothing</c> when it never was), or the payload its result variable holds;
    /// <see langword="null"/> when that variable holds no payload, or the procedure is not a function.
    /// </returns>
    internal VBTypedValue? EndWithoutFrame()
    {
        var returned = _body.ResultIsObject ? OwnResult() : TryReadResult(out var payload) ? payload.Typed() : null;
        LetGoOfResult();
        LetGoOfObjects(me: null);
        return returned;
    }
}
