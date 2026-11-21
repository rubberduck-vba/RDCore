using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// An activation of a procedure that runs compiled: what its compiled code is given, and everything it calls besides the payload
/// operations - reading its parameters, polling for what stops a program, and handing the activation to the interpreter.
/// </summary>
/// <remarks>
/// <para>
/// The activation's frame is laid out, its parameters given and its variables declared, as for an activation the interpreter runs, unless its body holds
/// every variable of its own (<see cref="CompiledBody.NeedsFrame"/>). The activation is then on the stack without a frame, however the call was made, and
/// is given the frame only when something is to see it. Compiled code reads its parameters from their cells, or from what the activation was given, when it
/// starts, and keeps its variables in its own locals from then on: the cells are written again only when the activation is handed to the interpreter.
/// </para>
/// <para>
/// It is handed to the interpreter - <em>walked back</em> - at an instruction boundary: when an operation declines or raises an error, which the
/// interpreter then evaluates as it always does, with the message and the location it always gives; and when the program is to stop, which the interpreter
/// stops it for. Compiled code writes its variables into their cells and the state of each <c>For</c> loop it is in into the frame, and the interpreter runs
/// the rest of the activation from that instruction.
/// </para>
/// </remarks>
public sealed partial class CompiledActivation : ILightActivation
{
    private readonly IRuntimeSession _session;
    private readonly InstructionList _list;
    private readonly ProcedureExecutor _executor;
    private RuntimeEvaluationContext _context;
    private readonly CompiledBody _body;
    private readonly ISessionHalt _halt;

    // what made the activation, which makes the activations of the calls it makes through their callees' typed entries, and ends them (EnterDirectly): the
    // session's own, never one the body was compiled with - a body is shared by every session composed over the same scopes.
    private readonly RuntimeProcedureInvoker _invoker;

    // the activation's frame; none for one of a call compiled code made directly, until something is to see it: it is then made
    // (RuntimeCallStack.MaterializeLights), and the activation is held in it from then on.
    private CallStackFrame? _frame;

    // what an activation without a frame holds in place of one: the procedure it is of, the stack it is on, the instruction of the call it is making and the
    // number of that call among the body's callees (-1 when it is making none), or of the call of a function of the standard library among the body's
    // libraries (-1 when it is making none), and its function result variable.
    private readonly VBTypeMemberSymbol? _procedure;
    private readonly RuntimeCallStack? _stack;
    private int _pc;
    private int _call = -1;
    private int _library = -1;
    private ScalarPayload _result;
    private bool _hasResult;

    // the error handling policy its On Error statements set, which its frame is given when the frame is made: an error compiled code fails at is handed to
    // the interpreter, which makes the frame, so an activation without a frame never has an error to handle.
    private ErrorHandlerState _errorHandler = ErrorHandlerState.Disabled;

    // the cell of each slot held in one (CompiledBody.CellSlots), by its number: what compiled code reads and writes the variable in, and what the frame
    // holds, given it as soon as there is one (TakeReference).
    private readonly object?[] _cells;

    // whether each numbered cell is the activation's own although its slot is a parameter passed by reference: the cell of a value (TakeReference).
    private readonly bool[] _ownsCell;

    // the cell of each String of the activation (CompiledBody.TextSlots), by its number, which its frame is given: when it is made (MakeFrame), or as the
    // entry finds the cell (StringCellOf); and whether each is the caller's variable, which a parameter passed by reference is a second name for
    // (TakeTextReference).
    private readonly TextCell?[] _texts;
    private readonly bool[] _textAliases;

    // the cell of each object parameter and variable of an activation without a frame (CompiledBody.ObjectSlots), by its number: made as compiled code
    // first finds it (ObjectCellOf), and given to the frame when it is made (MakeFrame). Cells no frame was given are the activation's alone: what they held
    // is let go of when it returns (ReturnedDirectly), and they hold Nothing for its next call, made in it again (Begin).
    private readonly ValueBindingHandle?[] _objects;

    // whether a frame was given the cells of _objects: they are then the frame's too, and the next call is given cells of its own.
    private bool _objectsGiven;

    internal CompiledActivation(IRuntimeSession session, CallStackFrame frame, InstructionList list, ProcedureExecutor executor, RuntimeEvaluationContext context,
        CompiledBody body, RuntimeProcedureInvoker invoker)
        : this(session, list, executor, context, body, invoker, procedure: null)
    {
        _frame = frame;
        frame.RunsCompiled = true;
    }

    // an activation of a call compiled code makes directly, on the stack without a frame (EnterDirectly).
    private CompiledActivation(IRuntimeSession session, InstructionList list, ProcedureExecutor executor, RuntimeEvaluationContext context, CompiledBody body,
        RuntimeProcedureInvoker invoker, VBTypeMemberSymbol? procedure)
    {
        (_session, _list, _executor, _context, _body, _invoker, _procedure) = (session, list, executor, context, body, invoker, procedure);
        _halt = session.Halt;
        (_sessionHalt, _table) = (session.Halt as SessionHalt, (session.Symbols as SessionSymbols)?.Table);
        _stack = session.CallStack as RuntimeCallStack;
        _cells = body.CellSlots.Count == 0 ? [] : new object?[body.CellSlots.Count];
        _ownsCell = body.CellSlots.Count == 0 ? [] : new bool[body.CellSlots.Count];
        _texts = body.TextSlots.Length == 0 ? [] : new TextCell?[body.TextSlots.Length];
        _textAliases = body.TextSlots.Length == 0 ? [] : new bool[body.TextSlots.Length];
        _objects = body.ObjectSlots.Length == 0 ? [] : new ValueBindingHandle?[body.ObjectSlots.Length];
        _objectAliases = body.ObjectSlots.Length == 0 ? [] : new bool[body.ObjectSlots.Length];
        Begin();
    }

    // what every activation begins with, one made again too (EnterDirectly): the session's symbols as they are now, which a call made directly is given by
    // the activation that makes it, which found them unchanged (MayEnterDirectly).
    private void Begin(ScopeTree? scopes = null)
    {
        Array.Clear(_cells);
        Array.Clear(_ownsCell);
        Array.Clear(_texts);
        Array.Clear(_textAliases);
        BeginObjects();
        (_entered, _instance, _me) = (null, null, null);
        _scopes = scopes ?? _session.Symbols.Scopes;
        _directAt = -1;
        (_pc, _call, _library, _hasResult, _failed, _unspilled) = (0, -1, -1, false, null, false);
        _givenCount = 0;
        _errorHandler = ErrorHandlerState.Disabled;
        _knowns?.Clear();
        _untilPoll = PollInterval;
        _handedOver = false;

        // the safepoint the activation is handed to the interpreter at, under the random walk-back (CompiledBody.NextRandomSafepoint).
        if (CompiledExecution.WalksBackAtRandom)
        {
            _untilPoll = _body.NextRandomSafepoint();
        }

        _untilFailedAccess = CompiledExecution.FailsReloadsAtRandom ? _body.NextRandomFailedAccess() : 0;
    }

    // whether the interpreter was given the activation's frame since it began (Interpret, AfterFailedCall, Recover). One it was not given ran its compiled
    // code to the end, and holds what an activation that returned from a call made directly holds: the invoker may begin it again for its next call of the
    // body (BeginAgain).
    private bool _handedOver;

    /// <summary>
    /// Whether the activation, made by the invoker for a call through it, ran its compiled code to the end without handing its frame to the interpreter: the
    /// invoker may then begin it again for its next call of the same body (<see cref="BeginAgain"/>).
    /// </summary>
    internal bool ReturnedInCompiledCode => !_handedOver;

    /// <summary>
    /// Whether the activation runs <paramref name="list"/>, the instructions <paramref name="body"/> was compiled from, for <paramref name="invoker"/>: an
    /// activation the invoker keeps is begun again only for a call of the same code.
    /// </summary>
    /// <param name="body">The compiled body of the call.</param>
    /// <param name="list">The instructions the body was compiled from.</param>
    /// <param name="invoker">The invoker that makes the call.</param>
    internal bool Runs(CompiledBody body, InstructionList list, RuntimeProcedureInvoker invoker)
        => ReferenceEquals(_body, body) && ReferenceEquals(_list, list) && ReferenceEquals(_invoker, invoker);

    /// <summary>
    /// Begins the activation again for another call the invoker makes of the body, in <paramref name="frame"/>, the call's frame, as an activation made for
    /// the call begins.
    /// </summary>
    /// <param name="frame">The call's frame.</param>
    /// <param name="context">The context the body's code is evaluated in.</param>
    internal void BeginAgain(CallStackFrame frame, RuntimeEvaluationContext context)
    {
        (_frame, _context) = (frame, context);
        frame.RunsCompiled = true;
        Begin();
    }

    /// <summary>
    /// Lets go of the frame of the call the activation returned from, and of what it held, while the invoker keeps it for its next call of the body.
    /// </summary>
    internal void LetGoAfterCall()
    {
        _frame = null;
        Array.Clear(_cells);
        LetGoOfMe();
    }

    // how many accesses of the frame the activation makes before the one it fails, under the random failed accesses (CompiledExecution.FailsReloadsAtRandom):
    // 0 when it fails none.
    private int _untilFailedAccess;

    private bool FailsAccessAtRandom() => _untilFailedAccess > 0 && --_untilFailedAccess == 0;

    CallStackFrame ILightActivation.MakeFrame()
    {
        // a member of a class entered on an object (EnterMemberDirectly): its frame holds Me in the cell the activation held it in, which lets go of it.
        var frame = _entered is { } member && _me is { } me
            ? _invoker.CreateMemberFrame(member, _list, _instance!.ObjectId, me)
            : _invoker.CreateCompiledFrame(_procedure!, _list);
        (frame.Pc, frame.RunsCompiled, frame.ErrorHandler) = (_pc, true, _errorHandler);
        if (_call >= 0)
        {
            _ = frame.EnterCall(_body.Callees[_call].CallLocation);
        }
        else if (_library >= 0)
        {
            _ = frame.EnterCall(_body.Libraries[_library].Site);
        }

        if (_hasResult)
        {
            frame.AssignResult(_result);
        }

        for (var number = 0; number < _cells.Length; number++)
        {
            Hold(frame, number);
        }

        for (var number = 0; number < _texts.Length; number++)
        {
            HoldText(frame, number);
        }

        GiveObjects(frame);
        GiveResult(frame);

        // a parameter the invoker gave a payload that compiled code has not read yet: the interpreter, given the activation before its entry ran, reads it
        // in its cell. One compiled code read is in its locals, which it writes into the frame before the interpreter is given the activation (Spill).
        for (var i = 0; i < _givenCount; i++)
        {
            if (_givenSlots[i] is var slot and >= 0 && !frame.IsSlotFilled(slot))
            {
                frame.PushPayload(_body.SlotSymbols[slot], _given[i], slot);
            }
        }

        return frame;
    }

    // the payload of each scalar parameter passed by value the invoker gave an activation it made without a frame (EnterThroughInvoker), and its slot - the
    // complement of it once compiled code read it as it began (TryReadIntegral, TryReadFloating): from then on, the parameter is compiled code's, as one
    // given through a typed entry is.
    private int[] _givenSlots = [];
    private ScalarPayload[] _given = [];
    private int _givenCount;

    /// <summary>
    /// The activation of a call the invoker makes of <paramref name="procedure"/>, whose compiled code holds every variable of its own
    /// (<see cref="CompiledBody.NeedsFrame"/>), on the stack without a frame, as a call compiled code makes directly is (<see cref="EnterDirectly"/>): in the
    /// activation that last left its place of the stack, begun again, when that was an activation of the same body. The invoker then gives it its arguments
    /// (<see cref="GivePayload"/>, <see cref="GiveReference"/>, <see cref="TakeText"/>, <see cref="TakeTextReference"/>), and runs its body.
    /// </summary>
    /// <param name="session">The session the call is made in.</param>
    /// <param name="stack">The session's call stack.</param>
    /// <param name="procedure">The procedure called.</param>
    /// <param name="list">The procedure's lowered body.</param>
    /// <param name="executor">The interpreter the activation is handed to.</param>
    /// <param name="body">The procedure's compiled body.</param>
    /// <param name="invoker">The invoker that makes the call.</param>
    /// <param name="made">Whether the activation was made for the call, rather than begun again.</param>
    /// <returns><see langword="null"/> when the call stack is full.</returns>
    internal static CompiledActivation? EnterThroughInvoker(IRuntimeSession session, RuntimeCallStack stack, VBTypeMemberSymbol procedure, InstructionList list,
        ProcedureExecutor executor, CompiledBody body, RuntimeProcedureInvoker invoker, out bool made)
    {
        CompiledActivation activation;
        if (stack.TakeSpare() is CompiledActivation spare && ReferenceEquals(spare._procedure, procedure) && ReferenceEquals(spare._list, list)
            && ReferenceEquals(spare._body, body) && ReferenceEquals(spare._invoker, invoker))
        {
            spare._frame = null;
            spare.Begin();
            (activation, made) = (spare, false);
        }
        else
        {
            (activation, made) = (new CompiledActivation(session, list, executor, new RuntimeEvaluationContext(procedure.Uri), body, invoker, procedure), true);
        }

        return stack.TryPushLight(activation) ? activation : null;
    }

    /// <summary>
    /// Gives the scalar parameter passed by value in <paramref name="slot"/> of an activation the invoker made without a frame
    /// (<see cref="EnterThroughInvoker"/>) its argument, a payload of the parameter's type, which compiled code reads as it begins.
    /// </summary>
    /// <param name="slot">The parameter's slot.</param>
    /// <param name="payload">The argument.</param>
    internal void GivePayload(int slot, ScalarPayload payload)
    {
        if (_givenCount == _given.Length)
        {
            Array.Resize(ref _given, Math.Max(4, _given.Length * 2));
            Array.Resize(ref _givenSlots, _given.Length);
        }

        (_givenSlots[_givenCount], _given[_givenCount]) = (slot, payload);
        _givenCount++;
    }

    /// <summary>
    /// Gives the scalar parameter passed by reference in <paramref name="slot"/> of an activation the invoker made without a frame
    /// (<see cref="EnterThroughInvoker"/>) the cell of its argument (<see cref="TakeReference"/>): the caller's variable, or a cell of a value
    /// (<see cref="ScalarCell.IsGivenValue"/>).
    /// </summary>
    /// <param name="slot">The parameter's slot.</param>
    /// <param name="cell">The argument's cell.</param>
    internal void GiveReference(int slot, ScalarCell cell) => TakeReference(CellNumberOf(slot), cell);

    // the number of the cell of the slot (CompiledBody.CellSlots); -1 when the slot is held in none.
    private int CellNumberOf(int slot)
    {
        for (var number = 0; number < _body.CellSlots.Count; number++)
        {
            if (_body.CellSlots[number] == slot)
            {
                return number;
            }
        }

        return -1;
    }

    // the payload the invoker gave the parameter in slot, when compiled code has not read it yet: it is read once, as the activation begins.
    private bool TryTakeGiven(int slot, out ScalarPayload payload)
    {
        for (var i = 0; i < _givenCount; i++)
        {
            if (_givenSlots[i] == slot)
            {
                _givenSlots[i] = ~slot;
                payload = _given[i];
                return true;
            }
        }

        payload = default;
        return false;
    }

    /// <summary>
    /// The activation's frame, when it has one; <see langword="null"/> when it is on the stack without one.
    /// </summary>
    internal CallStackFrame? HeldFrame => _frame;

    // the frame given the String numbered number, when it has none in its slot: a parameter passed by reference given its caller's variable is a second name
    // for it (PushAlias), as a scalar's is; any other is the activation's own (PushText).
    private void HoldText(CallStackFrame frame, int number)
    {
        var slot = _body.TextSlots[number];
        if (_texts[number] is not { } cell || frame.IsSlotFilled(slot))
        {
            return;
        }

        var symbol = _body.SlotSymbols[slot];
        if (_textAliases[number])
        {
            frame.PushAlias(symbol, cell, slot);
        }
        else
        {
            frame.PushText(symbol, cell, slot);
        }
    }

    /// <summary>
    /// Gives the <c>String</c> parameter in <paramref name="slot"/> of a call compiled code makes directly (<see cref="EnterDirectly"/>) the text of its
    /// argument (MS-VBAL §5.3.1.11): a cell of the activation's own holding it, as the interpreter's callee makes one of a <c>String</c> given by value, or
    /// of a value given to a parameter passed by reference.
    /// </summary>
    /// <param name="slot">The parameter's slot (<see cref="CompiledCallee.TextSlots"/>).</param>
    /// <param name="text">The text given; <see langword="null"/> for <c>vbNullString</c>.</param>
    public void TakeText(int slot, string? text) => HoldGivenText(slot, new TextCell(new VBRuntimeValue<string>(text!)), alias: false);

    /// <summary>
    /// Gives the <c>String</c> parameter passed by reference in <paramref name="slot"/> of a call compiled code makes directly (<see cref="EnterDirectly"/>)
    /// the cell of the caller's variable, a <see cref="TextCell"/>, which the parameter is a second name for (MS-VBAL §5.3.1.11): what it is assigned is
    /// what the variable holds, and the variable has one address, whichever of its names is asked for it first.
    /// </summary>
    /// <param name="slot">The parameter's slot (<see cref="CompiledCallee.TextSlots"/>).</param>
    /// <param name="cell">The cell of the caller's variable.</param>
    public void TakeTextReference(int slot, object cell) => HoldGivenText(slot, (TextCell)cell, alias: true);

    /// <summary>
    /// Whether the cell of a <c>String</c> variable is one a call made directly gives a parameter passed by reference (<see cref="TakeTextReference"/>): a
    /// <see cref="TextCell"/>, which knows the address the variable has. A parameter bound to another cell (an element of an array) is given the address,
    /// through the invoker (<see cref="SetTextReference"/>).
    /// </summary>
    /// <param name="cell">The cell of the variable given.</param>
    public static bool IsTextCell(object? cell) => cell is TextCell;

    // the cell held for the parameter: a frame made as the call was made, under a debugger, is given it as the entry finds its Strings' cells, before
    // anything can see the frame (StringCellOf); any other when it is made (MakeFrame).
    private void HoldGivenText(int slot, TextCell cell, bool alias)
    {
        var number = Array.IndexOf(_body.TextSlots, slot);
        (_texts[number], _textAliases[number]) = (cell, alias);
    }

    // the frame given the cell numbered number, when it has none for its slot: a parameter passed by reference's is its caller's variable, which the frame
    // has a second name for (PushAlias), unless it was given a value (TakeReference); any other is the activation's own.
    private void Hold(CallStackFrame frame, int number)
    {
        var slot = _body.CellSlots[number];
        if (_cells[number] is not ScalarCell cell || frame.IsSlotFilled(slot))
        {
            return;
        }

        var symbol = _body.SlotSymbols[slot];
        if (symbol is VBParameterSymbol parameter && RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind) && !_ownsCell[number])
        {
            frame.PushAlias(symbol, cell, slot);
        }
        else
        {
            frame.PushCell(symbol, cell, slot);
        }
    }

    /// <summary>
    /// Holds <paramref name="cell"/>, the argument of a parameter passed by reference (its caller's variable, or a cell of its own that a value was
    /// copied into), as the cell numbered <paramref name="number"/>. For a variable, the frame has the parameter as a second name for it, now or when
    /// the frame is made. A cell of a value (<see cref="NewCell"/>) is the activation's own, as the cell the interpreter makes of a value given to such a
    /// parameter is its callee's: the frame lets go of it, and of the address it was given, as it lets go of its variables. A procedure the activation
    /// passes the parameter to by reference gets that cell as a variable of the activation's.
    /// </summary>
    /// <param name="number">The number of the cell.</param>
    /// <param name="cell">The argument's cell.</param>
    public void TakeReference(int number, object cell)
    {
        _ownsCell[number] = cell is ScalarCell { IsGivenValue: true };
        if (_ownsCell[number])
        {
            Unsafe.As<ScalarCell>(cell).IsGivenValue = false;
        }

        _cells[number] = cell;
        if (_frame is { } frame)
        {
            Hold(frame, number);
        }
    }

    /// <summary>
    /// The cell numbered <paramref name="number"/>, of a slot of an integral kind that is not a parameter passed by reference: the frame's, when it holds
    /// one for it - the cell an interpreted caller gave a parameter, a variable it declared - and one holding <paramref name="value"/> otherwise, which the
    /// frame is given.
    /// </summary>
    /// <returns><see langword="null"/> when the frame holds the variable in no cell of its kind: the activation is then the interpreter's.</returns>
    public object? CellOfIntegral(int number, long value) => CellOf(number, value, 0);

    /// <summary>The cell numbered <paramref name="number"/>, of a slot of a floating-point kind (<see cref="CellOfIntegral"/>).</summary>
    public object? CellOfFloating(int number, double value) => CellOf(number, 0, value);

    private ScalarCell? CellOf(int number, long integral, double floating)
    {
        var slot = _body.CellSlots[number];
        var type = VBEnumType.AsDeclaredType(((ITypedSymbol)_body.SlotSymbols[slot]).ResolvedType);
        if (_frame is { } frame && frame.IsSlotFilled(slot))
        {
            var held = frame.TryGetSlotCell(slot, out var cell) && cell is ScalarCell scalar && scalar.Type.Equals(type) ? scalar : null;
            _cells[number] = held;
            return held;
        }

        var made = ScalarCell.Of(new ScalarPayload(type, integral, floating));
        TakeReference(number, made);
        return made;
    }

    /// <summary>
    /// The cell of the parameter passed by reference in <paramref name="slot"/>, as an interpreted caller gave it: its variable, or a cell its value was
    /// copied into: the frame's, or the one the invoker gave an activation without a frame (<see cref="GiveReference"/>).
    /// </summary>
    /// <returns><see langword="false"/> when the parameter is bound to an address - a field, an element - which is the interpreter's to read.</returns>
    public bool TryReadReference(int slot, out object? cell)
    {
        if (_frame is null && CellNumberOf(slot) is var number and >= 0 && _cells[number] is ScalarCell given)
        {
            cell = given;
            return true;
        }

        cell = Frame.TryGetSlotCell(slot, out var held) && held is ScalarCell scalar ? scalar : null;
        return cell is not null;
    }

    /// <summary>The payload of a cell of an integral kind, when it holds one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadIntegral(object cell, out long value)
    {
        var read = Unsafe.As<ScalarCell>(cell).TryRead(out var payload);
        value = payload.Integral;
        return read;
    }

    /// <summary>The payload of a cell of a floating-point kind, when it holds one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadFloating(object cell, out double value)
    {
        var read = Unsafe.As<ScalarCell>(cell).TryRead(out var payload);
        value = payload.Floating;
        return read;
    }

    /// <summary>Writes a payload of the cell's integral kind into it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteIntegral(object cell, long value)
    {
        var scalar = Unsafe.As<ScalarCell>(cell);
        scalar.Write(new ScalarPayload(scalar.Type, value, 0));
    }

    /// <summary>Writes a payload of the cell's floating-point kind into it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteFloating(object cell, double value)
    {
        var scalar = Unsafe.As<ScalarCell>(cell);
        scalar.Write(new ScalarPayload(scalar.Type, 0, value));
    }

    /// <summary>
    /// A cell of its own for the argument of a parameter passed by reference that is no variable, holding its value (MS-VBAL §5.3.1.11), and marked
    /// as the cell of a value (<see cref="ScalarCell.IsGivenValue"/>).
    /// </summary>
    /// <param name="kind">The value's scalar kind.</param>
    /// <param name="integral">The payload's <see cref="ScalarPayload.Integral"/>.</param>
    /// <param name="floating">The payload's <see cref="ScalarPayload.Floating"/>.</param>
    /// <returns>The cell.</returns>
    public static object NewCell(ScalarKind kind, long integral, double floating)
    {
        var cell = ScalarCell.Of(new ScalarPayload(ScalarKinds.TypeOf(kind), integral, floating));
        cell.IsGivenValue = true;
        return cell;
    }

    void ILightActivation.HeldIn(CallStackFrame frame)
    {
        _frame = frame;
        _body.CountFrameMade();
    }

    // the activation's frame, made now when it has none: the frames of every activation below it on the stack that has none are made with it, first.
    private CallStackFrame Frame
    {
        get
        {
            if (_frame is null)
            {
                _stack!.MaterializeLights();
            }

            return _frame!;
        }
    }

    // the instruction the activation is at, which its frame says, or says once it has one.
    private void SetPc(int pc)
    {
        if (_frame is { } frame)
        {
            frame.Pc = pc;
        }
        else
        {
            (_pc, _call, _library) = (pc, -1, -1);
        }
    }

    // the call numbered callee the activation makes at the instruction at pc, which its frame locates it at while the callee runs (CallStackFrame.EnterCall),
    // as the interpreter's does: a frame is told it now, one the activation is given later is told it then (MakeFrame). The call is not taken back when it
    // returns: the frame says the activation is at another instruction before anything can be called on top of it again (SetPc, Return, a hand-over to
    // the interpreter), and a call is the frame's only at the instruction that made it.
    private void SetCall(int pc, int callee)
    {
        if (_frame is { } frame)
        {
            frame.Pc = pc;
            _ = frame.EnterCall(_body.Callees[callee].CallLocation);
        }
        else
        {
            (_pc, _call, _library) = (pc, callee, -1);
        }
    }

    // the call of the function of the standard library numbered library, made at the instruction at pc, located at as SetCall locates a call.
    private void SetLibraryCall(int pc, int library)
    {
        if (_frame is { } frame)
        {
            frame.Pc = pc;
            _ = frame.EnterCall(_body.Libraries[library].Site);
        }
        else
        {
            (_pc, _call, _library) = (pc, -1, library);
        }
    }

    // the function result variable, as the activation's frame holds it, or as the activation does while it has none.
    private bool TryReadResult(out ScalarPayload payload)
    {
        if (_frame is { } frame)
        {
            return frame.TryReadResult(out payload);
        }

        payload = _result;
        return _hasResult;
    }

    private void AssignResult(ScalarPayload payload)
    {
        if (_frame is { } frame)
        {
            frame.AssignResult(payload);
            return;
        }

        (_result, _hasResult) = (payload, true);
    }

    // how many polls go by before the next looks: a program is stopped within a few microseconds all the same.
    private const int PollInterval = 256;

    private int _untilPoll = PollInterval;

    /// <summary>
    /// Whether the program is to stop, or the interpreter is wanted (<see cref="ISessionHalt.WantsInterpreter"/>): the activation is then handed to the
    /// interpreter, which stops it, or runs it under the debugger. Polled where the interpreter polls cancellation - at the back edges of loops - which is
    /// between two instructions; one poll in <c>256</c> looks.
    /// </summary>
    public bool ShouldWalkBack
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => --_untilPoll <= 0 && IsStopping();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool IsStopping()
    {
        _untilPoll = PollInterval;
        _body.NoteLook();

        // a cancellation the program was resumed from is not asked about again: the interpreter would run the activation on without breaking.
        return CompiledExecution.WalksBackAtRandom || _executor.IsCancellationPending || _halt.WantsInterpreter;
    }

    // the arguments of the call compiled code is making, as it gives them: each a payload's long and double, of the kind of its parameter.
    private (long Integral, double Floating)[] _arguments = [];

    // what the last call that did not return did: the error it raised, or that the program was stopped in it; none when the callee was not one compiled code
    // calls, and the interpreter makes the call.
    private RuntimeExecutionOutcome? _failed;

    // a variable compiled code could not write into its frame (Spill): the frame has the activation as compiled code does not, and the interpreter goes on with it.
    private bool _unspilled;

    private int _suspensionsBeforeCall;

    /// <summary>Gives the argument at <paramref name="index"/> of the call compiled code is about to make.</summary>
    public void SetArgument(int index, long integral, double floating)
    {
        if (index >= _arguments.Length)
        {
            Array.Resize(ref _arguments, Math.Max(index + 1, _arguments.Length * 2));
        }

        _arguments[index] = (integral, floating);
    }

    // the cells of the arguments of the call compiled code is making that are passed by reference, by index; none for one passed by value.
    private object?[] _references = [];

    /// <summary>
    /// Gives the argument at <paramref name="index"/> of the call compiled code is about to make, of a parameter passed by reference: the cell of the variable
    /// given. A value given to such a parameter is given with <see cref="SetArgument"/>.
    /// </summary>
    public void SetReference(int index, object cell)
    {
        if (index >= _references.Length)
        {
            Array.Resize(ref _references, Math.Max(index + 1, _references.Length * 2));
        }

        _references[index] = cell;
    }

    /// <summary>
    /// Gives the argument at <paramref name="index"/> of the call compiled code is about to make, of a <c>String</c> parameter given Strings: the text they
    /// are (<see langword="null"/> for <c>vbNullString</c>), a <c>String</c> as the interpreter passes one by value. A <c>String</c> variable given by
    /// reference is given with <see cref="SetTextReference"/>.
    /// </summary>
    /// <param name="index">The argument's position in the callee's parameter list.</param>
    /// <param name="text">The text given; <see langword="null"/> for <c>vbNullString</c>.</param>
    public void SetText(int index, string? text) => SetReference(index, new VBRuntimeValue<string>(text!));

    /// <summary>
    /// Gives the argument at <paramref name="index"/> of the call compiled code is about to make, of an optional <c>Variant</c> parameter the call leaves
    /// out: what the interpreter gives it (<c>RuntimeExpressionEvaluator.OmittedArgument</c>, MS-VBAL §5.3.1.11), the default the parameter declares or
    /// <c>Missing</c>. The parameter is the one of the member the call invokes, which <see cref="CanCallMember"/> found for a call of a member of an object.
    /// </summary>
    /// <param name="index">The argument's position in the callee's parameter list, after its <c>Me</c>.</param>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <returns>
    /// Whether the argument was given; <see langword="false"/> when making the default raises an error, which the interpreter raises as it makes the call.
    /// </returns>
    public bool SetOmitted(int index, int callee)
    {
        var called = _body.Callees[callee];
        var parameters = RuntimeProcedureInvoker.GetParameters(called.Site is not null || called.CallsOwnMember ? _member! : called.Procedure);
        var first = parameters is [{ Name: SDK.Model.Tokens.Me }, ..] ? 1 : 0;
        if (_executor.Expressions.OmittedArgument(_session, parameters[first + index]) is not { IsSuccess: true, Result: { } omitted })
        {
            return false;
        }

        SetReference(index, omitted.RuntimeValue);
        return true;
    }

    /// <summary>
    /// Gives the argument at <paramref name="index"/> of the call compiled code is about to make, of a <c>String</c> parameter passed by reference given
    /// a <c>String</c> variable of the activation: the variable's address, as the interpreter passes it (MS-VBAL §5.3.1.11). The variable has one address
    /// from then on, whichever name it is read by, so <c>VarPtr</c> of the parameter is <c>VarPtr</c> of the variable; its cell is bound at the address,
    /// and compiled code goes on reading and writing that cell.
    /// </summary>
    /// <param name="index">The argument's position in the callee's parameter list.</param>
    /// <param name="slot">The slot of the variable given.</param>
    /// <returns><see langword="false"/> when the variable cannot be given an address: the interpreter then makes the call.</returns>
    public bool SetTextReference(int index, int slot)
    {
        // a run that fails accesses at random fails this one too, so the recovery is taken. The interpreter makes the call: the arguments given for it
        // so far are let go of, as a call made lets go of them (ForgetReferences).
        if (FailsAccessAtRandom() || !Frame.TryGetAddress(_body.SlotSymbols[slot], out var address))
        {
            Array.Clear(_references);
            return false;
        }

        SetReference(index, new VBRuntimeReference(address));
        return true;
    }

    /// <summary>
    /// Whether compiled code writes its variables into its frame before a call: under a debugger, which shows the frame of a caller while the program waits
    /// in its callee.
    /// </summary>
    public bool SpillsBeforeCalls => _halt.Gate is not null;

    /// <summary>
    /// The session the activation runs in, which compiled code gives the typed form of a function of the standard library that reads it (its locale, its
    /// comparison rules).
    /// </summary>
    public IRuntimeSession Session => _session;

    /// <summary>
    /// Calls the procedure numbered <paramref name="callee"/> of the body with the arguments given (<see cref="SetArgument"/>, <see cref="SetReference"/>), as
    /// the interpreter calls a procedure every parameter of which is a scalar passed by value - or, when any is passed by reference, with the cell of each
    /// variable given to one, which the parameter is a second name for (<see cref="CellReference"/>; the address of a <c>String</c> variable,
    /// <see cref="SetTextReference"/>), and the value of anything else, of which the callee
    /// makes a cell of its own: the instruction at <paramref name="pc"/> is the one that makes
    /// the call, which the frame says while the callee runs.
    /// </summary>
    /// <returns>Whether the call returned: what it returned is then a payload of its kind. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>).</returns>
    public bool Call(int pc, int callee, out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        SetCall(pc, callee);
        _failed = null;
        _body.CountIndirectCall();
        var called = _body.Callees[callee];
        if (_executor.Expressions.ScalarInvokerOf(called.Procedure) is not { } invoker)
        {
            ForgetReferences(called);
            return false;
        }

        _suspensionsBeforeCall = Suspensions;
        var result = called.TakesReferences ? invoker.Invoke(called.Procedure, _session.Symbols.Resolver, ArgumentsOf(called, 0)) : invoker.InvokeScalars(called.Procedure, PayloadsOf(called));
        return Returned(called, result, out integral, out floating);
    }

    // the member a call of a member of an object invokes, and the object it invokes it on: found before the call (CanCallMember), and
    // let go of as it is made (CallMember).
    private VBTypeMemberSymbol? _member;
    private IRuntimeValue? _receiver;
    private IObjectInstance? _memberInstance;

    /// <summary>
    /// Whether compiled code makes the call numbered <paramref name="callee"/> of the body, of a member of the object the variable whose cell is
    /// <paramref name="cell"/> refers to: the member the object's class invokes for it is found as the interpreter finds it
    /// (<c>EvaluateBoundMember</c>), and is one compiled code calls as the member it was bound to is called: its parameters after <c>Me</c> of the same
    /// kinds, passed the same way, and what it returns of the same kind. A member of the class whose code runs, named bare, is called on <c>Me</c> as
    /// itself (<see cref="CompiledCallee.CallsOwnMember"/>), as the interpreter calls it. Not when the variable holds no reference compiled code reads, holds
    /// <c>Nothing</c> (error 91, which the interpreter raises), or refers to an object the session has no instance of, or whose class invokes another kind of
    /// member: the interpreter makes the call.
    /// </summary>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <param name="cell">The cell of the object variable the member is called on.</param>
    public bool CanCallMember(int callee, object? cell)
    {
        (_member, _receiver, _memberInstance) = (null, null, null);
        var called = _body.Callees[callee];
        if (_executor.Observation is not null || !ObjectOperations.TryRead(cell, out var instance) || instance.Equals(default(VBRuntimeObjectId))
            || !TryGetInstance(instance, out var target)
            || MemberOf(called, target) is not { } member || !Takes(called, member))
        {
            return false;
        }

        (_member, _receiver, _memberInstance) = (member, Unsafe.As<ValueBindingHandle>(cell!).Value, target);
        return true;
    }

    // the instance of the object a member is called on: the one the activation is on when it is called on Me, which the activation holds, as it holds Me.
    private bool TryGetInstance(VBRuntimeObjectId instance, [NotNullWhen(true)] out IObjectInstance? target)
    {
        if (_instance is { } me && me.ObjectId.Equals(instance))
        {
            target = me;
            return true;
        }

        return _session.Symbols.TryGetInstance(instance, out target);
    }

    // the member an object of actual invokes for a call: as the interpreter finds it for an expression (RuntimeExpressionEvaluator.MemberOf), or, for a
    // Property Let or Set, as an assignment finds it (ResolveAssignableMember): the accessor itself, of an object of the class the call was bound to, and the
    // one implementing it, of a class that implements that class as an interface. A member of the class whose code runs, named bare, has no site: it is
    // called on Me, which is of that class, and whose class is not looked up. What an object of a class invokes is the same at every call, so the call keeps
    // the last class's (Invoked).
    private VBTypeMemberSymbol? MemberOf(CompiledCallee called, IObjectInstance target)
    {
        // the default member of an object of the class it is declared as, and the member implementing it of an object of a class that implements that class
        // as an interface (MS-VBAL §5.3.1.9), as the interpreter finds it (RuntimeExpressionEvaluator.TryResolveDefaultMember). One of any other class is the
        // interpreter's to resolve.
        if (called.DefaultOf is { } declared)
        {
            var implementing = target.ClassModule;
            return ReferenceEquals(implementing, declared) ? called.Procedure
                : implementing.ImplementedInterfaces.Any(implemented => implemented.Uri.AbsoluteUri == declared.Uri.AbsoluteUri)
                    ? implementing.FindImplementation(declared, called.Procedure)
                    : null;
        }

        if (called.Site is null)
        {
            return called.Procedure;
        }

        var actual = target.ClassModule;
        if (called.Invoked is { } invoked && ReferenceEquals(invoked.Class, actual))
        {
            return invoked.Member;
        }

        if (InvokedOf(called, called.Site, actual) is not { } member)
        {
            return null;
        }

        called.Invoked = new InvokedMember(actual, member);
        return member;
    }

    private VBTypeMemberSymbol? InvokedOf(CompiledCallee called, LanguageCore.Binding.BoundMember site, VBClassModuleSymbol actual)
    {
        if (called.Procedure is not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol) || site.DeclaredClass is not { } declared)
        {
            return _executor.Expressions.MemberOf(site, actual);
        }

        return ReferenceEquals(actual, declared) ? called.Procedure
            : actual.ImplementedInterfaces.Any(implemented => implemented.Uri.AbsoluteUri == declared.Uri.AbsoluteUri) ? actual.FindImplementation(declared, called.Procedure)
            : null;
    }

    // whether a member is called as the member a call was bound to: itself, or one whose parameters after Me, and what it returns, are of the same kinds. The
    // last one found to be is remembered, as an object of one class nearly always is what a call is made on.
    private static bool Takes(CompiledCallee called, VBTypeMemberSymbol member)
    {
        if (ReferenceEquals(member, called.Procedure) || ReferenceEquals(member, called.Implementation))
        {
            return true;
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(member);
        var returnedType = ProcedureAnalysis.ReturnedType(member);
        var returned = ScalarKinds.Of(returnedType);
        if (member is not (VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) || parameters is not [{ Name: SDK.Model.Tokens.Me }, ..]
            || parameters.Length - 1 != called.Parameters.Count
            || member is VBPropertyGetMemberSymbol && called.Result is null && !called.AssignsResult && !called.ReturnsObject && !called.ReturnsText
                && !called.ReturnsValue
            || (called.ReturnsObject ? returnedType is not (SDK.Model.Types.VBObjectType or VBClassType)
                : called.ReturnsText ? !ProcedureAnalysis.IsText(returnedType)
                : called.ReturnsValue ? returnedType is not SDK.Model.Types.VBVariantType && !ProcedureAnalysis.IsText(returnedType)
                : called.AssignsResult ? !ProcedureAnalysis.ReturnsAssignable(member)
                : called.Result is { } read ? returned != read
                : returnedType is { } discarded && returned is null && discarded is not SDK.Model.Types.VBVariantType))
        {
            return false;
        }

        for (var i = 0; i < called.Parameters.Count; i++)
        {
            var parameter = parameters[i + 1];
            var omitted = i < called.Omitted.Count && called.Omitted[i];
            var passed = i < called.ObjectParameters.Count ? called.ObjectParameters[i] : null;
            var variant = i < called.VariantReferences.Count && called.VariantReferences[i];
            var text = i < called.TextSlots.Count && called.TextSlots[i] >= 0;
            // an object parameter is one of the member's too, of the very type, which what it is given is Set-coerced to (SetObject). A Variant the call
            // leaves out is one of the member's too, given what the member's parameter is given (SetOmitted), and so is one given a variable's address, passed
            // by reference as the call's is (SetVariantReference). A String of variable length is one of the member's too, given the text or the variable's
            // address (SetText, SetTextReference). A scalar the call leaves out is given the default the member it was bound to declares, which is this
            // member's: an implemented member declares the defaults of the interface member (MS-VBAL §5.3.1.9), and one that does not is refused before
            // the program runs.
            if (parameter is ParamArrayParameterSymbol || ScalarKinds.Of(parameter.ResolvedType) != called.Parameters[i]
                || RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind) != called.ByReference[i]
                || called.Parameters[i] is null && (passed is not null ? !passed.Type.Equals(parameter.ResolvedType)
                    : text ? !ProcedureAnalysis.IsText(parameter.ResolvedType)
                    : !(omitted || variant) || parameter.ResolvedType is not SDK.Model.Types.VBVariantType))
            {
                return false;
            }
        }

        called.Implementation = member;
        return true;
    }

    /// <summary>
    /// Calls the member <see cref="CanCallMember"/> found, on the object it found, with the arguments given (<see cref="SetArgument"/>,
    /// <see cref="SetReference"/>), as the interpreter calls a member of an object: through its binding, the object given as <c>Me</c>. The instruction at
    /// <paramref name="pc"/> is the one that makes the call, which the frame says while the member runs.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <param name="integral">What the member returned, when it is of an integral kind.</param>
    /// <param name="floating">What the member returned, when it is of a floating-point kind.</param>
    /// <returns>Whether the call returned, as <see cref="Call"/> says.</returns>
    public bool CallMember(int pc, int callee, out long integral, out double floating)
    {
        SetCall(pc, callee);
        _failed = null;
        _body.CountIndirectCall();
        var called = _body.Callees[callee];
        var (member, receiver) = (_member!, _receiver!);
        (_member, _receiver, _memberInstance) = (null, null, null);

        _suspensionsBeforeCall = Suspensions;
        var arguments = ArgumentsOf(called, 1);
        arguments[0] = receiver;
        var expressions = _executor.Expressions;
        var result = expressions.Bindings is { } bindings
            ? bindings.ForMember(member).Call(_session.Symbols.Resolver, arguments)
            : expressions.ProcedureInvoker is { } invoker ? invoker.Invoke(member, _session.Symbols.Resolver, arguments) : RuntimeSemanticsEvaluationResult.InternalError();
        return Returned(called, result, out integral, out floating);
    }

    // what a call returned: a payload of the callee's kind, or why it did not return.
    private bool Returned(CompiledCallee called, RuntimeSemanticsEvaluationResult result, out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        if (!result.IsSuccess)
        {
            _failed = result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
            EndStatementAfter(called, _failed.Value);
            return false;
        }

        // an object, a String or a Variant, which the statement's temporaries hold (RuntimeProcedureInvoker.ReleaseLocals): compiled code takes it
        // (TakeReturnedObject, TakeReturnedValue).
        if (called.ReturnsObject || called.ReturnsValue)
        {
            _returnedObject = result.Result;
            _failed = result.Result is null ? RuntimeExecutionOutcome.InternalError : null;
            return result.Result is not null;
        }

        // a String, which compiled code takes whole (TakeReturnedText).
        if (called.ReturnsText)
        {
            _returnedText = result.Result;
            _failed = result.Result is null ? RuntimeExecutionOutcome.InternalError : null;
            return result.Result is not null;
        }

        if (called.Result is null)
        {
            return AssignReturned(called, result.Result);
        }

        // a function of a scalar type returns a known value of it.
        if (result.Result is not { } value || !ScalarPayload.TryOf(value.TypeInfo, value.RuntimeValue, out var payload))
        {
            _failed = RuntimeExecutionOutcome.InternalError;
            return false;
        }

        (integral, floating) = (payload.Integral, payload.Floating);
        return true;
    }

    private ScalarPayload[] PayloadsOf(CompiledCallee called)
    {
        var arguments = new ScalarPayload[called.Parameters.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = new ScalarPayload(ScalarKinds.TypeOf(called.Parameters[i]!.Value), _arguments[i].Integral, _arguments[i].Floating);
        }

        return arguments;
    }

    // the arguments as the interpreter passes them (RuntimeExpressionEvaluator.BindArguments): the cell of a variable, or a value of the parameter's type -
    // of which the callee makes a cell of its own when the parameter is passed by reference. They come after the first ones, which are the caller's to give:
    // the object a member is called on. A String is given the text (SetText), or the address of the String variable given (SetTextReference). A Variant
    // passed by reference given a variable of a scalar type is given its cell and the type the variable is declared as: the parameter reads the variable
    // as a Variant of that type, and what it is assigned is let-coerced to that type (MS-VBAL §5.3.1.11).
    private IRuntimeValue[] ArgumentsOf(CompiledCallee called, int first)
    {
        var arguments = new IRuntimeValue[first + called.Parameters.Count];
        for (var i = 0; i < called.Parameters.Count; i++)
        {
            if (called.Parameters[i] is not { } kind)
            {
                arguments[first + i] = _references[i] is ScalarCell scalar ? new CellReference(scalar, scalar.Type) : (IRuntimeValue)_references[i]!;
                continue;
            }

            arguments[first + i] = i < _references.Length && _references[i] is { } cell
                ? new CellReference(Unsafe.As<ScalarCell>(cell))
                : new ScalarPayload(ScalarKinds.TypeOf(kind), _arguments[i].Integral, _arguments[i].Floating).Box();
        }

        ForgetReferences(called);
        return arguments;
    }

    // the cells given for a call are let go of once it is made, or once the interpreter is to make it: the next call is given its own.
    private void ForgetReferences(CompiledCallee called)
    {
        if (called.TakesReferences)
        {
            Array.Clear(_references, 0, Math.Min(called.Parameters.Count, _references.Length));
        }
    }

    /// <summary>
    /// The activation of the procedure numbered <paramref name="callee"/> of the body, a compiled one, which compiled code is about to call through its typed
    /// entry (<see cref="DirectCall"/>), with the arguments as it holds them: the instruction at <paramref name="pc"/> is the one that makes the call, which the
    /// frame says while the callee runs. The call is the invoker's (<see cref="Call"/>) when the callee is not called this way, or not now: the session runs
    /// no compiled code any more (its engine was changed); the session's symbols were defined again since this activation began, or the callee's body was
    /// loaded again, either of which the body it was compiled against may no longer be; the interpreter is wanted (<see cref="ISessionHalt.WantsInterpreter"/>);
    /// the session is being analyzed; a break was asked for and is not answered, which the invoker's call has the interpreter answer before the callee's
    /// first instruction; or the call stack has no room for another frame.
    /// </summary>
    /// <returns>The callee's activation, which its entry is called with; <see langword="null"/> when the call is to be made through the invoker.</returns>
    public CompiledActivation? EnterDirectly(int pc, int callee)
    {
        if (_body.Directs[callee] is not { Body: { } body } direct || !MayEnterDirectly() || !IsCurrent(direct))
        {
            return null;
        }

        SetCall(pc, callee);
        _failed = null;

        // under a debugger, which may look at the stack while the program waits in it, the callee's frame is made as it is called; any other is on the stack
        // without one until something is to see it, in the activation that last left its place of the stack, begun again, when that
        // was an activation of the same procedure: a loop of calls is made in one, a recursion in as many as it goes deep.
        CompiledActivation activation;
        if (_halt.Gate is not null || _stack is null)
        {
            if (_invoker.PushCompiledActivation(direct.Procedure, direct.List) is not { } frame)
            {
                return null;
            }

            activation = new CompiledActivation(_session, frame, direct.List, _executor, direct.Context, body, _invoker);
        }
        else
        {
            // the context the callee is evaluated in is its procedure's (DirectCall.Context).
            if (_stack.TakeSpare() is CompiledActivation spare && ReferenceEquals(spare._procedure, direct.Procedure) && ReferenceEquals(spare._list, direct.List)
                && ReferenceEquals(spare._body, body) && ReferenceEquals(spare._invoker, _invoker))
            {
                spare._frame = null;
                spare.Begin(_scopes);
                activation = spare;
            }
            else
            {
                activation = new CompiledActivation(_session, direct.List, _executor, direct.Context, body, _invoker, direct.Procedure);
                body.CountActivationMade();
            }

            if (!_stack.TryPushLight(activation))
            {
                return null;
            }
        }

        activation.EnteredBy(this);
        _body.CountDirectCall();
        _suspensionsBeforeCall = Suspensions;
        return activation;
    }

    // the member of a class the activation is of, entered directly on an object (EnterMemberDirectly): its end lets go of Me and of what its locals hold.
    private VBTypeMemberSymbol? _entered;

    // the typed entry of the member EnterMemberDirectly entered last, which compiled code calls next.
    private nint _memberEntry;

    /// <summary>
    /// The activation of the member <see cref="CanCallMember"/> found, which compiled code is about to call through its typed entry (<see cref="MemberEntry"/>)
    /// with the arguments as it holds them, on the object it found, the object its <c>Me</c>: on the stack without a frame, as
    /// <see cref="EnterDirectly"/> makes one, when the frame would hold nothing compiled code does not but <c>Me</c> (<see cref="CompiledBody.EntersWithoutFrame"/>)
    /// and no debugger is to see it (the activation holds <c>Me</c>, and its frame, once something is to see it, is the one the invoker would have made); with
    /// its frame otherwise (<c>RuntimeProcedureInvoker.PushMemberActivation</c>). The instruction at <paramref name="pc"/> is the one that makes the call,
    /// which the frame says while the member runs. The call is made through its binding (<see cref="CallMember"/>) when it is not made this way, or not now, for the reasons
    /// <see cref="EnterDirectly"/> gives; or when the member is not compiled, or is not the workspace's to run.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <returns>The member's activation, which its entry is called with; <see langword="null"/> when the call is to be made through its binding.</returns>
    public CompiledActivation? EnterMemberDirectly(int pc, int callee)
    {
        if (_member is not { } member || _receiver is not VBRuntimeValue<VBRuntimeObjectId> || !MayEnterDirectly()
            || EntryOf(_body.Callees[callee], member) is not { } entry)
        {
            return null;
        }

        SetCall(pc, callee);
        _failed = null;
        CompiledActivation activation;
        if (entry.Body.EntersWithoutFrame && _halt.Gate is null && _stack is not null && UnboxedEvaluation.IsEnabled)
        {
            // a member whose frame would hold nothing compiled code does not but Me is on the stack without one, as a procedure called directly is: in the
            // activation that last left its place of the stack, begun again, when that was an activation of the same member. Me is held as the invoker
            // holds it, by a cell of the activation's own, for as long as the activation is: two activations of the member on the stack - a recursion, a
            // call on another object - each hold their own.
            if (_stack.TakeSpare() is CompiledActivation spare && ReferenceEquals(spare._procedure, member) && ReferenceEquals(spare._list, entry.List)
                && ReferenceEquals(spare._body, entry.Body) && ReferenceEquals(spare._invoker, _invoker))
            {
                spare._frame = null;
                spare.Begin(_scopes);
                activation = spare;
            }
            else
            {
                activation = new CompiledActivation(_session, entry.List, _executor, entry.Context, entry.Body, _invoker, member);
                entry.Body.CountActivationMade();
            }

            if (!_stack.TryPushLight(activation))
            {
                return null;
            }

            // the reference as the variable or the With block holds it, which the cell holds as it is: no copy of it is made. A spare holds it in the cell
            // its last activation let go of, when nothing else can hold that cell (EndMember).
            var me = activation._spareMe ?? new ValueBindingHandle(_receiver);
            if (activation._spareMe is not null)
            {
                me.Store(_receiver, isKnown: true);
                activation._spareMe = null;
            }

            (activation._entered, activation._instance, activation._me) = (member, _memberInstance, me);
            ObjectReferences.HoldIn(_session, me, _memberInstance!.ObjectId);
        }
        else if (_invoker.PushMemberActivation(member, entry.List, (VBRuntimeValue<VBRuntimeObjectId>)_receiver) is { } frame)
        {
            activation = new CompiledActivation(_session, frame, entry.List, _executor, entry.Context, entry.Body, _invoker) { _entered = member };
        }
        else
        {
            return null;
        }

        activation.EnteredBy(this);
        (_member, _receiver, _memberInstance, _memberEntry) = (null, null, null, entry.Entry);
        _body.CountDirectCall();
        _suspensionsBeforeCall = Suspensions;
        return activation;
    }

    // the object an activation of a member of a class entered without a frame is on (EnterMemberDirectly), and the cell it holds Me in: the frame it is given
    // later holds Me in that cell (MakeFrame), and the activation lets go of it as the frame would have, when it returns (ReturnedDirectly).
    private IObjectInstance? _instance;
    private ValueBindingHandle? _me;

    // the cell this activation held Me in the last time it ran a member, let go of when that member returned without a frame: no frame, and so nothing
    // but the activation, ever held it, and no object is counted in it. The next member this activation runs holds Me in it (EnterMemberDirectly).
    private ValueBindingHandle? _spareMe;

    /// <summary>
    /// The typed entry of the member <see cref="EnterMemberDirectly"/> entered, which compiled code calls with its activation and its arguments.
    /// </summary>
    public nint MemberEntry => _memberEntry;

    // the compiled member a call enters directly: the one it entered last, while its body is the one the invoker runs; else the member compiled now, when it
    // is the workspace's to run - its binding is this invoker's.
    private MemberEntry? EntryOf(CompiledCallee called, VBTypeMemberSymbol member)
    {
        if (called.Entered is { } entered && ReferenceEquals(entered.Member, member) && IsCurrent(entered))
        {
            return entered;
        }

        var expressions = _executor.Expressions;
        var invoker = expressions.Bindings is { } bindings
            ? bindings.ForMember(member) is CallableBindingHandle { Receiver: null } handle ? handle.Invoker : null
            : expressions.ProcedureInvoker;
        if (!ReferenceEquals(invoker, _invoker) || _invoker.BodyOf(member) is not { } list
            || _invoker.CompiledBodyOf(member, list) is not { Entry: { } method } body)
        {
            return null;
        }

        entered = new MemberEntry(member, list, body, method.MethodHandle.GetFunctionPointer(), new RuntimeEvaluationContext(member.Uri)) { Image = _invoker.Image };
        called.Entered = entered;
        return entered;
    }

    /// <summary>
    /// Ends the activation <paramref name="callee"/> of the call numbered <paramref name="call"/> of the body, which compiled code made through the callee's
    /// typed entry (<see cref="EnterDirectly"/>, <see cref="EnterMemberDirectly"/>), and which returned <paramref name="outcome"/>: the frame off the stack,
    /// as the invoker takes it off.
    /// </summary>
    /// <param name="call">The number of the call among the body's callees.</param>
    /// <param name="callee">The activation the call entered.</param>
    /// <param name="outcome">How the callee's code ended.</param>
    /// <param name="integral">What the callee returned, when it is of an integral kind.</param>
    /// <param name="floating">What the callee returned, when it is of a floating-point kind.</param>
    /// <returns>
    /// Whether the call returned: what it returned is then a payload of its kind, when what it returns is read. When not, the interpreter is given the call
    /// (<see cref="AfterFailedCall"/>), with the error the callee raised, or with the program stopped in it.
    /// </returns>
    public bool ReturnedDirectly(int call, CompiledActivation callee, RuntimeExecutionOutcome outcome, out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        outcome = callee._entered is not { } member ? EndProcedure(callee, outcome)
            : callee._me is { } me && callee._frame is null ? EndMember(callee, me, outcome)
            : _invoker.PopMemberActivation(callee._frame!, member, outcome);
        callee.LetGoOfMe();
        if (outcome.Kind is not RuntimeExecutionOutcomeKind.ExitProcedure)
        {
            // as the invoker answers it (InvokeScalars): an error is the call's, and a call that was stopped has no answer.
            _failed = outcome.Kind is RuntimeExecutionOutcomeKind.Error ? outcome : RuntimeExecutionOutcome.InternalError;
            EndStatementAfter(_body.Callees[call], outcome);
            return false;
        }

        // what the call returns is read only where the call reads it, as Call reads it: a Function called as a statement returns what is let go of, and one
        // whose statement assigns what it returns is assigned it. One that returns a String inside a text returns the frame's function result, which compiled
        // code takes whole (TakeReturnedText).
        var called = _body.Callees[call];

        // an object, which the statement's temporaries hold since the callee returned (LetGoOfResult, RuntimeProcedureInvoker.ReleaseLocals): compiled
        // code takes it (TakeReturnedObject), as it takes what a call through the invoker returns.
        if (called.ReturnsObject)
        {
            _returnedObject = ReturnedBy(callee);
            _failed = _returnedObject is null ? RuntimeExecutionOutcome.InternalError : null;
            return _returnedObject is not null;
        }

        if (called.ReturnsText)
        {
            _returnedText = ReturnedBy(callee);
            if (_returnedText is null)
            {
                _failed = RuntimeExecutionOutcome.InternalError;
                return false;
            }

            return true;
        }

        if (called.Result is null)
        {
            return AssignReturned(called, called.AssignsResult ? ReturnedBy(callee) : null);
        }

        // a function of a scalar type returns a payload of it: its compiled code assigned it when it returned, and so did the interpreter it was handed to.
        if (!callee.TryReadResult(out var payload))
        {
            _failed = RuntimeExecutionOutcome.InternalError;
            return false;
        }

        (integral, floating) = (payload.Integral, payload.Floating);
        return true;
    }

    // the end of an activation of a member entered without a frame, which still has none (EnterMemberDirectly): Me let go of as the invoker lets go of it
    // (RuntimeProcedureInvoker.PopMemberActivation) - unless the program was ended in it - while the activation is still on the stack. A Class_Terminate that
    // runs then gives it its frame, as any call does; it is then taken off the stack with that frame, which holds Me in the same cell, let go of already.
    private RuntimeExecutionOutcome EndMember(CompiledActivation callee, ValueBindingHandle me, RuntimeExecutionOutcome outcome)
    {
        // what its variables hold is let go of before Me, as the frame lets go of them (RuntimeProcedureInvoker.ReleaseLocals).
        var released = outcome.Kind is not (RuntimeExecutionOutcomeKind.Halt or RuntimeExecutionOutcomeKind.Break);
        if (released)
        {
            callee.LetGoOfResult();
            callee.LetGoOfObjects(me);
        }

        outcome = _invoker.PopCompiledActivation(outcome, light: callee._frame is null);

        // a cell that was let go of and given to no frame, not even by a Class_Terminate run as it was let go of, is the next activation's (_spareMe).
        if (released && callee._frame is null)
        {
            callee._spareMe = me;
        }

        return outcome;
    }

    // an activation off the stack is the spare of its place (RuntimeCallStack.TakeSpare) until it is begun again: it keeps no object alive meanwhile.
    private void LetGoOfMe() => (_instance, _me) = (null, null);

    /// <summary>
    /// Takes <paramref name="callee"/>, the activation of a call compiled code made through the callee's typed entry (<see cref="EnterDirectly"/>), off the
    /// stack when a CLR exception goes by it - a defect of the platform, which ends the program: the activations the callee made are off it already, each
    /// taken off as the exception went by it, and what is left is what the stack was before the call.
    /// </summary>
    public void AbandonDirectCall(CompiledActivation callee)
    {
        if (callee._frame is null)
        {
            _ = _stack?.TryPopLight(callee);
        }
        else if (ReferenceEquals(_session.CallStack.Current, callee._frame))
        {
            _ = _session.CallStack.TryPop(out _);
        }

        callee.LetGoOfMe();
        callee._objectsGiven = true;
    }

    private int Suspensions => (_halt as SessionHalt)?.Suspensions ?? 0;

    /// <summary>
    /// Whether the program waited while the last call ran: a debugger may then have changed the variables of the frame, which compiled code reads back.
    /// </summary>
    public bool WaitedDuringCall => Suspensions != _suspensionsBeforeCall;

    /// <summary>
    /// Whether compiled code reads its variables back after the call it made returned: the program waited during it (<see cref="WaitedDuringCall"/>). Under
    /// the random failed accesses (<see cref="CompiledExecution.FailsReloadsAtRandom"/>), the first read-back then fails, whatever the seed: every call that
    /// waited reaches the hand-over of a variable compiled code can't read back.
    /// </summary>
    public bool ReadsBackAfterCall
    {
        get
        {
            if (!WaitedDuringCall)
            {
                return false;
            }

            if (CompiledExecution.FailsReloadsAtRandom)
            {
                _untilFailedAccess = 1;
            }

            return true;
        }
    }

    // the symbols compiled code was compiled against: a procedure it calls, defined again since - its parameters retyped, passed another way, one added - is
    // another call than the one compiled.
    private ScopeTree _scopes = null!;

    // what says, as one number, whether anything a call made directly depends on changed: what the interpreter is wanted for, the session's symbols, and
    // the engine (CompiledExecution.EngineEpoch).
    private readonly SessionHalt? _sessionHalt;
    private readonly SymbolTable? _table;

    // the number (DirectEntryEpoch) at which the activation last found that it makes the calls compiled code makes directly, and goes on after them: its
    // symbols not defined again since it began, the interpreter not wanted, no analysis made, and the session's engine the compiled one. -1 when it has not,
    // or when the session's halt or symbols are not ones that number their changes. A call made directly is made at the same number (EnteredBy).
    private long _directAt = -1;

    // the numbers only grow, so their sum is the same only while none changed.
    private long DirectEntryEpoch
        => _sessionHalt is { } halt && _table is { } table ? (long)halt.Epoch + table.Version + CompiledExecution.EngineEpoch : -1;

    // whether calls are made directly now (EnterDirectly, EnterMemberDirectly): an error that cannot be trapped, or the run cancelled, is looked at each
    // time, since compiled code is to stop at once for either; the rest only when the number of what they depend on changed (DirectEntryEpoch), which it is
    // read before, so a change made while they are looked at is looked at again.
    private bool MayEnterDirectly()
    {
        if (_session.Errors.Untrappable is not null || _executor.IsCancellationPending)
        {
            return false;
        }

        var epoch = DirectEntryEpoch;
        if (epoch >= 0 && epoch == _directAt)
        {
            return true;
        }

        if (!ReferenceEquals(_session.Symbols.Scopes, _scopes) || _halt.WantsInterpreter || _executor.Observation is not null
            || CompiledExecution.EngineFor(_session.Environment.ExecutionEngine) is not VBExecutionEngine.Compiled)
        {
            return false;
        }

        _directAt = epoch;
        return true;
    }

    // an activation of a call this one made directly is made where this one found that it makes them: on the same session, by the same executor, at the
    // same number, with the symbols this one began with (Begin).
    private void EnteredBy(CompiledActivation caller) => _directAt = caller._directAt;

    // whether the body a call made directly enters is the one the invoker runs: its procedures as they are now, which a module loaded again replaces
    // (RuntimeProcedureInvoker.Image), are the ones it was found in.
    private bool IsCurrent(DirectCall direct)
    {
        if (_invoker.Image is { } image && ReferenceEquals(direct.Image, image))
        {
            return true;
        }

        if (!ReferenceEquals(_invoker.BodyOf(direct.Procedure), direct.List))
        {
            return false;
        }

        direct.Image = _invoker.Image;
        return true;
    }

    private bool IsCurrent(MemberEntry entered)
    {
        if (_invoker.Image is { } image && ReferenceEquals(entered.Image, image))
        {
            return true;
        }

        if (!ReferenceEquals(_invoker.BodyOf(entered.Member), entered.List))
        {
            return false;
        }

        entered.Image = _invoker.Image;
        return true;
    }

    /// <summary>
    /// Whether the activation is handed to the interpreter after the call it made returns: the interpreter is wanted (<see cref="ISessionHalt.WantsInterpreter"/>);
    /// the procedure was edited while the program waited in the callee, and the activation goes on in its edited body (<see cref="CallStackFrame.HasPendingRebase"/>);
    /// or the session's symbols were defined again while the callee ran - an edit made while the program waited - and what compiled code calls, and how,
    /// may not be what it was compiled for. The interpreter goes on with them as they are now, from the instruction after the call. So it does after a call, or
    /// an instruction it ran alone, that let go of an object whose <c>Terminate</c> left an error unhandled (<see cref="ISessionErrorState.Untrappable"/>):
    /// the program stops with it there, before the next instruction, as the interpreter stops it (<c>ProcedureExecutor.RunInstructions</c>). And so it does
    /// after one during which a break was asked for, which is not answered: the interpreter breaks there, before the next instruction, where compiled code
    /// would look only at the back edge of a loop.
    /// </summary>
    /// <remarks>
    /// An activation without a frame has no edit to go on in: the program waited in none, since what looks at the stack while it waits gives every activation
    /// its frame (<see cref="RuntimeCallStack.MaterializeLights"/>).
    /// </remarks>
    // the interpreter wanted and the symbols defined again are looked at only when the number of what they depend on changed since the activation last found
    // neither (MayEnterDirectly).
    public bool WantsInterpreterAfterCall => _session.Errors.Untrappable is not null || _frame is { HasPendingRebase: true } || _executor.IsCancellationPending
        || (DirectEntryEpoch is var epoch && (epoch < 0 || epoch != _directAt) && (_halt.WantsInterpreter || !ReferenceEquals(_session.Symbols.Scopes, _scopes)));

    /// <summary>
    /// Hands the activation to the interpreter at the call at <paramref name="pc"/> that did not return: with the error the callee raised, which the
    /// interpreter gives the activation's handler as it would have; with the program stopped in the callee, which stops this activation too; or, when compiled
    /// code could not make the call, to make it as it always does. The frame has every variable and every loop that was opened.
    /// </summary>
    public RuntimeExecutionOutcome AfterFailedCall(int pc)
    {
        _handedOver = true;
        var frame = Frame;
        frame.RunsCompiled = false;

        // the procedure edited while the program waited in the callee: the error is the call's, where the call now is.
        if (frame.ApplyPendingRebase(atTheCall: true))
        {
            pc = frame.Pc;
        }

        var list = Body;
        _body.WalkedBack(pc < list.Items.Length ? BreakpointTable.LocationOf(list.Items[pc]) : null);
        if (!HandKnowns(frame, list, pc, inside: _failed is null))
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (_failed is not { } outcome)
        {
            frame.Pc = pc;
            return _executor.Run(_session, frame, list, _context, HandStatement());
        }

        return _executor.RunAfter(_session, frame, list, _context, pc, outcome, HandStatement());
    }

    /// <summary>
    /// The payload of the variable in <paramref name="slot"/>, of an integral kind, when its cell holds one it knows. When it does not, compiled code hands the
    /// activation to the interpreter, which reads the variable where the frame has it. A parameter of an activation the invoker made without a frame is
    /// read, the first time, in the payload it was given (<see cref="GivePayload"/>).
    /// </summary>
    public bool TryReadIntegral(int slot, out long value)
    {
        if (_frame is null && TryTakeGiven(slot, out var given))
        {
            value = given.Integral;
            return true;
        }

        value = 0;
        if (!TryGetScalarCell(slot, out var scalar) || !scalar.TryRead(out var payload))
        {
            return false;
        }

        value = payload.Integral;
        return true;
    }

    /// <summary>
    /// The payload of the variable in <paramref name="slot"/>, of a floating-point kind, when its cell holds one it knows. When it does not, compiled code
    /// hands the activation to the interpreter.
    /// </summary>
    public bool TryReadFloating(int slot, out double value)
    {
        if (_frame is null && TryTakeGiven(slot, out var given))
        {
            value = given.Floating;
            return true;
        }

        value = 0;
        if (!TryGetScalarCell(slot, out var scalar) || !scalar.TryRead(out var payload))
        {
            return false;
        }

        value = payload.Floating;
        return true;
    }

    /// <summary>
    /// The payload of the variable in <paramref name="slot"/>, of an integral kind, read back after the interpreter ran an instruction or the program waited
    /// (<see cref="TryReadIntegral"/>).
    /// </summary>
    public bool TryReloadIntegral(int slot, out long value)
    {
        value = 0;
        return !FailsAccessAtRandom() && TryReadIntegral(slot, out value);
    }

    /// <summary>
    /// The payload of the variable in <paramref name="slot"/>, of a floating-point kind, read back after the interpreter ran an instruction or the program
    /// waited (<see cref="TryReadFloating"/>).
    /// </summary>
    public bool TryReloadFloating(int slot, out double value)
    {
        value = 0;
        return !FailsAccessAtRandom() && TryReadFloating(slot, out value);
    }

    // the cell of the variable in slot: the one its slot holds; or, once the variable was given an address - passed by reference to a Declare, its VarPtr
    // taken - the cell bound at the address, while that is a cell of the variable's type.
    private bool TryGetScalarCell(int slot, [NotNullWhen(true)] out ScalarCell? scalar)
    {
        var frame = Frame;
        if (frame.TryGetSlotCell(slot, out var cell))
        {
            scalar = cell as ScalarCell;
            return scalar is not null;
        }

        scalar = frame.TryGetAddressedSlot(slot, out var bound) && bound is ScalarCell held
            && _body.SlotSymbols.TryGetValue(slot, out var symbol) && symbol is ITypedSymbol { ResolvedType: var type } && held.Type.Equals(VBEnumType.AsDeclaredType(type))
            ? held
            : null;
        return scalar is not null;
    }

    /// <summary>The payload of the function result variable, of an integral kind, when it holds one: what a debugger may have assigned it while the program waited.</summary>
    public bool TryReadResultIntegral(out long value)
    {
        ScalarPayload payload = default;
        var read = !FailsAccessAtRandom() && TryReadResult(out payload);
        value = read ? payload.Integral : 0;
        return read;
    }

    /// <summary>The payload of the function result variable, of a floating-point kind, when it holds one.</summary>
    public bool TryReadResultFloating(out double value)
    {
        ScalarPayload payload = default;
        var read = !FailsAccessAtRandom() && TryReadResult(out payload);
        value = read ? payload.Floating : 0;
        return read;
    }

    /// <summary>
    /// The cell the frame holds the <c>Variant</c> parameter or variable in <paramref name="slot"/> in: what compiled code reads and
    /// writes it in, for as long as the interpreter runs nothing of the activation. A parameter passed by reference that is bound to its caller's variable's
    /// address (<c>CallStackFrame.PushByRef</c>) is in the cell held at that address: the caller's variable.
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <returns>
    /// <see langword="null"/> when the frame holds it in no Variant's cell (a Variant parameter passed an object, an array or a record by value, a variable of
    /// another type passed by reference, an element of an array): compiled code reads and writes nothing of it (<see cref="VariantOperations.Read"/>), and
    /// the interpreter runs each statement that does.
    /// </returns>
    public object? VariantCellOf(int slot)
    {
        var frame = Frame;
        return (frame.TryGetSlotCell(slot, out var cell) || frame.TryResolve(_body.SlotSymbols[slot], out cell)) && cell is VariantCell variant ? variant : null;
    }

    /// <summary>
    /// The cell of the <c>Variant</c> in <paramref name="slot"/> (<see cref="VariantCellOf"/>), found again after the interpreter ran an instruction or the
    /// program waited.
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <param name="cell">The cell found, as <see cref="VariantCellOf"/> finds it.</param>
    /// <returns><see langword="false"/> when the frame cannot be read: the activation is then the interpreter's, from where it is.</returns>
    public bool TryReloadVariantCell(int slot, out object? cell)
    {
        cell = null;
        if (FailsAccessAtRandom())
        {
            return false;
        }

        cell = VariantCellOf(slot);
        return true;
    }

    /// <summary>
    /// The cell the frame holds the object parameter or variable in <paramref name="slot"/> in: what compiled code reads the reference
    /// it holds from (<see cref="ObjectOperations"/>), for as long as the interpreter runs nothing of the activation. A parameter passed by reference that is
    /// bound to its caller's variable's address is in the cell held at that address, as a <c>Variant</c>'s is (<see cref="VariantCellOf"/>).
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <returns>
    /// <see langword="null"/> when the frame holds it in no cell of an object reference: compiled code reads nothing of it, and the interpreter runs each
    /// statement that does.
    /// </returns>
    public object? ObjectCellOf(int slot)
    {
        // Me, of a member entered without a frame: the cell the activation holds it in, which its frame is given when it has one (EnterMemberDirectly). Any
        // other object of an activation without a frame: the cell the activation holds it in (OwnObjectCell).
        if (_frame is null)
        {
            if (_me is { } me && slot == _body.MeSlot)
            {
                return me;
            }

            if (Array.IndexOf(_body.ObjectSlots, slot) is var own and >= 0)
            {
                return OwnObjectCell(own);
            }
        }

        var frame = Frame;
        if (frame.TryGetSlotCell(slot, out var cell) || frame.TryResolve(_body.SlotSymbols[slot], out cell))
        {
            return cell is ValueBindingHandle handle ? handle : null;
        }

        // a frame made as the call was made, under a debugger, which holds none of the activation's variables: given the variable's cell now.
        return Array.IndexOf(_body.ObjectSlots, slot) is var number and >= 0 ? GiveObject(frame, number) : null;
    }

    /// <summary>
    /// The cell of the object in <paramref name="slot"/> (<see cref="ObjectCellOf"/>), found again after the interpreter ran an instruction or the program
    /// waited. A <c>Set</c> stores the reference into the cell the slot holds, which this finds again.
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <param name="cell">The cell found, as <see cref="ObjectCellOf"/> finds it.</param>
    /// <returns><see langword="false"/> when the frame cannot be read: the activation is then the interpreter's, from where it is.</returns>
    public bool TryReloadObjectCell(int slot, out object? cell)
    {
        cell = null;
        if (FailsAccessAtRandom())
        {
            return false;
        }

        cell = ObjectCellOf(slot);
        return true;
    }

    /// <summary>
    /// The cell of the <c>String</c> parameter or variable in <paramref name="slot"/>: what compiled code reads the text it holds from and writes the text
    /// it is assigned to (<see cref="StringOperations"/>), for as long as the interpreter runs nothing of the activation. The frame's, which the
    /// interpreter's own evaluation of Strings reads and writes (<c>RuntimeExpressionEvaluator.BindingOf</c>); without a frame, the activation's, which
    /// its frame is given once it has one: the cell its caller gave a parameter (<see cref="TakeText"/>, <see cref="TakeTextReference"/>), or one made
    /// for a variable, holding what the interpreter declares it with. A frame made as the call was made, under a debugger, is given the parameter's or
    /// the variable's cell so.
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <returns>
    /// <see langword="null"/> when the frame holds it in no cell of its own: a parameter passed by reference bound to a variable of another type, which
    /// compiled code reads and writes nothing of, and the interpreter runs each statement that does.
    /// </returns>
    public object? StringCellOf(int slot)
    {
        var number = Array.IndexOf(_body.TextSlots, slot);
        if (_frame is not { } frame)
        {
            return _texts[number] ??= NewText(slot);
        }

        var symbol = _body.SlotSymbols[slot];
        if (frame.Procedure is not { } procedure)
        {
            return null;
        }

        var held = new LanguageCore.Binding.FrameSlot(LanguageCore.Binding.FrameLayout.For(procedure), slot);
        if (frame.TryReadSlot(held, symbol, out var cell, out var referenced))
        {
            return referenced is null && cell is ValueBindingHandle handle ? handle : null;
        }

        if (number < 0 || frame.TryResolve(symbol, out _))
        {
            return null;
        }

        var made = _texts[number] ??= NewText(slot);
        HoldText(frame, number);
        return made;
    }

    // the cell of a String variable of the activation, holding what the interpreter declares it with (RuntimeProcedureInvoker.DefaultValueOf).
    private TextCell NewText(int slot)
        => new(_body.SlotSymbols[slot] is VBLocalVariableSymbol variable
            ? RuntimeProcedureInvoker.DefaultValueOf(_session, variable).Handle.Value
            : new VBRuntimeValue<string>(null!));

    /// <summary>
    /// The cell of the <c>String</c> in <paramref name="slot"/> (<see cref="StringCellOf"/>), found again after the interpreter ran an instruction or the
    /// program waited.
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <param name="cell">The cell found, as <see cref="StringCellOf"/> finds it.</param>
    /// <returns><see langword="false"/> when the frame cannot be read: the activation is then the interpreter's, from where it is.</returns>
    public bool TryReloadStringCell(int slot, out object? cell)
    {
        cell = null;
        if (FailsAccessAtRandom())
        {
            return false;
        }

        cell = StringCellOf(slot);
        return true;
    }

    /// <summary>
    /// Two texts compared under the comparison mode of the module (MS-VBAL §5.6.9.5), as the interpreter compares them
    /// (<c>RuntimeExpressionEvaluator.TryCompareText</c>): the mode of the frame the activation is, which is the current one; without a frame, the one
    /// its frame would have, of the module the procedure is declared in (<c>RuntimeProcedureInvoker.DirectivesOf</c>).
    /// </summary>
    /// <param name="comparison">The comparison operator.</param>
    /// <param name="left">The left text; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <param name="right">The right text; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>The comparison's result.</returns>
    public bool CompareText(ComparisonOperator comparison, string? left, string? right)
    {
        var rules = _frame is null && _procedure is { } procedure
            ? SDK.Runtime.RuntimeSessionExtensions.StringComparisonIn(_session, _invoker.DirectivesOf(procedure).Compare)
            : SDK.Runtime.RuntimeSessionExtensions.CurrentStringComparison(_session);
        var order = rules.Compare(left.AsSpan(), right.AsSpan());
        return comparison switch
        {
            ComparisonOperator.Equal => order == 0,
            ComparisonOperator.NotEqual => order != 0,
            ComparisonOperator.Less => order < 0,
            ComparisonOperator.LessOrEqual => order <= 0,
            ComparisonOperator.Greater => order > 0,
            _ => order >= 0,
        };
    }

    /// <summary>
    /// <c>TypeOf</c> of the object held in <paramref name="cell"/>, tested against the class the body knows by <paramref name="number"/> (MS-VBAL §5.6.7): as
    /// the interpreter tests it (<c>RuntimeExpressionEvaluator.IsInstanceOf</c>).
    /// </summary>
    /// <param name="cell">The cell of the object variable tested, or of the <c>Variant</c>: one that holds no object (a number, <c>Empty</c>, <c>Null</c>,
    /// a <c>String</c>) is no instance of any class.</param>
    /// <param name="number">The number the body knows the class by.</param>
    /// <param name="isOf">Whether the object is of the class.</param>
    /// <returns>
    /// <see langword="false"/> when it is the interpreter's: the cell holds no reference compiled code reads, or holds <c>Nothing</c>, of which <c>TypeOf</c>
    /// raises error 91.
    /// </returns>
    public bool TypeOf(object? cell, int number, out bool isOf)
    {
        isOf = false;
        if (cell is VariantCell { IsKnown: true, HoldsNoObject: true })
        {
            return true;
        }

        if (!ObjectOperations.TryRead(cell, out var instance) || instance.Equals(default(VBRuntimeObjectId))
            || RuntimeExpressionEvaluator.IsInstanceOf(_session, instance, _body.Classes[number]) is not { } answered)
        {
            return false;
        }

        isOf = answered;
        return true;
    }

    /// <summary>
    /// Steps the <c>For</c> loop whose statement is at <paramref name="opener"/>, of a <c>Variant</c> counter, as the interpreter steps it on payloads
    /// (<c>RuntimeExpressionEvaluator.TryStepForUnboxed</c>), with the state its statement, which the interpreter ran, gave the frame.
    /// </summary>
    /// <param name="opener">The offset of the loop's <c>For</c> statement.</param>
    /// <returns>1 when the counter is past the end and the loop ends; 0 when the body runs again; -1 when the step is the interpreter's.</returns>
    public int StepVariantFor(int opener)
        => Frame.TryGetForLoopState(opener, out var state) && _executor.Expressions.TryStepForUnboxed(_session, state, _context, out var past) ? (past ? 1 : 0) : -1;

    /// <summary>
    /// The step and the end of the loop of a Variant counter held in <paramref name="cell"/> whose <c>For</c> statement is at <paramref name="opener"/>,
    /// in the subtype <paramref name="kind"/> compiled code speculates the counter holds, and whether it counts down: what each Next that finds the counter
    /// holding a value of that subtype steps it with, as the interpreter does (<c>RuntimeExpressionEvaluator.TryVariantForBounds</c>).
    /// </summary>
    /// <param name="opener">The offset of the loop's <c>For</c> statement.</param>
    /// <param name="cell">The cell of the counter.</param>
    /// <param name="kind">The subtype compiled code speculates the counter holds.</param>
    /// <param name="stepIntegral">The step, when <paramref name="kind"/> is integral.</param>
    /// <param name="stepFloating">The step, when <paramref name="kind"/> is floating-point.</param>
    /// <param name="endIntegral">The end, when <paramref name="kind"/> is integral.</param>
    /// <param name="endFloating">The end, when <paramref name="kind"/> is floating-point.</param>
    /// <param name="countsDown">Whether the step is negative, so the loop counts down.</param>
    /// <returns>1 when each Next is stepped in that subtype; -1 when each is stepped as any is (<see cref="StepVariantFor"/>).</returns>
    public int VariantForBounds(int opener, object? cell, ScalarKind kind, out long stepIntegral, out double stepFloating, out long endIntegral,
        out double endFloating, out bool countsDown)
    {
        (stepIntegral, stepFloating, endIntegral, endFloating, countsDown) = (0, 0, 0, 0, false);
        if (cell is not VariantCell variant || !Frame.TryGetForLoopState(opener, out var state)
            || !_executor.Expressions.TryVariantForBounds(_session, state, _context, variant, ScalarKinds.TypeOf(kind), out var step, out var end))
        {
            return -1;
        }

        (stepIntegral, stepFloating, endIntegral, endFloating, countsDown) = (step.Integral, step.Floating, end.Integral, end.Floating, state.CountsDown);
        return 1;
    }

    /// <summary>Writes the variable in <paramref name="slot"/>, of an integral kind, into its cell.</summary>
    public void SpillIntegral(int slot, long value) => Spill(slot, value, 0);

    /// <summary>Writes the variable in <paramref name="slot"/>, of a floating-point kind, into its cell.</summary>
    public void SpillFloating(int slot, double value) => Spill(slot, 0, value);

    private void Spill(int slot, long integral, double floating)
    {
        if (TryGetScalarCell(slot, out var scalar))
        {
            scalar.Write(new ScalarPayload(scalar.Type, integral, floating));
            _unspilled |= FailsAccessAtRandom();
            return;
        }

        // a variable the frame holds where compiled code cannot write it - a cell of another kind, at its address - is the interpreter's (Recover).
        var frame = Frame;
        if (frame.IsSlotFilled(slot))
        {
            _unspilled = true;
            return;
        }

        // an activation of a call compiled code made through the callee's typed entry holds no cell until it needs one (EnterDirectly): it is given the cell
        // it would have been given at the call, holding what the variable holds now.
        if (_body.SlotSymbols.TryGetValue(slot, out var symbol) && symbol is ITypedSymbol { ResolvedType: var declared }
            && VBEnumType.AsDeclaredType(declared) is var type && ScalarPayload.IsScalar(type))
        {
            frame.PushPayload(symbol, new ScalarPayload(type, integral, floating), slot);
        }
    }

    /// <summary>Assigns the function result variable a payload of an integral kind.</summary>
    public void AssignResultIntegral(long value) => AssignResult(new ScalarPayload(ScalarKinds.TypeOf(_body.ResultKind!.Value), value, 0));

    /// <summary>Assigns the function result variable a payload of a floating-point kind.</summary>
    public void AssignResultFloating(double value) => AssignResult(new ScalarPayload(ScalarKinds.TypeOf(_body.ResultKind!.Value), 0, value));

    /// <summary>
    /// Writes the state of the <c>For</c> loop numbered <paramref name="loop"/> into the frame, as its opener would have stashed it: its end and its step,
    /// of the kind it counts in.
    /// </summary>
    public void SpillForLoop(int loop, long endIntegral, double endFloating, long stepIntegral, double stepFloating)
    {
        var info = _body.Loops[loop];
        var type = ScalarKinds.TypeOf(info.Kind);
        var end = new ScalarPayload(type, endIntegral, endFloating);
        var step = new ScalarPayload(type, stepIntegral, stepFloating);
        Frame.SetForLoopState(info.Opener, new ForLoopState(info.Counter, info.Control, type.CreateValue(new ValueBindingHandle(end.Box())),
            type.CreateValue(new ValueBindingHandle(step.Box())))
        {
            CountsDown = ScalarKinds.IsIntegral(info.Kind) ? stepIntegral < 0 : stepFloating < 0,
            RuntimeState = new ForLoopPayloads(),
        });
    }

    /// <summary>
    /// Hands the activation to the interpreter, which runs it from the instruction at <paramref name="pc"/>: what the activation returns is what the
    /// interpreter's run of it does. Every variable and every loop that was opened has been written into the frame.
    /// </summary>
    public RuntimeExecutionOutcome Interpret(int pc)
    {
        // the procedure edited while the program waited in a callee: the activation goes on after the call, in the edited body - or, handed over in the
        // middle of an instruction after a call inside it, in that instruction, where it now is (HandKnowns).
        _handedOver = true;
        var frame = Frame;
        (frame.Pc, frame.RunsCompiled) = (pc, false);
        var inside = HasKnowns;
        _ = frame.ApplyPendingRebase(atTheCall: inside);
        var list = Body;
        pc = frame.Pc;
        if (!HandKnowns(frame, list, pc, inside))
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        _body.WalkedBack(pc < list.Items.Length ? BreakpointTable.LocationOf(list.Items[pc]) : null);
        return _executor.Run(_session, frame, list, _context, HandStatement());
    }

    private RuntimeExecutionOutcome _recovered;

    /// <summary>
    /// Has the interpreter run the instruction at <paramref name="pc"/> alone - or, after a call there that raised an error, give the error to the
    /// activation's handler - and says where compiled code goes on: at the offset the interpreter left the activation at, every variable,
    /// the function result and each loop read back from the frame; or nowhere (<c>-1</c>), when the activation returned, the program stopped, or the
    /// interpreter goes on with it (<see cref="Recovered"/>). Every variable and every loop that was opened has been written into the frame.
    /// </summary>
    /// <remarks>
    /// The interpreter goes on with the activation when the program waited while the instruction ran - at its error, which a debugger asked to see - or the
    /// interpreter is wanted, or the symbols were defined again, or a break was asked for and is not answered (<see cref="WantsInterpreterAfterCall"/>); and
    /// when the activation goes on inside a <c>For</c> loop the frame holds no state of, which compiled code would step with what it does not have. It runs
    /// the activation from <paramref name="pc"/> without stepping, when compiled code could not write a variable into the frame.
    /// </remarks>
    public int Recover(int pc)
    {
        _handedOver = true;

        // a variable not written into the frame: the interpreter runs the activation from the instruction, as the frame has it.
        if (_unspilled)
        {
            _unspilled = false;
            _recovered = _failed is null ? Interpret(pc) : AfterFailedCall(pc);
            _failed = null;
            return -1;
        }

        var frame = Frame;
        (frame.Pc, frame.RunsCompiled) = (pc, false);
        var failed = _failed;
        _failed = null;
        var list = Body;
        if (!HandKnowns(frame, list, pc, inside: failed is null))
        {
            _recovered = RuntimeExecutionOutcome.InternalError;
            return -1;
        }

        var suspensions = Suspensions;
        var outcome = _executor.Step(_session, frame, list, _context, failed, HandStatement());
        if (outcome.Kind is not RuntimeExecutionOutcomeKind.Next)
        {
            _recovered = outcome;
            return -1;
        }

        // an instruction that falls through goes on inside the loops it was inside, whose state the frame has: compiled code was there. A For statement is the
        // one that falls through into a loop, when its start, end or step failed under Resume Next - and the interpreter gave the loop a state then, the one
        // it had or a 0 end and step (ProcedureExecutor.Unevaluated, as MS-VBA does), which the re-entry reads back. A Select falls through into the block
        // it gave its selector - boxed, when the interpreter evaluated it boxed - which is checked as a jump's destination is.
        if (Suspensions != suspensions || WantsInterpreterAfterCall || !ReferenceEquals(frame.Body ?? list, list)
            || ((frame.Pc != pc + 1 || list.Items[pc].Kind is InstructionKind.Select) && !CanGoOnAt(frame, list, frame.Pc)))
        {
            _recovered = Interpret(frame.Pc);
            return -1;
        }

        _body.CountRecovered();
        frame.RunsCompiled = true;
        return frame.Pc;
    }

    /// <summary>What the activation returns when <see cref="Recover"/> said it goes on nowhere.</summary>
    public RuntimeExecutionOutcome Recovered() => _recovered;

    // whether compiled code can go on at pc: every For loop it is inside has its state in the frame, of the kind compiled code steps it in; and every
    // Select Case its selector, of the kind compiled code compares it in - one the interpreter evaluated boxed is compared boxed.
    private bool CanGoOnAt(CallStackFrame frame, InstructionList list, int pc)
    {
        for (var i = 0; i < _body.Loops.Count; i++)
        {
            var info = _body.Loops[i];
            if (pc > info.Opener && list.Items[info.Opener].End is { } past && pc < past && !TryReadForLoop(frame, info, out _, out _, out _))
            {
                return false;
            }
        }

        for (var i = 0; i < _body.Selects.Count; i++)
        {
            var select = _body.Selects[i];
            if (pc > select.Opener && pc < select.End && !(select.IsText ? TryReadTextSelector(frame, select, out _) : TryReadSelector(frame, select, out _)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadSelector(CallStackFrame frame, CompiledSelect select, out ScalarPayload selector)
        => frame.TryGetSelectorPayload(select.Opener, out selector) && ScalarKinds.Of(selector.Type) == select.Kind;

    // a String selector, as the interpreter stashes it (ProcedureExecutor.ExecuteSelect): the String value it evaluated, whose text compiled code compares.
    private static bool TryReadTextSelector(CallStackFrame frame, CompiledSelect select, out string? text)
    {
        if (frame.TryGetBlockState(select.Opener, out var held) && held is VBStringValue { IsIndeterminate: false, RuntimeValue: VBRuntimeValue<string> value })
        {
            text = value.StoredValue;
            return true;
        }

        text = null;
        return false;
    }

    /// <summary>
    /// Writes the selector of the <c>Select Case</c> numbered <paramref name="select"/>, of a <c>String</c> selector, into the frame, as the interpreter
    /// stashes one: the String value of its text (<c>CallStackFrame.SetBlockState</c>).
    /// </summary>
    /// <param name="select">The number of the <c>Select Case</c>.</param>
    /// <param name="text">The text of its selector; <see langword="null"/> for <c>vbNullString</c>.</param>
    public void SpillTextSelector(int select, string? text)
        => Frame.SetBlockState(_body.Selects[select].Opener, text is null ? VBStringValue.VBNullString : new VBStringValue(text));

    /// <summary>
    /// The selector of the <c>Select Case</c> numbered <paramref name="select"/>, of a <c>String</c> selector, read back from the frame.
    /// </summary>
    /// <param name="select">The number of the <c>Select Case</c>.</param>
    /// <param name="selector">The text of its selector, when it was evaluated.</param>
    /// <returns>Whether it was evaluated, to a String whose text compiled code reads.</returns>
    public bool ReloadTextSelector(int select, out string? selector) => TryReadTextSelector(Frame, _body.Selects[select], out selector);

    /// <summary>
    /// Writes the selector of the <c>Select Case</c> numbered <paramref name="select"/> into the frame, as the interpreter stashes a selector evaluated on
    /// payloads (<c>CallStackFrame.SetSelectorPayload</c>).
    /// </summary>
    public void SpillSelector(int select, long integral, double floating)
    {
        var info = _body.Selects[select];
        Frame.SetSelectorPayload(info.Opener, new ScalarPayload(ScalarKinds.TypeOf(info.Kind), integral, floating));
    }

    /// <summary>
    /// The selector of the <c>Select Case</c> numbered <paramref name="select"/>, of an integral kind, read back from the frame: whether it was evaluated.
    /// </summary>
    public bool ReloadSelectorIntegral(int select, out long selector)
    {
        var evaluated = TryReadSelector(Frame, _body.Selects[select], out var payload);
        selector = payload.Integral;
        return evaluated;
    }

    /// <summary>
    /// The selector of the <c>Select Case</c> numbered <paramref name="select"/>, of a floating-point kind, read back from the frame: whether it was
    /// evaluated.
    /// </summary>
    public bool ReloadSelectorFloating(int select, out double selector)
    {
        var evaluated = TryReadSelector(Frame, _body.Selects[select], out var payload);
        selector = payload.Floating;
        return evaluated;
    }

    private static bool TryReadForLoop(CallStackFrame frame, CompiledForLoop info, out ScalarPayload end, out ScalarPayload step, out bool countsDown)
    {
        (end, step, countsDown) = (default, default, false);
        var type = ScalarKinds.TypeOf(info.Kind);
        if (!frame.TryGetForLoopState(info.Opener, out var state)
            || state.End is not { Handle: ValueBindingHandle boxedEnd } || !ScalarPayload.TryOf(type, boxedEnd.Value, out end)
            || state.Step is not { Handle: ValueBindingHandle boxedStep } || !ScalarPayload.TryOf(type, boxedStep.Value, out step))
        {
            return false;
        }

        countsDown = state.CountsDown;
        return true;
    }

    /// <summary>
    /// The end and the step of the <c>For</c> loop numbered <paramref name="loop"/>, of an integral kind, read back from the frame: whether the loop was
    /// opened.
    /// </summary>
    public bool ReloadForLoopIntegral(int loop, out long end, out long step, out bool countsDown)
    {
        var open = TryReadForLoop(Frame, _body.Loops[loop], out var endPayload, out var stepPayload, out countsDown);
        (end, step) = (endPayload.Integral, stepPayload.Integral);
        return open;
    }

    /// <summary>
    /// The end and the step of the <c>For</c> loop numbered <paramref name="loop"/>, of a floating-point kind, read back from the frame: whether the loop
    /// was opened.
    /// </summary>
    public bool ReloadForLoopFloating(int loop, out double end, out double step, out bool countsDown)
    {
        var open = TryReadForLoop(Frame, _body.Loops[loop], out var endPayload, out var stepPayload, out countsDown);
        (end, step) = (endPayload.Floating, stepPayload.Floating);
        return open;
    }

    /// <summary>
    /// What the <c>On Error</c> statement at <paramref name="pc"/> does, to the activation's frame (MS-VBAL §5.4.4): its error handling policy set, and the
    /// Err object cleared.
    /// </summary>
    public bool OnError(int pc)
        => _frame is null
            ? ProcedureExecutor.TryApplyOnError(_session, _errorHandler, Body.Items[pc], out _errorHandler)
            : ProcedureExecutor.TryApplyOnError(_session, _frame, Body.Items[pc]);

    /// <summary>A <c>GoSub</c>: <paramref name="returnPc"/> pushed onto the activation's GoSub Resumption List (MS-VBAL §5.4.2.14).</summary>
    public void GoSub(int returnPc) => Frame.PushGoSubReturn(returnPc);

    /// <summary>
    /// A jump from <paramref name="from"/> to <paramref name="to"/> that leaves a <c>For Each</c> block (an <c>Exit For</c>) ends the loop: what it held is
    /// let go of, as the interpreter lets go of it. The frame says the jump is the instruction being run, for a <c>Terminate</c> that runs there.
    /// </summary>
    /// <param name="from">The offset of the jump.</param>
    /// <param name="to">The offset it goes to.</param>
    public void LeaveLoops(int from, int to)
    {
        Frame.Pc = from;
        ProcedureExecutor.LeaveLoops(_session, Frame, from, to);
    }

    /// <summary>A <c>Return</c>: the offset popped off the GoSub Resumption List; <see langword="false"/> when it is empty (MS-VBAL §5.4.2.15).</summary>
    public bool TryReturnFromGoSub(out int pc) => Frame.TryPopGoSubReturn(out pc);

    // the body the interpreter goes on in: the frame's, which an edit made while the program waited may have given it, the same instructions at the same
    // offsets as the one compiled code was compiled from (CallStackFrame.TryRebase).
    private InstructionList Body => _frame?.Body ?? _list;

    /// <summary>
    /// What an activation that reached its end, or an <c>Exit</c>, returns. The frame says the activation is at <paramref name="pc"/>, the <c>Exit</c> or the
    /// end of the body, as the frame of an activation the interpreter runs says when it returns: what lets go of its locals then, and a <c>Class_Terminate</c>
    /// that runs of it, sees it there, and not at the last call it made.
    /// </summary>
    public RuntimeExecutionOutcome Return(int pc)
    {
        SetPc(pc);
        if (_frame is { } frame)
        {
            frame.RunsCompiled = false;
            ProcedureExecutor.ClearHandledError(_session, frame);
        }

        return RuntimeExecutionOutcome.ExitProcedure;
    }
}

/// <summary>
/// The compiled body of a procedure: its code, and what the activation needs to know of it to hand it to the interpreter.
/// </summary>
/// <param name="Run">The compiled code.</param>
/// <param name="ResultKind">The kind of the function result variable; <see langword="null"/> for a <c>Sub</c>.</param>
/// <param name="Loops">The <c>For</c> loops, by the number compiled code knows each by.</param>
/// <param name="Name">The procedure's name: what the random walk-back tells one body from another by, the same in every run.</param>
/// <param name="Callees">The procedures the body calls, by the number compiled code knows each by.</param>
/// <param name="Directs">For each of <paramref name="Callees"/>, the compiled callee it calls directly; <see langword="null"/> for one it calls through the invoker.</param>
/// <param name="SlotSymbols">The parameter or the variable each slot of the body's activations holds: what a cell is made for, when the activation has none yet.</param>
internal sealed record class CompiledBody(Func<CompiledActivation, RuntimeExecutionOutcome> Run, ScalarKind? ResultKind, IReadOnlyList<CompiledForLoop> Loops,
    string Name, IReadOnlyList<CompiledCallee> Callees, IReadOnlyList<DirectCall?> Directs, IReadOnlyDictionary<int, Symbol> SlotSymbols)
{
    private long _walkedBack;
    private int _activations;

    private long _directCalls;
    private long _indirectCalls;

    /// <summary>How many calls the body made of a compiled procedure directly, through its typed entry.</summary>
    public long DirectCalls => Interlocked.Read(ref _directCalls);

    /// <summary>How many calls the body made through the invoker: of a procedure that is not compiled, or not when it was.</summary>
    public long IndirectCalls => Interlocked.Read(ref _indirectCalls);

    internal void CountDirectCall() => Interlocked.Increment(ref _directCalls);

    private long _framesMade;

    /// <summary>
    /// How many activations of the body that compiled code called directly were given a frame after the call: because something looked at the stack while
    /// they were on it, or they were handed to the interpreter. One called under a debugger, which is given its frame as it is called, is
    /// not counted.
    /// </summary>
    public long FramesMade => Interlocked.Read(ref _framesMade);

    internal void CountFrameMade() => Interlocked.Increment(ref _framesMade);

    private long _textsCopied;

    /// <summary>
    /// How many times a <c>Mid</c> statement of an activation of the body copied the <c>String</c> it changes: of a variable, into the builder it changes the
    /// characters in, once for each <c>String</c> the variable is given (<see cref="CompiledActivation.TryMid"/>); of a parameter by reference, once for
    /// each statement, into the new String its cell is given (<see cref="CompiledActivation.TryMidInCell"/>).
    /// </summary>
    public long TextsCopied => Interlocked.Read(ref _textsCopied);

    internal void CountTextCopied() => Interlocked.Increment(ref _textsCopied);

    private long _activationsMade;

    /// <summary>
    /// How many activations of the body were made for calls compiled code made directly without a frame: each call is made in the activation that last left
    /// its place of the stack when it can be, so a loop of calls makes one, and a recursion as many as it goes deep.
    /// </summary>
    public long ActivationsMade => Interlocked.Read(ref _activationsMade);

    internal void CountActivationMade() => Interlocked.Increment(ref _activationsMade);

    private long _invokerActivationsMade;

    /// <summary>
    /// How many activations of the body the invoker made for the calls made through it: each call is made in the activation that last returned from one,
    /// when that one ran its compiled code to the end, so a loop of calls makes one.
    /// </summary>
    public long InvokerActivationsMade => Interlocked.Read(ref _invokerActivationsMade);

    internal void CountInvokerActivationMade() => Interlocked.Increment(ref _invokerActivationsMade);

    // the activation of the body each invoker last returned from a call it made of it, which its next call is made in (RuntimeProcedureInvoker.Run). Keyed
    // weakly by the invoker, which the activation holds: an entry goes with its invoker once the run its pipeline was composed for is over, and every entry
    // goes with the body when an edit replaces it.
    internal ConditionalWeakTable<RuntimeProcedureInvoker, StrongBox<CompiledActivation?>> Spares { get; } = [];

    internal void CountIndirectCall() => Interlocked.Increment(ref _indirectCalls);

    private long _supportCallsMade;

    /// <summary>
    /// How many statements of the body the interpreter ran for compiled code as support calls: a statement compiled code does not make itself, with every
    /// variable written into the frame before it (<c>CompiledActivation.RunStatement</c>, <c>CompiledActivation.LetMember</c>). A recovered instruction is
    /// not one.
    /// </summary>
    public long SupportCallsMade => Interlocked.Read(ref _supportCallsMade);

    internal void CountSupportCall() => Interlocked.Increment(ref _supportCallsMade);

    private long _libraryCallsMade;

    /// <summary>
    /// How many calls the body made of a function of the standard library, or of a member of one of its classes, that it calls inside an expression
    /// (<see cref="Libraries"/>), through the binding or straight to the library. A typed form compiled code calls, or what it reads of a Variant's cell
    /// in place of a call, is not one.
    /// </summary>
    public long LibraryCallsMade => Interlocked.Read(ref _libraryCallsMade);

    internal void CountLibraryCall() => Interlocked.Increment(ref _libraryCallsMade);

    /// <summary>The typed entry of the body: its compiled activation, then each parameter. Set once the body's type is created.</summary>
    public System.Reflection.MethodInfo? Entry { get; init; }

    /// <summary>
    /// The slots its activations hold in cells, by the number compiled code knows each cell by (<see cref="ProcedureAnalysis.CellSlots"/>): each parameter
    /// passed by reference, and each parameter or variable the body passes by reference.
    /// </summary>
    public IReadOnlyList<int> CellSlots { get; init; } = [];

    /// <summary>
    /// The slots of its <c>String</c> parameters and variables of variable length (<see cref="ProcedureAnalysis.TextSlots"/>), in order: an activation
    /// without a frame holds the cell of each, by its number here (<see cref="CompiledActivation.StringCellOf"/>).
    /// </summary>
    public int[] TextSlots { get; init; } = [];

    /// <summary>
    /// The slots of its object parameters and variables, but <c>Me</c> (<see cref="ProcedureAnalysis.HeldObjectSlots"/>), in order: an activation without a
    /// frame holds the cell of each, by its number here (<see cref="CompiledActivation.ObjectCellOf"/>).
    /// </summary>
    public int[] ObjectSlots { get; init; } = [];

    /// <summary>
    /// Whether the body is of a function that returns an object (<see cref="ProcedureAnalysis.ResultIsObject"/>): an activation without a frame holds its
    /// result in a cell of its own.
    /// </summary>
    public bool ResultIsObject { get; init; }

    /// <summary>
    /// Whether an activation of the body holds in its frame what compiled code does not (<see cref="ProcedureAnalysis.NeedsFrame"/>): it is called through
    /// the invoker, which makes the frame, and never directly without one.
    /// </summary>
    public bool NeedsFrame { get; init; }

    /// <summary>
    /// Whether the body is of a member of a class that a call compiled code makes of it on an object enters without a frame
    /// (<see cref="ProcedureAnalysis.EntersWithoutFrame"/>).
    /// </summary>
    public bool EntersWithoutFrame { get; init; }

    /// <summary>
    /// The slot of <c>Me</c>, when the body is of a member of a class; <c>-1</c> otherwise.
    /// </summary>
    public int MeSlot { get; init; } = -1;

    /// <summary>
    /// The functions of the standard library the body calls inside an expression, by the number its nodes know each by (<see cref="LibraryCallNode"/>).
    /// </summary>
    public IReadOnlyList<CompiledLibraryCall> Libraries { get; init; } = [];

    /// <summary>
    /// The fields compiled code reaches, by the number it knows each by (<see cref="ProcedureAnalysis.Fields"/>).
    /// </summary>
    public IReadOnlyList<CompiledField> Fields { get; init; } = [];

    /// <summary>
    /// The cells of the fields of <c>Me</c> the body found last: those of the object an activation of it was last on, which keeps
    /// them for as long as it lives (<see cref="FieldCells"/>). Another object's are found again, whatever class it is of. The cache keeps the object's
    /// identity and holds no reference to it.
    /// </summary>
    internal FieldCells? ObjectFields { get; set; }

    /// <summary>
    /// The cells of the fields of the session the body found last: those of the storage an activation of it last ran in, for as long as
    /// what it binds is what it bound then: a variable's cell is the one it was until anything is allocated, freed or rebound
    /// (<c>SessionStorage.Generation</c>).
    /// </summary>
    internal SessionFieldCells? SessionFields { get; set; }

    /// <summary>
    /// The public variables of objects the body's member accesses found last, by the number compiled code knows each access by (<see cref="MemberFields"/>):
    /// each kept for the object it was found of, while no module is defined again (<see cref="MemberFieldCell"/>).
    /// </summary>
    internal MemberFieldCell?[]? MemberFieldCells { get; set; }

    /// <summary>The <c>Select Case</c> statements of a scalar selector, by the number compiled code knows each by.</summary>
    public IReadOnlyList<CompiledSelect> Selects { get; init; } = [];

    /// <summary>
    /// The classes the body's <c>TypeOf</c> expressions test against, by the number compiled code knows each by (<see cref="ProcedureAnalysis.Classes"/>).
    /// </summary>
    public IReadOnlyList<VBClassModuleSymbol> Classes { get; init; } = [];

    /// <summary>
    /// The <c>New</c> expressions whose objects the body makes, by the number compiled code knows each by (<see cref="ProcedureAnalysis.News"/>).
    /// </summary>
    public IReadOnlyList<LanguageCore.Binding.BoundNew> News { get; init; } = [];

    /// <summary>
    /// The public variables of objects the body reads, by the number compiled code knows each by (<see cref="ProcedureAnalysis.MemberFields"/>).
    /// </summary>
    public IReadOnlyList<CompiledMemberField> MemberFields { get; init; } = [];

    /// <summary>
    /// Where each integral operation of the body is written, by the number compiled code knows it by (<see cref="ProcedureAnalysis.ArithmeticSites"/>).
    /// </summary>
    public IReadOnlyList<SDK.Model.Source.SourceLocation> ArithmeticSites { get; init; } = [];

    /// <summary>
    /// The safepoint the next activation of the body is handed to the interpreter at, under the random walk-back: how many safepoints it passes first -
    /// under that mode every instruction boundary is one. It is chosen from the body's name, how many activations of it came before, and the seed
    /// (<see cref="CompiledExecution.WalkBackSeed"/>), so that a run can be replayed whatever else runs beside it; and from 1 to 3,969 (a 5-bit count
    /// times a power of two up to 128), so that the late iterations of a loop are reached as well as the first.
    /// </summary>
    internal int NextRandomSafepoint()
    {
        var hash = 2166136261u;
        foreach (var character in Name)
        {
            hash = (hash ^ character) * 16777619u;
        }

        hash = (hash ^ (uint)CompiledExecution.WalkBackSeed) * 16777619u;
        hash = (hash ^ (uint)Interlocked.Increment(ref _activations)) * 2654435761u;
        return 1 + (int)((hash >> 27) << (int)((hash >> 8) % 8));
    }

    private int _failedAccesses;

    /// <summary>
    /// The access of its frame the next activation of the body fails, under the random failed accesses (<see cref="CompiledExecution.FailsReloadsAtRandom"/>):
    /// how many it makes first. Chosen as <see cref="NextRandomSafepoint"/> is, with a hash of its own, and from 1 to 121 (a 4-bit count times a power of two
    /// up to 8): an activation that steps few instructions makes few.
    /// </summary>
    internal int NextRandomFailedAccess()
    {
        var hash = 2166136261u ^ 0x5bd1e995u;
        foreach (var character in Name)
        {
            hash = (hash ^ character) * 16777619u;
        }

        hash = (hash ^ (uint)CompiledExecution.WalkBackSeed) * 16777619u;
        hash = (hash ^ (uint)Interlocked.Increment(ref _failedAccesses)) * 2654435761u;
        return 1 + (int)((hash >> 28) << (int)((hash >> 8) % 4));
    }

    /// <summary>How many times an activation of the body was handed to the interpreter.</summary>
    public long WalkBacks => Interlocked.Read(ref _walkedBack);

    /// <summary>
    /// Where the statement is that an activation of the body was last handed to the interpreter at: the one that failed or declined, or a back edge where the
    /// program was to stop, or the first when a parameter's cell held no payload. <see langword="null"/> before any was.
    /// </summary>
    public SDK.Model.Source.SourceLocation? LastWalkedBackAt { get; private set; }

    internal void WalkedBack(SDK.Model.Source.SourceLocation? at)
    {
        LastWalkedBackAt = at;
        _ = Interlocked.Increment(ref _walkedBack);
    }

    private long _recovered;

    /// <summary>
    /// How many times an activation of the body had the interpreter run one instruction and went on compiled: an error its handler
    /// caught, a <c>Resume</c>, an <c>On...GoTo</c> (<see cref="CompiledActivation.Recover"/>).
    /// </summary>
    public long Recoveries => Interlocked.Read(ref _recovered);

    internal void CountRecovered() => Interlocked.Increment(ref _recovered);

    private long _raised;

    /// <summary>
    /// How many errors an operation compiled code evaluated found and gave to the interpreter, which did not evaluate the instruction again to find them
    /// (<see cref="CompiledActivation.RaiseArithmetic"/>).
    /// </summary>
    public long RaisedErrors => Interlocked.Read(ref _raised);

    internal void CountRaised() => Interlocked.Increment(ref _raised);

    private readonly TaskCompletionSource _looked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when an activation of the body first looks whether the program is to stop, at a back edge of a loop
    /// (<see cref="CompiledActivation.ShouldWalkBack"/>): what is asked of it after that - a breakpoint, a pause - is seen at its next look.
    /// </summary>
    public Task Looked => _looked.Task;

    internal void NoteLook()
    {
        if (!_looked.Task.IsCompleted)
        {
            _ = _looked.TrySetResult();
        }
    }
}

/// <summary>
/// A <c>For</c> loop of a compiled body.
/// </summary>
/// <param name="Opener">The offset of its opener, which its state is stashed by.</param>
/// <param name="Counter">The variable it counts with.</param>
/// <param name="Control">The counter's expression.</param>
/// <param name="Kind">The kind it counts in, the counter's.</param>
internal sealed record class CompiledForLoop(int Opener, Symbol Counter, ExpressionNode Control, ScalarKind Kind);

/// <summary>
/// A <c>Select Case</c> of a scalar selector or of a <c>String</c> one, of a compiled body (MS-VBAL §5.4.2.10).
/// </summary>
/// <param name="Opener">The offset of its <c>Select</c>, which its selector is stashed by.</param>
/// <param name="End">The offset right past its block.</param>
/// <param name="Kind">The kind of a scalar selector; <c>default</c> for a String one, which is compared as text.</param>
/// <param name="IsText">Whether its selector is a String of variable length, which the frame holds as the interpreter stashes it, a value.</param>
internal sealed record class CompiledSelect(int Opener, int End, ScalarKind Kind, bool IsText = false);
