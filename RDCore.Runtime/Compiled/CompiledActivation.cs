using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Instructions;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// An activation of a procedure that runs compiled: what its compiled code is given, and everything it calls besides the payload
/// operations - reading its parameters, polling for what stops a program, and handing the activation to the interpreter.
/// </summary>
/// <remarks>
/// <para>
/// The activation's frame is laid out, its parameters given and its variables declared, as for an activation the interpreter runs. Compiled code reads
/// its parameters from their cells when it starts, and keeps its variables in its own locals from then on: the cells are written again only when the
/// activation is handed to the interpreter.
/// </para>
/// <para>
/// It is handed to the interpreter - <em>walked back</em> - at an instruction boundary: when an operation declines or raises an error, which the
/// interpreter then evaluates as it always does, with the message and the location it always gives; and when the program is to stop, which the interpreter
/// stops it for. Compiled code writes its variables into their cells and the state of each <c>For</c> loop it is in into the frame, and the interpreter runs
/// the rest of the activation from that instruction.
/// </para>
/// </remarks>
public sealed class CompiledActivation : ILightActivation
{
    private readonly IRuntimeSession _session;
    private readonly InstructionList _list;
    private readonly ProcedureExecutor _executor;
    private readonly RuntimeEvaluationContext _context;
    private readonly CompiledBody _body;
    private readonly ISessionHalt _halt;

    // what made the activation, which makes the activations of the calls it makes through their callees' typed entries, and ends them (EnterDirectly): the
    // session's own, never one the body was compiled with - a body is shared by every session composed over the same scopes.
    private readonly RuntimeProcedureInvoker _invoker;

    // the activation's frame; none for one of a call compiled code made directly, until something is to see it (IR lowering S2.3b2): it is then made
    // (RuntimeCallStack.MaterializeLights), and the activation is held in it from then on.
    private CallStackFrame? _frame;

    // what an activation without a frame holds in place of one: the procedure it is of, the stack it is on, the instruction of the call it is making, and its
    // function result variable.
    private readonly VBTypeMemberSymbol? _procedure;
    private readonly RuntimeCallStack? _stack;
    private int _pc;
    private ScalarPayload _result;
    private bool _hasResult;

    // the cell of each slot held in one (CompiledBody.CellSlots), by its number: what compiled code reads and writes the variable in, and what the frame
    // holds, given it as soon as there is one (TakeReference).
    private readonly object?[] _cells;

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
        _stack = session.CallStack as RuntimeCallStack;
        _cells = body.CellSlots.Count == 0 ? [] : new object?[body.CellSlots.Count];
        Begin();
    }

    // what every activation begins with, one made again too (EnterDirectly).
    private void Begin()
    {
        Array.Clear(_cells);
        (_entered, _instance, _me) = (null, null, null);
        _scopes = _session.Symbols.Scopes;
        (_pc, _hasResult, _failed, _unspilled) = (0, false, null, false);
        _untilPoll = PollInterval;

        // the safepoint the activation is handed to the interpreter at, under the random walk-back (CompiledBody.NextRandomSafepoint).
        if (CompiledExecution.WalksBackAtRandom)
        {
            _untilPoll = _body.NextRandomSafepoint();
        }

        _untilFailedAccess = CompiledExecution.FailsReloadsAtRandom ? _body.NextRandomFailedAccess() : 0;
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
        (frame.Pc, frame.RunsCompiled) = (_pc, true);
        if (_hasResult)
        {
            frame.AssignResult(_result);
        }

        for (var number = 0; number < _cells.Length; number++)
        {
            Hold(frame, number);
        }

        return frame;
    }

    // the frame given the cell numbered number, when it has none for its slot: a parameter passed by reference's is its caller's variable, which the frame
    // has a second name for (PushAlias); any other is the activation's own.
    private void Hold(CallStackFrame frame, int number)
    {
        var slot = _body.CellSlots[number];
        if (_cells[number] is not ScalarCell cell || frame.IsSlotFilled(slot))
        {
            return;
        }

        var symbol = _body.SlotSymbols[slot];
        if (symbol is VBParameterSymbol parameter && RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind))
        {
            frame.PushAlias(symbol, cell, slot);
        }
        else
        {
            frame.PushCell(symbol, cell, slot);
        }
    }

    /// <summary>
    /// Holds <paramref name="cell"/>, the argument of a parameter passed by reference - its caller's variable, or a cell of its own a value was copied into -
    /// as the cell numbered <paramref name="number"/>: the frame has the parameter as a second name for it, now or when it is made.
    /// </summary>
    public void TakeReference(int number, object cell)
    {
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
        var type = ((ITypedSymbol)_body.SlotSymbols[slot]).ResolvedType;
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
    /// copied into.
    /// </summary>
    /// <returns><see langword="false"/> when the parameter is bound to an address - a field, an element - which is the interpreter's to read.</returns>
    public bool TryReadReference(int slot, out object? cell)
    {
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

    /// <summary>A cell of its own for the argument of a parameter passed by reference that is no variable, holding its value (MS-VBAL §5.3.1.11).</summary>
    public static object NewCell(ScalarKind kind, long integral, double floating) => ScalarCell.Of(new ScalarPayload(ScalarKinds.TypeOf(kind), integral, floating));

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
            _pc = pc;
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
        SetPc(pc);
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
    /// kinds, passed the same way, and what it returns of the same kind. Not when the variable holds no reference compiled code reads, holds
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
            || !_session.Symbols.TryGetInstance(instance, out var target)
            || _executor.Expressions.MemberOf(called.Site!, target.ClassModule) is not { } member || !Takes(called, member))
        {
            return false;
        }

        (_member, _receiver, _memberInstance) = (member, Unsafe.As<ValueBindingHandle>(cell!).Value, target);
        return true;
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
        var returned = ScalarKinds.Of((member as VBFunctionMemberSymbol)?.ResolvedType);
        if (member is not (VBProcedureMemberSymbol or VBFunctionMemberSymbol) || parameters is not [{ Name: SDK.Model.Tokens.Me }, ..]
            || parameters.Length - 1 != called.Parameters.Count
            || (called.Result is { } read ? returned != read : member is VBFunctionMemberSymbol && returned is null))
        {
            return false;
        }

        for (var i = 0; i < called.Parameters.Count; i++)
        {
            var parameter = parameters[i + 1];
            if (parameter is ParamArrayParameterSymbol || ScalarKinds.Of(parameter.ResolvedType) != called.Parameters[i]
                || RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind) != called.ByReference[i])
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
        SetPc(pc);
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
            return false;
        }

        if (called.Result is null)
        {
            return true;
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
    // the object a member is called on. A String is given the text (SetText), or the address of the String variable given (SetTextReference).
    private IRuntimeValue[] ArgumentsOf(CompiledCallee called, int first)
    {
        var arguments = new IRuntimeValue[first + called.Parameters.Count];
        for (var i = 0; i < called.Parameters.Count; i++)
        {
            if (called.Parameters[i] is not { } kind)
            {
                arguments[first + i] = (IRuntimeValue)_references[i]!;
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
    /// the session is being analyzed; or the call stack has no room for another frame.
    /// </summary>
    /// <returns>The callee's activation, which its entry is called with; <see langword="null"/> when the call is to be made through the invoker.</returns>
    public CompiledActivation? EnterDirectly(int pc, int callee)
    {
        if (_body.Directs[callee] is not { Body: { } body } direct || !ReferenceEquals(_session.Symbols.Scopes, _scopes) || _halt.WantsInterpreter
            || _executor.Observation is not null || _session.Errors.Untrappable is not null
            || CompiledExecution.EngineFor(_session.Environment.ExecutionEngine) is not VBExecutionEngine.Compiled
            || !ReferenceEquals(_invoker.BodyOf(direct.Procedure), direct.List))
        {
            return null;
        }

        SetPc(pc);
        _failed = null;

        // under a debugger, which may look at the stack while the program waits in it, the callee's frame is made as it is called; any other is on the stack
        // without one until something is to see it (IR lowering S2.3b2), in the activation that last left its place of the stack, begun again, when that
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
                spare.Begin();
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
        if (_member is not { } member || _receiver is not VBRuntimeValue<VBRuntimeObjectId> receiver || !ReferenceEquals(_session.Symbols.Scopes, _scopes)
            || _halt.WantsInterpreter || _executor.Observation is not null || _session.Errors.Untrappable is not null
            || CompiledExecution.EngineFor(_session.Environment.ExecutionEngine) is not VBExecutionEngine.Compiled
            || EntryOf(_body.Callees[callee], member) is not { } entry)
        {
            return null;
        }

        SetPc(pc);
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
                spare.Begin();
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

            (activation._entered, activation._instance, activation._me) = (member, _memberInstance, new ValueBindingHandle(receiver));
            ObjectReferences.Rebind(_session, activation._me, ObjectReferences.Held.None);
        }
        else if (_invoker.PushMemberActivation(member, entry.List, receiver) is { } frame)
        {
            activation = new CompiledActivation(_session, frame, entry.List, _executor, entry.Context, entry.Body, _invoker) { _entered = member };
        }
        else
        {
            return null;
        }

        (_member, _receiver, _memberInstance, _memberEntry) = (null, null, null, entry.Entry);
        _body.CountDirectCall();
        _suspensionsBeforeCall = Suspensions;
        return activation;
    }

    // the object an activation of a member of a class entered without a frame is on (EnterMemberDirectly), and the cell it holds Me in: the frame it is given
    // later holds Me in that cell (MakeFrame), and the activation lets go of it as the frame would have, when it returns (ReturnedDirectly).
    private IObjectInstance? _instance;
    private ValueBindingHandle? _me;

    /// <summary>
    /// The typed entry of the member <see cref="EnterMemberDirectly"/> entered, which compiled code calls with its activation and its arguments.
    /// </summary>
    public nint MemberEntry => _memberEntry;

    // the compiled member a call enters directly: the one it entered last, while its body is the one the invoker runs; else the member compiled now, when it
    // is the workspace's to run - its binding is this invoker's.
    private MemberEntry? EntryOf(CompiledCallee called, VBTypeMemberSymbol member)
    {
        if (called.Entered is { } entered && ReferenceEquals(entered.Member, member) && ReferenceEquals(_invoker.BodyOf(member), entered.List))
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

        entered = new MemberEntry(member, list, body, method.MethodHandle.GetFunctionPointer(), new RuntimeEvaluationContext(member.Uri));
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
        outcome = callee._entered is not { } member ? _invoker.PopCompiledActivation(outcome, light: callee._frame is null)
            : callee._me is { } me && callee._frame is null ? EndMember(callee, me, outcome)
            : _invoker.PopMemberActivation(callee._frame!, member, outcome);
        callee.LetGoOfMe();
        if (outcome.Kind is not RuntimeExecutionOutcomeKind.ExitProcedure)
        {
            // as the invoker answers it (InvokeScalars): an error is the call's, and a call that was stopped has no answer.
            _failed = outcome.Kind is RuntimeExecutionOutcomeKind.Error ? outcome : RuntimeExecutionOutcome.InternalError;
            return false;
        }

        // what the call returns is read only where the call reads it, as Call reads it: a Function called as a statement returns what is let go of.
        if (_body.Callees[call].Result is null)
        {
            return true;
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
        if (outcome.Kind is not (RuntimeExecutionOutcomeKind.Halt or RuntimeExecutionOutcomeKind.Break))
        {
            ObjectReferences.Release(_session, me);
        }

        return _invoker.PopCompiledActivation(outcome, light: callee._frame is null);
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

    /// <summary>
    /// Whether the activation is handed to the interpreter after the call it made returns: the interpreter is wanted (<see cref="ISessionHalt.WantsInterpreter"/>);
    /// the procedure was edited while the program waited in the callee, and the activation goes on in its edited body (<see cref="CallStackFrame.HasPendingRebase"/>);
    /// or the session's symbols were defined again while the callee ran - an edit made while the program waited - and what compiled code calls, and how,
    /// may not be what it was compiled for. The interpreter goes on with them as they are now, from the instruction after the call.
    /// </summary>
    /// <remarks>
    /// An activation without a frame has no edit to go on in: the program waited in none, since what looks at the stack while it waits gives every activation
    /// its frame (<see cref="RuntimeCallStack.MaterializeLights"/>).
    /// </remarks>
    public bool WantsInterpreterAfterCall => _halt.WantsInterpreter || _frame is { HasPendingRebase: true } || !ReferenceEquals(_session.Symbols.Scopes, _scopes);

    /// <summary>
    /// Hands the activation to the interpreter at the call at <paramref name="pc"/> that did not return: with the error the callee raised, which the
    /// interpreter gives the activation's handler as it would have; with the program stopped in the callee, which stops this activation too; or, when compiled
    /// code could not make the call, to make it as it always does. The frame has every variable and every loop that was opened.
    /// </summary>
    public RuntimeExecutionOutcome AfterFailedCall(int pc)
    {
        var frame = Frame;
        frame.RunsCompiled = false;

        // the procedure edited while the program waited in the callee: the error is the call's, where the call now is.
        if (frame.ApplyPendingRebase(atTheCall: true))
        {
            pc = frame.Pc;
        }

        var list = Body;
        _body.WalkedBack(pc < list.Items.Length ? list.Items[pc].Location : null);
        if (_failed is not { } outcome)
        {
            frame.Pc = pc;
            return _executor.Run(_session, frame, list, _context);
        }

        return _executor.RunAfter(_session, frame, list, _context, pc, outcome);
    }

    /// <summary>
    /// The payload of the variable in <paramref name="slot"/>, of an integral kind, when its cell holds one it knows. When it does not, compiled code hands the
    /// activation to the interpreter, which reads the variable where the frame has it.
    /// </summary>
    public bool TryReadIntegral(int slot, out long value)
    {
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
            && _body.SlotSymbols.TryGetValue(slot, out var symbol) && symbol is ITypedSymbol { ResolvedType: var type } && held.Type.Equals(type)
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
    /// The cell numbered <paramref name="number"/> of a field of <c>Me</c> of a scalar type: the one the object holds it in, as the
    /// interpreter resolves it, which compiled code reads and writes it in.
    /// </summary>
    /// <param name="number">The number the body knows the field's cell by.</param>
    /// <returns><see langword="null"/> when the object holds it in no cell of its type: the activation is then the interpreter's.</returns>
    public object? FieldCellOf(int number)
    {
        // Me is the object the activation was entered on (EnterMemberDirectly), or the one its frame is a call on, which the interpreter resolves a field on.
        var instance = _instance ?? (Frame.Target is { } target && _session.Symbols.TryGetInstance(target, out var on) ? on : null);
        if (instance is null)
        {
            return null;
        }

        // the cell the body found last of the field of the same object (CompiledBody.Fields).
        var known = _body.Fields;
        if (known is null || !known.Object.Equals(instance.ObjectId))
        {
            known = new FieldCells(instance.ObjectId, new object?[_cells.Length]);
            _body.Fields = known;
        }

        if (known.Cells[number] is { } cached)
        {
            return cached;
        }

        var field = _body.SlotSymbols[_body.CellSlots[number]];
        var found = instance.TryResolve(field, out var held) && held is ScalarCell cell && cell.Type.Equals(((ITypedSymbol)field).ResolvedType) ? cell : null;
        known.Cells[number] = found;
        return found;
    }

    /// <summary>
    /// The cell of a field of <c>Me</c> (<see cref="FieldCellOf"/>), found again after the interpreter ran an instruction or the program waited.
    /// </summary>
    /// <param name="number">The number the body knows the field's cell by.</param>
    /// <returns><see langword="null"/> when the frame cannot be read, or the object holds the field in no cell of its type.</returns>
    public object? ReloadFieldCell(int number) => FailsAccessAtRandom() ? null : FieldCellOf(number);

    /// <summary>
    /// The cell the frame holds the <c>Variant</c> parameter or variable in <paramref name="slot"/> in: what compiled code reads and
    /// writes it in, for as long as the interpreter runs nothing of the activation. A parameter passed by reference that is bound to its caller's variable's
    /// address (<c>CallStackFrame.PushByRef</c>) is in the cell held at that address: the caller's variable.
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <returns>
    /// <see langword="null"/> when the frame holds it in no Variant's cell (a Variant parameter passed a <c>String</c> by value, a variable of another type
    /// passed by reference, an element of an array): compiled code reads and writes nothing of it (<see cref="VariantOperations.Read"/>), and the interpreter
    /// runs each statement that does.
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
        // Me, of a member entered without a frame: the cell the activation holds it in, which its frame is given when it has one (EnterMemberDirectly).
        if (_frame is null && _me is { } me && slot == _body.MeSlot)
        {
            return me;
        }

        var frame = Frame;
        return (frame.TryGetSlotCell(slot, out var cell) || frame.TryResolve(_body.SlotSymbols[slot], out cell)) && cell is ValueBindingHandle handle ? handle : null;
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
    /// The cell the frame holds the <c>String</c> parameter or variable in <paramref name="slot"/> in: what compiled code reads the text
    /// it holds from and writes the text it is assigned to (<see cref="StringOperations"/>), for as long as the interpreter runs nothing of the activation:
    /// the cell the interpreter's own evaluation of Strings reads and writes (<c>RuntimeExpressionEvaluator.BindingOf</c>).
    /// </summary>
    /// <param name="slot">The slot of the parameter or variable.</param>
    /// <returns>
    /// <see langword="null"/> when the frame holds it in no cell of its own: a parameter passed by reference bound to a variable of another type, which
    /// compiled code reads and writes nothing of, and the interpreter runs each statement that does.
    /// </returns>
    public object? StringCellOf(int slot)
    {
        var frame = Frame;
        return frame.Procedure is { } procedure
            && frame.TryReadSlot(new SDK.Semantics.Binding.FrameSlot(SDK.Semantics.Binding.FrameLayout.For(procedure), slot), _body.SlotSymbols[slot], out var cell, out var referenced)
            && referenced is null && cell is ValueBindingHandle handle
                ? handle
                : null;
    }

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
    /// (<c>RuntimeExpressionEvaluator.TryCompareText</c>): the mode of the frame the activation is, which is the current one.
    /// </summary>
    /// <param name="comparison">The comparison operator.</param>
    /// <param name="left">The left text; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <param name="right">The right text; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>The comparison's result.</returns>
    public bool CompareText(ComparisonOperator comparison, string? left, string? right)
    {
        var order = SDK.Runtime.RuntimeSessionExtensions.CurrentStringComparison(_session).Compare(left.AsSpan(), right.AsSpan());
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
    /// <param name="cell">The cell of the object variable tested.</param>
    /// <param name="number">The number the body knows the class by.</param>
    /// <param name="isOf">Whether the object is of the class.</param>
    /// <returns>
    /// <see langword="false"/> when it is the interpreter's: the cell holds no reference compiled code reads, or holds <c>Nothing</c>, of which <c>TypeOf</c>
    /// raises error 91.
    /// </returns>
    public bool TypeOf(object? cell, int number, out bool isOf)
    {
        isOf = false;
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
        if (_body.SlotSymbols.TryGetValue(slot, out var symbol) && symbol is ITypedSymbol { ResolvedType: var type } && ScalarPayload.IsScalar(type))
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
        // the procedure edited while the program waited in a callee: the activation goes on after the call, in the edited body.
        var frame = Frame;
        (frame.Pc, frame.RunsCompiled) = (pc, false);
        _ = frame.ApplyPendingRebase(atTheCall: false);
        var list = Body;
        pc = frame.Pc;
        _body.WalkedBack(pc < list.Items.Length ? list.Items[pc].Location : null);
        return _executor.Run(_session, frame, list, _context);
    }

    private RuntimeExecutionOutcome _recovered;

    /// <summary>
    /// Has the interpreter run the instruction at <paramref name="pc"/> alone - or, after a call there that raised an error, give the error to the
    /// activation's handler - and says where compiled code goes on (IR lowering S2.4): at the offset the interpreter left the activation at, every variable,
    /// the function result and each loop read back from the frame; or nowhere (<c>-1</c>), when the activation returned, the program stopped, or the
    /// interpreter goes on with it (<see cref="Recovered"/>). Every variable and every loop that was opened has been written into the frame.
    /// </summary>
    /// <remarks>
    /// The interpreter goes on with the activation when the program waited while the instruction ran - at its error, which a debugger asked to see - or the
    /// interpreter is wanted, or the symbols were defined again; and when the activation goes on inside a <c>For</c> loop the frame holds no state of, which
    /// compiled code would step with what it does not have. It runs the activation from <paramref name="pc"/> without stepping, when compiled code could not
    /// write a variable into the frame.
    /// </remarks>
    public int Recover(int pc)
    {
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
        var suspensions = Suspensions;
        var outcome = _executor.Step(_session, frame, list, _context, failed);
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
        LeaveBlocks(frame.Pc);
        return frame.Pc;
    }

    /// <summary>
    /// Lets go of what a block the activation is no longer in held at <paramref name="pc"/> - a <c>For Each</c>'s enumerator, or the array it reads - as the
    /// interpreter does before each instruction it runs (<c>ProcedureExecutor.RunInstructions</c>): after a step, and where a jump of compiled code out of
    /// a <c>For Each</c> lands.
    /// </summary>
    public void LeaveBlocks(int pc)
    {
        if (_frame is not { } frame)
        {
            return;
        }

        foreach (var holder in frame.TakeBlockHoldersOutside(pc))
        {
            ObjectReferences.Release(_session, holder);
        }
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
            if (pc > select.Opener && pc < select.End && !TryReadSelector(frame, select, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadSelector(CallStackFrame frame, CompiledSelect select, out ScalarPayload selector)
        => frame.TryGetSelectorPayload(select.Opener, out selector) && ScalarKinds.Of(selector.Type) == select.Kind;

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
    public bool OnError(int pc) => ProcedureExecutor.TryApplyOnError(_session, Frame, Body.Items[pc]);

    /// <summary>A <c>GoSub</c>: <paramref name="returnPc"/> pushed onto the activation's GoSub Resumption List (MS-VBAL §5.4.2.14).</summary>
    public void GoSub(int returnPc) => Frame.PushGoSubReturn(returnPc);

    /// <summary>A <c>Return</c>: the offset popped off the GoSub Resumption List; <see langword="false"/> when it is empty (MS-VBAL §5.4.2.15).</summary>
    public bool TryReturnFromGoSub(out int pc) => Frame.TryPopGoSubReturn(out pc);

    // the body the interpreter goes on in: the frame's, which an edit made while the program waited may have given it, the same instructions at the same
    // offsets as the one compiled code was compiled from (CallStackFrame.TryRebase).
    private InstructionList Body => _frame?.Body ?? _list;

    /// <summary>What an activation that reached its end, or an <c>Exit</c>, returns.</summary>
    public RuntimeExecutionOutcome Return()
    {
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
    /// they were on it, or they were handed to the interpreter (IR lowering S2.3b2). One called under a debugger, which is given its frame as it is called, is
    /// not counted.
    /// </summary>
    public long FramesMade => Interlocked.Read(ref _framesMade);

    internal void CountFrameMade() => Interlocked.Increment(ref _framesMade);

    private long _activationsMade;

    /// <summary>
    /// How many activations of the body were made for calls compiled code made directly without a frame: each call is made in the activation that last left
    /// its place of the stack when it can be, so a loop of calls makes one, and a recursion as many as it goes deep (IR lowering S2.3b2).
    /// </summary>
    public long ActivationsMade => Interlocked.Read(ref _activationsMade);

    internal void CountActivationMade() => Interlocked.Increment(ref _activationsMade);

    internal void CountIndirectCall() => Interlocked.Increment(ref _indirectCalls);

    /// <summary>The typed entry of the body: its compiled activation, then each parameter. Set once the body's type is created.</summary>
    public System.Reflection.MethodInfo? Entry { get; init; }

    /// <summary>
    /// The slots its activations hold in cells, by the number compiled code knows each cell by (<see cref="ProcedureAnalysis.CellSlots"/>): each parameter
    /// passed by reference, and each parameter or variable the body passes by reference (IR lowering S2.3c).
    /// </summary>
    public IReadOnlyList<int> CellSlots { get; init; } = [];

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
    /// The cells of the fields of <c>Me</c> the body found last: those of the object an activation of it was last on, which keeps
    /// them for as long as it lives (<see cref="FieldCells"/>). Another object's are found again, whatever class it is of. The cache keeps the object's
    /// identity and holds no reference to it.
    /// </summary>
    internal FieldCells? Fields { get; set; }

    /// <summary>The <c>Select Case</c> statements of a scalar selector, by the number compiled code knows each by.</summary>
    public IReadOnlyList<CompiledSelect> Selects { get; init; } = [];

    /// <summary>
    /// The classes the body's <c>TypeOf</c> expressions test against, by the number compiled code knows each by (<see cref="ProcedureAnalysis.Classes"/>).
    /// </summary>
    public IReadOnlyList<VBClassModuleSymbol> Classes { get; init; } = [];

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
    /// How many times an activation of the body had the interpreter run one instruction and went on compiled (IR lowering S2.4): an error its handler
    /// caught, a <c>Resume</c>, an <c>On...GoTo</c> (<see cref="CompiledActivation.Recover"/>).
    /// </summary>
    public long Recoveries => Interlocked.Read(ref _recovered);

    internal void CountRecovered() => Interlocked.Increment(ref _recovered);

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
/// A <c>Select Case</c> of a scalar selector, of a compiled body (MS-VBAL §5.4.2.10).
/// </summary>
/// <param name="Opener">The offset of its <c>Select</c>, which its selector is stashed by.</param>
/// <param name="End">The offset right past its block.</param>
/// <param name="Kind">The kind of its selector.</param>
internal sealed record class CompiledSelect(int Opener, int End, ScalarKind Kind);
