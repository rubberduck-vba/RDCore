using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
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
    /// <param name="procedure">
    /// The kind of the procedure <paramref name="block"/> is the body of, which decides whether an <c>Exit Sub</c>,
    /// <c>Exit Function</c> or <c>Exit Property</c> statement in it is where it may be
    /// (<see cref="ExitStatementStaticSemantics"/>). <see langword="null"/> when it is not known: the kind of procedure is not checked then, and the
    /// <c>Exit For</c> and <c>Exit Do</c> statements still are.
    /// </param>
    /// <returns>
    /// Every compile error found, in traversal order, followed by a
    /// <see cref="VBCompileErrorId.LabelNotDefined"/> error for each label reference that no line label
    /// or line number in <paramref name="block"/> defines. Empty when the whole tree is valid.
    /// </returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(StaticEvaluationContext context, StatementBlock block, MemberKind? procedure = null)
        => Evaluate(context, block, default, procedure);

    /// <summary>
    /// Walks every statement in <paramref name="block"/> like <see cref="Evaluate(StaticEvaluationContext, StatementBlock, MemberKind?)"/>, for the
    /// build and the language <paramref name="options"/> state.
    /// </summary>
    /// <param name="context">The compile-time context to start walking from.</param>
    /// <param name="block">The statement block to walk - a procedure body.</param>
    /// <param name="options">The conditional-compilation branches that are excluded, and the language the body is written in.</param>
    /// <param name="procedure">The kind of the procedure <paramref name="block"/> is the body of, when it is known.</param>
    /// <returns>Every compile error found, in traversal order, followed by those that are told once the whole body has been walked.</returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(
        StaticEvaluationContext context, StatementBlock block, StaticSemanticsOptions options, MemberKind? procedure = null)
        => Run(context, block, new Walk { Procedure = procedure, Options = options });

    /// <summary>
    /// Walks every statement in <paramref name="block"/> and checks what needs no name resolution: where an <c>Exit</c> statement is written, that every
    /// label a jump names is defined and none is defined twice, and the statements the language has. The types of expressions, which need the symbols
    /// of a workspace, are not evaluated.
    /// </summary>
    /// <param name="block">The statement block to walk - a procedure body.</param>
    /// <param name="options">The conditional-compilation branches that are excluded, and the language the body is written in.</param>
    /// <param name="procedure">The kind of the procedure <paramref name="block"/> is the body of, when it is known.</param>
    /// <returns>Every compile error found, in traversal order, followed by those that are told once the whole body has been walked.</returns>
    public static ImmutableArray<VBCompileErrorInfo> CheckStructure(StatementBlock block, StaticSemanticsOptions options = default, MemberKind? procedure = null)
        => Run(default, block, new Walk { Procedure = procedure, Options = options, Structural = true });

    /// <summary>
    /// Analyzes the body of a procedure, and describes what the static pass found out about it.
    /// </summary>
    /// <param name="procedure">The identity of the procedure.</param>
    /// <param name="block">The statement block to walk - the procedure's body.</param>
    /// <param name="options">The conditional-compilation branches that are excluded, and the language the body is written in.</param>
    /// <param name="kind">The kind of the procedure, when it is known.</param>
    /// <param name="context">
    /// The compile-time context the body's expressions are evaluated in, or <see langword="null"/> when there is no workspace to resolve names in:
    /// only the structure of the body is checked then (<see cref="CheckStructure"/>).
    /// </param>
    /// <returns>The model of the procedure.</returns>
    public static ProcedureSemanticModel Analyze(
        SemanticId procedure, StatementBlock block, StaticSemanticsOptions options = default, MemberKind? kind = null, StaticEvaluationContext? context = null)
    {
        if (context is not { } resolved)
        {
            return new(procedure, CheckStructure(block, options, kind));
        }

        var facts = new ExpressionFactCollector();
        var errors = Evaluate(resolved with { Facts = facts }, block, options, kind);
        return new(procedure, errors) { Expressions = facts.ToImmutable(), IsFullyAnalyzed = errors.IsEmpty && ExpressionCoverage.IsCovered(block, options, facts) };
    }

    private static ImmutableArray<VBCompileErrorInfo> Run(StaticEvaluationContext context, StatementBlock block, Walk walk)
    {
        EvaluateBlock(context, block, walk);
        ReportUndefinedLabels(walk);
        return walk.Errors.ToImmutable();
    }

    private static void EvaluateBlock(StaticEvaluationContext context, StatementBlock block, Walk walk)
    {
        foreach (var child in block.Children)
        {
            // MS-VBAL §3.4.2: an excluded branch is logically removed, so what is in it is not analyzed and defines no label.
            if (walk.Options.IsDead(child.SourceLocation.Range))
            {
                continue;
            }

            switch (child)
            {
                case LineLabelNode label:
                    // MS-VBAL §5.4.1.1: a label is defined once in its procedure.
                    if (!walk.LabelDefinitions.Add(label.Name))
                    {
                        walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateLabelDefinition, label.SourceLocation, label.Name));
                    }
                    break;
                // a name is declared once in its procedure: the parameters and the variables and constants of the body share one scope, and what a name is
                // written with - Dim x$ and Dim x% - is how its type is said, not a different name.
                case VariableDeclarationNode { Name: var name } declaration:
                    ReportDuplicateDeclaration(walk, name, declaration.SourceLocation);
                    goto default;
                case ConstantDeclarationNode { Name: var name } declaration:
                    ReportDuplicateDeclaration(walk, name, declaration.Location);
                    goto default;
                case ParameterDeclarationNode { Name: var name } declaration:
                    ReportDuplicateDeclaration(walk, name, declaration.Location);
                    goto default;
                default:
                    if (child is StatementNode statement)
                    {
                        EvaluateStatement(context, statement, walk);
                    }

                    break;
            }
        }
    }

    private static void ReportDuplicateDeclaration(Walk walk, string name, SourceLocation location)
    {
        if (name.Length > 0 && !walk.DeclaredNames.Add(name))
        {
            walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateDeclaration, location, $"'{name}' is declared more than once in this procedure."));
        }
    }

    private static void EvaluateStatement(StaticEvaluationContext context, StatementNode statement, Walk walk)
    {
        // WithStatementNode is the one case whose own Inputs result changes the context its Body (and
        // everything the body recursively contains) evaluates against - handled before the generic
        // Inputs pass below so the resolved target type can be threaded straight into bodyContext.
        if (statement is WithStatementNode withStatement)
        {
            if (walk.Structural)
            {
                EvaluateBlock(context, withStatement.Body, walk);
                return;
            }

            var targetResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, withStatement.WithExpression);
            CollectError(targetResult, walk);

            var bodyContext = targetResult.IsSuccess ? context with { EnclosingWithTargetType = targetResult.Result } : context;
            EvaluateBlock(bodyContext, withStatement.Body, walk);
            return;
        }

        // a bare Print is a statement of some languages only.
        if (statement is PrintStatementNode print)
        {
            if (BarePrintStaticSemantics.Evaluate(print, walk.Options.Language) is { } printError)
            {
                walk.Errors.Add(printError);
                return;
            }
        }

        // AssignmentStatementNode needs both Target's and Value's declared types kept around (not just
        // their error status) to run the coercion rule matching its Kind - the generic Inputs pass below
        // only ever checks IsError, so this is handled separately rather than folded into it.
        if (statement is AssignmentStatementNode assignment)
        {
            if (walk.Structural)
            {
                return;
            }

            var targetResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Target);
            CollectError(targetResult, walk);
            MarkWritten(context, assignment.Target);
            if (assignment.Kind == AssignmentKind.Set && DefaultInstanceNamedBy(context, assignment.Target) is { } defaultInstance)
            {
                walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfObject, assignment.Target.Location,
                    $"'{defaultInstance.Name}' is the default instance variable of a predeclared class and can't be the target of a Set assignment (MS-VBAL §5.2.4.1.2)."));
            }
            var valueResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Value);
            CollectError(valueResult, walk);

            // MS-VBAL §5.4.3.8: a value let-assigned to an object is assigned to its default member, which is not the coercion of the value to the object.
            var assignsADefaultMember = assignment.Kind != AssignmentKind.Set && targetResult.Result is VBClassType or VBObjectType;
            if (targetResult.IsSuccess && valueResult.IsSuccess && !assignsADefaultMember && ResolveCoercionRule(assignment.Kind) is { } coercionRule)
            {
                CollectError(coercionRule.DetermineDeclaredType(context, assignment.Value, valueResult.Result!, targetResult.Result!), walk);
            }
            return;
        }

        // MS-VBAL §5.4.2.5, .7, .17-.19: where an Exit statement may be written.
        if (statement is KeywordStatementNode { Token: Tokens.ExitFor or Tokens.ExitDo or Tokens.ExitSub or Tokens.ExitFunction or Tokens.ExitProperty } exit)
        {
            if (ExitStatementStaticSemantics.Evaluate(exit, walk.ForDepth > 0, walk.DoDepth > 0, walk.Procedure) is { } exitError)
            {
                walk.Errors.Add(exitError);
            }
            return;
        }

        // MS-VBAL §5.4.3.5.
        if (statement is MidStatementNode mid)
        {
            if (!walk.Structural)
            {
                walk.Errors.AddRange(MidStatementStaticSemantics.Evaluate(context, mid));
            }
            return;
        }

        // MS-VBAL §5.4.5, and Name.
        if (!walk.Structural && FileStatementStaticSemantics.TryEvaluate(context, statement, out var fileStatementErrors))
        {
            walk.Errors.AddRange(fileStatementErrors);
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
            if (!walk.Structural)
            {
                EvaluateRaiseEvent(context, raiseEvent, walk);
            }
            return;
        }

        if (!walk.Structural)
        {
            foreach (var input in statement.Inputs)
            {
                if (input is ExpressionNode expression)
                {
                    CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, expression), walk);
                }
            }

            // what a statement writes to, apart from an assignment's target: the counter or the control variable of a loop, the string a Mid statement
            // replaces a part of, and the array a ReDim gives its dimensions.
            switch (statement)
            {
                case ForStatementNode forStatement:
                    MarkWritten(context, forStatement.ControlExpression);
                    break;
                case ForEachStatementNode forEachStatement:
                    MarkWritten(context, forEachStatement.ControlExpression);
                    break;
                case MidStatementNode midStatement:
                    MarkWritten(context, midStatement.Target);
                    break;
                case RedimDeclarationNode redim:
                    // the target is the array, which is not one of the statement's operands: it is evaluated here.
                    CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, redim.Target), walk);
                    MarkWritten(context, redim.Target);
                    break;
            }

            // the keyword is how the statement is written, which the expression it calls is not: a fact of the callee, for whoever finds it obsolete.
            if (statement is CallStatementNode callStatement)
            {
                // the arguments of the statement may be taken by reference like those of a call written as an expression, unless the callee is an array.
                if (context.Facts is { } facts && facts.TryGet(callStatement.Callee.Identity, out var callee) && callee.DeclaredType is not VBArrayType)
                {
                    ExpressionStaticSemanticsEvaluator.MarkPassedAsArguments(context, callStatement.Arguments);
                }

                if (callStatement.IsExplicitCall && context.Facts is { } explicitFacts && explicitFacts.TryGet(callStatement.Callee.Identity, out var explicitCallee))
                {
                    explicitFacts.Record(explicitCallee with { Flags = explicitCallee.Flags | ValueExpressionSemanticFlags.ExplicitCallKeyword });
                }
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
                EvaluateLoopBody(context, doLoop.Body, walk, isForLoop: false);
                break;
            case DoLoopUntilStatementNode doLoopUntil:
                EvaluateLoopBody(context, doLoopUntil.Body, walk, isForLoop: false);
                break;
            case DoLoopWhileStatementNode doLoopWhile:
                EvaluateLoopBody(context, doLoopWhile.Body, walk, isForLoop: false);
                break;
            case DoUntilLoopStatementNode doUntilLoop:
                EvaluateLoopBody(context, doUntilLoop.Body, walk, isForLoop: false);
                break;
            case DoWhileLoopStatementNode doWhileLoop:
                EvaluateLoopBody(context, doWhileLoop.Body, walk, isForLoop: false);
                break;
            // a While...Wend loop is not a Do loop, and has no Exit statement of its own.
            case WhileWendStatementNode whileWend:
                EvaluateBlock(context, whileWend.Body, walk);
                break;
            case ForStatementNode forStatement:
                EvaluateLoopBody(context, forStatement.Body, walk, isForLoop: true);
                break;
            case ForEachStatementNode forEachStatement:
                EvaluateLoopBody(context, forEachStatement.Body, walk, isForLoop: true);
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

    // an expression a statement writes to is a fact of it: the element of an array is written through the array it is an element of.
    private static void MarkWritten(StaticEvaluationContext context, ExpressionNode target)
    {
        if (context.Facts is not { } facts)
        {
            return;
        }

        while (target is IndexExpressionNode index)
        {
            target = index.Callee;
        }

        if (facts.TryGet(target.Identity, out var fact))
        {
            facts.Record(fact with { Flags = fact.Flags | ValueExpressionSemanticFlags.AssignmentTarget });
        }
    }

    // an Exit For or Exit Do is checked against the loops it is lexically inside, so a body is walked with the kind of loop it belongs to counted.
    private static void EvaluateLoopBody(StaticEvaluationContext context, StatementBlock body, Walk walk, bool isForLoop)
    {
        if (isForLoop)
        {
            walk.ForDepth++;
        }
        else
        {
            walk.DoDepth++;
        }

        EvaluateBlock(context, body, walk);

        if (isForLoop)
        {
            walk.ForDepth--;
        }
        else
        {
            walk.DoDepth--;
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
            && context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol
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
                if (!walk.Structural)
                {
                    CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, onGoTo.Selector), walk);
                }

                foreach (var label in onGoTo.Labels)
                {
                    ReferenceLabel(label, walk);
                }
                return true;
            case OnGoSubStatementNode onGoSub:
                if (!walk.Structural)
                {
                    CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, onGoSub.Selector), walk);
                }

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
            ? context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol as VBPredeclaredInstanceSymbol
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

        // the names the walked procedure has declared so far: its parameters, variables and constants share one scope.
        public HashSet<string> DeclaredNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        // the kind of procedure the walked body belongs to, when it is known.
        public MemberKind? Procedure { get; init; }

        // which branches are excluded, and which language the body is written in.
        public StaticSemanticsOptions Options { get; init; }

        // when there is no workspace to resolve names in, only what needs none is checked: the types of expressions are not.
        public bool Structural { get; init; }

        // how many loops of each kind the statement being walked is lexically inside.
        public int ForDepth { get; set; }

        public int DoDepth { get; set; }
    }
}
