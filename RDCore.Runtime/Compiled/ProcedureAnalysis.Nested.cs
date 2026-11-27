using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call inside an expression: a function of the workspace called for an operand, a condition, or an argument of another call.
internal sealed partial class ProcedureAnalysis
{
    // the statement of the instruction being translated, when it calls a function inside an expression: what compiled code evaluated of it is named by where
    // it is in that statement (KnownOperands.IndexOf), for the interpreter to be given when the instruction is handed to it after a call.
    private StatementNode? _nesting;

    // a Let of a scalar, String or Variant variable of the activation or of the function result variable, of a field of a record, of a member of an object,
    // or of an element of an array, a call statement, a Debug.Print, a condition of an If or a loop, the bounds of a For, the selector of a Select Case or
    // the values of a Case, that calls a function inside an expression, is translated with what it reads and what it calls known (CallNode, KnownNode).
    // Every other instruction is translated as any instruction is.
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
            // a Let of a String variable of the activation, or of the String result of the function, that calls a function of the workspace inside its value.
            { Kind: InstructionKind.Simple, Node: AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet, Target: SimpleNameExpressionNode } assignment }
                when IsTextTarget(Bind(assignment.Target))
                => CallsInsideText(Bind(assignment.Value)),
            { Kind: InstructionKind.Simple, Node: AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment }
                => IsNestedTarget(assignment.Target)
                && (CallsInside(Bind(assignment.Value)) || SubscriptsCall(assignment.Target)
                    || CallsForAnotherKind(Bind(assignment.Target), Bind(assignment.Value))),
            // a Debug.Print of an item that calls a function of the workspace.
            { Kind: InstructionKind.Simple, Node: DebugPrintStatementNode print }
                => print.Items.Any(item => item.Value is { } value and not (PrintSpcClauseNode or PrintTabClauseNode) && CallsWorkspace(Bind(value))),
            // a Set of an object variable, or of the function result variable, to what a call returns, or to a New, whose Initialize is a call.
            { Kind: InstructionKind.Simple, Node: AssignmentStatementNode { Kind: AssignmentKind.Set, Target: SimpleNameExpressionNode } assignment }
                => Bind(assignment.Value) is var value && (value is BoundNew || Calls(value)),
            // a Set of a property of an object to what a call returns, which is given to its Property Set (AccessorCallOf).
            { Kind: InstructionKind.Simple, Node: AssignmentStatementNode { Kind: AssignmentKind.Set, Target: MemberAccessExpressionNode } assignment }
                => Bind(assignment.Target) is BoundMember { Member: VBPropertyGetMemberSymbol or VBPropertySetMemberSymbol } && Calls(Bind(assignment.Value)),
            // a With of what a call returns, or of a New, whose Initialize is a call.
            { Kind: InstructionKind.With, Node: WithStatementNode with }
                => Bind(with.WithExpression) is var target && (target is BoundNew || Calls(target)),
            { Kind: InstructionKind.Simple, Node: CallStatementNode call }
                => CallsInside(Bind(call.Arguments.IsEmpty ? call.Callee : new IndexExpressionNode(call.Callee.Identity, call.Callee.Location, call.Callee, call.Arguments))),
            { Kind: InstructionKind.ConditionalBranch, Matching: null } or { Kind: InstructionKind.LoopBack }
                => ConditionOf(instruction.Node) is ({ } condition, _) && Calls(Bind(condition)),
            // the start, the end and the step of a For, evaluated once, in that order, before the counter is given the start (MS-VBAL §5.4.2.3).
            { Kind: InstructionKind.ForOpener, Node: ForStatementNode forStatement }
                => Calls(Bind(forStatement.StartExpression)) || Calls(Bind(forStatement.EndExpression))
                || forStatement.StepExpression is { } step && Calls(Bind(step)),
            // the selector of a Select Case, evaluated once (MS-VBAL §5.4.2.10); and the values of a Case, each evaluated as its clause is compared, in
            // order, until one matches.
            { Kind: InstructionKind.Select, Node: SelectCaseStatementNode selectCase } => Calls(Bind(selectCase.ControlExpression)),
            { Kind: InstructionKind.ConditionalBranch, Matching: not null, Node: CaseExpressionStatementNode caseBlock }
                => caseBlock.RangeClauses.SelectMany(ValuesOf).Any(value => Calls(Bind(value))),
            _ => false,
        };

        return nests ? instruction.Node : null;
    }

    private static IEnumerable<ExpressionNode> ValuesOf(CaseRangeClauseNode clause) => clause switch
    {
        CaseValueRangeClauseNode value => [value.Value],
        CaseComparisonRangeClauseNode comparison => [comparison.Value],
        CaseToRangeClauseNode range => [range.Start, range.End],
        _ => [],
    };

    // what a Let that calls inside its value, or inside a subscript of its target, assigns: a variable (IsScalarTarget); a field of a record, a member of an
    // object, or an element of an array of a scalar type or of Variants, each found once the value is evaluated, after its calls, as the interpreter finds
    // it (MS-VBAL §5.4.3.8). A String field of variable length is not among them: no step of these assigns it, and a Let of it is translated as any is.
    private bool IsNestedTarget(ExpressionNode target)
    {
        var bound = Bind(target);
        return target switch
        {
            SimpleNameExpressionNode => IsScalarTarget(bound),
            MemberAccessExpressionNode => IsMemberOfAnObject(bound) || RecordFieldOf(bound) is (_, var type) && !IsText(type),
            IndexExpressionNode => (FieldElementOf(bound) ?? bound) is BoundIndex index && ArrayOf(index.Callee) is { Class: ElementClass.Scalar or ElementClass.Variant },
            _ => false,
        };
    }

    // whether a subscript of the element a Let assigns calls (`this.State(Position.X, Position.Y) = Hit`): made after a value that calls is evaluated,
    // and before a value that calls nothing is read, as the interpreter makes it.
    private bool SubscriptsCall(ExpressionNode target)
        => target is IndexExpressionNode && (FieldElementOf(Bind(target)) ?? Bind(target)) is BoundIndex index && index.Arguments.Any(Calls);

    // a scalar variable of the activation or the scalar result variable, and a Variant variable of the activation or the Variant result variable, assigned
    // what was evaluated of it once: its Let made of the payload the evaluation gives (VariantLetStep, VariantResultLetStep), or of what a member of a class
    // of the standard library returns (LibraryMemberLetStep), never speculated, which would evaluate the value again where the speculation fails.
    private bool IsScalarTarget(BoundNode target) => target switch
    {
        BoundName { Symbol: var function } => (ResultKind is not null || ResultIsVariant) && IsOwnFunction(function),
        BoundVariable variable => SlotOf(variable) is not null || VariantSlotOf(variable) is not null,
        _ => false,
    };

    // the function of the workspace a Let calls for the whole value it assigns: a function of a standard module, or a function or Property Get member of an
    // object. None of the standard library, which compiled code calls otherwise.
    private VBTypeMemberSymbol? WholeCallOf(BoundNode value) => value switch
    {
        BoundCall { Procedure: VBTypeMemberSymbol called } when !IsCompiledLibrary(called) && !IsVariantLibrary(called) => called,
        BoundMember { Member: (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) and VBTypeMemberSymbol called } member
            when ErrNumberOf(member) is null && LibraryMemberCallOf(member) is null => called,
        _ => null,
    };

    // whether a function returns kind, or what is not a scalar: one that returns another scalar kind is called inside the value, as NestedCall calls one,
    // and what it returns is let-coerced to kind (MS-VBAL §5.4.3.8).
    private static bool ReturnsKind(VBTypeMemberSymbol function, ScalarKind kind)
        => ScalarKinds.Of(ReturnedType(function)) is not { } returned || returned == kind;

    // a Let of a scalar variable of the activation, or of the scalar result variable, of what a function returns of another scalar kind.
    private bool CallsForAnotherKind(BoundNode target, BoundNode value)
    {
        var kind = target switch
        {
            BoundName { Symbol: var function } when IsOwnFunction(function) => ResultKind,
            BoundVariable variable when SlotOf(variable) is { } slot => _slots[slot],
            _ => null,
        };

        return kind is { } assigned && WholeCallOf(value) is { } called && !ReturnsKind(called, assigned);
    }

    private bool IsTextTarget(BoundNode target) => target switch
    {
        BoundName { Symbol: var function } => ResultIsText && IsOwnFunction(function),
        BoundVariable variable => TextSlotOf(variable) is not null,
        _ => false,
    };

    // whether the value of a String Let calls a function of the workspace inside it. What is called for the whole value is a call step (AssignedCallOf),
    // which calls nothing inside it unless its arguments do.
    private bool CallsInsideText(BoundNode value) => value switch
    {
        BoundCall { BoundArguments: var arguments } => arguments?.Any(argument => CallsWorkspace(argument.Value)) ?? false,
        BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol, BoundArguments: var arguments }
            => arguments?.Any(argument => CallsWorkspace(argument.Value)) ?? false,
        BoundName { Symbol: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } => false,
        _ => CallsWorkspace(value),
    };

    // whether evaluating node calls a function of the workspace: a function of the standard library that compiled code calls for a String (Chr$, TypeName,
    // Replace) calls only what its arguments call.
    private bool CallsWorkspace(BoundNode? node) => node switch
    {
        null => false,
        BoundCall { Procedure: var library, BoundArguments: var arguments } when StandardLibrary.IsLibraryMember(library)
            => arguments?.Any(argument => CallsWorkspace(argument.Value)) ?? false,
        BoundBinary binary => CallsWorkspace(binary.Left) || CallsWorkspace(binary.Right),
        BoundUnary unary => CallsWorkspace(unary.Operand),
        _ => Calls(node),
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

    // whether evaluating node calls a procedure: a function, called with arguments or by its bare name, a Property Get named so, or a function or Property
    // Get member of an object.
    private bool Calls(BoundNode? node) => node switch
    {
        null => false,
        // a function of the standard library that keeps nothing calls nothing of the workspace: only what its arguments call. The interpreter makes it again
        // when the instruction is handed to it (LibraryCallOf). Named with no arguments (Now, VBA.DateTime.Timer), it calls nothing.
        BoundCall { Procedure: var library, BoundArguments: var arguments } when KeepsNothing(library) => arguments?.Any(argument => Calls(argument.Value)) ?? false,
        BoundCall => true,
        BoundName { Symbol: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } name => !IsOwnFunction(name.Symbol) && !KeepsNothing(name.Symbol),
        BoundQualifiedName { Member: var qualified } => IsCompiledLibrary(qualified) && !KeepsNothing(qualified),
        // a member of Err compiled code reads where the session holds it calls nothing.
        BoundMember member when ErrNumberOf(member) is not null || ErrTextOf(member) is not null => false,
        // a pure member of a class of the standard library compiled code calls on an object calls nothing of the workspace: only what its arguments call.
        // The interpreter makes it again when the instruction is handed to it (ProcedureAnalysis.LibraryMembers.cs).
        (BoundMember or BoundIndex) and var library when LibraryMemberCallOf(library) is { Arguments: var given } => given.Any(argument => Calls(argument.Value)),
        // the default member of an object of a class of the workspace is a call (ProcedureAnalysis.DefaultMembers.cs).
        BoundMember or BoundIndex when DefaultCallOf(node) is not null => true,
        BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } => true,
        // an element of an array field of a record, which calls what its subscripts call.
        BoundMember member when FieldElementOf(member) is { } element => Calls(element),
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
            BoundIndex or BoundMember when DefaultCallOf(node) is not null => DefaultMemberCallOf(node, CallStep.NoResult, -1, kind),
            BoundCall call => CallOf(call, CallStep.NoResult, -1, kind),
            BoundMember member => MemberCallOf(member, CallStep.NoResult, -1, kind),
            _ => BareCallOf(function, -1, kind, node.Expression),
        };

        return made is null ? Unsupported(node) : new CallNode(made.Callee, made.Arguments, made.Receiver, kind, syntax)
        {
            WithAt = made.WithAt, ReceiverField = made.ReceiverField, ReceiverCall = made.ReceiverCall,
        };
    }

    // the function or the Property Get of the workspace node calls, inside an instruction that calls, when it returns a String of variable length: called
    // with arguments, as a member of an object, or by its bare name (the procedure's own name is its result variable). None of the standard library, nor a
    // Declare.
    private VBTypeMemberSymbol? TextCalledBy(BoundNode node)
    {
        var function = _nesting is null ? null : node switch
        {
            BoundCall { Procedure: VBTypeMemberSymbol called } => called,
            BoundMember { Member: (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) and VBTypeMemberSymbol called } => called,
            BoundName { Symbol: (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) and VBTypeMemberSymbol called } when !IsOwnFunction(called) => called,
            _ => null,
        };

        return function is not null && !SymbolProperties.IsOfALibraryProject(function) && function.GetProperty(SymbolProperties.ExternalTarget) is null
            && IsText(ReturnedType(function))
                ? function
                : null;
    }

    // a function of the workspace that returns a String, called inside the text of an instruction that calls, as NestedCall calls one of a scalar type: what
    // it returns is the String itself (CompiledCallee.ReturnsText), which the interpreter is given as it is, by where the call is in its statement.
    private TextCallNode? TextCallOf(BoundNode node, VBTypeMemberSymbol function)
    {
        var syntax = KnownOperands.IndexOf(_nesting!, node.Expression);
        var made = syntax < 0 ? null : node switch
        {
            BoundCall call => CallOf(call, CallStep.NoResult, -1, resultKind: null, returnsText: true),
            BoundMember member => MemberCallOf(member, CallStep.NoResult, -1, resultKind: null, returnsText: true),
            _ => BareCallOf(function, -1, kind: null, node.Expression, returnsText: true),
        };

        if (made is null)
        {
            _ = Unsupported(node);
            return null;
        }

        return new TextCallNode(made.Callee, made.Arguments, made.Receiver, syntax)
        {
            WithAt = made.WithAt, ReceiverField = made.ReceiverField, ReceiverCall = made.ReceiverCall,
        };
    }

    // a function of the workspace that returns a String or a Variant, called inside an expression for the argument of a Variant parameter of the standard
    // library, which takes it by value, or, of a Variant, for what it holds read as a payload (Dynamic): called as NestedCall calls one, by where it is in its
    // statement, what it returns given to the parameter as the interpreter gives it (CallValueNode). Only in an instruction that calls inside an
    // expression, and not when the instruction is translated with the interpreter making its calls for objects (AnalyzeInstruction), whose temporaries
    // these are held by too.
    private CallValueNode? ValueCallOf(BoundNode node)
    {
        if (_nesting is null || _withoutObjectCalls
            || node is not (BoundCall or BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } or BoundName { Symbol: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol })
                && DefaultCallOf(node) is null
            || KnownOperands.IndexOf(_nesting, node.Expression) is not (>= 0 and var syntax))
        {
            return null;
        }

        // a call compiled code cannot make is the interpreter's: the reasons found making it are not the statement's.
        var before = _reasons.Count;
        var made = node switch
        {
            BoundIndex or BoundMember when DefaultCallOf(node) is not null
                => DefaultMemberCallOf(node, CallStep.NoResult, -1, resultKind: null, returnsValue: true),
            BoundCall call => CallOf(call, CallStep.NoResult, -1, resultKind: null, returnsValue: true),
            BoundMember member when LibraryMemberCallOf(member) is null => MemberCallOf(member, CallStep.NoResult, -1, resultKind: null, returnsValue: true),
            // a function, or a member of the class whose code runs, named by itself (BareCallOf); not the function's own result variable.
            BoundName { Symbol: VBTypeMemberSymbol named } when !IsOwnFunction(named) => BareCallOf(named, -1, kind: null, node.Expression, returnsValue: true),
            _ => null,
        };

        if (made is null)
        {
            _reasons.RemoveRange(before, _reasons.Count - before);
            return null;
        }

        return new CallValueNode(made.Callee, made.Arguments, made.Receiver, syntax)
        {
            WithAt = made.WithAt, ReceiverField = made.ReceiverField, ReceiverCall = made.ReceiverCall,
        };
    }

    // a function of a standard module named by itself, which is a call of it given no argument (MS-VBAL §5.6.10); and a member of the class whose code runs
    // named so, called on Me (OwnMemberReceiver): a Function or a Property Get for what it returns, whose result the statement assigns (AssignedCallOf), for
    // the object it returns (ObjectCallOf), for the String it returns inside a text (TextCallOf), or for what it returns to the standard library
    // (ValueCallOf), and a Sub or a Function as a statement, whose kind is null.
    private CallStep? BareCallOf(VBTypeMemberSymbol function, int offset, ScalarKind? kind, ExpressionNode named, bool assigns = false, bool returnsObject = false,
        bool returnsText = false, bool returnsValue = false)
    {
        var read = kind is not null || assigns || returnsObject || returnsText || returnsValue;
        if (SymbolProperties.IsOfALibraryProject(function) || function.GetProperty(SymbolProperties.ExternalTarget) is not null || IsDeclared(function))
        {
            return null;
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(function).AsSpan();
        int? receiver = null;
        if (parameters is [{ Name: Tokens.Me }, ..])
        {
            if (OwnMemberReceiver(function, named) is not { } me || function is VBPropertyGetMemberSymbol && !read)
            {
                return null;
            }

            parameters = parameters[1..];
            receiver = me;
        }
        else if (function is VBPropertyGetMemberSymbol || !read)
        {
            return null;
        }

        // each of its parameters Optional, left out as the interpreter binds it (MS-VBAL §5.3.1.11).
        System.Collections.Immutable.ImmutableArray<BoundArgument> arguments
            = parameters.IsEmpty ? [] : ArgumentMapping.Bind([.. parameters], [], static _ => null, static _ => null) ?? default;
        return !arguments.IsDefault && arguments.Length == parameters.Length
            ? CallWith(function, parameters, arguments, CallStep.NoResult, offset, kind, site: null, receiver, assigns, named.SourceLocation, returnsObject,
                returnsText, returnsValue)
            : null;
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
    // compiled code reads that the interpreter is not given known (a Variant, a String, an object) would be
    // read again by the interpreter after the call that may have changed it. A statement the interpreter runs as a support call is its own whole, calls
    // inside it included: compiled code evaluates nothing of it.
    private static bool Admits(CompiledStep step) => step switch
    {
        SupportCallStep => true,
        LetStep let => Admits(let.Value),
        VariantLetStep let => Admits(let.Value),
        FieldLetStep let => Admits(let.Value),
        VariantFieldLetStep let => Admits(let.Value),
        VariantResultLetStep let => Admits(let.Value),
        LibraryMemberLetStep let => Admits(let.Call),
        MemberFieldLetStep let => Admits(let.Value),
        MemberLetStep let => Admits(let.Value),
        ElementLetStep let => Admits(let.Value) && let.Element.Subscripts.All(Admits),
        VariantElementLetStep let => Admits(let.Value) && let.Element.Subscripts.All(Admits),
        CallStep call => call.Arguments.All(Admits) && Admits(call.ReceiverCall),
        SetStep { SourceCall: { } source } => Admits(source),
        SetStep { SourceNew: not null } => true,
        WithStep { SourceCall: { } source } => Admits(source),
        WithStep { SourceNew: not null } => true,
        BranchStep branch => Admits(branch.Condition),
        LoopBackStep loop => Admits(loop.Condition),
        ForOpenStep open => Admits(open.Start) && Admits(open.End) && Admits(open.Step),
        SelectStep select => Admits(select.Selector),
        CaseStep caseStep => caseStep.Clauses.All(clause => Admits(clause.Lower) && (clause.Upper is not { } upper || Admits(upper))),
        TextLetStep let => Admits(let.Value),
        TextResultLetStep let => Admits(let.Value),
        PrintStep print => Admits(print.Items),
        _ => false,
    };

    private static bool Admits(TextNode node) => Admits(new[] { node });

    // the items of a Debug.Print, which compiled code evaluates all before it writes the first. The interpreter writes each item before it evaluates the next
    // (MS-VBAL §5.4.5.8): an item after the first that calls is the interpreter's, since what the call writes, or an error it raises, comes after the items
    // left of it are written.
    private static bool Admits(IReadOnlyList<TextNode> texts)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            var calls = false;
            if (!Admits(texts[i], ref calls) || calls && i > 0)
            {
                return false;
            }
        }

        return true;
    }

    // the text of a String Let, made of what is admitted. A String compiled code reads (a variable, a public variable of an object, Err.Description) is
    // read when its operation is computed (RD-VBAL §5.6.9.2), after a call right of it that may change it: one read left of a call is the interpreter's
    // to read. The text is walked right to left, so that whether a call comes after a String is known when the String is reached.
    private static bool Admits(TextNode node, ref bool calledAfter)
    {
        switch (node)
        {
            case TextConstantNode:
                return true;

            case TextSlotNode or TextResultNode or MemberTextNode or ErrTextNode:
                return !calledAfter;

            case ConcatNode concat:
                return Admits(concat.Right, ref calledAfter) && Admits(concat.Left, ref calledAfter);

            case WholeTextNode { Value: var whole }:
                return Admits(whole, ref calledAfter);

            case FloatingTextNode { Value: var floating }:
                return Admits(floating, ref calledAfter);

            case ChrTextNode { Code: var code }:
                return Admits(code, ref calledAfter);

            // TypeName of an object field of a record: the field is read as the call is made. One with a call right of it is the interpreter's, which reads
            // the field before that call, since the call may put another object in it.
            case FieldTypeNameTextNode:
                return !calledAfter;

            // TypeName of an object variable or of Me: the object is read as the call is made. One with a call right of it is the interpreter's, which reads
            // the object before that call. Of Nothing, nothing is read.
            case TypeNameTextNode { Slot: var slot }:
                return slot is null || !calledAfter;

            // TypeName of a value (LibraryVariantTextOf, the only call of VariantTextLibraryFunctions inside such an instruction): its arguments evaluated
            // before the call.
            case LibraryVariantTextNode { Arguments: var arguments }:
                calledAfter |= arguments.SelectMany(OperandsOf).SelectMany(Within).Any(each => each is CallNode || ReceiverCallsOf(each).Any());
                return arguments.All(Admits);

            // a call that returns a String, made after the calls among its arguments: a String left of it is read after it.
            case TextCallNode call:
                calledAfter = true;
                return call.Arguments.All(Admits) && Admits(call.ReceiverCall);

            default:
                return false;
        }
    }

    private static bool Admits(PayloadNode payload, ref bool calledAfter)
    {
        calledAfter |= Within(payload).Any(each => each is CallNode || ReceiverCallsOf(each).Any() || ValueCallsOf(each).Any());
        return Admits(payload);
    }

    // an object given to a call is read as the call is made, after the calls left of it, where the interpreter reads it (CompiledActivation.SetObject).
    private static bool Admits(PayloadNode node) => node switch
    {
        KnownNode { Value: MemberFieldNode { ReceiverCall: { } returned } } => Admits(returned),
        // an element, read once its subscripts are evaluated, calls among them.
        KnownNode { Value: ElementNode element } => element.Subscripts.All(Admits),
        ObjectArgumentNode { Call: { } returned } => Admits(returned),
        // the selector of a Select Case is what its Select evaluated, which no call changes.
        KnownNode or ConstantNode or ReferenceNode or VariantReferenceNode or ObjectArgumentNode or ObjectReferenceNode or SelectorNode or OmittedArgumentNode => true,
        CallNode call => call.Arguments.All(Admits) && Admits(call.ReceiverCall),
        LibraryCallNode library => library.Arguments.All(argument => argument is VariantOfNode { Value: var value } && Admits(value) || argument is CallValueNode && Admits(argument)),
        ConvertNode convert => Admits(convert.Operand),
        IntegralNode integral => Admits(integral.Left) && Admits(integral.Right),
        LogicalNode logical => Admits(logical.Left) && Admits(logical.Right),
        PowerNode power => Admits(power.Left) && Admits(power.Right),
        CompareNode compare => Admits(compare.Left) && Admits(compare.Right),
        NotNode not => Admits(not.Operand),
        NegateNode negate => Admits(negate.Operand),
        StoreNode store => Admits(store.Value),
        ExtendedCompareNode compare => Admits(compare.Left) && Admits(compare.Right),
        ExtendedSignNode sign => Admits(sign.Value),
        TemporaryNode temporary => Admits(temporary.Value),
        UnvariantNode unvariant => Admits(unvariant.Value),
        TruthNode truth => Admits(truth.Value),
        // what a call returns, compared with Nothing or with what another returns: an object read (of a variable, a field, an element) is not known.
        ElementIsNode compared => Admits(compared.Left) && Admits(compared.Right),
        // a String given to a call by value: its text, evaluated as the call is made, where the interpreter evaluates it, when it calls nothing. A text that
        // calls, and a String variable given by reference, which the call may change before the expression reads it where compiled code does not see a
        // read of it (ReadAndPassed), are the interpreter's.
        TextArgumentNode { Value: var text } => CallsNothing(text),
        _ => false,
    };

    private static bool CallsNothing(TextNode text)
    {
        var calls = false;
        return Admits(text, ref calls) && !calls;
    }

    private static bool Admits(ObjectOperand? operand) => operand switch
    {
        null => true,
        ObjectCallOperand { Call: var returned } => Admits(returned),
        _ => false,
    };

    // a Variant computed of what is admitted, what a function or a member of a class of the standard library returns of it, and what a function of the
    // workspace returns, which is known once it is made; a Variant read (of a variable, a field, an element) is not known, and would be read again.
    private static bool Admits(VariantNode node) => node switch
    {
        VariantOfNode of => Admits(of.Value),
        VariantBinaryNode binary => Admits(binary.Left) && Admits(binary.Right),
        VariantUnaryNode unary => Admits(unary.Operand),
        LibraryVariantNode library => library.Arguments.All(Admits),
        LibraryMemberNode member => member.Arguments.All(Admits),
        CallValueNode call => call.Arguments.All(Admits) && Admits(call.ReceiverCall),
        _ => false,
    };

    // a call whose object a member is called on, or read of: none, or one made with arguments that are.
    private static bool Admits(ObjectCallNode? call) => call is null || call.Arguments.All(Admits) && Admits(call.ReceiverCall);

    private static bool Admits(ExtendedNode node) => node switch
    {
        WidenNode widen => Admits(widen.Operand),
        ExtendedOperationNode operation => Admits(operation.Left) && Admits(operation.Right),
        ExtendedNegateNode negate => Admits(negate.Operand),
        ExtendedApplyNode applied => Admits(applied.Argument),
        _ => false,
    };

    // a variable compiled code reads as it is before a call that may change it - one given it by reference, or given a parameter passed by reference, which
    // can be the same variable as one the procedure was given by reference, or any call, of a field - inside the same expression, where MS-VBA may read it
    // after the call: in an argument of a call,
    // whose value is taken when the call is made, or in an expression that computes anything of a type MS-VBA reads when its operation is computed. When it
    // is read there is the interpreter's to say. A read a String Let or a Debug.Print makes after every call of the statement is not (ReadAfterEveryCall).
    private KnownNode? ReadAndPassed(CompiledStep step)
    {
        var after = ReadAfterEveryCall(step);
        var nodes = PayloadsOf(step).SelectMany(Within).ToList();
        // the arguments of each call: of one inside the expression, and of the call of the statement itself - a call statement, a function whose result
        // is assigned whole.
        var given = nodes.OfType<CallNode>().Select(call => call.Arguments).ToList();
        given.AddRange(nodes.SelectMany(ReceiverCallsOf).Select(call => call.Arguments));
        foreach (var call in TextCallsOf(step))
        {
            given.Add(call.Arguments);
            given.AddRange(ObjectCallsOf(call.ReceiverCall).Select(made => made.Arguments));
        }

        given.AddRange(ValueCallsOf(step).Select(call => call.Arguments));
        given.AddRange(ValueCallsOf(step).SelectMany(call => ObjectCallsOf(call.ReceiverCall)).Select(call => call.Arguments));
        if (step is CallStep statement)
        {
            given.Add(statement.Arguments);
            given.AddRange(ObjectCallsOf(statement.ReceiverCall).Select(call => call.Arguments));
        }

        if (((step as SetStep)?.SourceCall ?? (step as WithStep)?.SourceCall) is { } source)
        {
            given.AddRange(ObjectCallsOf(source).Select(call => call.Arguments));
        }

        var passed = given.SelectMany(list => list.OfType<ReferenceNode>().Select(reference => reference.Slot)
            .Concat(list.OfType<VariantReferenceNode>().Select(reference => reference.Slot))).ToHashSet();
        var arguments = new HashSet<KnownNode>(given.SelectMany(list => list.SelectMany(Within)).OfType<KnownNode>(), ReferenceEqualityComparer.Instance);
        // a whole number joined to a String is an operand of an operation of the String type, which is read when it is computed.
        var early = step is not (TextLetStep or TextResultLetStep or PrintStep) && nodes.All(node => IsReadWhereWritten(node.Kind));

        var byReference = Parameters.Where(parameter => parameter.ByReference).Select(parameter => parameter.Slot).ToHashSet();
        var aliased = passed.Overlaps(byReference);
        return nodes.OfType<KnownNode>().FirstOrDefault(read => !after.Contains(read) && (read.Value is SlotNode { Slot: var slot } && (!early || arguments.Contains(read))
            && (passed.Contains(slot) || aliased && byReference.Contains(slot) || FieldSlots.Contains(slot))
            // a field of a record, an element of an array, and a public variable of an object, which any call may change, as a field.
            || read.Value is FieldNode or ElementNode or MemberFieldNode && (!early || arguments.Contains(read))));
    }

    // the reads of a String Let or of the items of a Debug.Print that no call of the statement comes after, in the order the text is evaluated: the left of
    // a & before its right, an item before the next. MS-VBA reads such an operand when its operation is computed (RD-VBAL §5.6.9.2), after every call,
    // and so does compiled code: it reads it where it is written, after the calls, in the cell the variable is held in. A Static has module extent and
    // outlives the activation (MS-VBAL §5.4.3.1, §2.3), so its cell is the one a call of the procedure itself writes it in. What a call left it is
    // what each reads. A text compiled code does not know of is taken to call: what is read left of it is left to the rule above.
    private static HashSet<KnownNode> ReadAfterEveryCall(CompiledStep step)
    {
        var after = new HashSet<KnownNode>(ReferenceEqualityComparer.Instance);
        IReadOnlyList<TextNode> texts = step switch
        {
            TextLetStep let => [let.Value],
            TextResultLetStep let => [let.Value],
            PrintStep print => print.Items,
            _ => [],
        };

        var calledAfter = false;
        for (var i = texts.Count - 1; i >= 0; i--)
        {
            Collect(texts[i]);
        }

        return after;

        // the text walked right to left, so that whether a call comes after a read is known when the read is reached.
        void Collect(TextNode node)
        {
            switch (node)
            {
                case TextConstantNode or TextSlotNode or MemberTextNode or ErrTextNode or TypeNameTextNode:
                    return;
                case ConcatNode concat:
                    Collect(concat.Right);
                    Collect(concat.Left);
                    return;
                case WholeTextNode { Value: var whole }:
                    CollectPayload(whole);
                    return;
                case FloatingTextNode { Value: var floating }:
                    CollectPayload(floating);
                    return;
                case ChrTextNode { Code: var code }:
                    CollectPayload(code);
                    return;
                default:
                    calledAfter = true;
                    return;
            }
        }

        // a payload that calls is read before its own call, or as its argument: none of its reads is after every call.
        void CollectPayload(PayloadNode payload)
        {
            var within = Within(payload).ToList();
            if (within.Exists(each => each is CallNode || ReceiverCallsOf(each).Any()))
            {
                calledAfter = true;
            }
            else if (!calledAfter)
            {
                after.UnionWith(within.OfType<KnownNode>());
            }
        }
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
        ElementIsNode compared => [.. ObjectCallsOf((compared.Left as ObjectCallOperand)?.Call), .. ObjectCallsOf((compared.Right as ObjectCallOperand)?.Call)],
        _ => [],
    };

    private static IEnumerable<PayloadNode> PayloadsOf(CompiledStep step) => step switch
    {
        LetStep let => [let.Value],
        VariantLetStep let => OperandsOf(let.Value),
        FieldLetStep let => [let.Value],
        VariantFieldLetStep let => OperandsOf(let.Value),
        VariantResultLetStep let => OperandsOf(let.Value),
        LibraryMemberLetStep let => OperandsOf(let.Call),
        MemberFieldLetStep let => [let.Value],
        MemberLetStep let => OperandsOf(let.Value),
        // the subscripts of the element assigned too, read after the value's calls, and before their own.
        ElementLetStep let => [let.Value, .. let.Element.Subscripts],
        VariantElementLetStep let => [.. OperandsOf(let.Value), .. let.Element.Subscripts],
        CallStep call => [.. call.Arguments, .. ObjectCallsOf(call.ReceiverCall).SelectMany(made => made.Arguments)],
        SetStep { SourceCall: { } source } => ObjectCallsOf(source).SelectMany(made => made.Arguments),
        WithStep { SourceCall: { } source } => ObjectCallsOf(source).SelectMany(made => made.Arguments),
        BranchStep branch => [branch.Condition],
        LoopBackStep loop => [loop.Condition],
        ForOpenStep open => [open.Start, open.End, open.Step],
        SelectStep select => [select.Selector],
        CaseStep caseStep => caseStep.Clauses.SelectMany(clause => clause.Upper is { } upper ? new[] { clause.Lower, upper } : [clause.Lower]),
        TextLetStep let => PayloadsOf(let.Value),
        TextResultLetStep let => PayloadsOf(let.Value),
        PrintStep print => print.Items.SelectMany(PayloadsOf),
        _ => [],
    };

    // the payloads a text is made of: the numbers joined to it, the codes of Chr$, the arguments of TypeName of a value, and what the calls that
    // return a String are given.
    private static IEnumerable<PayloadNode> PayloadsOf(TextNode node) => node switch
    {
        ConcatNode concat => [.. PayloadsOf(concat.Left), .. PayloadsOf(concat.Right)],
        WholeTextNode whole => [whole.Value],
        FloatingTextNode floating => [floating.Value],
        ChrTextNode chr => [chr.Code],
        LibraryVariantTextNode library => library.Arguments.SelectMany(OperandsOf),
        TextCallNode call => [.. call.Arguments, .. ObjectCallsOf(call.ReceiverCall).SelectMany(made => made.Arguments)],
        _ => [],
    };

    // the calls that return a String inside the text of a String Let or of the items of a Debug.Print.
    private static IEnumerable<TextCallNode> TextCallsOf(CompiledStep step) => step switch
    {
        TextLetStep let => TextCallsOf(let.Value),
        TextResultLetStep let => TextCallsOf(let.Value),
        PrintStep print => print.Items.SelectMany(TextCallsOf),
        _ => [],
    };

    private static IEnumerable<TextCallNode> TextCallsOf(TextNode node) => node switch
    {
        ConcatNode concat => [.. TextCallsOf(concat.Left), .. TextCallsOf(concat.Right)],
        TextCallNode call => [call],
        _ => [],
    };

    // a payload and every payload it is made of.
    private static IEnumerable<PayloadNode> Within(PayloadNode node)
    {
        yield return node;
        var operands = node switch
        {
            CallNode call => [.. call.Arguments, .. ReceiverCallsOf(call).SelectMany(made => made.Arguments)],
            MemberFieldNode or ObjectArgumentNode or ElementIsNode => ReceiverCallsOf(node).SelectMany(made => made.Arguments),
            LibraryCallNode library => library.Arguments.Where(argument => argument is VariantOfNode or CallValueNode).SelectMany(OperandsOf),
            KnownNode known => [known.Value],
            ElementNode element => element.Subscripts,
            ConvertNode convert => [convert.Operand],
            IntegralNode integral => [integral.Left, integral.Right],
            LogicalNode logical => [logical.Left, logical.Right],
            PowerNode power => [power.Left, power.Right],
            CompareNode compare => [compare.Left, compare.Right],
            NotNode not => [not.Operand],
            NegateNode negate => [negate.Operand],
            StoreNode store => Within(store.Value),
            ExtendedCompareNode compare => [.. Within(compare.Left), .. Within(compare.Right)],
            ExtendedSignNode sign => Within(sign.Value),
            TemporaryNode temporary => [temporary.Value],
            UnvariantNode unvariant => OperandsOf(unvariant.Value),
            TruthNode truth => OperandsOf(truth.Value),
            _ => (IEnumerable<PayloadNode>)[],
        };

        foreach (var operand in operands.SelectMany(Within))
        {
            yield return operand;
        }
    }

    // the payloads a Variant is computed of.
    private static IEnumerable<PayloadNode> OperandsOf(VariantNode node) => node switch
    {
        VariantOfNode of => [of.Value],
        VariantBinaryNode binary => [.. OperandsOf(binary.Left), .. OperandsOf(binary.Right)],
        VariantUnaryNode unary => OperandsOf(unary.Operand),
        LibraryVariantNode library => library.Arguments.SelectMany(OperandsOf),
        LibraryMemberNode member => member.Arguments.SelectMany(OperandsOf),
        CallValueNode call => [.. call.Arguments, .. ObjectCallsOf(call.ReceiverCall).SelectMany(made => made.Arguments)],
        _ => [],
    };

    // the calls of the workspace that return a String or a Variant (CallValueNode) a step makes, each once.
    private static IEnumerable<CallValueNode> ValueCallsOf(CompiledStep step)
    {
        var variants = step switch
        {
            VariantLetStep let => [let.Value],
            VariantFieldLetStep let => [let.Value],
            VariantResultLetStep let => [let.Value],
            LibraryMemberLetStep let => [let.Call],
            MemberLetStep let => [let.Value],
            VariantElementLetStep let => [let.Value],
            _ => (IEnumerable<VariantNode>)[],
        };

        // a text's too, in a String Let or an item of a Debug.Print: the Variant arguments of a function of the standard library that returns a String
        // (TypeName, CStr), through every part of a concatenation.
        var texts = step switch
        {
            TextLetStep let => [let.Value],
            TextResultLetStep let => [let.Value],
            PrintStep print => print.Items,
            _ => (IEnumerable<TextNode>)[],
        };

        return variants.SelectMany(ValueCallsOf).Concat(texts.SelectMany(ValueCallsOf)).Concat(PayloadsOf(step).SelectMany(Within).SelectMany(ValueCallsOf))
            .Distinct<CallValueNode>(ReferenceEqualityComparer.Instance);
    }

    private static IEnumerable<CallValueNode> ValueCallsOf(TextNode node) => node switch
    {
        ConcatNode concat => [.. ValueCallsOf(concat.Left), .. ValueCallsOf(concat.Right)],
        LibraryVariantTextNode library => library.Arguments.SelectMany(ValueCallsOf),
        _ => [],
    };

    // the calls of the workspace that return a String or a Variant given to a function of the standard library a payload is what it returns of.
    private static IEnumerable<CallValueNode> ValueCallsOf(PayloadNode node)
        => node is LibraryCallNode library ? library.Arguments.SelectMany(ValueCallsOf) : [];

    private static IEnumerable<CallValueNode> ValueCallsOf(VariantNode node) => node switch
    {
        CallValueNode call => [call],
        VariantBinaryNode binary => [.. ValueCallsOf(binary.Left), .. ValueCallsOf(binary.Right)],
        VariantUnaryNode unary => ValueCallsOf(unary.Operand),
        LibraryVariantNode library => library.Arguments.SelectMany(ValueCallsOf),
        LibraryMemberNode member => member.Arguments.SelectMany(ValueCallsOf),
        _ => [],
    };

    // the payloads an extended operation is made of.
    private static IEnumerable<PayloadNode> Within(ExtendedNode node) => node switch
    {
        WidenNode widen => [widen.Operand],
        ExtendedOperationNode operation => [.. Within(operation.Left), .. Within(operation.Right)],
        ExtendedNegateNode negate => Within(negate.Operand),
        ExtendedApplyNode applied => Within(applied.Argument),
        _ => [],
    };
}
