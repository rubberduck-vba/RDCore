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
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;
using RDCore.SDK;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.15, §5.4.2.12, §5.4.2.16, §5.4.4.1, §5.4.4.2, §5.2.4.1.2; RD-VBAL §5.2.4.1.2
// collects every error rather than stopping at the first: unlike an expression's operands, statements are largely independent.
// a Set to the default instance variable of a predeclared class is valid, as MS-VBA has it.
public static class StatementStaticSemanticsEvaluator
{
    // a label is scoped to its procedure: a nested block walked on its own would report every jump out of it as undefined.
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(StaticEvaluationContext context, StatementBlock block, MemberKind? procedure = null)
        => Evaluate(context, block, default, procedure);

    public static ImmutableArray<VBCompileErrorInfo> Evaluate(
        StaticEvaluationContext context, StatementBlock block, StaticSemanticsOptions options, MemberKind? procedure = null)
        => Run(context, block, new Walk { Procedure = procedure, Options = options });

    public static ImmutableArray<VBCompileErrorInfo> CheckStructure(StatementBlock block, StaticSemanticsOptions options = default, MemberKind? procedure = null)
        => Run(default, block, new Walk { Procedure = procedure, Options = options, Structural = true });

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
        if (!walk.Structural)
        {
            FindNamesUsedBeforeTheirReDim(context, block, walk);
        }

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

            if (SplitBlockErrorOf(child, walk.Options) is { } split)
            {
                walk.Errors.Add(split);
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
                    EvaluateDeclarationExpressions(context, declaration, walk);
                    goto default;
                case ConstantDeclarationNode { Name: var name } declaration:
                    ReportDuplicateDeclaration(walk, name, declaration.Location);
                    EvaluateDeclarationExpressions(context, declaration, walk);
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

    // the value of a constant and the bounds of an array that a procedure declares are expressions of its code, as its statements are: evaluated for what the
    // names in them refer to. What is wrong with them is the declaration's to report (DeclaredSizeStaticSemantics, the constant's own evaluation).
    private static void EvaluateDeclarationExpressions(StaticEvaluationContext context, SyntaxNode declaration, Walk walk)
    {
        if (walk.Structural)
        {
            return;
        }

        foreach (var expression in DeclarationExpressions(declaration))
        {
            _ = ExpressionStaticSemanticsEvaluator.Evaluate(context, expression);
        }
    }

    private static IEnumerable<ExpressionNode> DeclarationExpressions(SyntaxNode declaration)
        => declaration.Children.SelectMany(IEnumerable<ExpressionNode> (child) => child switch
        {
            ArrayBoundsNode { Bounds: { IsDefault: false } bounds } => bounds.SelectMany(bound => new[] { bound.LowerExpression, bound.UpperExpression }).OfType<ExpressionNode>(),
            // a type is no value, and a name written in an As clause is a type.
            AsTypeExpressionNode => [],
            ExpressionNode value => [value],
            _ => [],
        });

    // MS-VBAL §3.4.2: the parser reads every branch, so a block can be opened on one side of an excluded branch and closed on the other. The tree has the
    // block, and the code MS-VBA compiles does not: a live opening statement whose end is excluded is not closed, and a live end whose opening statement is
    // excluded closes no block. Each is reported at its live statement, with MS-VBA's description; a block with both ends excluded and live code between them is
    // reported where that code begins.
    private static VBCompileErrorInfo? SplitBlockErrorOf(SyntaxNode node, StaticSemanticsOptions options)
    {
        (VBCompileErrorId Opened, VBCompileErrorId Closed)? block = node switch
        {
            IfBlockStatementNode => (VBCompileErrorId.BlockIfWithoutEndIf, VBCompileErrorId.EndIfWithoutBlockIf),
            ForStatementNode or ForEachStatementNode => (VBCompileErrorId.ForWithoutNext, VBCompileErrorId.NextWithoutFor),
            DoLoopStatementNode or DoLoopUntilStatementNode or DoLoopWhileStatementNode or DoUntilLoopStatementNode or DoWhileLoopStatementNode
                => (VBCompileErrorId.DoWithoutLoop, VBCompileErrorId.LoopWithoutDo),
            WithStatementNode => (VBCompileErrorId.ExpectedEndWith, VBCompileErrorId.EndWithWithoutWith),
            SelectCaseStatementNode => (VBCompileErrorId.SelectCaseWithoutEndSelect, VBCompileErrorId.SyntaxError),
            WhileWendStatementNode => (VBCompileErrorId.SyntaxError, VBCompileErrorId.SyntaxError),
            _ => null,
        };

        if (block is not (var opened, var closed))
        {
            return null;
        }

        var range = node.SourceLocation.Range;
        (VBCompileErrorId Id, SourcePosition At)? error = (options.DeadRangeAt(range.Start), options.DeadRangeAt(range.End)) switch
        {
            (null, null) => null,
            (null, not null) => (opened, range.Start),
            (not null, null) => (closed, range.End),
            ({ } start, not null) => (VBCompileErrorId.SyntaxError, start.End),
        };

        return error is (var id, var at)
            ? VBCompileErrorInfo.For(id, node.SourceLocation with { Range = new SourceRange(at, at) }, Exceptions.VBCompileError_BlockEndsInAnotherBranch_Verbose)
            : null;
    }

    private static void ReportDuplicateDeclaration(Walk walk, string name, SourceLocation location)
    {
        if (name.Length == 0)
        {
            return;
        }

        if (!walk.DeclaredNames.TryGetValue(name, out var prior))
        {
            walk.DeclaredNames.Add(name, prior = []);
        }

        // a name declared in each branch of a #If block is declared once, however the block evaluates.
        var blocks = walk.Options.Blocks ?? ConditionalCompilationBlocks.None;
        if (prior.Any(site => !blocks.AreAlternatives(site, location.Range.Start)))
        {
            walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateDeclaration, location, $"'{name}' is declared more than once in this procedure."));
        }

        prior.Add(location.Range.Start);
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

            var targetResult = WithTarget(withStatement.WithExpression, ExpressionStaticSemanticsEvaluator.Evaluate(context, withStatement.WithExpression));
            CollectError(targetResult, walk);

            // a target that is an error is reported once: the body is still in a With block, of a target whose type is not known, and its members are
            // late-bound rather than each an implicit With-reference outside any.
            var bodyContext = context with { EnclosingWithTargetType = targetResult.IsSuccess ? targetResult.Result : VBUnknownType.TypeInfo };
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

        // RD-VBAL: the graphics statements draw on an object, and a statement with none has no surface to draw on. What the points and the arguments hold
        // is evaluated all the same, so that a name written in one is a reference the pass knows of.
        // TODO the coordinates and the colors are numbers: a coordinate that cannot be one is an error the pass does not report yet.
        if (statement is GraphicsMethodStatementNode graphics)
        {
            if (GraphicsStatementStaticSemantics.Evaluate(graphics) is { } graphicsError)
            {
                walk.Errors.Add(graphicsError);
                return;
            }

            if (!walk.Structural)
            {
                foreach (var expression in GraphicsStatementStaticSemantics.Expressions(graphics))
                {
                    CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, expression), walk);
                }
            }

            return;
        }

        // an array is no value Debug.Print can write, written `f` or `f()`: a type mismatch when it compiles (MS-VBA, Excel-verified, oracle\s53 E02, E03, F02,
        // F03). Its items are evaluated as any statement's are, after.
        if (statement is DebugPrintStatementNode debugPrint && !walk.Structural
            && debugPrint.Items.FirstOrDefault(item => item.Value is { } printed
                && ExpressionStaticSemanticsEvaluator.Evaluate(context, printed) is { IsSuccess: true, Result: VBArrayType }) is { Value: { } printedArray })
        {
            walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, printedArray.Location, Exceptions.VBCompileError_ArrayPrinted_Verbose));
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
            var valueResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Value);

            // what a Set statement assigns is an object, which a project, a module or an enumeration is not: a type mismatch (MS-VBA, Excel-verified,
            // oracle\s29 C03, C04, O20), rather than what the name is classified as.
            if (assignment.Kind == AssignmentKind.Set && NameClassificationStaticSemantics.IsNotAValue(assignment.Value, valueResult.ErrorInfo as VBCompileErrorInfo))
            {
                valueResult = StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, assignment.Value.Location,
                    string.Format(Exceptions.VBCompileError_NameIsNoObject_Verbose, NameClassificationStaticSemantics.NameOf(assignment.Value),
                        valueResult.ErrorInfo!.Verbose)));
            }

            CollectError(valueResult, walk);

            // a name assigned before the ReDim that declares it is no variable yet: that is what is wrong ("Variable not defined", reported at the name), and
            // not what it is assigned.
            if (assignment.Target is SimpleNameExpressionNode implicitVariable
                && walk.NamesUsedBeforeTheirReDim.TryGetValue(implicitVariable.IdentifierName, out var redimAt) && assignment.SourceLocation.Range.Start < redimAt)
            {
                return;
            }

            // MS-VBAL §5.4.3.8: a value let-assigned to an object is assigned to its default member, which is not the coercion of the value to the object.
            var assignsADefaultMember = assignment.Kind != AssignmentKind.Set && targetResult.Result is VBClassType or VBObjectType;

            // MS-VBAL §5.4.3.8, §5.4.3.9, §5.6.16.6: what is assigned to is a variable - or a property, or a function's own result - and never a
            // constant nor a function of a library. A Let statement assigns an object's default member through whatever gives the object, a library's
            // function too, and its target may be a library's function whose declared type is Variant (§5.4.3.8 has that raise error 450 when it
            // runs); the target of an LSet or an RSet statement is a bound variable expression, whatever its declared type.
            var isLet = assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet;
            var letsADefaultMember = assignsADefaultMember && isLet;
            var (role, section) = TargetOf(assignment.Kind);
            if (!letsADefaultMember && !RequireTarget(context, assignment.Target, role, section, assignment.Kind, walk))
            {
                return;
            }

            // what the target is declared as, or the member it is, comes next: a target that is no variable at all says so first.
            if (targetResult.IsSuccess && TargetKindError(context, assignment, targetResult.Result!) is { } targetKindError)
            {
                walk.Errors.Add(targetKindError);
                return;
            }

            if (assignment.Kind == AssignmentKind.Set)
            {
                MarkSetThroughACall(context, assignment.Target);
            }

            // a resizable array is assigned an array, or a Variant that may hold one (or a String, the data of a Byte array, §5.5.1.2.6); a number, a
            // String, a Boolean or a Date is never one, which MS-VBA refuses before the coercion of it is considered (Excel-verified, oracle\findings-labels
            // A03, A04, A06, R02, oracle\s44 G01-G08, G10: "Can't assign to array"; G09 assigns a Byte() a String).
            if (isLet && targetResult.IsSuccess && targetResult.Result is VBArrayType { ItemType: var itemType } && assignment.Target is SimpleNameExpressionNode arrayName
                && valueResult.IsSuccess && valueResult.Result is VBNumericType or VBBooleanType or VBDateType or VBStringType
                && !(valueResult.Result is VBStringType && itemType is VBByteType))
            {
                walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.CantAssignToArray, assignment.Target.Location, arrayName.IdentifierName));
                return;
            }

            if (!assignsADefaultMember && targetResult.IsSuccess && valueResult.IsSuccess && ResolveCoercionRule(assignment.Kind) is { } coercionRule)
            {
                CollectError(coercionRule.DetermineDeclaredType(context, assignment.Value, valueResult.Result!, DeclaredTypeOfTarget(context, assignment, targetResult.Result!)), walk);
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

        // MS-VBAL §5.4.3.5: the string it replaces a part of is written to.
        if (statement is MidStatementNode mid)
        {
            if (!walk.Structural)
            {
                walk.Errors.AddRange(MidStatementStaticSemantics.Evaluate(context, mid));
                MarkWritten(context, mid.Target);
            }
            return;
        }

        // MS-VBAL §5.4.5, and Name. The variables a file statement reads into are written to.
        if (!walk.Structural && FileStatementStaticSemantics.TryEvaluate(context, statement, out var fileStatementErrors))
        {
            walk.Errors.AddRange(fileStatementErrors);
            foreach (var read in ReadIntoBy(statement))
            {
                MarkWritten(context, read);
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
                    var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, expression);
                    CollectError(statement is ForEachStatementNode forEach && ReferenceEquals(expression, forEach.CollectionExpression)
                        ? ForEachSource(expression, result)
                        : result, walk);
                }
            }

            // what a statement writes to, apart from an assignment's target: the counter or the control variable of a loop, the array a ReDim gives its
            // dimensions, and the arrays an Erase empties (a Mid statement's target, and a file statement's variables, are marked where they are evaluated). The control variable of a loop is a bound
            // variable expression, which is "invalid if it is classified as something other than a variable expression" (MS-VBAL §5.4.2.3, §5.4.2.4,
            // §5.6.16.6); the name a ReDim matches is the "redimensioned variable" (§5.4.3.3); and an element of an Erase "MUST be classified as a
            // variable, property, function or unbound member" (§5.4.3.4). None of them is ever a constant.
            switch (statement)
            {
                case ForStatementNode forStatement:
                    RequireVariable(context, forStatement.ControlExpression, "The control variable of a For statement", "5.6.16.6", walk);
                    MarkWritten(context, forStatement.ControlExpression);
                    RequireNextOfControl(forStatement.ControlExpression, forStatement.NextExpression, walk);
                    break;
                case ForEachStatementNode forEachStatement:
                    RequireVariable(context, forEachStatement.ControlExpression, "The control variable of a For Each statement", "5.6.16.6", walk);
                    MarkWritten(context, forEachStatement.ControlExpression);
                    RequireNextOfControl(forEachStatement.ControlExpression, forEachStatement.NextExpression, walk);
                    break;
                case RedimDeclarationNode redim:
                    // the target is the array, which is not one of the statement's operands: it is evaluated here.
                    var redimensioned = ExpressionStaticSemanticsEvaluator.Evaluate(context, redim.Target);
                    CollectError(redimensioned, walk);
                    if (redimensioned.IsSuccess && MemberOfAnObject(context, redim.Target) is { } refused)
                    {
                        walk.Errors.Add(refused);
                    }

                    // the type a ReDim states is a type expression as a declaration's is (MS-VBAL §5.6.16.7): a module or a project is no type (MS-VBA, Excel-
                    // verified, oracle\s29 D03, D09). A ReDim that declares its array states the type of a declaration, which is checked as one.
                    if (redim.AsType is { FixedLength: null } statedType && !DeclaresItsArray(context, redim)
                        && NoTypeErrorOf(context, statedType) is { } notAType)
                    {
                        walk.Errors.Add(notAType);
                    }

                    var redimensionedSymbol = RedimensionedSymbolOf(context, redim.Target);
                    if (redimensionedSymbol is VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol)
                    {
                        // a constant - the module's, another's, the library's (vbCrLf), a member of an enumeration - is the name the ReDim matches, and no array
                        // (MS-VBA: "Expected array", Excel-verified, oracle\s29 E01, E03, E11, E15), rather than a constant that is assigned.
                        walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedArray, redim.Target.Location,
                            string.Format(Exceptions.VBCompileError_RedimOfAConstant_Verbose, redimensionedSymbol.Name)));
                    }
                    else if (RequireVariable(context, redim.Target, "The target of a ReDim statement", "5.4.3.3", walk)
                        && redimensionedSymbol is { } array
                        && array.TryGetProperty(SymbolProperties.ArrayBounds, out var fixedBounds) && !fixedBounds.IsDefaultOrEmpty)
                    {
                        // an array declared with fixed dimensions keeps them, this module's or another's, bare or qualified (MS-VBA: "Array already
                        // dimensioned", Excel-verified, oracle\s29 E04, E14).
                        walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.ArrayAlreadyDimensioned, redim.Target.Location, array.Name));
                    }
                    else if (redimensioned.Result is VBNumericType or VBStringType or VBBooleanType or VBDateType)
                    {
                        // what is re-dimensioned is an array, as what is erased is: a variable of an intrinsic type other than Variant never holds one (MS-VBA:
                        // "Expected array", Excel-verified).
                        walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedArray, redim.Target.Location,
                            redim.Target is SimpleNameExpressionNode scalar ? scalar.IdentifierName : string.Empty));
                    }
                    else if (redimensioned.Result is VBResizableArrayType { ItemType: var itemType } && redim.AsType is { } asType
                        && StatedTypeOf(context, asType) is { } stated
                        && itemType is not (VBUnknownType or VBFixedStringType)
                        && (!IsSameType(itemType, stated) || asType.AsAutoObject != IsAutoInstantiated(context, redim.Target)))
                    {
                        // MS-VBAL §5.4.3.3: a ReDim of an array declared as one gives it its dimensions, not another element type - and an array declared As New
                        // is one of automatic instantiation variables, which one that is not declared so is not (MS-VBA: "Can't change data types of array
                        // elements", Excel-verified, oracle\s19 N18, N33, N35). Its As clause may be As New, as MS-VBA has it, where the specification says it
                        // MUST NOT (RD-VBAL §5.4.3.3).
                        walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.CantChangeDataTypesOfArrayElements, redim.Target.Location,
                            string.Format(Exceptions.VBCompileError_CantChangeDataTypesOfArrayElements_Verbose, redim.Name, itemType.Name,
                                asType.AsAutoObject ? $"{Tokens.New} {stated.Name}" : stated.Name)));
                    }

                    MarkWritten(context, redim.Target);
                    break;
                case KeywordStatementNode { Token: Tokens.Erase } erase:
                    var elements = new StatementOperandChecks(context);
                    foreach (var element in erase.Inputs.OfType<ExpressionNode>())
                    {
                        elements.RequireNotAValue(element, "An element of an Erase statement", "5.4.3.4");
                        elements.RequireErasable(element);
                    }
                    walk.Errors.AddRange(elements.Errors);

                    // what is erased is an array: a variable of an intrinsic type other than Variant never holds one (MS-VBA: "Expected array",
                    // Excel-verified).
                    foreach (var element in elements.Errors.IsEmpty ? erase.Inputs.OfType<ExpressionNode>() : [])
                    {
                        if (ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, element).Result is VBNumericType or VBStringType or VBBooleanType or VBDateType)
                        {
                            walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.ExpectedArray, element.Location, element is SimpleNameExpressionNode erased ? erased.IdentifierName : string.Empty));
                        }
                    }

                    foreach (var element in erase.Inputs.OfType<ExpressionNode>())
                    {
                        MarkWritten(context, element);
                    }
                    break;
            }

            // the keyword is how the statement is written, which the expression it calls is not: a fact of the callee, for whoever finds it obsolete.
            if (statement is CallStatementNode callStatement)
            {
                if (NotAProcedureError(context, callStatement) is { } notAProcedure)
                {
                    walk.Errors.Add(notAProcedure);
                }

                // an argument written ByVal is valid where MS-VBA compiles it (RD-VBAL §5.6.13.1), in a statement as in an expression.
                var calleeType = ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, callStatement.Callee).Result;
                if (ByValArgumentStaticSemantics.Error(context, callStatement.Callee, callStatement.Arguments, calleeType) is { } byValError)
                {
                    walk.Errors.Add(byValError);
                }
                else
                {
                    ByValArgumentStaticSemantics.MarkRedundant(context, callStatement.Callee, callStatement.Arguments, calleeType);
                }

                // the arguments of the statement may be taken by reference like those of a call written as an expression, unless the callee is an array.
                if (context.Facts is { } facts && facts.TryGet(callStatement.Callee.Identity, out var callee) && callee.DeclaredType is not VBArrayType)
                {
                    ExpressionStaticSemanticsEvaluator.MarkPassedAsArguments(context, callStatement.Callee, callStatement.Arguments);
                    ObjectVariableArgumentStaticSemantics.MarkPassedAsCopies(context, callStatement.Callee, callStatement.Arguments);
                }

                // a ParamArray is no variable a ByRef parameter can be bound to (MS-VBAL §5.3.1.11), and an object given to a parameter that expects a
                // value of it must have one (MS-VBAL §5.5.1.1).
                if (ObjectValueStaticSemantics.MemberOfCallee(context, callStatement.Callee) is { } calledMember
                    && ObjectValueStaticSemantics.ParametersOfCallee(context, callStatement.Callee) is { } parameters
                    && (ArgumentListStaticSemantics.Error(context, callStatement.Callee, calledMember, parameters, callStatement.Arguments)
                        ?? ParamArrayStaticSemantics.ArgumentsError(context, calledMember, parameters, callStatement.Arguments)
                        ?? ObjectValueStaticSemantics.ArgumentsError(context, parameters, callStatement.Arguments)) is { } argumentError)
                {
                    walk.Errors.Add(argumentError);
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
                EvaluateLoopBody(context, forStatement.Body, walk, isForLoop: true, forStatement.ControlExpression);
                break;
            case ForEachStatementNode forEachStatement:
                EvaluateLoopBody(context, forEachStatement.Body, walk, isForLoop: true, forEachStatement.ControlExpression);
                MarkCollectionSetToNothing(context, forEachStatement);
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

    // MS-VBAL §5.4.2.21: a With block is about an object, a record or a Variant, whose members a with-member-access refers to. MS-VBA refuses any other value,
    // a project, a module or an enumeration included (Excel-verified, oracle\s29 C20, C33, O21, W1-W3, W9, Y8, Y10); Nothing is an object's value.
    private static StaticSemanticsEvaluationResult WithTarget(ExpressionNode target, StaticSemanticsEvaluationResult result)
    {
        if (NameClassificationStaticSemantics.IsNotAValue(target, result.ErrorInfo as VBCompileErrorInfo))
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.WithObjectMustBeUserDefinedTypeObjectOrVariant, target.Location,
                string.Format(Exceptions.VBCompileError_WithName_Verbose, result.ErrorInfo!.Verbose)));
        }

        return result is { IsSuccess: true, Result: var type } && !HasMembers(type!)
            ? StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.WithObjectMustBeUserDefinedTypeObjectOrVariant, target.Location,
                string.Format(Exceptions.VBCompileError_WithObject_Verbose, NameClassificationStaticSemantics.NameOf(target) is { Length: > 0 } name ? name : type!.Name, type!.Name)))
            : result;
    }

    private static bool HasMembers(VBType type) => type is VBUserDefinedType or VBClassType or VBObjectType or VBVariantType or VBUnknownType;

    // MS-VBAL §5.4.2.4: what a For Each loop iterates is an array or an object, written as a name, a member or a call: a literal, an operator's result and
    // anything in parentheses are a syntax error, and a value of any other type, a record, a project, a module or an enumeration a compile error of its own
    // (MS-VBA, Excel-verified, oracle\s29 C21, C34, O22, W4-W7, Y1-Y7).
    private static StaticSemanticsEvaluationResult ForEachSource(ExpressionNode source, StaticSemanticsEvaluationResult result)
    {
        if (source is LiteralExpressionNode or VBUnaryOperatorExpressionNode or VBBinaryOperatorExpressionNode or TypeOfIsExpressionNode)
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, source.Location, Tokens.In));
        }

        if (NameClassificationStaticSemantics.IsNotAValue(source, result.ErrorInfo as VBCompileErrorInfo))
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ForEachMayOnlyIterateOverACollectionObjectOrAnArray, source.Location,
                string.Format(Exceptions.VBCompileError_ForEachName_Verbose, result.ErrorInfo!.Verbose)));
        }

        return result is { IsSuccess: true, Result: VBNumericType or VBStringType or VBBooleanType or VBDateType or VBUserDefinedType }
            ? StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ForEachMayOnlyIterateOverACollectionObjectOrAnArray, source.Location,
                string.Format(Exceptions.VBCompileError_ForEachSource_Verbose,
                    NameClassificationStaticSemantics.NameOf(source) is { Length: > 0 } name ? name : result.Result!.Name, result.Result!.Name)))
            : result;
    }

    // MS-VBAL §5.4.2.1: what a call statement calls is a procedure; a variable or a constant is none. MS-VBA says which by where it is declared (Excel-
    // verified, oracle\s12 J12, oracle\s21 E01-E21), with Call or without: "Expected Sub, Function, or Property" for one of the procedure - a parameter
    // too - and "Expected procedure, not variable" for one of a module, bare or qualified. Without Call, a variable of an object is called on its default
    // member, which is valid; a Variant is no object, and is the error too.
    private static VBCompileErrorInfo? NotAProcedureError(StaticEvaluationContext context, CallStatementNode call)
    {
        var (named, name) = call.Callee switch
        {
            SimpleNameExpressionNode simple => (context.Resolver.ResolveValue(simple, ScopeKind.Local, context.Scope.Uri).Symbol, simple.IdentifierName),
            MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
                => (context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol, access.Member.IdentifierName),
            _ => (null, string.Empty),
        };

        if (named is not ITypedSymbol { ResolvedType: var type }
            || named is VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol
                or VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol
            || !call.IsExplicitCall && type is VBClassType or VBObjectType or VBUnknownType)
        {
            return null;
        }

        return named.ScopeKind is ScopeKind.Local
            ? VBCompileErrorInfo.For(VBCompileErrorId.ExpectedSubFunctionOrProperty, call.Callee.Location, name)
            : VBCompileErrorInfo.For(VBCompileErrorId.ExpectedProcedureNotVariable, call.Callee.Location, name);
    }

    // the variables a file statement reads into (MS-VBAL §5.4.5): every variable of an Input #, the variable of a Line Input # and of a Get #, which is its last
    // operand, after the file number and the record number, written or not.
    private static IEnumerable<ExpressionNode> ReadIntoBy(StatementNode statement) => statement switch
    {
        KeywordStatementNode { Token: Tokens.Input, Inputs: [_, .. var variables] } => variables.OfType<ExpressionNode>(),
        KeywordStatementNode { Token: Tokens.LineInput or Tokens.Get, Inputs: [_, .., ExpressionNode variable] } => [variable],
        _ => [],
    };

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

    // an Exit For or Exit Do is checked against the loops it is lexically inside, so a body is walked with the kind of loop it belongs to counted. A loop
    // nested in a For loop is not controlled by its variable (MS-VBA: "For control variable already in use", Excel-verified).
    private static void EvaluateLoopBody(StaticEvaluationContext context, StatementBlock body, Walk walk, bool isForLoop, ExpressionNode? control = null)
    {
        var controlName = control is SimpleNameExpressionNode { IdentifierName: var name } ? name : null;
        if (controlName is not null && !walk.Structural && walk.ControlVariables.Contains(controlName, StringComparer.OrdinalIgnoreCase))
        {
            walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.ForControlVariableAlreadyInUse, control!.Location, controlName));
        }

        if (controlName is not null)
        {
            walk.ControlVariables.Add(controlName);
        }

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

        if (controlName is not null)
        {
            walk.ControlVariables.RemoveAt(walk.ControlVariables.Count - 1);
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

        // a handler of the event may write to what it is given by reference, as a procedure called may: Cancel, say.
        ExpressionStaticSemanticsEvaluator.MarkPassedAsArguments(context, declared?.Parameters, arguments, recordsParameters: false);
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

        // the handlers are given a copy of a public variable of an object named as its member, as a procedure called is (oracle\s58 F05).
        ObjectVariableArgumentStaticSemantics.MarkPassedAsCopies(context, parameters, arguments);

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
    // its declared type, an Enum being the declared type Long (§5.2.3.4). Null when the argument is compatible, or not
    // known to be incompatible.
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
            && !ParameterLists.SameType(parameter.ResolvedType, argumentType))
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
    private static IStaticSemantics? ResolveCoercionRule(AssignmentKind kind) => kind switch
    {
        AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet => LetCoercionStaticSemantics.Instance,
        AssignmentKind.Set => SetCoercionStaticSemantics.Instance,
        _ => null,
    };

    // what the target of each kind of assignment is to its statement, and the section of MS-VBAL that says what it can be: the target of an LSet or
    // an RSet statement is a bound variable expression.
    private static (string Role, string Section) TargetOf(AssignmentKind kind) => kind switch
    {
        AssignmentKind.Set => ("The target of a Set assignment", "5.4.3.9"),
        AssignmentKind.LSet => ("The target of an LSet statement", "5.6.16.6"),
        AssignmentKind.RSet => ("The target of an RSet statement", "5.6.16.6"),
        _ => ("The target of a Let assignment", "5.4.3.8"),
    };

    // reports an operand that has to be a variable and certainly is not one (StatementOperandChecks.RequireVariable): whether it may be one.
    // MS-VBA refuses to re-dimension a member of an object (recorded in Excel), whatever the member is - a public Variant of the class, a property: the
    // object's member is reached through the object, and a class re-dimensions its own arrays. A member of an object of a declared class is "Method or data
    // member not found", and of an Object "Invalid ReDim"; one of a Variant is not known to be an object's until it runs, and re-dimensions a value of it then.
    // The member of a With block's object is the same member. MS-VBAL §5.4.3.3 only says the target is a variable.
    // 🧩 A language-level extension could allow it - the runtime could write the array back through the member - but RD-VBA does what MS-VBA does.
    private static VBCompileErrorInfo? MemberOfAnObject(StaticEvaluationContext context, ExpressionNode target)
    {
        if (target is not MemberAccessExpressionNode access)
        {
            return null;
        }

        var owner = access.Owner is { } ownerExpression
            ? ExpressionStaticSemanticsEvaluator.Evaluate(context, ownerExpression) is { IsSuccess: true, Result: var evaluated } ? evaluated : null
            : context.EnclosingWithTargetType;

        var id = owner switch
        {
            VBClassType => VBCompileErrorId.MethodOrDataMemberNotFound,
            VBObjectType => VBCompileErrorId.InvalidReDim,
            _ => (VBCompileErrorId?)null,
        };

        return id is { } refused
            ? VBCompileErrorInfo.For(refused, target.Location, string.Format(Exceptions.VBCompileError_ReDimOfAMemberOfAnObject_Verbose, target))
            : null;
    }

    // the variable a Next statement names, when it names one, is the control variable of the loop it ends (MS-VBA: "Invalid Next control variable
    // reference", Excel-verified).
    private static void RequireNextOfControl(ExpressionNode control, ExpressionNode? next, Walk walk)
    {
        if (next is SimpleNameExpressionNode { IdentifierName: var named } && control is SimpleNameExpressionNode { IdentifierName: var controlName }
            && !string.Equals(named, controlName, StringComparison.OrdinalIgnoreCase))
        {
            walk.Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidNextControlVariableReference, next.Location, named));
        }
    }

    private static bool RequireVariable(StaticEvaluationContext context, ExpressionNode operand, string role, string section, Walk walk)
    {
        var checks = new StatementOperandChecks(context);
        checks.RequireVariable(operand, role, section);
        walk.Errors.AddRange(checks.Errors);
        return checks.Errors.IsEmpty;
    }

    // the same, for the target of an assignment: a Let statement's may also be a library's function whose declared type is a class, Object or Variant
    // (StatementOperandChecks.RequireLetTarget), and a constant assigned by a Let or a Set statement is MS-VBA's "Assignment to constant not permitted"
    // (StatementOperandChecks.RequireAssignmentTarget).
    private static bool RequireTarget(StaticEvaluationContext context, ExpressionNode operand, string role, string section, AssignmentKind kind, Walk walk)
    {
        var checks = new StatementOperandChecks(context);
        var isLet = kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet;
        if (isLet || kind == AssignmentKind.Set)
        {
            checks.RequireAssignmentTarget(operand, role, section, isLet);
        }
        else
        {
            checks.RequireVariable(operand, role, section);
        }

        walk.Errors.AddRange(checks.Errors);
        return checks.Errors.IsEmpty;
    }

    // what the declared type of an assignment's target, or the member it is, cannot be for the kind of assignment, as MS-VBA has it (Excel-verified):
    // the type an As clause states, resolved as seen from the scope it is written in; null for one this pass cannot tell for certain: a name that resolves
    // to no type, a LongPtr, which is what the environment's pointers are and which a static pass does not know, and a fixed-length String, whose length
    // is an expression.
    private static VBType? StatedTypeOf(StaticEvaluationContext context, AsTypeExpressionNode asType)
    {
        if (asType.FixedLength is not null || string.Equals(asType.TypeName, Tokens.LongPtr, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = asType.QualifierName is { Length: > 0 } qualifier ? $"{qualifier}.{asType.TypeName}" : asType.TypeName;
        return DeclaredTypes.Resolve(context.Resolver, name, context.Scope.Uri, is64Bit: true) is { } type and not VBUnknownType ? type : null;
    }

    // what a ReDim's target names: a name, or a member of a project or a procedural module (`Globals.Items`).
    private static Symbol? RedimensionedSymbolOf(StaticEvaluationContext context, ExpressionNode target) => target switch
    {
        SimpleNameExpressionNode name => context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol,
        MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
            => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol,
        _ => null,
    };

    // MS-VBAL §5.4.3.3: a ReDim of a name that matches nothing declares its array. Where declarations are implicit, a name used before its first ReDim was
    // declared by that use, and the ReDim re-dimensions it (the symbol pass, RDC11022). Under Option Explicit no use declares anything, and the ReDim
    // declares the array from where it is: a name used before it - written, read, on a line that never runs - is "Variable not defined" there, and the
    // ReDim is valid (MS-VBA, Excel-verified, oracle\findings-labels cases-redim4 E01). The symbols declare the ReDim's array for the whole procedure, so the
    // names used before are found here, by where they are written.
    // A name in the bounds of a declared array (`Dim b(n)`) is not looked for: what MS-VBA reports of one used before its ReDim is not measured yet.
    private static void FindNamesUsedBeforeTheirReDim(StaticEvaluationContext context, StatementBlock block, Walk walk)
    {
        var nodes = block.Children.SelectMany(OutsideArrayBounds).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var redim in nodes.OfType<RedimDeclarationNode>())
        {
            // only the first ReDim of a name declares it: a name the first one declared is the array for the ReDims after it.
            if (redim.Target is not SimpleNameExpressionNode name || !seen.Add(name.IdentifierName) || !DeclaresItsArray(context, redim))
            {
                continue;
            }

            var at = redim.Location.Range.Start;
            var usedBefore = nodes.OfType<SimpleNameExpressionNode>()
                .Where(used => used.Location.Range.Start < at && string.Equals(used.IdentifierName, name.IdentifierName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (usedBefore.Length > 0)
            {
                walk.NamesUsedBeforeTheirReDim[name.IdentifierName] = at;
                walk.Errors.AddRange(usedBefore.Select(used => VBCompileErrorInfo.For(VBCompileErrorId.VariableNotDefined, used.Location, used.WrittenName)));
            }
        }
    }

    // a node and every node under it, in the order they are written, but for what the bounds of a declared array are made of.
    private static IEnumerable<SyntaxNode> OutsideArrayBounds(SyntaxNode node)
    {
        var pending = new Stack<SyntaxNode>();
        pending.Push(node);
        while (pending.TryPop(out var next))
        {
            yield return next;
            if (next is ArrayBoundsNode)
            {
                continue;
            }

            foreach (var child in next.ChildNodes().Reverse())
            {
                pending.Push(child);
            }
        }
    }

    private static bool DeclaresItsArray(StaticEvaluationContext context, RedimDeclarationNode redim)
        => redim.Target is SimpleNameExpressionNode name
            && context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is VBLocalVariableSymbol { DeclaredBy: LocalDeclarationKind.ReDim };

    // the error of a stated type that names a module or a project, which a type binding context binds and which is no type; null for anything else.
    private static VBCompileErrorInfo? NoTypeErrorOf(StaticEvaluationContext context, AsTypeExpressionNode asType)
    {
        var (bound, _) = TypeNameClassifier.Classify(asType.QualifierName is { Length: > 0 } ? asType.QualifierName : null, asType.TypeName,
            context.Scope.Uri, context.Resolver);
        var written = asType.QualifierName is { Length: > 0 } qualifier ? $"{qualifier}.{asType.TypeName}" : asType.TypeName;
        return DeclarationStaticSemanticsEvaluator.NoTypeError(bound, written, asType.Location);
    }

    // whether the variable a bare name refers to is declared As New (MS-VBAL §2.5.1).
    private static bool IsAutoInstantiated(StaticEvaluationContext context, ExpressionNode target)
        => target is SimpleNameExpressionNode name
            && context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is { } variable
            && variable.GetProperty(SymbolProperties.AutoInstantiated);

    // the same type, as a declaration states it: a class is its declaration, whichever snapshot of it a type was built from.
    private static bool IsSameType(VBType declared, VBType stated)
        => declared is VBClassType { Symbol: var declaredClass } && stated is VBClassType { Symbol: var statedClass }
            ? declaredClass.Uri.AbsoluteUri == statedClass.Uri.AbsoluteUri
            : declared.GetType() == stated.GetType() && string.Equals(declared.Name, stated.Name, StringComparison.OrdinalIgnoreCase);

    // the default instance a bare name refers to; a local or field of the same name hides it, and is what the name refers to instead.
    private static VBPredeclaredInstanceSymbol? DefaultInstanceNamedBy(StaticEvaluationContext context, ExpressionNode target)
        => target is SimpleNameExpressionNode simpleName
            ? context.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope.Uri).Symbol as VBPredeclaredInstanceSymbol
            : null;

    // MS-VBAL §5.4.3.6-7's LSet and RSet targets; §5.4.3.9's Set target, which holds an object; a property that has no accessor for the assignment
    // (§5.4.3.8, §5.4.3.9); and the default member a Let statement assigns through an object, which must be callable with no argument (§5.4.3.8).
    private static VBCompileErrorInfo? TargetKindError(StaticEvaluationContext context, AssignmentStatementNode assignment, VBType target)
    {
        var location = assignment.Target.Location;
        var name = assignment.Target is MemberAccessExpressionNode { Member: var member } ? member.IdentifierName : target.Name;

        // a property with nothing to assign it through says so first: the type it reads as is not what the assignment writes (MS-VBA, Excel-verified:
        // `Set c.Value = …` of a Property Get Value() As Long is an invalid use of the property, not a Set of a Long).
        var isSet = assignment.Kind == AssignmentKind.Set;
        if (assignment.Kind is not (AssignmentKind.LSet or AssignmentKind.RSet) && HasOnlyAPropertyGet(context, assignment.Target, isSet))
        {
            var property = assignment.Target is SimpleNameExpressionNode { IdentifierName: var named } ? named : name;
            return VBCompileErrorInfo.For(isSet ? VBCompileErrorId.InvalidUseOfProperty : VBCompileErrorId.CantAssignToReadOnlyProperty, location, property);
        }

        // the default instance of a class can be the target of a Set, as it is in MS-VBA (RD-VBAL §5.2.4.1.2), but not an extensible class's - a document
        // module, `ThisWorkbook`, `Sheet1` - which is a property of the host that extends it, and which a Set cannot assign (MS-VBA, Excel-verified,
        // oracle\s18: "Invalid use of property", whatever is assigned). `Debug` is no target at all: its name is a syntax error (SimpleNameExpressionStaticSemantics).
        if (isSet && DefaultInstanceNamedBy(context, assignment.Target) is { } instance && instance.ClassModule.GetProperty(SymbolProperties.Extensible))
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfProperty, location, instance.Name);
        }

        // a Sub of an object or of a module, named through it, has no value to assign through, with arguments or without, by a Let or a Set (MS-VBAL §5.6.2.1;
        // MS-VBA, Excel-verified, oracle\s44 E04, E10, F02, F03, F08, F09: "Expected Function or variable"). MS-VBA compiles a Sub named by its bare name (F10).
        var reached = assignment.Target is IndexExpressionNode { Callee: var indexed } ? indexed : assignment.Target;
        if (target is VBVoidType && reached is MemberAccessExpressionNode { Member: var sub }
            && ObjectValueStaticSemantics.MemberOfCallee(context, reached) is VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol))
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.ExpectedFunctionOrVariable, location, sub.IdentifierName);
        }

        // a function of an object, named as the target with no argument, is called as one given arguments is (`c.Count = 3`): a Let of one that returns neither
        // a Variant nor an object, as below; a Set of one is an invalid use of the property, and with arguments a type mismatch (MS-VBA, Excel-verified,
        // oracle\s44 E03, F01, F05, F07).
        if (reached is MemberAccessExpressionNode { Owner: { } owner } && context.Resolver.NamespaceOf(owner, context.Scope.Uri) is null
            && ObjectValueStaticSemantics.MemberOfCallee(context, reached) is VBFunctionMemberSymbol { ResolvedType: not (VBVariantType or VBObjectType or VBClassType or VBUnknownType) } called)
        {
            var isIndexed = assignment.Target is IndexExpressionNode;
            if (assignment.Kind == AssignmentKind.Set)
            {
                return VBCompileErrorInfo.For(isIndexed ? VBCompileErrorId.TypeMismatch : VBCompileErrorId.InvalidUseOfProperty, location, called.Name);
            }

            if (assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet && !isIndexed)
            {
                return VBCompileErrorInfo.For(VBCompileErrorId.FunctionCallOnLeftHandSideMustReturnVariantOrObject, location, called.Name);
            }
        }

        // a call of a function, written as the target of a Let statement, assigns the default member of what it returns: a value that is neither a Variant
        // nor an object has none (MS-VBAL §5.4.3.8; MS-VBA, Excel-verified). The function's own name in its body is its result, and is no call.
        if (assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet
            && assignment.Target is IndexExpressionNode { Callee: var callee }
            && ObjectValueStaticSemantics.MemberOfCallee(context, callee) is VBFunctionMemberSymbol { ResolvedType: not (VBVariantType or VBObjectType or VBClassType or VBUnknownType) } function
            && function.Uri.AbsoluteUri != context.Scope.Uri.AbsoluteUri)
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.FunctionCallOnLeftHandSideMustReturnVariantOrObject, location, function.Name);
        }

        // an array declared with fixed dimensions is not assigned another; one declared without them is (MS-VBA: "Can't assign to array", Excel-verified).
        if (assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet
            && assignment.Target is SimpleNameExpressionNode arrayName
            && context.Resolver.ResolveValue(arrayName, ScopeKind.Local, context.Scope.Uri).Symbol is { } array
            && array.TryGetProperty(SymbolProperties.ArrayBounds, out var bounds) && !bounds.IsDefaultOrEmpty)
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.CantAssignToArray, location, arrayName.IdentifierName);
        }

        switch (assignment.Kind)
        {
            case AssignmentKind.LSet when target is not (VBStringType or VBVariantType or VBUserDefinedType or VBUnknownType):
                return VBCompileErrorInfo.For(VBCompileErrorId.LSetAllowedOnlyOnStringsAndUserDefinedTypes, location, target.Name);
            case AssignmentKind.RSet when target is not (VBStringType or VBVariantType or VBUnknownType):
                return VBCompileErrorInfo.For(VBCompileErrorId.RSetAllowedOnlyOnStrings, location, target.Name);
            case AssignmentKind.Set when target is VBNumericType or VBStringType or VBBooleanType or VBDateType:
                return VBCompileErrorInfo.For(VBCompileErrorId.ObjectRequired, location, target.Name);
        }

        return !isSet && target is VBClassType && ObjectValueStaticSemantics.ValueOf(context, assignment.Target, target, ValueDestination.AnyValue) is { IsError: true } refused
            ? refused.ErrorInfo as VBCompileErrorInfo
            : null;
    }

    // whether a property assigned to has a Property Get and nothing the assignment writes through - no Property Set for a Set (MS-VBAL §5.4.3.9); no
    // Property Let for a Let, whose Get is then of a type that has no default member to assign instead: a type other than a class, Object or Variant
    // (§5.4.3.8) - and no public variable of the name. A property of the class named by its bare name, from another of its procedures, is one too; in its
    // own Property Get, the name is the result (§5.4.3.8, §5.4.3.9: "does not refer to the enclosing procedure").
    private static bool HasOnlyAPropertyGet(StaticEvaluationContext context, ExpressionNode target, bool isSet)
        => AccessorsOf(context, target) is { } members
            && members.OfType<VBPropertyGetMemberSymbol>().FirstOrDefault() is { } get
            && (isSet ? !SetsThroughWhatItReturns(context, get.ResolvedType) : get.ResolvedType is not (VBClassType or VBObjectType or VBVariantType))
            && !members.Any(member => member.Kind == SymbolKindExt.Field || (isSet ? member is VBPropertySetMemberSymbol : member is VBPropertyLetMemberSymbol));

    // MS-VBAL §5.3.1.7: the declared type of a Property Let or a Property Set is the declared type of its value parameter. A property that can't be read -
    // that has no Property Get - is assigned through its Property Set by a Set statement (§5.4.3.9), and through its Property Let by a Let statement
    // (§5.4.3.8): what is assigned is coerced to that accessor's value parameter. A property that can be read is declared the type its Property Get
    // returns, as its target is evaluated.
    private static VBType DeclaredTypeOfTarget(StaticEvaluationContext context, AssignmentStatementNode assignment, VBType evaluated)
    {
        // a property with parameters is named with its arguments, `w.Item(1)`: the accessors are those of the property they are given to.
        var property = assignment.Target is IndexExpressionNode { Callee: var called } ? called : assignment.Target;
        if (evaluated is not VBVoidType || AccessorsOf(context, property) is not { } members || members.OfType<VBPropertyGetMemberSymbol>().Any())
        {
            return evaluated;
        }

        VBProcedureMemberSymbol? accessor = assignment.Kind == AssignmentKind.Set
            ? members.OfType<VBPropertySetMemberSymbol>().FirstOrDefault()
            : members.OfType<VBPropertyLetMemberSymbol>().FirstOrDefault();
        return accessor is { Parameters: [.., var value] } ? value.ResolvedType : evaluated;
    }

    // the accessors of the property the target names, and whatever else the module declares of the name; none when the target names no property, or names
    // the procedure it is assigned in.
    private static VBTypeMemberSymbol[]? AccessorsOf(StaticEvaluationContext context, ExpressionNode target)
    {
        VBTypeMemberSymbol[] members;
        if (target is SimpleNameExpressionNode name)
        {
            // whichever accessor the name resolves to, the property is all of them: of the module's own, or of another module of the workspace (MS-VBA,
            // Excel-verified, oracle\s44 W03). A library's property named by its bare name outside its module is called, as MS-VBA compiles
            // `Set Now = Nothing` (s28 W5); a form's control, named in the form, is the form's own.
            var module = context.Scope.SelfAndAncestors().FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Module)?.Uri;
            if (context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is not VBTypeMemberSymbol { } property
                || property is not IVBPropertyMemberSymbol
                || property.Uri.AbsoluteUri == context.Scope.Uri.AbsoluteUri
                || module is null
                || property.ParentUri.AbsoluteUri != module.AbsoluteUri && property.GetProperty(SymbolProperties.ExternalTarget) is { Length: > 0 })
            {
                return null;
            }

            // its accessors and whatever else its module declares of the name, as the scope has them
            members = [.. context.Scope.SelfAndAncestors().SelectMany(scope => scope.DeclaredAs(property.Name)).OfType<VBTypeMemberSymbol>()
                .Where(member => member.ParentUri.AbsoluteUri == property.ParentUri.AbsoluteUri)];
        }
        else if (target is MemberAccessExpressionNode { Owner: { } qualified } moduleAccess
            && context.Resolver.NamespaceOf(qualified, context.Scope.Uri) is { } qualifier)
        {
            // a property of a standard module named through the module - `Set Program.Items = Nothing` - is the property its bare name is, from anywhere
            // (MS-VBA, Excel-verified, oracle\s44 V01-V04): its accessors are the module's members of the name.
            if (context.Resolver.ResolveMember(qualifier, moduleAccess.Member, context.Scope.Uri).Symbol is not VBTypeMemberSymbol { } property
                || property is not IVBPropertyMemberSymbol
                || property.Uri.AbsoluteUri == context.Scope.Uri.AbsoluteUri)
            {
                return null;
            }

            members = qualifier is VBModuleSymbol { Members: { IsDefaultOrEmpty: false } declared }
                ? [.. declared.Where(member => string.Equals(member.Name, property.Name, StringComparison.OrdinalIgnoreCase))]
                : [.. context.Scope.SelfAndAncestors().SelectMany(scope => scope.DeclaredAs(property.Name)).OfType<VBTypeMemberSymbol>()
                    .Where(member => member.ParentUri.AbsoluteUri == property.ParentUri.AbsoluteUri)];
            if (members.Length == 0)
            {
                members = [property];
            }
        }
        else if (target is MemberAccessExpressionNode access)
        {
            var owner = access.Owner is { } ownerExpression
                ? ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, ownerExpression).Result
                : context.EnclosingWithTargetType;
            if (owner is not VBClassType { Symbol: var snapshot })
            {
                return null;
            }

            members = [.. (VBProjectSymbol.ResolveClass(context.Resolver, snapshot) ?? snapshot).DefaultInterfaceMembers
                .Where(member => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase))];
        }
        else
        {
            return null;
        }

        return members;
    }

    // a Set to Nothing of the variable the loop walks, inside the loop, which RD-VBA leaves as it is while the loop runs (ForEachCollectionStaticSemantics):
    // the fact is the Set's target, marked once the body has recorded it.
    private static void MarkCollectionSetToNothing(StaticEvaluationContext context, ForEachStatementNode loop)
    {
        if (context.Facts is not { } facts)
        {
            return;
        }

        foreach (var set in ForEachCollectionStaticSemantics.SetsToNothingOfItsCollection(loop))
        {
            if (facts.TryGet(set.Target.Identity, out var fact))
            {
                facts.Record(fact with { Flags = fact.Flags | ValueExpressionSemanticFlags.CollectionSetToNothingInItsOwnLoop });
            }
        }
    }

    // MS-VBAL §5.4.3.9: a function, or a property that has no Property Set, named as the target of a Set outside its own body is called, and what it returns
    // is Set through its default member. MS-VBA compiles it, and a call of a class whose default member has no Property Set fails whenever it runs: error 438
    // (Excel-measured, oracle\s28 B25), which its declared type alone says. A call of another type that is no object is a compile error already (B23); an
    // Object or a Variant can return an object whose default member has one (s31 D02, D07), or no object at all (s28 W4, W5: error 424), which only its value
    // says. The fact is the callee's, for an analyzer to report what the language leaves to the run time.
    private static void MarkSetThroughACall(StaticEvaluationContext context, ExpressionNode target)
    {
        var callee = target is IndexExpressionNode { Callee: var called } ? called : target;
        if (context.Facts is not { } facts
            // a variable of the module is a VBReturningMemberSymbol too: what is called is a function, or a Property Get
            || ObjectValueStaticSemantics.MemberOfCallee(context, callee) is not VBReturningMemberSymbol { } member
            || member is not (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol)
            || member.Uri.AbsoluteUri == context.Scope.Uri.AbsoluteUri
            || member is VBPropertyGetMemberSymbol && HasAPropertySet(context, callee, member))
        {
            return;
        }

        if (member.ResolvedType is VBClassType returned && !SetsThroughWhatItReturns(context, returned) && facts.TryGet(callee.Identity, out var fact))
        {
            facts.Record(fact with { Flags = fact.Flags | ValueExpressionSemanticFlags.SetHasNoDefaultMemberToSet });
        }
    }

    // whether the property a target names has a Property Set, which a Set of it invokes: among what its module declares of the name, or its class.
    private static bool HasAPropertySet(StaticEvaluationContext context, ExpressionNode target, VBTypeMemberSymbol property)
    {
        IEnumerable<Symbol> named = target is MemberAccessExpressionNode { Owner: { } owner } access
            && ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, owner).Result is VBClassType { Symbol: var snapshot }
            ? (VBProjectSymbol.ResolveClass(context.Resolver, snapshot) ?? snapshot).DefaultInterfaceMembers
                .Where(member => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase))
            : context.Scope.SelfAndAncestors().SelectMany(scope => scope.DeclaredAs(property.Name))
                .Where(member => member.ParentUri.AbsoluteUri == property.ParentUri.AbsoluteUri);
        return named.Any(member => member is VBPropertySetMemberSymbol);
    }

    // a Set of a property that has no Property Set is a Set of the default member of what its Property Get returns (MS-VBA, Excel-measured): an object of a
    // class whose default member has a Property Set takes it (oracle\s31 D04, D08), and what an Object or a Variant property returns is only known when it
    // runs (D07, s28 W4); an object of a class whose default member has none has nothing to Set (s30 C32, C35, s31 D09, D10: "Invalid use of property").
    private static bool SetsThroughWhatItReturns(StaticEvaluationContext context, VBType returned) => returned switch
    {
        VBObjectType or VBVariantType => true,
        VBClassType { Symbol: var snapshot } declared when CurrentTypes.Of(context.Resolver, declared) is { DefaultMember: { } defaultMember }
            => (VBProjectSymbol.ResolveClass(context.Resolver, snapshot) ?? snapshot).Members.Any(member
                => member is VBPropertySetMemberSymbol && string.Equals(member.Name, defaultMember.Name, StringComparison.OrdinalIgnoreCase)),
        _ => false,
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

        // the names the walked procedure has declared so far, and where: its parameters, variables and constants share one scope.
        public Dictionary<string, List<SourcePosition>> DeclaredNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        // the names the procedure uses before the first ReDim that would declare them, and where that ReDim is: see FindNamesUsedBeforeTheirReDim.
        public Dictionary<string, SourcePosition> NamesUsedBeforeTheirReDim { get; } = new(StringComparer.OrdinalIgnoreCase);

        // the kind of procedure the walked body belongs to, when it is known.
        public MemberKind? Procedure { get; init; }

        // which branches are excluded, and which language the body is written in.
        public StaticSemanticsOptions Options { get; init; }

        // when there is no workspace to resolve names in, only what needs none is checked: the types of expressions are not.
        public bool Structural { get; init; }

        // how many loops of each kind the statement being walked is lexically inside.
        public int ForDepth { get; set; }

        public int DoDepth { get; set; }

        // the control variables of the For loops the statement being walked is inside, innermost last.
        public List<string> ControlVariables { get; } = [];
    }
}
