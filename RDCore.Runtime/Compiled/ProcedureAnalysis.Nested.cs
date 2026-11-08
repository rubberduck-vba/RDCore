using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Instructions;

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
            var step = AnalyzeStep(instruction);
            if (step is not null && _nesting is not null && !Admits(step))
            {
                Refuse(IneligibilityKind.Call, StatementNameOf(instruction.Node), instruction.Node?.SourceLocation);
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
        BoundMember { Member: VBFunctionMemberSymbol or VBProcedureMemberSymbol, BoundArguments: { } arguments } => arguments.Any(argument => Calls(argument.Value)),
        _ => Calls(node),
    };

    // whether evaluating node calls a procedure: a function, called with arguments or by its bare name, or a member of an object.
    private bool Calls(BoundNode? node) => node switch
    {
        null => false,
        BoundCall => true,
        BoundName { Symbol: VBFunctionMemberSymbol function } => !IsOwnFunction(function),
        BoundMember { Member: VBFunctionMemberSymbol } => true,
        BoundMember member => Calls(member.Owner) || member.BoundArguments is { } arguments && arguments.Any(argument => Calls(argument.Value)),
        BoundBinary binary => Calls(binary.Left) || Calls(binary.Right),
        BoundUnary unary => Calls(unary.Operand),
        BoundIndex index => Calls(index.Callee) || index.Arguments.Any(Calls),
        BoundTypeOf typeOf => Calls(typeOf.Operand),
        _ => false,
    };

    // a function of the workspace called inside an expression, as CallOf and MemberCallOf call one, by where it is in its statement: a function of a
    // standard module called with its arguments, or by its bare name when it takes none, or a function member of an object variable of the activation.
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
            _ => BareCallOf(function, kind),
        };

        return made is null ? Unsupported(node) : new CallNode(made.Callee, made.Arguments, made.Receiver, kind, syntax);
    }

    // a function of a standard module named by itself, which is a call of it given no argument (MS-VBAL §5.6.10).
    private CallStep? BareCallOf(VBTypeMemberSymbol function, ScalarKind kind)
    {
        if (function.GetProperty(SymbolProperties.Library) is not null || function.GetProperty(SymbolProperties.ExternalTarget) is not null
            || RuntimeProcedureInvoker.GetParameters(function) is not { Length: 0 } parameters)
        {
            return null;
        }

        return CallWith(function, parameters.AsSpan(), [], CallStep.NoResult, -1, kind, site: null, receiver: null);
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
    // read again by the interpreter after the call that may have changed it.
    private static bool Admits(CompiledStep step) => step switch
    {
        LetStep let => Admits(let.Value),
        CallStep call => call.Arguments.All(Admits),
        BranchStep branch => Admits(branch.Condition),
        LoopBackStep loop => Admits(loop.Condition),
        _ => false,
    };

    private static bool Admits(PayloadNode node) => node switch
    {
        KnownNode or ConstantNode or ReferenceNode => true,
        CallNode call => call.Arguments.All(Admits),
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

    private static bool Admits(ExtendedNode node) => node switch
    {
        WidenNode widen => Admits(widen.Operand),
        ExtendedOperationNode operation => Admits(operation.Left) && Admits(operation.Right),
        ExtendedNegateNode negate => Admits(negate.Operand),
        _ => false,
    };
}
