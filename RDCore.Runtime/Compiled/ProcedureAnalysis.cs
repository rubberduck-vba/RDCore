using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// Whether a procedure is compiled, and what it compiles to: the instructions of its body, each as the payload operations
/// it is made of, in the order the interpreter's evaluation on payloads makes them.
/// </summary>
/// <remarks>
/// <para>
/// Each expression is translated as <c>RuntimeExpressionEvaluator</c> evaluates it on payloads - <c>TryEvaluatePayload</c>, <c>TryEvaluatePayloadAs</c>,
/// <c>TryEvaluateWide</c> - case for case: what the interpreter evaluates boxed, compiled code does not do, and a procedure that has any is interpreted, with
/// the reason recorded. What the interpreter's evaluation declines at run time - a conversion that is not exact, an operation that overflows - compiled code
/// declines too, and hands the activation to the interpreter at that instruction (<see cref="CompiledActivation.Interpret"/>).
/// </para>
/// <para>
/// Compiled code does a procedure of a standard module whose parameters are scalars passed by value and whose variables are scalars: literals,
/// constants, arithmetic, comparisons and the logical operators; <c>Let</c>; <c>If</c>, <c>Do</c>, <c>While</c> and <c>For</c>; <c>Exit</c>; and calls of
/// procedures of the workspace whose parameters are scalars passed by value - a call statement, or a function's result assigned whole.
/// </para>
/// </remarks>
internal sealed class ProcedureAnalysis
{
    private readonly IRuntimeSession _session;
    private readonly RuntimeExpressionEvaluator _evaluator;
    private readonly VBTypeMemberSymbol _procedure;
    private readonly FrameLayout _layout;
    private readonly List<Ineligibility> _reasons = [];
    private readonly Dictionary<int, ScalarKind> _slots = [];
    private readonly List<CompiledForLoop> _loops = [];
    private readonly Dictionary<int, int> _loopOfOpener = [];

    private ProcedureAnalysis(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol procedure)
    {
        (_session, _evaluator, _procedure) = (session, evaluator, procedure);
        _layout = FrameLayout.For(procedure);
    }

    /// <summary>Why the procedure is not compiled; empty when it is.</summary>
    public IReadOnlyList<Ineligibility> Reasons => _reasons;

    /// <summary>The kind of each slot: its parameters', then its variables'.</summary>
    public IReadOnlyDictionary<int, ScalarKind> Slots => _slots;

    /// <summary>The slot of each parameter, in the order they are declared, and its kind.</summary>
    public List<(int Slot, ScalarKind Kind)> Parameters { get; } = [];

    /// <summary>The kind of the function result variable; <see langword="null"/> for a <c>Sub</c>.</summary>
    public ScalarKind? ResultKind { get; private set; }

    /// <summary>The <c>For</c> loops, numbered.</summary>
    public IReadOnlyList<CompiledForLoop> Loops => _loops;

    /// <summary>The instructions, compiled.</summary>
    public List<CompiledStep> Steps { get; } = [];

    /// <summary>The instruction count of the body: the offset its end is at.</summary>
    public int Length { get; private set; }

    /// <summary>
    /// Analyzes <paramref name="procedure"/>, whose body is <paramref name="list"/>, as the session binds it now.
    /// </summary>
    public static ProcedureAnalysis Of(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol procedure, InstructionList list)
    {
        var analysis = new ProcedureAnalysis(session, evaluator, procedure);
        analysis.Analyze(list);
        return analysis;
    }

    private void Analyze(InstructionList list)
    {
        Length = list.Items.Length;
        if (!AnalyzeSignature())
        {
            return;
        }

        foreach (var instruction in list.Items)
        {
            if (AnalyzeInstruction(instruction) is { } step)
            {
                Steps.Add(step);
            }

            // the reasons of one instruction are enough to say why: the rest is not translated, which would only find more.
            if (_reasons.Count > 0)
            {
                return;
            }
        }
    }

    private void Refuse(IneligibilityKind kind, string detail, SourceLocation? location = null) => _reasons.Add(new Ineligibility(kind, detail, location));

    // where a declaration is, which is what a reason it gives is reported at.
    private static SourceLocation LocationOf(BoundTypedSymbol declared) => new(declared.Uri, declared.Range);

    private static SourceLocation LocationOf(VBTypeMemberSymbol declared) => new(declared.Uri, declared.Range);

    // the procedure itself: a Sub, a Function or a Property Get of a standard module, whose parameters are scalars by value and whose variables scalars.
    private bool AnalyzeSignature()
    {
        if (_procedure is not (VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol))
        {
            Refuse(IneligibilityKind.MemberOfAClass, _procedure.Name, LocationOf(_procedure));
            return false;
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(_procedure);
        if (parameters is [{ Name: "Me" }, ..])
        {
            Refuse(IneligibilityKind.MemberOfAClass, _procedure.Name, LocationOf(_procedure));
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter is ParamArrayParameterSymbol || RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind))
            {
                Refuse(IneligibilityKind.ParameterByReference, parameter.Name, LocationOf(parameter));
                continue;
            }

            if (ScalarKinds.Of(parameter.ResolvedType) is not { } kind || _layout.ParameterSlots[i] < 0)
            {
                Refuse(IneligibilityKind.NotAScalar, $"{parameter.Name} As {parameter.ResolvedType.Name}", LocationOf(parameter));
                continue;
            }

            _slots[_layout.ParameterSlots[i]] = kind;
            Parameters.Add((_layout.ParameterSlots[i], kind));
        }

        var locals = RuntimeProcedureInvoker.GetLocals(_procedure);
        for (var i = 0; i < locals.Length; i++)
        {
            switch (locals[i])
            {
                case VBLocalConstantSymbol:
                    continue;
                case VBLocalVariableSymbol { IsStatic: true } variable:
                    Refuse(IneligibilityKind.StaticVariable, variable.Name, LocationOf(variable));
                    continue;
                case VBLocalVariableSymbol variable when ScalarKinds.Of(variable.ResolvedType) is { } kind && _layout.LocalSlots[i] >= 0:
                    _slots[_layout.LocalSlots[i]] = kind;
                    continue;
                default:
                    Refuse(IneligibilityKind.NotAScalar, $"{locals[i].Name} As {locals[i].ResolvedType.Name}", LocationOf(locals[i]));
                    continue;
            }
        }

        if (_procedure is VBReturningMemberSymbol returning)
        {
            if (ScalarKinds.Of(returning.ResolvedType) is not { } result)
            {
                Refuse(IneligibilityKind.NotAScalar, $"{_procedure.Name} As {returning.ResolvedType.Name}", LocationOf(_procedure));
            }
            else
            {
                ResultKind = result;
            }
        }

        return _reasons.Count == 0;
    }

    private CompiledStep? AnalyzeInstruction(Instruction instruction)
    {
        var location = instruction.Node?.SourceLocation;
        switch (instruction.Kind)
        {
            case InstructionKind.Simple when instruction.Node is AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment:
                return AnalyzeLet(instruction.Offset, assignment);

            case InstructionKind.Simple when instruction.Node is CallStatementNode call:
                return Bind(call.Arguments.IsEmpty ? call.Callee : new IndexExpressionNode(call.Callee.Identity, call.Callee.Location, call.Callee, call.Arguments))
                    is BoundCall bound && CallOf(bound, CallStep.NoResult, instruction.Offset, resultKind: null) is { } made
                    ? made
                    : RefusedCall(call.Callee, location);

            case InstructionKind.Simple:
                Refuse(IneligibilityKind.Statement, StatementNameOf(instruction.Node), location);
                return null;

            case InstructionKind.ConditionalBranch when instruction.Matching is null && instruction.Else is { } otherwise:
                return ConditionOf(instruction.Node) is ({ } condition, var negate) && Condition(condition) is { } value
                    ? new BranchStep(instruction.Offset, value, negate, otherwise)
                    : Refused(instruction);

            case InstructionKind.LoopBack when instruction.Target is { } target:
                return ConditionOf(instruction.Node) is ({ } loopCondition, var loopNegate) && Condition(loopCondition) is { } loopValue
                    ? new LoopBackStep(instruction.Offset, loopValue, loopNegate, target)
                    : Refused(instruction);

            case InstructionKind.Jump or InstructionKind.ExitLoop when instruction.Node is GoToStatementNode:
                Refuse(IneligibilityKind.GoTo, "GoTo", location);
                return null;

            case InstructionKind.Jump or InstructionKind.ExitLoop when instruction.Target is { } jumpTarget:
                return new JumpStep(instruction.Offset, jumpTarget);

            case InstructionKind.ForOpener when instruction.Node is ForStatementNode forStatement && instruction.End is { } past:
                return AnalyzeForOpener(instruction.Offset, forStatement, past);

            case InstructionKind.ForNext when instruction.Matching is { } opener && instruction.Target is { } body && _loopOfOpener.TryGetValue(opener, out var loop):
                return new ForNextStep(instruction.Offset, loop, CounterSlotOf(loop), _loops[loop].Kind, body);

            case InstructionKind.ExitProcedure:
                return new ExitStep(instruction.Offset);

            default:
                return Refused(instruction);
        }
    }

    // what a statement is called: its node's type, less what every node's type ends with - DebugPrint, Assignment, Open.
    private static string StatementNameOf(StatementNode? node)
    {
        var name = node?.GetType().Name ?? nameof(InstructionKind.Simple);
        foreach (var suffix in new[] { "StatementNode", "Node" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    private CompiledStep? Refused(Instruction instruction)
    {
        if (_reasons.Count == 0)
        {
            Refuse(IneligibilityKind.Instruction, instruction.Kind.ToString(), instruction.Node?.SourceLocation);
        }

        return null;
    }

    // MS-VBAL §5.4.3.8, as TryAssignUnboxed assigns: a scalar variable of the activation, or the function result variable, given its value let-coerced to
    // its type.
    private CompiledStep? AnalyzeLet(int offset, AssignmentStatementNode assignment)
    {
        if (assignment.Target is not SimpleNameExpressionNode)
        {
            Refuse(IneligibilityKind.AssignmentTarget, assignment.Target.GetType().Name, assignment.SourceLocation);
            return null;
        }

        // a function called for the whole value assigned, of the kind of what it is assigned to, is a call step: nothing of it can fail after the call.
        var assigned = Bind(assignment.Value);
        switch (Bind(assignment.Target))
        {
            case BoundName { Symbol: var function } when ResultKind is { } result && IsOwnFunction(function):
                return assigned is BoundCall resultCall
                    ? CallOf(resultCall, -1, offset, result) ?? RefusedCall(resultCall.Call.Callee, assignment.SourceLocation)
                    : PayloadAs(assigned, result) is { } value ? new LetStep(offset, -1, value) : null;
            case BoundVariable variable when SlotOf(variable) is { } slot:
                return assigned is BoundCall slotCall
                    ? CallOf(slotCall, slot, offset, _slots[slot]) ?? RefusedCall(slotCall.Call.Callee, assignment.SourceLocation)
                    : PayloadAs(assigned, _slots[slot]) is { } slotValue ? new LetStep(offset, slot, slotValue) : null;
            default:
                Refuse(IneligibilityKind.AssignmentTarget, ((SimpleNameExpressionNode)assignment.Target).IdentifierName, assignment.SourceLocation);
                return null;
        }
    }

    /// <summary>The procedures the body calls, by the number its call steps know each by.</summary>
    public List<CompiledCallee> Callees { get; } = [];

    // a call of a procedure of the workspace in a standard module - no Me to give it - every parameter a scalar passed by value and given an argument of
    // its kind; of a function returning resultKind when what it returns is read. RuntimeExpressionEvaluator.TryInvokeScalars makes the same call.
    private CallStep? CallOf(BoundCall call, int result, int offset, ScalarKind? resultKind)
    {
        if (call is not { Procedure: VBTypeMemberSymbol procedure, BoundArguments: { } arguments, ResultType: null }
            || procedure.GetProperty(SymbolProperties.Library) is not null || procedure.GetProperty(SymbolProperties.ExternalTarget) is not null
            || procedure is not (VBProcedureMemberSymbol or VBFunctionMemberSymbol))
        {
            return null;
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(procedure);
        if (parameters.Length != arguments.Length || parameters is [{ Name: "Me" }, ..])
        {
            return null;
        }

        // what is returned is read in its own kind, as it is assigned: a Function of another kind, or a Sub whose result is read, is not this call.
        if (resultKind is { } read && (procedure is not VBFunctionMemberSymbol { ResolvedType: var returned } || ScalarKinds.Of(returned) != read))
        {
            return null;
        }

        var kinds = new List<ScalarKind>(parameters.Length);
        var values = new List<PayloadNode>(parameters.Length);
        for (var i = 0; i < parameters.Length; i++)
        {
            if (RuntimeProcedureInvoker.IsByRef(parameters[i].ParameterKind) || parameters[i] is ParamArrayParameterSymbol
                || ScalarKinds.Of(parameters[i].ResolvedType) is not { } kind
                || arguments[i] is not { Passing: ArgumentPassing.Value, Value: { } argument } || PayloadAs(argument, kind) is not { } value)
            {
                return null;
            }

            kinds.Add(kind);
            values.Add(value);
        }

        Callees.Add(new CompiledCallee(procedure, kinds, resultKind));
        return new CallStep(offset, Callees.Count - 1, values, result);
    }

    private CompiledStep? RefusedCall(ExpressionNode callee, SourceLocation? location)
    {
        if (_reasons.Count == 0)
        {
            Refuse(IneligibilityKind.Call, (callee as SimpleNameExpressionNode)?.IdentifierName ?? callee.GetType().Name, location);
        }

        return null;
    }

    private bool IsOwnFunction(Symbol symbol) => ReferenceEquals(symbol, _procedure) || symbol.Uri.AbsoluteUri == _procedure.Uri.AbsoluteUri && symbol.Name == _procedure.Name;

    // MS-VBAL §5.4.2.3, as ProcedureExecutor.ExecuteForOpener and TryStepForUnboxed do it: a counter of the activation, which counts in its own type.
    private CompiledStep? AnalyzeForOpener(int offset, ForStatementNode forStatement, int past)
    {
        if (forStatement.ControlExpression is not SimpleNameExpressionNode || Bind(forStatement.ControlExpression) is not BoundVariable counter
            || SlotOf(counter) is not { } slot)
        {
            Refuse(IneligibilityKind.ForCounter, (forStatement.ControlExpression as SimpleNameExpressionNode)?.IdentifierName ?? string.Empty, forStatement.SourceLocation);
            return null;
        }

        var kind = _slots[slot];
        if (kind is ScalarKind.Boolean)
        {
            Refuse(IneligibilityKind.ForCounter, counter.Variable.Name, forStatement.SourceLocation);
            return null;
        }

        // MS-VBAL 5.4.2.3: "If no <step-clause> is present, the <step-increment> value is the integer data value 1", let-coerced to the counter's type.
        _ = PayloadOperations.Convert(ScalarKind.Integer, 1, 0, kind, out var one, out var oneFloating);
        var start = PayloadAs(Bind(forStatement.StartExpression), kind);
        var end = PayloadAs(Bind(forStatement.EndExpression), kind);
        var step = forStatement.StepExpression is { } stepExpression ? PayloadAs(Bind(stepExpression), kind) : new ConstantNode(kind, one, oneFloating);
        if (start is null || end is null || step is null)
        {
            return null;
        }

        var loop = _loops.Count;
        _loops.Add(new CompiledForLoop(offset, counter.Variable, forStatement.ControlExpression, kind));
        _loopOfOpener[offset] = loop;
        _counterSlots.Add(slot);
        return new ForOpenStep(offset, loop, slot, kind, start, end, step, past);
    }

    private readonly List<int> _counterSlots = [];

    private int CounterSlotOf(int loop) => _counterSlots[loop];

    private BoundNode Bind(ExpressionNode expression) => _evaluator.BindInProcedure(_session, expression, _procedure);

    // a variable of the activation, by its slot in the procedure's layout.
    private int? SlotOf(BoundVariable variable)
        => variable is { IsAutoInstantiated: false, Slot: { } slot } && ReferenceEquals(slot.Layout, _layout) && _slots.ContainsKey(slot.Index) ? slot.Index : null;

    // the condition of an If, an ElseIf, an inline If and a loop, and whether it is an Until (ProcedureExecutor.GetCondition).
    private static (ExpressionNode? Condition, bool Negate) ConditionOf(StatementNode? node) => node switch
    {
        IfBlockStatementNode ifBlock => (ifBlock.ConditionExpression, false),
        ElseIfBlockStatementNode elseIf => (elseIf.ConditionExpression, false),
        InlineIfStatementNode inlineIf => (inlineIf.ConditionExpression, false),
        WhileWendStatementNode whileWend => (whileWend.ConditionExpression, false),
        DoWhileLoopStatementNode doWhile => (doWhile.ConditionExpression, false),
        DoUntilLoopStatementNode doUntil => (doUntil.ConditionExpression, true),
        DoLoopWhileStatementNode doLoopWhile => (doLoopWhile.ConditionExpression, false),
        DoLoopUntilStatementNode doLoopUntil => (doLoopUntil.ConditionExpression, true),
        _ => (null, false),
    };

    // TryEvaluateConditionUnboxed: an extended intermediate converted to a Boolean from its 80 bits, anything else evaluated as it is; its truth is that it
    // is not zero.
    private PayloadNode? Condition(ExpressionNode condition)
    {
        var node = Bind(condition);
        return IsExtended(node) ? PayloadAs(node, ScalarKind.Boolean) : Payload(node);
    }

    private static bool IsExtended(BoundNode node) => RuntimeExpressionEvaluator.ExtendedTypeOf(node) is not null;

    private static ScalarKind? ExtendedKindOf(BoundNode node) => ScalarKinds.Of(RuntimeExpressionEvaluator.ExtendedTypeOf(node));

    // TryEvaluatePayloadAs: an extended intermediate converted to the destination from its 80 bits, anything else coerced from its payload.
    private PayloadNode? PayloadAs(BoundNode node, ScalarKind destination)
    {
        if (IsExtended(node))
        {
            return Extended(node) is { } extended ? new StoreNode(extended, destination) : null;
        }

        return Payload(node) is { } payload ? Converted(payload, destination) : null;
    }

    // an exact conversion, made once when it is of a constant: one that declines then declines every time, which the interpreter is handed.
    private static PayloadNode Converted(PayloadNode payload, ScalarKind destination)
    {
        if (payload.Kind == destination)
        {
            return payload;
        }

        if (payload is ConstantNode constant
            && PayloadOperations.Convert(constant.Kind, constant.Integral, constant.Floating, destination, out var integral, out var floating) is PayloadStatus.Ok)
        {
            return new ConstantNode(destination, integral, floating);
        }

        return new ConvertNode(payload, destination);
    }

    // TryEvaluatePayload, case for case.
    private PayloadNode? Payload(BoundNode node)
    {
        switch (node)
        {
            case BoundBinary or BoundUnary when IsExtended(node):
                return Extended(node) is { } extended ? new StoreNode(extended, ExtendedKindOf(node)!.Value) : null;

            case BoundBinary binary when IsExtended(binary.Left) || IsExtended(binary.Right):
                return PlanOf(binary) is { } extendedPlan ? OperateWithExtended(binary, extendedPlan) : null;

            case BoundUnary { Plan: { IsDeferred: false, OperandDestination: { } destination } plan } unary when IsExtended(unary.Operand):
                return ScalarKinds.Of(destination) is { } to && PayloadAs(unary.Operand, to) is { } converted ? OperateUnary(plan, converted, unary) : Unsupported(unary);

            case BoundLiteral literal:
                return ScalarKinds.Of(literal.Literal.StaticValue.TypeInfo) is { } literalKind
                    && ScalarPayload.TryOf(literal.Literal.StaticValue.TypeInfo, literal.Literal.StaticValue.RuntimeValue, out var payload)
                        ? new ConstantNode(literalKind, payload.Integral, payload.Floating)
                        : Unsupported(literal);

            case BoundVariable variable:
                return SlotOf(variable) is { } slot ? new SlotNode(slot, _slots[slot]) : Unsupported(variable);

            case BoundName { Symbol: var symbol } when RuntimeExpressionEvaluator.IsConstant(symbol):
                return Constant(node, symbol);

            case BoundQualifiedName { Member: var member } when RuntimeExpressionEvaluator.IsConstant(member):
                return Constant(node, member);

            // MS-VBAL §5.3.1: the name of a function in its own body is its function result variable, which compiled code keeps in a local of its own.
            case BoundName { Symbol: var function } when IsOwnFunction(function) && ResultKind is { } result:
                return new ResultNode(result);

            case BoundUnary { Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }:
                return Payload(enclosed);

            case BoundUnary { Plan: { IsDeferred: false } plan, Operand: var operand } unary when plan.Operator is Tokens.NegationOp or Tokens.LogicalNotOp:
                return Payload(operand) is { } value ? OperateUnary(plan, value, unary) : null;

            case BoundBinary binary:
                return PlanOf(binary) is { } binaryPlan && Payload(binary.Left) is { } left && Payload(binary.Right) is { } right
                    ? Operate(binaryPlan, left, right, binary)
                    : null;

            default:
                return Unsupported(node);
        }
    }

    private PayloadNode? Constant(BoundNode node, Symbol constant)
    {
        if (!_evaluator.TryFoldConstant(_session, node, constant, out var payload) || ScalarKinds.Of(payload.Type) is not { } kind)
        {
            return Unsupported(node);
        }

        return new ConstantNode(kind, payload.Integral, payload.Floating);
    }

    // the plan of an operation of declared types: a plan deferred to the values - of a Variant, an object - is the boxed evaluation's.
    private OperationPlan? PlanOf(BoundBinary binary)
    {
        if (binary.Plan is { IsDeferred: false } plan)
        {
            return plan;
        }

        Refuse(binary.Plan is null ? IneligibilityKind.LateBound : IneligibilityKind.VariantOperand, binary.Operation.Token, binary.Expression.Location);
        return null;
    }

    private PayloadNode? Unsupported(BoundNode node)
    {
        var (kind, detail) = node switch
        {
            BoundCall call => (IneligibilityKind.Call, call.Procedure.Name),
            BoundMember member => (IneligibilityKind.Member, member.Access.Member.IdentifierName),
            BoundIndex => (IneligibilityKind.Element, string.Empty),
            BoundLate or BoundError => (IneligibilityKind.LateBound, (node.Expression as SimpleNameExpressionNode)?.IdentifierName ?? string.Empty),
            BoundLiteral literal => (IneligibilityKind.Literal, literal.Literal.StaticValue.TypeInfo.Name),
            BoundVariable variable => (IneligibilityKind.ForeignVariable, variable.Variable.Name),
            BoundBinary { Plan: { } plan } => (IneligibilityKind.Operator, plan.Operator),
            BoundUnary { Plan: { } plan } => (IneligibilityKind.Operator, plan.Operator),
            _ => (IneligibilityKind.LateBound, node.GetType().Name),
        };

        Refuse(kind, detail, node.Expression.Location);
        return null;
    }

    // TryOperateWithExtended: a comparison of a floating-point effective type compares the extended values at its precision; any other operation is given
    // each operand let-coerced to the type it coerces them to.
    private PayloadNode? OperateWithExtended(BoundBinary binary, OperationPlan plan)
    {
        if (ExtendedIntermediates.IsComparison(plan.Operator) && ExtendedIntermediates.IsFloating(plan.EffectiveType))
        {
            return ExtendedOperand(binary.Left) is { } left && ExtendedOperand(binary.Right) is { } right
                ? new ExtendedCompareNode(OperatorTokens.ComparisonOf(plan.Operator)!.Value, left, right, ScalarKinds.Of(plan.EffectiveType)!.Value)
                : null;
        }

        if (ScalarKinds.Of(plan.OperandDestination) is not { } destination)
        {
            return Unsupported(binary);
        }

        return PayloadAs(binary.Left, destination) is { } l && PayloadAs(binary.Right, destination) is { } r ? Operate(plan, l, r, binary) : null;
    }

    // TryOperateBinary: each operand converted to the type the plan coerces them to, then the operation of the effective type - an integral operation, a
    // comparison or a logical one.
    private PayloadNode? Operate(OperationPlan plan, PayloadNode left, PayloadNode right, BoundBinary binary)
    {
        if (ScalarKinds.Of(plan.OperandDestination) is not { } destination || ScalarKinds.Of(plan.EffectiveType) is not { } effective)
        {
            return Unsupported(binary);
        }

        var (l, r) = (Converted(left, destination), Converted(right, destination));
        if (effective is not ScalarKind.Boolean && ScalarKinds.IsIntegral(effective)
            && plan.Operator is Tokens.AdditionOp or Tokens.SubtractionOp or Tokens.MultiplicationOp or Tokens.IntegerDivisionOp or Tokens.ModuloOp)
        {
            return new IntegralNode(plan.Operator, l, r, effective);
        }

        if (OperatorTokens.ComparisonOf(plan.Operator) is { } comparison)
        {
            return new CompareNode(comparison, l, r);
        }

        if (OperatorTokens.LogicalOf(plan.Operator) is { } logical && ScalarKinds.IsIntegral(destination))
        {
            return new LogicalNode(logical, l, r, effective);
        }

        return Unsupported(binary);
    }

    // TryOperateUnary: the operand converted to the effective type, then negated or inverted.
    private PayloadNode? OperateUnary(OperationPlan plan, PayloadNode operand, BoundUnary unary)
    {
        if (ScalarKinds.Of(plan.EffectiveType) is not { } effective)
        {
            return Unsupported(unary);
        }

        var converted = Converted(operand, effective);
        return plan.Operator == Tokens.NegationOp
            ? new NegateNode(converted, effective)
            : ScalarKinds.IsIntegral(effective) ? new NotNode(converted, effective) : Unsupported(unary);
    }

    // TryEvaluateWide: an operation or a negation of extended operands, in parentheses or not.
    private ExtendedNode? Extended(BoundNode node)
    {
        switch (node)
        {
            case BoundBinary { Plan: { } plan } binary:
                return ExtendedOperand(binary.Left) is { } left && ExtendedOperand(binary.Right) is { } right ? new ExtendedOperationNode(plan.Operator, left, right) : null;
            case BoundUnary { Plan: not null } unary:
                return ExtendedOperand(unary.Operand) is { } operand ? new ExtendedNegateNode(operand) : null;
            case BoundUnary { Plan: null } unary:
                return Extended(unary.Operand);
            default:
                Unsupported(node);
                return null;
        }
    }

    // TryEvaluateWideOperand: an extended intermediate, or a payload widened.
    private ExtendedNode? ExtendedOperand(BoundNode operand)
        => IsExtended(operand) ? Extended(operand) : Payload(operand) is { } payload ? new WidenNode(payload) : null;
}
