using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call inside an expression: a function of the workspace called for an operand, a condition, or an argument of another call.
internal sealed partial class ProcedureAnalysis
{
    // the statement of the instruction being translated, when it calls a function inside an expression: what compiled code evaluated of it is named by where
    // it is in that statement (KnownOperands.IndexOf), for the interpreter to be given when the instruction is handed to it after a call.
    private StatementNode? _nesting;

    // a Let of a scalar variable of the activation or of the function result variable, a call statement, or a condition of an If or a loop, that calls a
    // function inside an expression, is translated with what it reads and what it calls known (CallNode, KnownNode); and so is every other instruction, as
    // it always is.
    private CompiledStep? AnalyzeInstruction(Instruction instruction)
    {
        _nesting = NestingOf(instruction);
        try
        {
            var before = _reasons.Count;
            var step = AnalyzeStep(instruction);

            // a statement with a call for an object that compiled code cannot make is translated again without it, the interpreter making the call.
            if (step is not null && _nesting is not null && CallsForAnObject(step) && (!Admits(step) || ReadAndPassed(step) is not null))
            {
                _reasons.RemoveRange(before, _reasons.Count - before);
                _withoutObjectCalls = true;
                try
                {
                    step = AnalyzeStep(instruction);
                }
                finally
                {
                    _withoutObjectCalls = false;
                }
            }

            if (step is not null && _nesting is not null && !Admits(step))
            {
                Refuse(IneligibilityKind.Call, StatementNameOf(instruction.Node), instruction.Node?.SourceLocation);
                return null;
            }

            if (step is not null && _nesting is not null && ReadAndPassed(step) is { } read)
            {
                Refuse(IneligibilityKind.OperandPassedByReference, (KnownOperands.At(_nesting, read.Syntax) as SimpleNameExpressionNode)?.IdentifierName ?? string.Empty,
                    instruction.Node?.SourceLocation);
                return null;
            }

            return step;
        }
        finally
        {
            _nesting = null;
        }
    }

    private StatementNode? NestingOf(Instruction instruction)
    {
        var nests = instruction switch
        {
            { Kind: InstructionKind.Simple, Node: AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet, Target: SimpleNameExpressionNode } assignment }
                => IsScalarTarget(Bind(assignment.Target)) && CallsInside(Bind(assignment.Value)),
            { Kind: InstructionKind.Simple, Node: CallStatementNode call }
                => CallsInside(Bind(call.Arguments.IsEmpty ? call.Callee : new IndexExpressionNode(call.Callee.Identity, call.Callee.Location, call.Callee, call.Arguments))),
            { Kind: InstructionKind.ConditionalBranch, Matching: null } or { Kind: InstructionKind.LoopBack }
                => ConditionOf(instruction.Node) is ({ } condition, _) && Calls(Bind(condition)),
            // the start, the end and the step of a For, evaluated once, in that order, before the counter is given the start (MS-VBAL §5.4.2.3).
            { Kind: InstructionKind.ForOpener, Node: ForStatementNode forStatement }
                => Calls(Bind(forStatement.StartExpression)) || Calls(Bind(forStatement.EndExpression))
                || forStatement.StepExpression is { } step && Calls(Bind(step)),
            _ => false,
        };

        return nests ? instruction.Node : null;
    }

    private bool IsScalarTarget(BoundNode target) => target switch
    {
        BoundName { Symbol: var function } => ResultKind is not null && IsOwnFunction(function),
        BoundVariable variable => SlotOf(variable) is not null,
        _ => false,
    };

    // whether what is called for the whole value - a call statement, a function whose result is assigned - is given the value of a call; or, of anything
    // else, whether it calls.
    private bool CallsInside(BoundNode node) => node switch
    {
        BoundCall { BoundArguments: { } arguments } => arguments.Any(argument => Calls(argument.Value)),
        BoundMember { Member: VBFunctionMemberSymbol or VBProcedureMemberSymbol, BoundArguments: { } arguments } member
            => arguments.Any(argument => Calls(argument.Value)) || Calls(member.Owner),
        _ => Calls(node),
    };

    // whether evaluating node calls a procedure: a function, called with arguments or by its bare name, or a function or Property Get member of an object.
    private bool Calls(BoundNode? node) => node switch
    {
        null => false,
        BoundCall => true,
        BoundName { Symbol: VBFunctionMemberSymbol function } => !IsOwnFunction(function),
        // a member of Err compiled code reads where the session holds it calls nothing.
        BoundMember member when ErrNumberOf(member) is not null || ErrTextOf(member) is not null => false,
        BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } => true,
        BoundMember member => Calls(member.Owner) || member.BoundArguments is { } arguments && arguments.Any(argument => Calls(argument.Value)),
        BoundBinary binary => Calls(binary.Left) || Calls(binary.Right),
        BoundUnary unary => Calls(unary.Operand),
        BoundIndex index => Calls(index.Callee) || index.Arguments.Any(Calls),
        BoundTypeOf typeOf => Calls(typeOf.Operand),
        _ => false,
    };

    // a function of the workspace called inside an expression, as CallOf and MemberCallOf call one, by where it is in its statement: a function of a
    // standard module called with its arguments, or by its bare name when it takes none, or a function or Property Get member of an object variable of the
    // activation, of the default instance of a class, or of the object of the enclosing With block.
    private PayloadNode? NestedCall(BoundNode node, VBTypeMemberSymbol function)
    {
        var syntax = KnownOperands.IndexOf(_nesting!, node.Expression);
        if (ScalarKinds.Of((function as VBReturningMemberSymbol)?.ResolvedType) is not { } kind || syntax < 0)
        {
            return Unsupported(node);
        }

        var made = node switch
        {
            BoundCall call => CallOf(call, CallStep.NoResult, -1, kind),
            BoundMember member => MemberCallOf(member, CallStep.NoResult, -1, kind),
            _ => BareCallOf(function, kind, node.Expression.SourceLocation),
        };

        return made is null ? Unsupported(node) : new CallNode(made.Callee, made.Arguments, made.Receiver, kind, syntax)
        {
            WithAt = made.WithAt, ReceiverField = made.ReceiverField, ReceiverCall = made.ReceiverCall,
        };
    }

    // a function of a standard module named by itself, which is a call of it given no argument (MS-VBAL §5.6.10).
    private CallStep? BareCallOf(VBTypeMemberSymbol function, ScalarKind kind, SourceLocation at)
    {
        if (function.GetProperty(SymbolProperties.Library) is not null || function.GetProperty(SymbolProperties.ExternalTarget) is not null
            || RuntimeProcedureInvoker.GetParameters(function) is not { Length: 0 } parameters)
        {
            return null;
        }

        return CallWith(function, parameters.AsSpan(), [], CallStep.NoResult, -1, kind, site: null, receiver: null, assigns: false, at);
    }

    // a variable, or the function result variable, read in an instruction that calls inside an expression: known as it was read.
    private PayloadNode? Read(PayloadNode read, BoundNode node)
    {
        if (_nesting is null)
        {
            return read;
        }

        return KnownOperands.IndexOf(_nesting, node.Expression) is var syntax and >= 0 ? new KnownNode(read, syntax) : Unsupported(node);
    }

    // what an instruction that calls inside an expression is made of: payloads known as they were read, and the calls, and the operations of them. What
    // compiled code reads that the interpreter is not given known - a Variant, a String, an object, a field of a record, an element of an array - would be
    // read again by the interpreter after the call that may have changed it. A statement the interpreter runs as a support call is its own whole, calls
    // inside it included: compiled code evaluates nothing of it.
    private static bool Admits(CompiledStep step) => step switch
    {
        SupportCallStep => true,
        LetStep let => Admits(let.Value),
        CallStep call => call.Arguments.All(Admits) && Admits(call.ReceiverCall),
        BranchStep branch => Admits(branch.Condition),
        LoopBackStep loop => Admits(loop.Condition),
        ForOpenStep open => Admits(open.Start) && Admits(open.End) && Admits(open.Step),
        _ => false,
    };

    private static bool Admits(PayloadNode node) => node switch
    {
        KnownNode { Value: MemberFieldNode { ReceiverCall: { } returned } } => Admits(returned),
        ObjectArgumentNode { Call: { } returned } => Admits(returned),
        KnownNode or ConstantNode or ReferenceNode => true,
        CallNode call => call.Arguments.All(Admits) && Admits(call.ReceiverCall),
        ConvertNode convert => Admits(convert.Operand),
        IntegralNode integral => Admits(integral.Left) && Admits(integral.Right),
        LogicalNode logical => Admits(logical.Left) && Admits(logical.Right),
        CompareNode compare => Admits(compare.Left) && Admits(compare.Right),
        NotNode not => Admits(not.Operand),
        NegateNode negate => Admits(negate.Operand),
        StoreNode store => Admits(store.Value),
        ExtendedCompareNode compare => Admits(compare.Left) && Admits(compare.Right),
        TemporaryNode temporary => Admits(temporary.Value),
        _ => false,
    };

    // a call whose object a member is called on, or read of: none, or one made with arguments that are.
    private static bool Admits(ObjectCallNode? call) => call is null || call.Arguments.All(Admits) && Admits(call.ReceiverCall);

    private static bool Admits(ExtendedNode node) => node switch
    {
        WidenNode widen => Admits(widen.Operand),
        ExtendedOperationNode operation => Admits(operation.Left) && Admits(operation.Right),
        ExtendedNegateNode negate => Admits(negate.Operand),
        _ => false,
    };

    // a variable compiled code reads as it is before a call that may change it - one given it by reference, or given a parameter passed by reference, which
    // can be the same variable as one the procedure was given by reference, or any call, of a field - inside the same expression, where MS-VBA may read it
    // after the call: in an argument of a call,
    // whose value is taken when the call is made, or in an expression that computes anything of a type MS-VBA reads when its operation is computed. When it
    // is read there is the interpreter's to say.
    private KnownNode? ReadAndPassed(CompiledStep step)
    {
        var nodes = PayloadsOf(step).SelectMany(Within).ToList();
        // the arguments of each call: of one inside the expression, and of the call of the statement itself - a call statement, a function whose result
        // is assigned whole.
        var given = nodes.OfType<CallNode>().Select(call => call.Arguments).ToList();
        given.AddRange(nodes.SelectMany(ReceiverCallsOf).Select(call => call.Arguments));
        if (step is CallStep statement)
        {
            given.Add(statement.Arguments);
            given.AddRange(ObjectCallsOf(statement.ReceiverCall).Select(call => call.Arguments));
        }

        var passed = given.SelectMany(list => list.OfType<ReferenceNode>()).Select(reference => reference.Slot).ToHashSet();
        var arguments = new HashSet<KnownNode>(given.SelectMany(list => list.SelectMany(Within)).OfType<KnownNode>(), ReferenceEqualityComparer.Instance);
        var early = nodes.All(node => IsReadWhereWritten(node.Kind));

        var byReference = Parameters.Where(parameter => parameter.ByReference).Select(parameter => parameter.Slot).ToHashSet();
        var aliased = passed.Overlaps(byReference);
        return nodes.OfType<KnownNode>().FirstOrDefault(read => read.Value is SlotNode { Slot: var slot } && (!early || arguments.Contains(read))
            && (passed.Contains(slot) || aliased && byReference.Contains(slot) || FieldSlots.Contains(slot))
            // a public variable of an object, which any call may change, as a field.
            || read.Value is MemberFieldNode && (!early || arguments.Contains(read)));
    }

    // the types of an operand MS-VBA reads where it is written, before a call right of it changes it (RD-VBAL §5.6.9.2): what compiled code reads, and the
    // interpreter. An operand of another type is read when its operation is computed, which compiled code leaves to the interpreter.
    private static bool IsReadWhereWritten(ScalarKind kind)
        => kind is ScalarKind.Byte or ScalarKind.Boolean or ScalarKind.Integer or ScalarKind.Long or ScalarKind.LongLong or ScalarKind.Currency;

    // the calls whose objects a call or a read of a public variable is made on, the outermost first.
    private static IEnumerable<ObjectCallNode> ReceiverCallsOf(PayloadNode node) => node switch
    {
        CallNode call => ObjectCallsOf(call.ReceiverCall),
        MemberFieldNode field => ObjectCallsOf(field.ReceiverCall),
        ObjectArgumentNode argument => ObjectCallsOf(argument.Call),
        _ => [],
    };

    private static IEnumerable<PayloadNode> PayloadsOf(CompiledStep step) => step switch
    {
        LetStep let => [let.Value],
        CallStep call => [.. call.Arguments, .. ObjectCallsOf(call.ReceiverCall).SelectMany(made => made.Arguments)],
        BranchStep branch => [branch.Condition],
        LoopBackStep loop => [loop.Condition],
        ForOpenStep open => [open.Start, open.End, open.Step],
        _ => [],
    };

    // a payload and every payload it is made of.
    private static IEnumerable<PayloadNode> Within(PayloadNode node)
    {
        yield return node;
        var operands = node switch
        {
            CallNode call => [.. call.Arguments, .. ReceiverCallsOf(call).SelectMany(made => made.Arguments)],
            MemberFieldNode or ObjectArgumentNode => ReceiverCallsOf(node).SelectMany(made => made.Arguments),
            KnownNode known => [known.Value],
            ConvertNode convert => [convert.Operand],
            IntegralNode integral => [integral.Left, integral.Right],
            LogicalNode logical => [logical.Left, logical.Right],
            CompareNode compare => [compare.Left, compare.Right],
            NotNode not => [not.Operand],
            NegateNode negate => [negate.Operand],
            StoreNode store => Within(store.Value),
            ExtendedCompareNode compare => [.. Within(compare.Left), .. Within(compare.Right)],
            TemporaryNode temporary => [temporary.Value],
            _ => (IEnumerable<PayloadNode>)[],
        };

        foreach (var operand in operands.SelectMany(Within))
        {
            yield return operand;
        }
    }

    // the payloads an extended operation is made of.
    private static IEnumerable<PayloadNode> Within(ExtendedNode node) => node switch
    {
        WidenNode widen => [widen.Operand],
        ExtendedOperationNode operation => [.. Within(operation.Left), .. Within(operation.Right)],
        ExtendedNegateNode negate => Within(negate.Operand),
        _ => [],
    };
}
