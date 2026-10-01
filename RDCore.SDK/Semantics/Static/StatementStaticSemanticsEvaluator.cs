using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Recursively walks a real, arbitrarily-nested statement tree, evaluating every expression it
/// contains via <see cref="ExpressionStaticSemanticsEvaluator"/>, threading the innermost enclosing
/// <c>With</c> block's target type (<strong>MS-VBAL §5.6.15</strong>) through its body, checking
/// <c>Let</c>/<c>Set</c> assignment coercion validity between an assignment's <c>Target</c> and
/// <c>Value</c>, and checking that every label a jump statement names is defined
/// (<strong>MS-VBAL §5.4.2.12</strong>–<strong>§5.4.2.16</strong>, <strong>§5.4.4.1</strong>,
/// <strong>§5.4.4.2</strong>).
/// </summary>
/// <remarks>
/// This is the statement-tree analogue of <see cref="ExpressionStaticSemanticsEvaluator"/>: nothing
/// previously walked a <see cref="StatementBlock"/>'s nested blocks (<c>If</c>/<c>Do</c>/<c>For</c>/
/// <c>Select Case</c>/<c>With</c>, ...) at all, so a <c>With</c> block's target type never had
/// anywhere to flow from — <see cref="ExpressionStaticSemanticsEvaluator"/> could only ever defer a
/// with-relative access, and an assignment's own coercion validity was never checked at all. Unlike an
/// expression tree, a statement tree's individual statements are largely independent of one another,
/// so this collects every error found across the whole tree rather than short-circuiting on the first
/// one the way the expression evaluator does.
/// <para>
/// The operand of a jump statement names a label, not a value, and a label is not a symbol, so it is
/// never handed to the expression evaluator. <c>On Error GoTo 0</c>, <c>On Error GoTo -1</c> and
/// <c>Resume 0</c> are not jumps at all: their operand is a sentinel, not a label reference.
/// </para>
/// <para>
/// A <c>Set</c> assignment whose target is the default instance variable of a predeclared class
/// (<see cref="VBPredeclaredInstanceSymbol"/>) is invalid (<strong>MS-VBAL §5.2.4.1.2</strong>) and reported as
/// <see cref="VBCompileErrorId.InvalidUseOfObject"/>.
/// </para>
/// </remarks>
public static class StatementStaticSemanticsEvaluator
{
    /// <summary>
    /// Walks every statement in <paramref name="block"/>, recursing into nested blocks and collecting
    /// every compile error found anywhere in the tree.
    /// </summary>
    /// <param name="context">
    /// The compile-time context to start walking from. <see cref="StaticEvaluationContext.EnclosingWithTargetType"/>
    /// should be <c>null</c> unless <paramref name="block"/> is itself already inside a <c>With</c>
    /// block relative to some outer context the caller is threading through.
    /// </param>
    /// <param name="block">
    /// The statement block to walk — a procedure body. A label is scoped to its procedure, not to the
    /// block it appears in, so label references are checked against the labels defined anywhere within
    /// this block: passing a nested block on its own would report every jump out of it as undefined.
    /// </param>
    /// <returns>
    /// Every compile error found, in traversal order, followed by a
    /// <see cref="VBCompileErrorId.LabelNotDefined"/> error for each label reference that no line label
    /// or line number in <paramref name="block"/> defines. Empty when the whole tree is valid.
    /// </returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(StaticEvaluationContext context, StatementBlock block)
    {
        var walk = new Walk();
        EvaluateBlock(context, block, walk);
        ReportUndefinedLabels(walk);
        return walk.Errors.ToImmutable();
    }

    private static void EvaluateBlock(StaticEvaluationContext context, StatementBlock block, Walk walk)
    {
        foreach (var child in block.Children)
        {
            switch (child)
            {
                case LineLabelNode label:
                    walk.LabelDefinitions.Add(label.Name);
                    break;
                case StatementNode statement:
                    EvaluateStatement(context, statement, walk);
                    break;
            }
        }
    }

    private static void EvaluateStatement(StaticEvaluationContext context, StatementNode statement, Walk walk)
    {
        // WithStatementNode is the one case whose own Inputs result changes the context its Body (and
        // everything the body recursively contains) evaluates against - handled before the generic
        // Inputs pass below so the resolved target type can be threaded straight into bodyContext.
        if (statement is WithStatementNode withStatement)
        {
            var targetResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, withStatement.WithExpression);
            CollectError(targetResult, walk);

            var bodyContext = targetResult.IsSuccess ? context with { EnclosingWithTargetType = targetResult.Result } : context;
            EvaluateBlock(bodyContext, withStatement.Body, walk);
            return;
        }

        // AssignmentStatementNode needs both Target's and Value's declared types kept around (not just
        // their error status) to run the coercion rule matching its Kind - the generic Inputs pass below
        // only ever checks IsError, so this is handled separately rather than folded into it.
        if (statement is AssignmentStatementNode assignment)
        {
            var targetResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Target);
            CollectError(targetResult, walk);
            if (assignment.Kind == AssignmentKind.Set && DefaultInstanceNamedBy(context, assignment.Target) is { } defaultInstance)
            {
                walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfObject, assignment.Target.Location,
                    $"'{defaultInstance.Name}' is the default instance variable of a predeclared class and can't be the target of a Set assignment (MS-VBAL §5.2.4.1.2)."));
            }
            var valueResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Value);
            CollectError(valueResult, walk);

            if (targetResult.IsSuccess && valueResult.IsSuccess && ResolveCoercionRule(assignment.Kind) is { } coercionRule)
            {
                CollectError(coercionRule.DetermineDeclaredType(context, assignment.Value, valueResult.Result!, targetResult.Result!), walk);
            }
            return;
        }

        if (TryEvaluateJump(context, statement, walk))
        {
            return;
        }

        // the first operand of RaiseEvent names an event, which is not a value either: evaluated as an expression, a
        // bare `Changed` would come back as an undefined variable.
        if (statement is KeywordStatementNode { Token: Tokens.RaiseEvent } raiseEvent)
        {
            EvaluateRaiseEvent(context, raiseEvent, walk);
            return;
        }

        foreach (var input in statement.Inputs)
        {
            if (input is ExpressionNode expression)
            {
                CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, expression), walk);
            }
        }

        switch (statement)
        {
            case IfBlockStatementNode ifBlock:
                EvaluateBlock(context, ifBlock.Body, walk);
                foreach (var elseIfBlock in ifBlock.ElseIfBlocks)
                {
                    EvaluateStatement(context, elseIfBlock, walk);
                }
                if (ifBlock.ElseBlock is { } elseBlock)
                {
                    EvaluateStatement(context, elseBlock, walk);
                }
                break;
            case ElseIfBlockStatementNode elseIfBlockStatement:
                EvaluateBlock(context, elseIfBlockStatement.Body, walk);
                break;
            case ElseBlockStatementNode elseBlockStatement:
                EvaluateBlock(context, elseBlockStatement.Body, walk);
                break;
            case InlineIfStatementNode inlineIf:
                EvaluateBlock(context, inlineIf.ThenBody, walk);
                if (inlineIf.ElseBody is { } elseBody)
                {
                    EvaluateBlock(context, elseBody, walk);
                }
                break;
            case DoLoopStatementNode doLoop:
                EvaluateBlock(context, doLoop.Body, walk);
                break;
            case DoLoopUntilStatementNode doLoopUntil:
                EvaluateBlock(context, doLoopUntil.Body, walk);
                break;
            case DoLoopWhileStatementNode doLoopWhile:
                EvaluateBlock(context, doLoopWhile.Body, walk);
                break;
            case DoUntilLoopStatementNode doUntilLoop:
                EvaluateBlock(context, doUntilLoop.Body, walk);
                break;
            case DoWhileLoopStatementNode doWhileLoop:
                EvaluateBlock(context, doWhileLoop.Body, walk);
                break;
            case WhileWendStatementNode whileWend:
                EvaluateBlock(context, whileWend.Body, walk);
                break;
            case ForStatementNode forStatement:
                EvaluateBlock(context, forStatement.Body, walk);
                break;
            case ForEachStatementNode forEachStatement:
                EvaluateBlock(context, forEachStatement.Body, walk);
                break;
            case SelectCaseStatementNode selectCase:
                foreach (var caseExpressionBlock in selectCase.CaseExpressionBlocks)
                {
                    EvaluateStatement(context, caseExpressionBlock, walk);
                }
                if (selectCase.CaseElseBlock is { } caseElseBlock)
                {
                    EvaluateStatement(context, caseElseBlock, walk);
                }
                break;
            case CaseExpressionStatementNode caseExpressionStatement:
                foreach (var rangeClause in caseExpressionStatement.RangeClauses)
                {
                    EvaluateStatement(context, rangeClause, walk);
                }
                EvaluateBlock(context, caseExpressionStatement.Block, walk);
                break;
            case CaseElseClauseStatementNode caseElseClauseStatement:
                EvaluateBlock(context, caseElseClauseStatement.Body, walk);
                break;
        }
    }

    // MS-VBAL §5.4.2.20: the event is one the enclosing class module declares - a standard module has none, so a
    // RaiseEvent in one names no event - and the arguments are compatible with its parameter list under the rules of
    // procedure invocation, all treated as positional.
    private static void EvaluateRaiseEvent(StaticEvaluationContext context, KeywordStatementNode statement, Walk walk)
    {
        if (statement.Inputs is not [SimpleNameExpressionNode eventName, .. var inputs])
        {
            // a RaiseEvent with no name is a syntax error, which the parser has already reported.
            return;
        }

        var arguments = inputs.OfType<ExpressionNode>().ToArray();
        var argumentTypes = new VBType?[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, arguments[i]);
            CollectError(result, walk);
            argumentTypes[i] = result.IsSuccess ? result.Result : null;
        }

        var declared = context.Scope.SelfAndAncestors()
            .FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Module)?
            .DeclaredAs(eventName.IdentifierName).OfType<VBEventMemberSymbol>().FirstOrDefault();
        if (declared is null)
        {
            walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.EventNotDefined, eventName.Location,
                $"'{eventName.IdentifierName}' is not an event of the class module this RaiseEvent is written in."));
            return;
        }

        var parameters = declared.Parameters;
        var acceptsExtra = parameters.Length > 0 && parameters[^1] is ParamArrayParameterSymbol;
        var required = parameters.Count(parameter => !parameter.IsOptional && parameter is not ParamArrayParameterSymbol);
        if (arguments.Length < required || (arguments.Length > parameters.Length && !acceptsExtra))
        {
            walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.EventArgumentsIncompatible, statement.SourceLocation,
                $"Event '{declared.Name}' takes {(required == parameters.Length ? required.ToString() : $"{required} to {parameters.Length}")} argument(s), and {arguments.Length} were given."));
            return;
        }

        for (var i = 0; i < Math.Min(arguments.Length, parameters.Length); i++)
        {
            var parameter = parameters[i];
            if (argumentTypes[i] is not { } argumentType || parameter.ResolvedType is VBUnknownType)
            {
                continue;
            }

            if (ArgumentIncompatibility(context, parameter, arguments[i], argumentType) is { } reason)
            {
                walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.EventArgumentsIncompatible, arguments[i].Location,
                    $"Argument {i + 1} of event '{declared.Name}' is not compatible with parameter '{parameter.Name}': {reason} (MS-VBAL §5.3.1.11)."));
            }
            else if (!IsByRef(parameter.ParameterKind) && parameter.ResolvedType is not (VBClassType or VBObjectType))
            {
                // a ByVal parameter of a type other than a class or Object takes the argument by Let-coercion.
                CollectError(LetCoercionStaticSemantics.Instance.DetermineDeclaredType(context, arguments[i], argumentType, parameter.ResolvedType), walk);
            }
        }
    }

    // MS-VBAL §5.3.1.11, for each mapped parameter: a class or Object parameter takes an argument of a specific class or
    // Object, and a ByVal one a Variant too; a ByRef parameter of any other type but Variant takes an argument of exactly
    // its declared type. Null when the argument is compatible, or not known to be incompatible.
    private static string? ArgumentIncompatibility(
        StaticEvaluationContext context, VBParameterSymbol parameter, ExpressionNode argument, VBType argumentType)
    {
        if (argumentType is VBUnknownType)
        {
            return null;
        }

        var byRef = IsByRef(parameter.ParameterKind);
        if (parameter.ResolvedType is VBClassType or VBObjectType)
        {
            return argumentType is VBClassType or VBObjectType || (!byRef && argumentType is VBVariantType)
                ? null
                : $"a parameter declared as {parameter.ResolvedType.Name} takes {(byRef ? "an object" : "an object or a Variant")}, and the argument is a {argumentType.Name}";
        }

        // The runtime semantics of a ByRef parameter given a value (a literal, an operator's result) is a new local that is
        // Let-assigned, which is no reference to a type: only an argument that is a variable is passed by reference.
        // 🚧 TODO a member access that names a field is a variable too; it is taken for a value, and not checked.
        if (byRef && parameter.ResolvedType is not VBVariantType && IsVariable(context, argument)
            && !string.Equals(parameter.ResolvedType.Name, argumentType.Name, StringComparison.OrdinalIgnoreCase))
        {
            return $"a ByRef parameter of type {parameter.ResolvedType.Name} takes a variable of exactly that type, and the argument is a {argumentType.Name}";
        }

        return null;
    }

    private static bool IsByRef(ParameterKind kind) => kind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;

    // an argument is a variable when it names one: a local, a parameter or a field, not a constant, a procedure or a value.
    private static bool IsVariable(StaticEvaluationContext context, ExpressionNode argument)
        => argument is SimpleNameExpressionNode name
            && context.Resolver.ResolveValue(name.IdentifierName, ScopeKind.Local, context.Scope.Uri).Symbol
                is VBLocalVariableSymbol or VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol;

    // A jump's target operand names a label, not a value, and a label is not a symbol: evaluated as an
    // expression, a bare `Done` would come back as an undefined variable. Only the operands that really
    // are expressions (an On...GoTo/On...GoSub selector) go through the expression evaluator.
    private static bool TryEvaluateJump(StaticEvaluationContext context, StatementNode statement, Walk walk)
    {
        switch (statement)
        {
            case GoToStatementNode goTo:
                ReferenceLabel(goTo.LabelExpression, walk);
                return true;
            case GoSubStatementNode goSub:
                ReferenceLabel(goSub.LabelExpression, walk);
                return true;
            case OnGoToStatementNode onGoTo:
                CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, onGoTo.Selector), walk);
                foreach (var label in onGoTo.Labels)
                {
                    ReferenceLabel(label, walk);
                }
                return true;
            case OnGoSubStatementNode onGoSub:
                CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, onGoSub.Selector), walk);
                foreach (var label in onGoSub.Labels)
                {
                    ReferenceLabel(label, walk);
                }
                return true;
            case OnErrorGoToStatementNode onError:
                // MS-VBAL §5.4.4.1 makes the line number 0 mean "error handling disabled", and VBA treats
                // -1 (clear the active error) the same way: neither is a label, so neither can be undefined.
                if (!LabelOperands.IsIntegerConstant(onError.LabelExpression, 0)
                    && !LabelOperands.IsIntegerConstant(onError.LabelExpression, -1))
                {
                    ReferenceLabel(onError.LabelExpression, walk);
                }
                return true;
            case ResumeStatementNode { LabelExpression: { } resumeTarget }:
                // MS-VBAL §5.4.4.2 carves out the line number 0 here as well.
                if (!LabelOperands.IsIntegerConstant(resumeTarget, 0))
                {
                    ReferenceLabel(resumeTarget, walk);
                }
                return true;
            default:
                return false;
        }
    }

    // Labels are scoped to the procedure, not the block, and a jump may precede its target - so a
    // reference is only recorded while walking, and checked once every definition has been seen.
    private static void ReferenceLabel(ExpressionNode operand, Walk walk) => walk.LabelReferences.Add(operand);

    private static void ReportUndefinedLabels(Walk walk)
    {
        foreach (var operand in walk.LabelReferences)
        {
            if (!LabelOperands.TryGetLabelName(operand, out var name))
            {
                walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.LabelNotDefined, operand.Location,
                    "A jump target must be a line label or a line number."));
            }
            else if (!walk.LabelDefinitions.Contains(name))
            {
                walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.LabelNotDefined, operand.Location, name));
            }
        }
    }

    // LSet/RSet (MS-VBAL 5.4.3.6/5.4.3.7) have their own distinct static semantics - neither Let- nor
    // Set-coercion - which aren't modeled yet (a real, accepted gap; see FixedString let-coercion in
    // the runtime layer for the same kind of deliberate deferral). Falls through to null, deferred by
    // the caller like any other unmapped case.
    // MS-VBAL §5.2.4.1.2: the variable a predeclared class's name refers to can't be the target of a Set
    // assignment. Only a simple name reaches it - a local or field of the same name hides it, and resolves
    // to that instead.
    private static VBPredeclaredInstanceSymbol? DefaultInstanceNamedBy(StaticEvaluationContext context, ExpressionNode target)
        => target is SimpleNameExpressionNode name
            ? context.Resolver.ResolveValue(name.IdentifierName, ScopeKind.Local, context.Scope.Uri).Symbol as VBPredeclaredInstanceSymbol
            : null;

    private static IStaticSemantics? ResolveCoercionRule(AssignmentKind kind) => kind switch
    {
        AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet => LetCoercionStaticSemantics.Instance,
        AssignmentKind.Set => SetCoercionStaticSemantics.Instance,
        _ => null,
    };

    private static void CollectError(StaticSemanticsEvaluationResult result, Walk walk)
    {
        if (result.IsError)
        {
            walk.Errors.Add(result.ErrorInfo!);
        }
    }

    private sealed class Walk
    {
        public ImmutableArray<VBCompileErrorInfo>.Builder Errors { get; } = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

        // VBA identifiers are case-insensitive, and so are label names.
        public HashSet<string> LabelDefinitions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ExpressionNode> LabelReferences { get; } = [];
    }
}
