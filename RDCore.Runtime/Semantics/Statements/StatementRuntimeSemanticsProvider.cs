using RDCore.External.Forms;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.LanguageCore.Static;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// Executes one <see cref="StatementNode"/> against a real session/frame, dispatching by the node's own
/// C# type — the statement analogue of <see cref="RDCore.Runtime.Semantics.RuntimeExpressionEvaluator"/>.
/// </summary>
public interface IStatementRuntimeSemanticsProvider
{
    /// <summary>
    /// Executes <paramref name="statement"/>, producing the control-flow outcome the executor loop
    /// reacts to.
    /// </summary>
    RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement);
}

/// <inheritdoc cref="IStatementRuntimeSemanticsProvider"/>
public sealed class StatementRuntimeSemanticsProvider : IStatementRuntimeSemanticsProvider
{
    private readonly RuntimeExpressionEvaluator _expressionEvaluator;
    private readonly LetAssignmentEvaluator _assignments;
    private readonly ISetCoercionRuntimeSemantics _setCoercion;
    private readonly PrintOutputEvaluator _printOutput;
    private readonly ConditionEvaluator _conditions;
    private readonly FileStatementRuntimeSemantics _files;
    private readonly FixedAssignmentRuntimeSemantics _fixedAssignment;
    private readonly ArrayStatementRuntimeSemantics _arrays;
    private readonly MidStatementRuntimeSemantics _mid;

    public StatementRuntimeSemanticsProvider(RuntimeExpressionEvaluator expressionEvaluator, LetAssignmentEvaluator assignments, ISetCoercionRuntimeSemantics setCoercion, PrintOutputEvaluator printOutput, ConditionEvaluator conditions, FileStatementRuntimeSemantics files, FixedAssignmentRuntimeSemantics fixedAssignment, ArrayStatementRuntimeSemantics arrays, MidStatementRuntimeSemantics mid)
    {
        _expressionEvaluator = expressionEvaluator;
        _assignments = assignments;
        _setCoercion = setCoercion;
        _printOutput = printOutput;
        _conditions = conditions;
        _files = files;
        _fixedAssignment = fixedAssignment;
        _arrays = arrays;
        _mid = mid;
    }

    /// <inheritdoc/>
    public RuntimeExecutionOutcome Execute(IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement)
        => statement switch
        {
            AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment => ExecuteLetAssignment(session, context, assignment),
            AssignmentStatementNode { Kind: AssignmentKind.Set } assignment => ExecuteSetAssignment(session, context, assignment),
            // MS-VBAL §5.4.3.6-7: LSet and RSet fit a value into the target's own current width.
            AssignmentStatementNode { Kind: AssignmentKind.LSet or AssignmentKind.RSet } assignment => _fixedAssignment.Execute(session, context, assignment),
            // MS-VBAL §5.4.3.5: Mid replaces a span of the characters (or bytes) of a string variable.
            MidStatementNode mid => _mid.Execute(session, context, mid),
            // MS-VBAL §5.4.3.3-4: the statements that change an array's shape.
            RedimDeclarationNode redim => _arrays.ExecuteRedim(session, context, redim),
            KeywordStatementNode { Token: Tokens.Erase } erase => _arrays.ExecuteErase(session, context, erase),
            // Debug.Print: MS-VBAL §5.4.5.8's output rules, against the session's output rather than a
            // file. The parser gives these a node of their own, so this is a type test rather than a
            // match on the spelling of a call's owner - and a build that lowers them away never gets
            // here at all.
            DebugPrintStatementNode print => _printOutput.Execute(session, context, print.Items),
            DebugAssertStatementNode assert => ExecuteAssert(session, context, assert),
            // MS-VBAL §5.4.5.1/.2: the statements that associate and disassociate a file number.
            OpenStatementNode open => OnFiles(_files.ExecuteOpen(session, context, open)),
            KeywordStatementNode { Token: Tokens.Close or Tokens.Reset } close => OnFiles(_files.ExecuteClose(session, context, close)),
            // MS-VBAL §5.4.5.8: Print to a file channel. The channel exists now; writing to one does not yet,
            // and the bare object-relative form needs an enclosing form or report, which does not either.
            // MS-VBAL §5.4.5.8-9. The bare object-relative form prints on the Visual Basic 6.0 form whose code it is.
            PrintStatementNode bare when PrintsOnMe(session, context, bare, out var me) => PrintOn(session, context, me!, null, bare.Items, bare),
            PrintStatementNode print => OnFiles(_files.ExecutePrint(session, context, print)),
            // MS-VBAL §5.4.5.6: Line Input # reads one line and Let-assigns it.
            KeywordStatementNode { Token: Tokens.LineInput } lineInput => OnFiles(_files.ExecuteLineInput(session, context, lineInput)),
            // MS-VBAL §5.4.5.10: Input # reads a field per variable and Let-assigns each.
            KeywordStatementNode { Token: Tokens.Input } input => OnFiles(_files.ExecuteInput(session, context, input)),
            // MS-VBAL §5.4.5.3/.7: the statements that reposition a channel and set its line width.
            KeywordStatementNode { Token: Tokens.Seek } seek => OnFiles(_files.ExecuteSeek(session, context, seek)),
            KeywordStatementNode { Token: Tokens.Width } width => OnFiles(_files.ExecuteWidth(session, context, width)),
            // RD-VBAL §5.4.5.13: Name renames a file or a directory; it is not in MS-VBAL.
            KeywordStatementNode { Token: Tokens.Name } name => OnFiles(_files.ExecuteName(session, context, name)),
            // MS-VBAL §5.4.5.11-12: the record statements, which move bytes rather than characters.
            KeywordStatementNode { Token: Tokens.Put } put => OnFiles(_files.ExecutePut(session, context, put)),
            KeywordStatementNode { Token: Tokens.Get } get => OnFiles(_files.ExecuteGet(session, context, get)),
            // MS-VBAL §5.4.5.4-5: Lock and Unlock, which share a node because they share a record range.
            FileLockStatementNode fileLock => OnFiles(_files.ExecuteLock(session, context, fileLock)),
            // MS-VBAL §5.4.2.20: invokes the procedures that handle an event of the object whose code this is.
            KeywordStatementNode { Token: Tokens.RaiseEvent } raise => ExecuteRaiseEvent(session, context, raise),
            // RD-VBAL: Circle, Line, PSet and Scale are methods of an object that can be drawn on.
            GraphicsMethodStatementNode graphics => ExecuteGraphics(session, context, graphics),
            // the Print method of an object (Form1.Print): Debug.Print is its own statement.
            CallStatementNode { Callee: ObjectPrintExpressionNode print } => ExecuteObjectPrint(session, context, print),
            CallStatementNode call => ExecuteCall(session, context, call),
            _ => RuntimeExecutionOutcome.InternalError,
        };

    /// <summary>
    /// The observation of the analysis the statements are evaluated for, if they are: told that a file statement relied on what it does not know.
    /// </summary>
    public AnalysisObservation? Observation { get; init; }

    // the files and the channels a file statement works with are the outside world's, and the state the statements before it left them in, which an analysis
    // does not know to be what the code would find: what the statement raises is not known to be the code's.
    private RuntimeExecutionOutcome OnFiles(RuntimeExecutionOutcome outcome)
    {
        Observation?.Assume();
        return outcome;
    }

    // a graphics statement calls the method of its name on the object it draws on, a Visual Basic 6.0 form or PictureBox, as VB6 compiles it: the
    // method takes every argument its type library declares, after GraphicsFlags, which say what the syntax said. An object that has no such method raises 438.
    // A statement with no object is the form's own in a Visual Basic 6.0 form's code, and never gets here anywhere else (GraphicsStatementStaticSemantics).
    private RuntimeExecutionOutcome ExecuteGraphics(IRuntimeSession session, RuntimeEvaluationContext context, GraphicsMethodStatementNode graphics)
    {
        VBTypedValue? owner = null;
        var ownerExpression = graphics.Target;
        if (graphics.Target is { } target)
        {
            var result = _expressionEvaluator.Evaluate(session, target, context);
            if (!result.IsSuccess)
            {
                return result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
            }

            owner = result.Result;
        }
        else if (graphics.IsWithRelative)
        {
            owner = context.EnclosingWithTarget;
            ownerExpression = context.EnclosingWithTargetExpression;
        }
        else if (session.Symbols.Resolver.ResolveValue(Tokens.Me, ScopeKind.Local, context.Scope).Symbol is { } me)
        {
            // written with no object, in a Visual Basic 6.0 form's code: the form's own.
            owner = new VBObjectValue(session.Symbols.Resolver.GetValue(me));
        }

        if (owner is null)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, graphics.SourceLocation, $"The object has no {graphics.Token} method."));
        }

        var called = _expressionEvaluator.InvokeMember(session, context, owner, ownerExpression, graphics.Token, _graphicsArguments.GetOrAdd(graphics, GraphicsArguments),
            graphics);
        return called.IsSuccess ? RuntimeExecutionOutcome.Next
            : called.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(called.ErrorInfo!);
    }

    // the arguments of each graphics statement's call, made once for the statement: the evaluator lives for one execution.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<GraphicsMethodStatementNode, ImmutableArray<ExpressionNode>> _graphicsArguments =
        new(ReferenceEqualityComparer.Instance);

    // Line Flags, X1, Y1, X2, Y2, Color; Circle Flags, X, Y, Radius, Color, Start, End, Aspect; PSet Flags, X, Y, Color; Scale Flags[, X1, Y1, X2, Y2]. What the
    // statement leaves out is 0, and its flag says so.
    private static ImmutableArray<ExpressionNode> GraphicsArguments(GraphicsMethodStatementNode graphics)
    {
        var flags = GraphicsFlags.None;
        var zero = new LiteralExpressionNode(graphics.Identity, graphics.SourceLocation, new VBIntegerValue(0));
        ExpressionNode Given(int index, GraphicsFlags given)
        {
            if (index < graphics.Arguments.Length && graphics.Arguments[index] is { } argument)
            {
                flags |= given;
                return argument;
            }

            return zero;
        }

        if (graphics.From is { IsRelative: true })
        {
            flags |= GraphicsFlags.FirstStep;
        }

        ImmutableArray<ExpressionNode> after;
        switch (graphics.Token.ToUpperInvariant())
        {
            case "LINE":
                if (graphics.From is not null)
                {
                    flags |= GraphicsFlags.HasFirstPoint;
                }

                if (graphics.To is { IsRelative: true })
                {
                    flags |= GraphicsFlags.SecondStep;
                }

                flags |= graphics.LineOption?.ToUpperInvariant() switch
                {
                    "B" => GraphicsFlags.Box,
                    "BF" => GraphicsFlags.FilledBox,
                    _ => GraphicsFlags.None,
                };
                after = [graphics.From?.X ?? zero, graphics.From?.Y ?? zero, graphics.To!.X, graphics.To.Y, Given(0, GraphicsFlags.HasColor)];
                break;

            case "CIRCLE":
                // the radius is not optional: the syntax has it.
                after = [graphics.From!.X, graphics.From.Y, graphics.Arguments[0] ?? zero, Given(1, GraphicsFlags.HasColor),
                    Given(2, GraphicsFlags.HasStart), Given(3, GraphicsFlags.HasEnd), Given(4, GraphicsFlags.HasAspect)];
                break;

            case "PSET":
                after = [graphics.From!.X, graphics.From.Y, Given(0, GraphicsFlags.HasColor)];
                break;

            default:
                if (graphics.From is not null)
                {
                    flags |= GraphicsFlags.HasFirstPoint;
                }

                after = graphics.From is { } first && graphics.To is { } second ? [first.X, first.Y, second.X, second.Y] : [];
                break;
        }

        return [new LiteralExpressionNode(graphics.Identity, graphics.SourceLocation, new VBIntegerValue((short)flags)), .. after];
    }

    // obj.Print calls the Print method of the object, a Visual Basic 6.0 form or PictureBox or an Access form or report, once its output list is evaluated.
    private RuntimeExecutionOutcome ExecuteObjectPrint(IRuntimeSession session, RuntimeEvaluationContext context, ObjectPrintExpressionNode print)
    {
        var owner = _expressionEvaluator.Evaluate(session, print.Owner, context);
        return owner.IsSuccess ? PrintOn(session, context, owner.Result!, print.Owner, print.Items, print)
            : owner.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(owner.ErrorInfo!);
    }

    // a Print with no file number, in a language that has no such statement, is the form's own: the code of a Visual Basic 6.0 form is the only code it loads
    // in (BarePrintStaticSemantics).
    private bool PrintsOnMe(IRuntimeSession session, RuntimeEvaluationContext context, PrintStatementNode print, out VBTypedValue? me)
    {
        me = print is { FileNumber: null } && session.Environment.Language is { HasBarePrint: false }
            && session.Symbols.Resolver.ResolveValue(Tokens.Me, ScopeKind.Local, context.Scope).Symbol is { } symbol
                ? new VBObjectValue(session.Symbols.Resolver.GetValue(symbol))
                : null;
        return me is not null;
    }

    // the object lays the output out itself, through the member its server answers (FormPrintMembers.Print). Nothing raises 91, and an object no server
    // answers for has no Print method (438).
    private RuntimeExecutionOutcome PrintOn(IRuntimeSession session, RuntimeEvaluationContext context, VBTypedValue owner, ExpressionNode? ownerExpression,
        ImmutableArray<PrintOutputItemNode> items, SyntaxNode call)
    {
        var evaluated = _printOutput.Collect(session, context, items, out var printed);
        if (evaluated != RuntimeExecutionOutcome.Next)
        {
            return evaluated;
        }

        var value = owner;
        while (value is VBVariantValue { TypedValue: var wrapped })
        {
            value = wrapped;
        }

        if (value is not VBObjectValue target || target.IsNothing())
        {
            return RuntimeExecutionOutcome.Error(value is VBObjectValue
                ? VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, call.SourceLocation, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose)
                : VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectRequired, ownerExpression?.Location ?? call.SourceLocation, "Print is a method of an object."));
        }

        var called = _expressionEvaluator.InvokeMember(session, context, target, ownerExpression, FormPrintMembers.Print, PrintArguments(call, printed), call);
        return called.IsSuccess ? RuntimeExecutionOutcome.Next
            : called.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : called.ErrorInfo!.ErrorId == (int)VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod
                ? RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, call.SourceLocation, "The object has no Print method."))
                : RuntimeExecutionOutcome.Error(called.ErrorInfo!);
    }

    // whether the output ends the line, then the kind of each part and its text, count or column (FormPrintMembers).
    private static ImmutableArray<ExpressionNode> PrintArguments(SyntaxNode call, PrintedOutput printed)
    {
        LiteralExpressionNode Literal(VBTypedValue value) => new(call.Identity, call.SourceLocation, value);
        var arguments = ImmutableArray.CreateBuilder<ExpressionNode>(1 + 2 * printed.Parts.Length);
        arguments.Add(Literal(new VBBooleanValue(printed.EndsLine)));
        foreach (var part in printed.Parts)
        {
            var kind = part.Kind switch
            {
                PrintedPartKind.Text => FormPrintPartKind.Text,
                PrintedPartKind.Spc => FormPrintPartKind.Spc,
                _ => FormPrintPartKind.Tab,
            };
            arguments.Add(Literal(new VBIntegerValue((short)kind)));
            arguments.Add(Literal(part switch
            {
                { Text: { } text } => new VBStringValue(text),
                { Count: { } count } => new VBLongValue(count),
                _ => VBEmptyValue.Empty,
            }));
        }

        return arguments.MoveToImmutable();
    }

    // the parser gives the event's name as the first input, a bare name that is not an expression to evaluate, and the
    // arguments after it.
    private RuntimeExecutionOutcome ExecuteRaiseEvent(IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode raise)
    {
        if (raise.Inputs is not [SimpleNameExpressionNode eventName, ..var arguments])
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var result = _expressionEvaluator.RaiseEvent(session, context, eventName.IdentifierName, [.. arguments.OfType<ExpressionNode>()]);
        return result.IsSuccess
            ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }

    // Debug.Assert: suspends execution when its expression is False, which is what Break means here -
    // the same outcome a Stop statement produces. An expression that cannot be coerced to Boolean is a
    // real run-time error, reported as one.
    private RuntimeExecutionOutcome ExecuteAssert(IRuntimeSession session, RuntimeEvaluationContext context, DebugAssertStatementNode assert)
    {
        var result = _conditions.EvaluateBoolean(session, assert.Condition, context);
        if (!result.IsSuccess)
        {
            return result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
        }

        return result.Result is VBBooleanValue { Value.StoredValue: 0 }
            ? RuntimeExecutionOutcome.Break
            : RuntimeExecutionOutcome.Next;
    }

    // MS-VBAL §5.4.2.1, and whatever the call returns is discarded - a Sub's own Void result discards
    // just as cleanly as a real one would.
    //
    // The two shapes differ in where the arguments are. `Call Foo(1, 2)` carries them inside the
    // Callee's own IndexExpressionNode, so evaluating the Callee is the whole call. The bare
    // `Foo 1, 2` has no parenthesized lExpression equivalent in the grammar, so its arguments are the
    // statement's own (see CallStatementNode) and the call is made from here - which is what S9a left
    // undone, on the ordinary VBA call form.
    private RuntimeExecutionOutcome ExecuteCall(IRuntimeSession session, RuntimeEvaluationContext context, CallStatementNode call)
    {
        var result = _expressionEvaluator.EvaluateCallStatement(session, context, call);

        return result.IsSuccess ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }

    // MS-VBAL §5.4.3.8. Evaluating the source expression is this statement's own business; assigning the
    // result into the target is LetAssignmentEvaluator's, which the file statements that Let-assign what
    // they read share with it.
    private RuntimeExecutionOutcome ExecuteLetAssignment(IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment)
    {
        // a scalar variable, or the result of a function of a scalar type, assigned an expression of them is assigned on payloads (IR lowering S1.2,
        // S1.3), which make no value of the expression.
        if (_expressionEvaluator.TryAssignUnboxed(session, assignment.Target, assignment.Value, context))
        {
            return RuntimeExecutionOutcome.Next;
        }

        // a Single, Double or Date expression, computed in the extended format, is converted to the declared type of the target from it (MS-VBAL §5.6.9.3).
        var valueResult = _expressionEvaluator.EvaluateAssigned(session, assignment.Target, assignment.Value, context);
        if (!valueResult.IsSuccess)
        {
            if (valueResult is { IsError: true, Result: VBSingleValue or VBDoubleValue } overflowed)
            {
                AssignFloatingPointValue(session, context, assignment, (VBNumericTypedValue)overflowed.Result!);
            }

            return valueResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(valueResult.ErrorInfo!);
        }

        return AssignValue(session, context, assignment, valueResult.Result!);
    }

    /// <summary>
    /// Let-assigns <paramref name="value"/>, the value of <paramref name="assignment"/>'s expression already evaluated, to its target (MS-VBAL §5.4.3.8): what
    /// the statement does once its expression is evaluated. Compiled code that made the call the expression is gives what the call returned to it.
    /// </summary>
    /// <param name="session">The session the statement runs in.</param>
    /// <param name="context">The context the statement is evaluated in.</param>
    /// <param name="assignment">The statement.</param>
    /// <param name="value">The value of its expression.</param>
    /// <returns>How the statement ended.</returns>
    internal RuntimeExecutionOutcome AssignValue(IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment, VBTypedValue value)
    {
        // what a call returned, a scalar, is written to a scalar variable as its payload (S1.3).
        if (_expressionEvaluator.TryAssignValueUnboxed(session, assignment.Target, value, context))
        {
            return RuntimeExecutionOutcome.Next;
        }

        var outcome = _assignments.Assign(session, context, assignment, assignment.Target, assignment.Value, value);

        // a Double past the range of a Single, assigned to one, overflows - and leaves the Single infinite (MS-VBA, grid oracle\s16 D02, F07).
        if (outcome is { Kind: RuntimeExecutionOutcomeKind.Error, ErrorInfo.ErrorId: (int)VBRuntimeErrorId.Overflow } && value is VBDoubleValue { Value: var wide }
            && double.IsFinite(wide))
        {
            AssignFloatingPointValue(session, context, assignment, new VBDoubleValue(wide));
        }

        return outcome;
    }

    // MS-VBA assigns what IEEE 754 arithmetic made of an operation that raised an error - an infinity, a NaN - to the Single or Double variable the
    // operation is assigned to, then raises it: `s = s * 10` leaves s infinite, `d = 0 / 0` a NaN. MS-VBAL §5.6.9.3.5 says so of a division of operands
    // declared Double; MS-VBA does it for any Single or Double arithmetic, and for a Double let-coerced to a Single. A variable of any other type - a
    // Variant, a Long, a Currency - is left as it was (recorded in Excel, grid oracle\s16 D01-D09, F01-F15). The error is raised either way.
    private void AssignFloatingPointValue(IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment, VBNumericTypedValue ieee)
    {
        VBTypedValue? assigned = _expressionEvaluator.DeclaredTypeOf(session, assignment.Target, context) switch
        {
            VBSingleType => new VBSingleValue((float)ieee.AsDouble),
            VBDoubleType => new VBDoubleValue(ieee.AsDouble),
            _ => null,
        };

        if (assigned is not null)
        {
            _ = _assignments.Assign(session, context, assignment, assignment.Target, assignment.Value, assigned);
        }
    }

    // MS-VBAL §5.4.3.9. The target is a variable, or a member of an object: a public variable of its class, or a property,
    // which its Property Set is invoked for.
    private RuntimeExecutionOutcome ExecuteSetAssignment(IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment)
    {
        // the variable a running For Each walks, set to Nothing inside the loop, is left as it is (ForEachCollectionStaticSemantics).
        if (session.CallStack.Current is CallStackFrame { Body: { } body, Pc: var pc } running && pc < body.Items.Length
            && ReferenceEquals(body.Items[pc].Node, assignment)
            && ForEachCollectionStaticSemantics.LoopsWhoseCollectionItSetsToNothing(body, pc).Any(opener => running.BlockHolderOf(opener) is not null))
        {
            return RuntimeExecutionOutcome.Next;
        }

        // `Set Module1.Kept = x` is a variable of the module, written as its bare name is (MS-VBAL §5.6.12): not a member of an object.
        var qualified = assignment.Target is MemberAccessExpressionNode qualifiedAccess
            ? _expressionEvaluator.ResolveNamespaceQualified(session, context, qualifiedAccess)
            : null;

        // `Set Module1.Items(1) = x` is an element of the module's array, which the bare name's element is.
        var isElementOfAQualifiedArray = assignment.Target is IndexExpressionNode { Callee: MemberAccessExpressionNode calleeAccess }
            && _expressionEvaluator.ResolveNamespaceQualified(session, context, calleeAccess) is not null;

        // `Set G() = x`: a call of a function, Set-assigned through what it returns.
        if (_assignments.IsACallOfAFunction(session, context, assignment.Target))
        {
            var called = _expressionEvaluator.Evaluate(session, assignment.Value, context);
            return called.IsSuccess
                ? _assignments.AssignThroughFunction(session, context, assignment, assignment.Target, assignment.Value, called.Result!, isSet: true)
                : called.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(called.ErrorInfo!);
        }

        if (qualified is null && !isElementOfAQualifiedArray && assignment.Target is MemberAccessExpressionNode or IndexExpressionNode { Callee: MemberAccessExpressionNode })
        {
            var memberValue = _expressionEvaluator.Evaluate(session, assignment.Value, context);
            return memberValue.IsSuccess
                ? _assignments.AssignObjectMember(session, context, assignment, assignment.Target, assignment.Value, memberValue.Result!, isSet: true)
                : memberValue.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(memberValue.ErrorInfo!);
        }

        if (assignment.Target is IndexExpressionNode element)
        {
            var elementValue = _expressionEvaluator.Evaluate(session, assignment.Value, context);
            return elementValue.IsSuccess
                ? _assignments.AssignArrayElement(session, context, assignment, element.Callee, element.Arguments, assignment.Value, elementValue.Result!, isSet: true)
                : elementValue.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(elementValue.ErrorInfo!);
        }

        // a variable is bound once, and written through the slot it is bound to (IR lowering S0.11); a variable named through its module, and a
        // function's result, are resolved.
        var bound = qualified is null ? _expressionEvaluator.BoundVariableOf(session, assignment.Target, context) : null;
        var targetSymbol = qualified ?? bound?.Variable ?? (assignment.Target is SimpleNameExpressionNode simpleName
            ? session.Symbols.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope).Symbol
            : null);
        if (targetSymbol is not ITypedSymbol target)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var valueResult = _expressionEvaluator.Evaluate(session, assignment.Value, context);
        if (!valueResult.IsSuccess)
        {
            return valueResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(valueResult.ErrorInfo!);
        }

        // MS-VBAL §5.3.1.11: a Variant reference parameter bound to a variable of another type is Set-assigned as that variable is declared.
        var (boundHandle, referenced) = bound is not null
            ? _expressionEvaluator.BindingOf(session, bound)
            : ((IBindingHandle?)null, session.CallStack.Current is { } frame && frame.TryGetReferencedType((Symbol)target, out var referencedType) ? referencedType : null);
        var declared = referenced ?? target.ResolvedType;

        // MS-VBAL §5.4.3.9: a function named outside its own body is called, and what it returns is Set-assigned through its default member - and so is a
        // property of a module that has no Property Set (MS-VBA, oracle\s28 W4, W5: `Set Now = Nothing` is error 424). The value is Set-coerced to what the
        // default member's Property Set takes, and not to what the function returns: `Set One = New Collection` sets One's default member to the collection
        // (oracle\s31 D01, D04).
        var isOwnResult = target is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol && ((Symbol)target).Uri.AbsoluteUri == context.Scope.AbsoluteUri;
        if (!isOwnResult
            && (target is VBFunctionMemberSymbol
                || target is VBPropertyGetMemberSymbol property && RuntimeProcedureInvoker.GetParameters(property) is not [{ Name: Tokens.Me }, ..]
                    && !session.Symbols.MembersOf(property.ParentUri).OfType<VBPropertySetMemberSymbol>()
                        .Any(candidate => string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase))))
        {
            return _assignments.AssignThroughFunction(session, context, assignment, assignment.Target, assignment.Value, valueResult.Result!, isSet: true);
        }

        // MS-VBAL §5.4.3.9: a property is Set by invoking its Property Set with the value - one of a standard module on nothing, from anywhere, by its bare
        // name or through its module (MS-VBA, oracle\s44 W01); one of a class, from the class's own code, on the object whose code it is. It has no storage of
        // its own for the value to be written to.
        if (!isOwnResult && target is IVBPropertyMemberSymbol && ((Symbol)target).Uri.AbsoluteUri != context.Scope.AbsoluteUri
            && (target as VBPropertySetMemberSymbol ?? session.Symbols.MembersOf(((Symbol)target).ParentUri).OfType<VBPropertySetMemberSymbol>()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, ((Symbol)target).Name, StringComparison.OrdinalIgnoreCase))) is { } setter)
        {
            return SetProperty(session, context, assignment, setter, valueResult.Result!);
        }

        // Set-coercion (MS-VBAL §5.5.2.2) is not an operator - the same direct entry point
        // WithStatementRuntimeSemantics already uses for its own With-target coercion.
        var coercionResult = _setCoercion.EvaluateSetCoercion(session, assignment.Value, valueResult.Result!, declared);
        if (!coercionResult.IsSuccess)
        {
            return RuntimeExecutionOutcome.Error(coercionResult.ErrorInfo!);
        }

        // MS-VBAL §5.3.1: `Set Foo = obj` inside Foo's own body Set-assigns its function result variable, as `Foo = value` Let-assigns it
        // (LetAssignmentEvaluator): it is not a real addressable symbol with a binding, but the activation's own result.
        if (target is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol
            && ((Symbol)target).Uri.AbsoluteUri == context.Scope.AbsoluteUri && session.CallStack.Current is { } enclosing)
        {
            // the result variable holds the object it is Set to for as long as the activation runs (ObjectReferences.AssignResult).
            var resultFrame = (CallStackFrame)enclosing;
            resultFrame.ReturnValue = ObjectReferences.AssignResult(session, resultFrame.ResultCell, coercionResult.Result!);
            return RuntimeExecutionOutcome.Next;
        }

        var handle = boundHandle ?? session.Symbols.Resolver.GetValue((Symbol)target);
        if (!handle.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            // MS-VBAL static semantics should already have rejected an assignment to a read-only
            // target (a Const, chiefly) at compile time - reaching here means that check was skipped.
            return RuntimeExecutionOutcome.InternalError;
        }

        // a value is a view of its handle, which is about to be written to: what the variable held is its object's
        // identity as of now.
        var previous = declared.CreateValue(handle) is VBObjectValue held ? new VBObjectValue(held.Value) : null;

        // MS-VBAL §5.4.3.9: a WithEvents variable's handlers are detached from the object it holds before the
        // assignment, and attached to the object it is given after it.
        var variable = (Symbol)target;
        var withEvents = variable.GetProperty(SymbolProperties.WithEvents);
        if (withEvents)
        {
            EventAttachments.Detach(session, context, variable, previous);
        }

        var before = ObjectReferences.HeldIn(session, handle);
        handle.Store(session.Symbols.Resolver, coercionResult.Result!);

        // MS-VBAL §5.3.1.10: the object the variable held loses a reference, and Terminate runs when it was the last.
        // The variable already holds the new one by now, so a handler that reads it sees what the program wrote.
        ObjectReferences.Rebind(session, handle, before);

        if (withEvents)
        {
            EventAttachments.Attach(session, context, variable, coercionResult.Result as VBObjectValue);
        }

        return RuntimeExecutionOutcome.Next;
    }

    // the Property Set invoked with the value, which it Set-coerces to its value parameter's type: on the object whose code this is when it is a class's, and
    // on nothing when it is a standard module's.
    private RuntimeExecutionOutcome SetProperty(
        IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment, VBPropertySetMemberSymbol setter, VBTypedValue value)
    {
        IRuntimeValue? receiver = null;
        if (RuntimeProcedureInvoker.GetParameters(setter) is [{ Name: Tokens.Me }, ..])
        {
            var me = _expressionEvaluator.Evaluate(session, new InstanceExpressionNode(assignment.Target.Identity, assignment.Target.Location), context);
            if (!me.IsSuccess)
            {
                return me.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(me.ErrorInfo!);
            }

            receiver = me.Result!.RuntimeValue;
        }

        var invocation = _expressionEvaluator.InvokeAssignment(session, context, setter, receiver, [], value, assignment.Value, isSet: true);
        return invocation.IsSuccess ? RuntimeExecutionOutcome.Next
            : invocation.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(invocation.ErrorInfo!);
    }
}
