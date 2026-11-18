using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.Flow;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Services.Refactoring.InlineMember;

/// <summary>
/// One call of a member, inlined: the edits of the module the call is in.
/// </summary>
/// <param name="Module">The module the call is in.</param>
/// <param name="Callee">The member.</param>
/// <param name="Edits">The edits of <paramref name="Module"/>, unindented: the lines they write are indented by the module's formatting.</param>
internal sealed record class InlinedCall(Uri Module, Symbol Callee, IReadOnlyList<RefactorTextEdit> Edits);

/// <summary>
/// Inlines one call of a procedure: the call statement is replaced by the procedure's body, its parameters bound to the call's arguments, its locals declared
/// in the caller, under names that mean nothing else there.
/// </summary>
/// <remarks>
/// <para>
/// The arguments are mapped to the parameters by MS-VBAL §5.3.1.11 and evaluated once each, in parameter order, before the body, as the call evaluates them. A
/// <c>ByRef</c> parameter given a variable of its declared type, or any variable when it is a <c>Variant</c>, becomes that variable: what the body writes
/// to it is written to the variable, as through the reference. Any other parameter becomes a new local, assigned its argument; one the body never reads is
/// dropped when its argument is a literal or a name, which evaluating does nothing.
/// </para>
/// <para>
/// Each call of a procedure has locals of its own, initialized to their type's default (MS-VBAL §5.3.1, §5.4.3.1). A local the body may read before it
/// writes it is set to its default before the body, and an object local is set to <c>Nothing</c> after it, where the call ended and let go of it.
/// </para>
/// </remarks>
internal static class InlineCall
{
    /// <summary>
    /// The call at <paramref name="position"/> inlined, or why it is not.
    /// </summary>
    /// <param name="context">The workspace, whose modules are bound.</param>
    /// <param name="analysisOf">The analysis of a module of the workspace, by its address in <paramref name="context"/>.</param>
    /// <param name="module">The address of the module the call is in.</param>
    /// <param name="position">A position in the name of the called member.</param>
    public static (InlinedCall? Inlined, InlineRefusal? Refusal) At(RenameContext context, Func<Uri, ExtractionModuleAnalysis?> analysisOf, Uri module, SourcePosition position)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } syntax
            || NameOccurrences.NameAt(syntax, position) is not { } name)
        {
            return (null, new(InlineRefusalKind.NotAProcedure));
        }

        var occurrence = NameOccurrences.Find(context, name, [syntax]).Occurrences
            .FirstOrDefault(each => each.Kind is OccurrenceKind.Name && each.Module.AbsoluteUri == module.AbsoluteUri && Contains(each.Range, position));
        if (occurrence.Binding is not { } binding || NameOccurrences.SymbolOf(context, binding) is not { } callee)
        {
            return (null, new(InlineRefusalKind.NotAProcedure, name));
        }

        var kind = callee switch
        {
            VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol => (MemberKind?)null,
            VBFunctionMemberSymbol => MemberKind.Function,
            VBPropertyGetMemberSymbol => MemberKind.PropertyGet,
            VBProcedureMemberSymbol => MemberKind.Procedure,
            _ => null,
        };
        if (kind is null)
        {
            return (null, new(callee is VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol ? InlineRefusalKind.PropertyAssignment : InlineRefusalKind.NotAProcedure, callee.Name));
        }

        var calleeModule = NameOccurrences.ModuleOf(callee.Uri);
        if (calleeModule.AbsoluteUri != module.AbsoluteUri)
        {
            return (null, new(InlineRefusalKind.OtherModule, callee.Name));
        }

        if (SiteAt(syntax.Syntax, occurrence.Range) is not { } site)
        {
            return (null, new(InlineRefusalKind.UnsupportedSite, callee.Name, new SourceLocation(module, occurrence.Range)));
        }

        if (analysisOf(calleeModule) is not { } analysis || analysis.Procedure(callee.Name, kind.Value) is not { } procedure)
        {
            return (null, new(InlineRefusalKind.NotAProcedure, callee.Name));
        }

        var text = new ExtractionText(syntax.Source);
        if (RefusalOf(context, callee, procedure) is { } refused)
        {
            return (null, refused);
        }

        // the expression form is the call's value where the call is: the statement that raised an error is the same one.
        var writer = new Writer(context, syntax, text, callee, procedure, site);
        if (site.Call is not null && writer.WriteExpression() is { } expression)
        {
            return expression;
        }

        // the statement form writes the body in place of the statement, or before it, for a call in a larger one.
        if (!IsAloneOnItsLines(text, site.Statement))
        {
            return (null, new(InlineRefusalKind.UnsupportedSite, callee.Name, site.Statement.SourceLocation));
        }

        if (site.IsInAnExpression && HoistingRefusalOf(context, syntax, callee, procedure, site) is { } unhoisted)
        {
            return (null, unhoisted);
        }

        if (CallerRefusalOf(analysis, callee, procedure, syntax.Syntax, site.Statement) is { } changes)
        {
            return (null, changes);
        }

        return writer.Write();
    }

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;

    // a call whose callee is the name, unqualified: a call statement (Call F(a), F a), the whole value of an assignment (x = F(a), Set x = F, x = F), or a
    // call in any other expression of a statement (Debug.Print F(a) + 1), whose expression is Call.
    private sealed record class Site(StatementNode Statement, ImmutableArray<ExpressionNode> Arguments, AssignmentStatementNode? Assignment, ExpressionNode? Call = null)
    {
        public bool IsInAnExpression => Call is not null && Assignment is null;
    }

    // the body of a call in a larger statement runs before the statement, once: where the call's expression is evaluated once each time the statement runs,
    // and when nothing else the statement evaluates has an effect, or reads what the body can write, so that it can't tell the body ran first (MS-VBAL leaves
    // the order of an operator's operands open). A loop's condition is evaluated at each turn, an ElseIf's only when the conditions before it are False, a Case clause's until one
    // matches (MS-VBAL §5.4.2.2, §5.4.2.6, §5.4.2.8, §5.4.2.10).
    private static InlineRefusal? HoistingRefusalOf(RenameContext context, RenameModule syntax, Symbol callee, ExtractionProcedureAnalysis procedure, Site site)
    {
        if (callee is VBProcedureMemberSymbol
            || site.Statement is DoLoopUntilStatementNode or DoLoopWhileStatementNode or DoUntilLoopStatementNode or DoWhileLoopStatementNode or WhileWendStatementNode
                or ElseIfBlockStatementNode or CaseExpressionStatementNode)
        {
            return new(InlineRefusalKind.UnsupportedSite, callee.Name, site.Statement.SourceLocation);
        }

        // what the body can write of the caller's: the module's variables it writes, and any it calls something that may.
        var bindings = NameOccurrences.BindingsOf(context, syntax);
        var own = procedure.Scope.Declarations.Select(symbol => symbol.SemanticId).Append(callee.SemanticId).ToHashSet();
        var writes = procedure.Accesses.Definitions.Where(definition => definition.Kind is not VariableDefinitionKind.Entry && !own.Contains(definition.Variable))
            .Select(definition => definition.Variable).ToHashSet();
        var calls = procedure.Declaration.Descendants().Any(node => node is CallStatementNode or MemberAccessExpressionNode
            || node is SimpleNameExpressionNode && bindings((ExpressionNode)node) is { } bound && NameOccurrences.SymbolOf(context, bound) is VBProcedureMemberSymbol or VBReturningMemberSymbol
                and not (VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol or VBConstantMemberSymbol));

        foreach (var node in OtherOperands(site.Statement, site.Call!))
        {
            var symbol = node is SimpleNameExpressionNode or IndexExpressionNode && bindings(node is IndexExpressionNode index ? index.Callee : node) is { } id
                ? NameOccurrences.SymbolOf(context, id)
                : null;
            var harmless = node switch
            {
                LiteralExpressionNode => true,
                MemberAccessExpressionNode => false,
                SimpleNameExpressionNode or IndexExpressionNode => symbol switch
                {
                    VBLocalVariableSymbol or VBParameterSymbol => !writes.Contains(symbol.SemanticId),
                    VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol => !calls && !writes.Contains(symbol.SemanticId),
                    VBLocalConstantSymbol or VBConstantMemberSymbol => true,
                    _ => false,
                },
                _ => true,
            };
            if (!harmless)
            {
                return new(InlineRefusalKind.EvaluationOrder, callee.Name, node.SourceLocation);
            }
        }

        return null;
    }

    // the expressions a statement evaluates besides the call, without those of the statements in its body.
    private static IEnumerable<ExpressionNode> OtherOperands(SyntaxNode node, ExpressionNode call)
    {
        foreach (var child in node.Children)
        {
            if (child == call || child is StatementNode)
            {
                continue;
            }

            if (child is ExpressionNode expression)
            {
                yield return expression;
            }

            // the arguments and indexes of an element are evaluated; a name is what it names.
            if (child is not SimpleNameExpressionNode)
            {
                foreach (var nested in OtherOperands(child, call))
                {
                    yield return nested;
                }
            }
        }
    }

    private static Site? SiteAt(ModuleNode module, SourceRange name)
    {
        var nodes = NameOccurrences.Walk(module).Select(each => each.Node).ToArray();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case CallStatementNode { Callee: SimpleNameExpressionNode callee } call when callee.SourceLocation.Range == name:
                    return new(call, call.Arguments, null);
                case AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet or AssignmentKind.Set } assignment:
                    switch (assignment.Value)
                    {
                        case SimpleNameExpressionNode value when value.SourceLocation.Range == name:
                            return new(assignment, [], assignment, value);
                        case IndexExpressionNode { Callee: SimpleNameExpressionNode callee } value when callee.SourceLocation.Range == name:
                            return new(assignment, value.Arguments, assignment, value);
                    }

                    break;
            }
        }

        // the innermost statement the call is in: a block statement's own expressions are its, and not its body's.
        var expression = nodes.OfType<IndexExpressionNode>().FirstOrDefault(each => each.Callee is SimpleNameExpressionNode callee && callee.SourceLocation.Range == name)
            ?? (ExpressionNode?)nodes.OfType<SimpleNameExpressionNode>().FirstOrDefault(each => each.SourceLocation.Range == name);
        var statement = nodes.OfType<StatementNode>()
            .Where(each => each.SourceLocation.Range.Start <= name.Start && name.End <= each.SourceLocation.Range.End)
            .MinBy(each => (each.SourceLocation.Range.End.Line - each.SourceLocation.Range.Start.Line, each.SourceLocation.Range.End.Character - each.SourceLocation.Range.Start.Character));
        return expression is null || statement is null
            ? null
            : new(statement, expression is IndexExpressionNode index ? index.Arguments : [], null, expression);
    }

    // the statement is the only code of the lines it spans: no other statement on them, as one of a line of statements separated by colons, or the
    // statement of a single-line If.
    private static bool IsAloneOnItsLines(ExtractionText text, StatementNode call)
    {
        var range = call.SourceLocation.Range;
        var before = text.Of(new SourceRange(new SourcePosition(range.Start.Line, 0), range.Start));
        var lineEnd = text.Of(new SourceRange(range.End, new SourcePosition(range.End.Line + 1, 0))).TrimEnd('\r', '\n').Trim();
        return before.Trim().Length == 0 && (lineEnd.Length == 0 || lineEnd.StartsWith('\''));
    }

    // what in the member itself is not inlined, wherever it is called.
    private static InlineRefusal? RefusalOf(RenameContext context, Symbol callee, ExtractionProcedureAnalysis procedure)
    {
        var declaration = procedure.Declaration;
        if (NameOccurrences.SymbolOf(context, new SemanticId(NameOccurrences.ModuleOf(callee.Uri))) is VBClassModuleSymbol owner
            && (owner.IsDocument || callee.Name.Contains('_')) && SymbolRename.IsNamedByConvention(context, owner, callee))
        {
            return new(InlineRefusalKind.CalledByTheRuntime, callee.Name);
        }

        if (declaration.IsStatic || declaration.Descendants().OfType<VariableDeclarationNode>().Any(variable => variable.IsStatic))
        {
            return new(InlineRefusalKind.Static, callee.Name);
        }

        foreach (var instruction in procedure.Instructions.Items)
        {
            var kind = instruction.Kind switch
            {
                InstructionKind.GoSub or InstructionKind.GoSubTable or InstructionKind.Return => InlineRefusalKind.GoSub,
                InstructionKind.OnErrorGoTo or InstructionKind.OnErrorDisable or InstructionKind.OnErrorReset or InstructionKind.OnErrorResumeNext
                    or InstructionKind.ResumeCurrentStatement or InstructionKind.ResumeNext or InstructionKind.ResumeLabel => InlineRefusalKind.ErrorHandling,
                _ => (InlineRefusalKind?)null,
            };
            if (kind is { } refusal)
            {
                return new(refusal, callee.Name, instruction.Location);
            }
        }

        foreach (var node in declaration.Descendants())
        {
            switch (node)
            {
                case KeywordStatementNode { Token: Tokens.RaiseEvent }:
                    return new(InlineRefusalKind.RaisesAnEvent, callee.Name, node.SourceLocation);
            }
        }

        // the member reaches itself through what it calls.
        if (SymbolHierarchy.ItemOf(context, callee.SemanticId) is { } item)
        {
            var seen = new HashSet<SemanticId>();
            var pending = new Queue<HierarchyItem>([item]);
            while (pending.TryDequeue(out var caller))
            {
                foreach (var called in SymbolHierarchy.OutgoingCalls(context, caller))
                {
                    if (called.Item.Symbol.SemanticId == callee.SemanticId)
                    {
                        return new(InlineRefusalKind.Recursive, callee.Name);
                    }

                    if (seen.Add(called.Item.Symbol.SemanticId))
                    {
                        pending.Enqueue(called.Item);
                    }
                }
            }
        }

        return null;
    }

    // an error the body raises is the caller's to handle (MS-VBAL §5.4.4): with the call as the statement that raised, and inlined, with the body's statement
    // that did. Under On Error Resume Next the rest of the body then runs, where it was skipped; a handler's Resume runs that statement again, or goes on
    // inside the body, where it ran the call again or went on after it.
    private static InlineRefusal? CallerRefusalOf(ExtractionModuleAnalysis analysis, Symbol callee, ExtractionProcedureAnalysis procedure, ModuleNode module, StatementNode statement)
    {
        if (!Enumerable.Range(0, procedure.Instructions.Items.Length).Any(procedure.Graph.CanRaise))
        {
            return null;
        }

        var range = statement.SourceLocation.Range;
        if (module.Children.OfType<MemberDeclarationNode>().FirstOrDefault(member => member.SourceLocation.Range.Start <= range.Start && range.End <= member.SourceLocation.Range.End) is not { } declaration
            || analysis.Procedure(declaration.Name, declaration.MemberKind) is not { } caller || !caller.Instructions.TryGetOffset(statement.Identity, out var offset))
        {
            return new(InlineRefusalKind.UnsupportedSite, callee.Name, statement.SourceLocation);
        }

        var states = ErrorHandlingStates.Of(caller.Graph);
        var at = states.At(offset);
        if (at.Any(state => state is { Policy: ErrorHandlingPolicy.ResumeNext, IsHandling: false }))
        {
            return new(InlineRefusalKind.CallerResumesNext, callee.Name, statement.SourceLocation);
        }

        foreach (var handler in at.Select(state => state.CatchingHandler).OfType<int>().Distinct())
        {
            if (caller.Instructions.Items.FirstOrDefault(item => item.Kind is InstructionKind.ResumeCurrentStatement or InstructionKind.ResumeNext or InstructionKind.ResumeLabel
                && states.At(item.Offset).Any(state => state.Handler == handler && state.IsHandling)) is { } resume)
            {
                return new(InlineRefusalKind.CallerHandlerResumes, callee.Name, resume.Location);
            }
        }

        return null;
    }

    private static System.Collections.Immutable.ImmutableArray<VBParameterSymbol> ParametersOf(Symbol member) => member switch
    {
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        VBReturningMemberSymbol returning => returning.Parameters,
        _ => [],
    };

    // writes the inlined body at the call.
    private sealed class Writer(
        RenameContext context, RenameModule syntax, ExtractionText text, Symbol callee, ExtractionProcedureAnalysis procedure, Site site)
    {
        private readonly StatementNode call = site.Statement;
        private readonly Func<ExpressionNode, SemanticId?> _bindings = NameOccurrences.BindingsOf(context, syntax);
        private readonly LexicalScope _callerScope = NameOccurrences.ScopeOf(context, syntax, site.Statement.SourceLocation.Range.Start);
        private readonly List<string> _result = [];
        private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<SemanticId, string> _replacements = [];
        private readonly List<string> _before = [];
        private readonly List<string> _after = [];
        private readonly List<(SourceRange Range, string NewText)> _folds = [];
        private int _released;

        public (InlinedCall? Inlined, InlineRefusal? Refusal) Write()
        {
            // the names the caller writes, and those the body writes that are not its own parameters and locals, which it takes along.
            var body = BodyRange();
            var own = procedure.Scope.Declarations.Select(symbol => symbol.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var used in NameOccurrences.NamesIn(syntax, body).Where(used => !own.Contains(used)).Concat(NameOccurrences.NamesIn(syntax, CallerRange())))
            {
                _taken.Add(used);
            }

            if ((Arguments() ?? Result() ?? Locals() ?? Captured()) is { } refused)
            {
                return (null, refused);
            }

            if (site.IsInAnExpression)
            {
                // the statement reads the result local where the call was: one that holds an object would be let go of after the statement.
                if (_result.Count > 0)
                {
                    return (null, new(InlineRefusalKind.UnsupportedSite, callee.Name, site.Call!.SourceLocation));
                }

                var line = new SourcePosition(call.SourceLocation.Range.Start.Line, 0);
                var hoisted = string.Join(text.NewLine, [.. _before, .. BodyLines(body), .. _after]) + text.NewLine;
                return (new InlinedCall(syntax.Module, callee,
                [
                    new RefactorTextEdit { Range = new SourceRange(line, line), NewText = hoisted },
                    new RefactorTextEdit { Range = site.Call!.SourceLocation.Range, NewText = _replacements[callee.SemanticId] },
                ]), null);
            }

            var inlined = string.Join(text.NewLine, [.. _before, .. BodyLines(body), .. _after, .. _result]);
            return (new InlinedCall(syntax.Module, callee, [new RefactorTextEdit { Range = call.SourceLocation.Range, NewText = inlined }]), null);
        }

        // the expression form (the call replaced by its value, in parentheses), when the body is one Let of the result of a value of the function's type, and
        // each parameter can be the argument itself: a literal, or a variable of the parameter's type that the body does not write, which evaluating at the
        // parameter's place does nothing else than evaluating before the body; a literal is written with the type character of the parameter's type when it
        // has another. Null when the body is not one.
        public (InlinedCall? Inlined, InlineRefusal? Refusal)? WriteExpression()
        {
            // what was bound for the expression form is not the statement form's.
            if (ExpressionForm() is { } written)
            {
                return written;
            }

            _replacements.Clear();
            return null;
        }

        private (InlinedCall? Inlined, InlineRefusal? Refusal)? ExpressionForm()
        {
            var declaration = procedure.Declaration;
            var type = (callee as ITypedSymbol)?.ResolvedType;
            if (callee is VBProcedureMemberSymbol || site.Call is not { } called
                || declaration.Children.OfType<StatementNode>().ToArray() is not [AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet, Target: SimpleNameExpressionNode target, Value: var value }]
                || declaration.Children.OfType<LineLabelNode>().Any() || _bindings(target) != callee.SemanticId
                || procedure.Scope.Declarations.Any(symbol => symbol is not VBParameterSymbol)
                || type is not VBIntrinsicType || type is VBVariantType or VBObjectType || DeclaredTypeOf(value) is not { } valueType || !SameType(valueType, type))
            {
                return null;
            }

            var parameters = ParametersOf(callee);
            var map = ArgumentMapping.Map(parameters, site.Arguments);
            if (map.Error is not ArgumentMappingError.None || map.Mapped is not { } mapped || parameters.Any(parameter => parameter is ParamArrayParameterSymbol))
            {
                return null;
            }

            var names = value.Descendants().Prepend(value).OfType<SimpleNameExpressionNode>().ToArray();
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = procedure.Scope.Declarations.OfType<VBParameterSymbol>()
                    .First(symbol => symbol.Name.Equals(parameters[index].Name, StringComparison.OrdinalIgnoreCase));
                if (mapped[index] is not { } argument || argument is MissingArgumentNode || IsMissingOf(parameter).Count > 0 || IsWrittenOtherwise(parameter, []))
                {
                    return null;
                }

                var variable = VariableOf(argument);
                var argumentText = text.Of(argument.SourceLocation.Range).Trim();
                if (argument is not LiteralExpressionNode && variable is null || TypeOf(argument, variable) is not { } given)
                {
                    return null;
                }

                if (!SameType(given, parameter.ResolvedType))
                {
                    if (argument is not LiteralExpressionNode || TypedLiteral(argumentText, parameter.ResolvedType) is not { } typed)
                    {
                        return null;
                    }

                    argumentText = typed;
                }

                _replacements[parameter.SemanticId] = argument is LiteralExpressionNode && argumentText.StartsWith('-') ? $"({argumentText})" : argumentText;
            }

            // the function's own name in the expression is its result, which the expression form has none of.
            if (names.Any(name => _bindings(name) == callee.SemanticId) || Captured() is not null)
            {
                return null;
            }

            var edits = names.Where(name => _bindings(name) is { } bound && _replacements.ContainsKey(bound))
                .Select(name => (Range: name.SourceLocation.Range, NewText: _replacements[_bindings(name)!.Value])).OrderByDescending(edit => edit.Range.Start);
            var written = text.Of(value.SourceLocation.Range).Trim();
            var start = text.OffsetOf(value.SourceLocation.Range.Start) + (text.Of(value.SourceLocation.Range).Length - text.Of(value.SourceLocation.Range).TrimStart().Length);
            foreach (var (range, newText) in edits)
            {
                var from = text.OffsetOf(range.Start) - start;
                written = string.Concat(written.AsSpan(0, from), newText, written.AsSpan(text.OffsetOf(range.End) - start));
            }

            return (new InlinedCall(syntax.Module, callee, [new RefactorTextEdit { Range = called.SourceLocation.Range, NewText = $"({written})" }]), null);
        }

        // a decimal literal written as a literal of a numeric type, by its type character, when its value is one of that type (MS-VBAL §3.3.2).
        private static string? TypedLiteral(string literal, VBType? type)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(literal, @"^-?\d+(\.\d+)?$"))
            {
                return null;
            }

            var integral = !literal.Contains('.');
            return type?.Name switch
            {
                "Integer" when integral && long.TryParse(literal, out var value) && value is >= short.MinValue and <= short.MaxValue => literal + "%",
                "Long" when integral && long.TryParse(literal, out var value) && value is >= int.MinValue and <= int.MaxValue => literal + "&",
                "LongLong" when integral && long.TryParse(literal, out _) => literal + "^",
                "Single" => literal + "!",
                "Double" => literal + "#",
                "Currency" => literal + "@",
                _ => null,
            };
        }

        // the lines of the body, after the header and before End Sub; the lines of a procedure are its own, from their start.
        private SourceRange BodyRange()
        {
            var declaration = procedure.Declaration;
            var header = declaration.ParameterListRange?.End.Line ?? declaration.NameRange?.End.Line ?? declaration.SourceLocation.Range.Start.Line;
            while (text.Of(new SourceRange(new SourcePosition(header, 0), new SourcePosition(header + 1, 0))).TrimEnd('\r', '\n').EndsWith(" _", StringComparison.Ordinal))
            {
                header++;
            }

            return new SourceRange(new SourcePosition(header + 1, 0), new SourcePosition(declaration.EndRange?.Start.Line ?? declaration.SourceLocation.Range.End.Line, 0));
        }

        private SourceRange CallerRange()
            => NameOccurrences.Walk(syntax.Syntax).Select(each => each.Node).OfType<MemberDeclarationNode>()
                .First(member => member.SourceLocation.Range.Start <= call.SourceLocation.Range.Start && call.SourceLocation.Range.End <= member.SourceLocation.Range.End)
                .SourceLocation.Range;

        // each parameter bound to its argument, in parameter order (MS-VBAL §5.3.1.11).
        private InlineRefusal? Arguments()
        {
            var parameters = ParametersOf(callee);
            var map = ArgumentMapping.Map(parameters, site.Arguments);
            if (map.Error is not ArgumentMappingError.None || map.Mapped is not { } mapped)
            {
                return new(InlineRefusalKind.ArgumentsDoNotMap, callee.Name, call.SourceLocation);
            }

            var written = procedure.Accesses.Definitions.Where(definition => definition.Kind is not VariableDefinitionKind.Entry).Select(definition => definition.Variable).ToHashSet();
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = procedure.Scope.Declarations.OfType<VBParameterSymbol>()
                    .First(symbol => symbol.Name.Equals(parameters[index].Name, StringComparison.OrdinalIgnoreCase));
                var missing = IsMissingOf(parameter);
                if (parameter is ParamArrayParameterSymbol)
                {
                    if (ParamArray(parameter, map.ParamArrayArguments, missing, written) is { } refusal)
                    {
                        return refusal;
                    }

                    continue;
                }

                if (mapped[index] is not { } argument || argument is MissingArgumentNode)
                {
                    if (Omitted(parameter, missing, written) is { } refusal)
                    {
                        return refusal;
                    }

                    continue;
                }

                // IsMissing of a parameter the body writes is told by what it was written.
                if (missing.Count > 0 && IsWrittenOtherwise(parameter, missing))
                {
                    return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, missing[0].SourceLocation);
                }

                var argumentText = text.Of(argument.SourceLocation.Range).Trim();
                var type = (parameter as ITypedSymbol)?.ResolvedType;
                var variable = VariableOf(argument);
                if (BoundArgument.IsByRef(parameter) && variable is ITypedSymbol { ResolvedType: { } given } && (type is VBVariantType || SameType(given, type)))
                {
                    // the argument's variable keeps its own IsMissing answer: a caller's Optional Variant passed along is missing when it is.
                    _replacements[parameter.SemanticId] = argumentText;
                    continue;
                }

                if (BoundArgument.IsByRef(parameter) && argument is IndexExpressionNode or MemberAccessExpressionNode && IsStorage(argument))
                {
                    return new(InlineRefusalKind.ByRefElement, parameter.Name, argument.SourceLocation);
                }

                if (type is null or VBArrayType or VBUserDefinedType or VBUnknownType or VBUnresolvedType)
                {
                    return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, argument.SourceLocation);
                }

                // what is given is not missing (MS-VBAL §6.1.2.7.1.6): a Variant argument that could be is refused below.
                Fold(missing, "False");
                var isObject = type is VBClassType or VBObjectType;
                if (!IsWrittenOtherwise(parameter, missing) && !IsReadOtherwise(parameter, missing) && (argument is LiteralExpressionNode || variable is not null))
                {
                    continue;
                }

                // a Variant takes an object as it is given one, which an assignment does not know when the argument is not of a known intrinsic type.
                if (type is VBVariantType && TypeOf(argument, variable) is not (VBIntrinsicType and not VBVariantType and not VBObjectType))
                {
                    return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, argument.SourceLocation);
                }

                var local = Fresh(parameter.Name);
                _replacements[parameter.SemanticId] = local;
                _before.Add($"Dim {local} As {NameOfType(type)}");
                _before.Add($"{(isObject ? "Set " : string.Empty)}{local} = {argumentText}");

                // the copy of an object the caller holds in no variable of its own (`New Tracker`, a function's result) is the callee's to let go of.
                if (isObject && variable is null && argument is not LiteralExpressionNode)
                {
                    if (RaisingAfter([]) is { } raising)
                    {
                        return new(InlineRefusalKind.HeldThroughAnError, parameter.Name, procedure.Instructions.Items[raising].Location);
                    }

                    _after.Add($"Set {local} = Nothing");
                    _released++;
                }
                else if (Release(parameter, type, local, OfObjects(parameter.SemanticId)) is { } refused)
                {
                    return refused;
                }
            }

            return null;
        }

        // an Optional parameter given no argument has its default value, or its type's default when it declares none; a Variant that declares none is
        // missing (MS-VBAL §5.3.1.5, §5.3.1.11). A body that reads the Missing value, other than to ask IsMissing, is refused.
        private InlineRefusal? Omitted(VBParameterSymbol parameter, List<IndexExpressionNode> missing, HashSet<SemanticId> written)
        {
            var type = parameter.ResolvedType;
            var isMissing = parameter.DefaultValue is null && type is VBVariantType;
            Fold(missing, isMissing ? "True" : "False");
            if (!IsWrittenOtherwise(parameter, missing) && !IsReadOtherwise(parameter, missing))
            {
                return null;
            }

            if (isMissing || type is null or VBArrayType or VBUserDefinedType or VBUnknownType or VBUnresolvedType)
            {
                return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, call.SourceLocation);
            }

            var local = Fresh(parameter.Name);
            _replacements[parameter.SemanticId] = local;
            _before.Add($"Dim {local} As {NameOfType(type)}");
            if (parameter.DefaultValue is { } value)
            {
                // the default is a constant expression of the member's module, which a local of the caller with a name it uses would shadow.
                if (NameOccurrences.NamesIn(syntax, value.SourceLocation.Range).Any(name => _callerScope.Declarations.Any(declared => declared.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
                {
                    return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, value.SourceLocation);
                }

                _before.Add($"{(type is VBClassType or VBObjectType ? "Set " : string.Empty)}{local} = {text.Of(value.SourceLocation.Range).Trim()}");
                return Release(parameter, type, local, OfObjects(parameter.SemanticId));
            }

            if (DefaultOf(type, local) is not { } reset)
            {
                return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, call.SourceLocation);
            }

            _before.Add(reset);
            return Release(parameter, type, local, OfObjects(parameter.SemanticId));
        }

        // a ParamArray is a new Variant array with a lower bound of 0 that holds the arguments it collects (MS-VBAL §5.3.1.11), built element by element:
        // Array() follows Option Base. Its elements are the caller's variables, which a copy cannot write: a body that writes the array, or a variable it
        // collects, is refused. It is missing when it collects nothing.
        private InlineRefusal? ParamArray(VBParameterSymbol parameter, System.Collections.Immutable.ImmutableArray<ExpressionNode> elements, List<IndexExpressionNode> missing, HashSet<SemanticId> written)
        {
            if (IsWrittenOtherwise(parameter, missing))
            {
                return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, call.SourceLocation);
            }

            Fold(missing, elements.IsEmpty ? "True" : "False");
            var variables = elements.Select(VariableOf).ToArray();
            if (!IsReadOtherwise(parameter, missing) && elements.Zip(variables).All(each => each.First is LiteralExpressionNode || each.Second is not null))
            {
                return null;
            }

            var local = Fresh(parameter.Name);
            _replacements[parameter.SemanticId] = local;
            _before.Add($"Dim {local}() As Variant");
            if (elements.IsEmpty)
            {
                if (syntax.Syntax.Children.OfType<ModuleOptionDirectiveNode>().Any(directive => directive.ModuleOption == ModuleOptions.OptionBase1))
                {
                    return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, call.SourceLocation);
                }

                _before.Add($"{local} = Array()");
                return null;
            }

            _before.Add($"ReDim {local}(0 To {elements.Length - 1})");
            for (var index = 0; index < elements.Length; index++)
            {
                // an element is assigned as it is given: Set for an object, Let for a value of an intrinsic type; a Variant could hold either.
                var element = elements[index];
                if (element is MissingArgumentNode || variables[index] is { } variable && written.Contains(variable.SemanticId)
                    || TypeOf(element, variables[index]) is not { } type || type is VBVariantType || type is not (VBIntrinsicType or VBClassType))
                {
                    return new(InlineRefusalKind.UnsupportedParameter, parameter.Name, element.SourceLocation);
                }

                _before.Add($"{(type is VBClassType or VBObjectType ? "Set " : string.Empty)}{local}({index}) = {text.Of(element.SourceLocation.Range).Trim()}");
            }

            return null;
        }

        // the calls of the library's IsMissing of a parameter in the body (MS-VBAL §6.1.2.7.1.6), found by what they are bound to.
        private List<IndexExpressionNode> IsMissingOf(VBParameterSymbol parameter)
            => [.. procedure.Declaration.Descendants().OfType<IndexExpressionNode>()
                .Where(each => each.Arguments is [SimpleNameExpressionNode argument] && _bindings(argument) == parameter.SemanticId
                    && _bindings(each.Callee) is { } bound && NameOccurrences.SymbolOf(context, bound) is { } function && NewNames.IsOfALibrary(function)
                    && string.Equals(SymbolUri.QualifiedNameOf(function.Uri), "Information.IsMissing", StringComparison.OrdinalIgnoreCase))];

        // whether the body reads a parameter other than to ask IsMissing of it.
        private bool IsReadOtherwise(VBParameterSymbol parameter, List<IndexExpressionNode> missing)
            => procedure.Accesses.Uses.Any(use => use.Variable == parameter.SemanticId
                && !missing.Any(each => Within(use.Expression.SourceLocation.Range, each.SourceLocation.Range)));

        // whether the body may write a parameter other than where it gives it to IsMissing, whose argument is a reference that IsMissing does not write.
        private bool IsWrittenOtherwise(VBParameterSymbol parameter, List<IndexExpressionNode> missing)
            => procedure.Accesses.Definitions.Any(definition => definition.Variable == parameter.SemanticId && definition.Kind is not VariableDefinitionKind.Entry
                && (definition.Expression is not { } expression || !missing.Any(each => Within(expression.SourceLocation.Range, each.SourceLocation.Range))));

        private void Fold(List<IndexExpressionNode> missing, string answer) => _folds.AddRange(missing.Select(each => (each.SourceLocation.Range, answer)));

        // a function's result variable, a distinct variable of each call initialized to its type's default (MS-VBAL §5.3.1): a local of the caller, reset before
        // the body, which the caller's assignment is then given. A call statement of a function discards it.
        private InlineRefusal? Result()
        {
            if (callee is VBProcedureMemberSymbol)
            {
                return null;
            }

            var type = (callee as ITypedSymbol)?.ResolvedType;
            var name = Fresh($"{callee.Name}_result");
            if (type is null or VBUnknownType or VBUnresolvedType || DefaultOf(type, name) is not { } reset)
            {
                return new(InlineRefusalKind.UnsupportedConstruct, callee.Name);
            }

            if (site.Assignment is { Target: var target } && target is not SimpleNameExpressionNode)
            {
                return new(InlineRefusalKind.UnsupportedSite, callee.Name, site.Assignment.SourceLocation);
            }

            var isObject = type is VBClassType or VBObjectType;
            _replacements[callee.SemanticId] = name;
            _before.Add($"Dim {name} As {NameOfType(type)}");
            _before.Add(reset);
            if (site.Assignment is { } assignment)
            {
                var keyword = assignment.Kind switch { AssignmentKind.Set => "Set ", AssignmentKind.ExplicitLet => "Let ", _ => string.Empty };
                _result.Add($"{keyword}{text.Of(assignment.Target.SourceLocation.Range).Trim()} = {name}");
            }

            // the result the caller is given, or discards, is let go of after it, as the caller let go of the value of the call; one held when an error leaves
            // the body would have been let go of with the callee's activation.
            if (OfObjects(callee.SemanticId) is { Count: > 0 } givenAnObjectAt)
            {
                if (RaisingAfter(givenAnObjectAt) is { } raising)
                {
                    return new(InlineRefusalKind.HeldThroughAnError, callee.Name, procedure.Instructions.Items[raising].Location);
                }

                if (ReleaseOf(type, name) is not { } release)
                {
                    return new(InlineRefusalKind.UnsupportedConstruct, callee.Name);
                }

                _result.Add(release);
            }

            return null;
        }

        // the locals of the body, renamed where their names mean something else at the call; reset when the body may read them before writing them, and let
        // go of after it when they are objects.
        private InlineRefusal? Locals()
        {
            var live = LiveVariables.Of(procedure.Graph, procedure.Accesses).LiveBefore(procedure.Graph.Entry);
            foreach (var local in procedure.Scope.Declarations.Where(symbol => symbol is not VBParameterSymbol))
            {
                var name = Fresh(local.Name);
                _replacements[local.SemanticId] = name;
                if (local is VBLocalVariableSymbol variable && variable.DeclaredBy.IsImplicit())
                {
                    _before.Add($"Dim {name}");
                }

                var type = (local as ITypedSymbol)?.ResolvedType;
                if (local is VBLocalVariableSymbol && live.Contains(local.SemanticId))
                {
                    if (DefaultOf(type, name) is not { } reset)
                    {
                        return new(InlineRefusalKind.UnsupportedConstruct, local.Name);
                    }

                    _before.Add(reset);
                }

                if (local is VBLocalVariableSymbol && Release(local, type, name, OfObjects(local.SemanticId)) is { } refused)
                {
                    return refused;
                }
            }

            // the order MS-VBA lets go of a procedure's locals in, when several may run a Class_Terminate, is not measured.
            return _released > 1 ? new(InlineRefusalKind.ReleaseOrder, callee.Name, call.SourceLocation) : null;
        }

        // a local that may hold an object when the body ends is let go of after it, where the call ended and the callee's activation let go of it (MS-VBAL
        // §5.3.1): every way out of the body goes there. One that holds it when an error leaves the body would have been let go of before the caller's handler
        // ran, which a local of the caller is not: refused.
        private InlineRefusal? Release(Symbol local, VBType? type, string name, IReadOnlyList<int> givenAnObjectAt)
        {
            if (givenAnObjectAt.Count == 0)
            {
                return null;
            }

            if (RaisingAfter(givenAnObjectAt) is { } raising)
            {
                return new(InlineRefusalKind.HeldThroughAnError, local.Name, procedure.Instructions.Items[raising].Location);
            }

            if (ReleaseOf(type, name) is not { } release)
            {
                return new(InlineRefusalKind.UnsupportedConstruct, local.Name);
            }

            _after.Add(release);
            _released++;
            return null;
        }

        // the statement that lets go of what a variable holds; none for a Type, which has no statement that empties it.
        private static string? ReleaseOf(VBType? type, string name) => type switch
        {
            VBClassType or VBObjectType => $"Set {name} = Nothing",
            VBArrayType => $"Erase {name}",
            VBVariantType => $"{name} = Empty",
            _ => null,
        };

        // the instructions of the body that may give a variable an object (an object, an array of them, a Variant that holds either, a Type with a field that
        // can), by the type of what they write it: Mid, LSet and RSet write a string or the bytes of a Type, a For loop a number.
        private List<int> OfObjects(SemanticId variable)
        {
            var symbol = NameOccurrences.SymbolOf(context, variable);
            if (!CanHoldAnObject((symbol as ITypedSymbol)?.ResolvedType))
            {
                return [];
            }

            return [.. procedure.Accesses.Definitions.Where(definition => definition.Variable == variable && GivesAnObject(definition)).Select(definition => definition.Offset).Distinct()];
        }

        private bool GivesAnObject(VariableDefinition definition)
        {
            // the value an activation starts with is not an instruction's; an IsMissing that is folded away is no longer there.
            if (definition.Kind is VariableDefinitionKind.Entry || definition.Offset < 0 || definition.Offset >= procedure.Instructions.Items.Length
                || definition.Expression is { } written && _folds.Any(fold => Within(written.SourceLocation.Range, fold.Range)))
            {
                return false;
            }

            var instruction = procedure.Instructions.Items[definition.Offset];
            return definition.Kind switch
            {
                VariableDefinitionKind.Redimension or VariableDefinitionKind.FileInput => false,
                VariableDefinitionKind.LoopControl => instruction.Kind is InstructionKind.ForEachOpener or InstructionKind.ForEachNext,
                VariableDefinitionKind.ByRefArgument => true,
                _ => instruction.Node switch
                {
                    AssignmentStatementNode { Kind: AssignmentKind.LSet or AssignmentKind.RSet } or MidStatementNode => false,
                    AssignmentStatementNode assignment => CanHoldAnObject(DeclaredTypeOf(assignment.Value)),
                    _ => true,
                },
            };
        }

        private VBType? DeclaredTypeOf(ExpressionNode expression)
            => procedure.Model.Expressions.TryGetValue(expression.Identity, out var fact) ? fact.DeclaredType : null;

        // a type whose values can hold an object reference; one not known can.
        private static bool CanHoldAnObject(VBType? type) => type switch
        {
            null or VBVariantType or VBObjectType or VBClassType or VBUnknownType or VBUnresolvedType => true,
            VBArrayType array => CanHoldAnObject(array.ItemType),
            VBUserDefinedType record => record.Members.Any(member => CanHoldAnObject((member as ITypedSymbol)?.ResolvedType)),
            _ => false,
        };

        // an instruction that can raise an error, reached from after one of these, or from the start of the body when there are none to start from.
        private int? RaisingAfter(IReadOnlyList<int> starts)
        {
            var graph = procedure.Graph;
            var seen = new HashSet<int>();
            var pending = new Queue<int>(starts.Count == 0 ? [graph.Entry] : starts.SelectMany(start => graph.SuccessorsOf(start).Select(edge => edge.To)));
            while (pending.TryDequeue(out var node))
            {
                if (node >= graph.Exit || !seen.Add(node))
                {
                    continue;
                }

                if (graph.CanRaise(node))
                {
                    return node;
                }

                foreach (var edge in graph.SuccessorsOf(node))
                {
                    pending.Enqueue(edge.To);
                }
            }

            return null;
        }

        // a name of the body bound to something of the module or the project is written into the caller as it is, where a parameter or a local of the caller
        // with that name would be found first (MS-VBAL §5.6.10): refused.
        private InlineRefusal? Captured()
        {
            var callers = _callerScope.Declarations.ToLookup(declared => declared.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var name in procedure.Declaration.Descendants().OfType<SimpleNameExpressionNode>())
            {
                if (_bindings(name) is not { } bound || _replacements.ContainsKey(bound) || bound == callee.SemanticId
                    || _folds.Any(fold => Within(name.SourceLocation.Range, fold.Range)))
                {
                    continue;
                }

                if (callers[name.IdentifierName].FirstOrDefault(declared => declared.SemanticId != bound) is { } capturing)
                {
                    return new(InlineRefusalKind.Captured, capturing.Name, name.SourceLocation);
                }
            }

            return null;
        }

        // the lines of the body, its names bound to the parameters and locals written as the call site names them, its labels renamed and its exits
        // rewritten; then the lines that close what the exits opened.
        private IEnumerable<string> BodyLines(SourceRange body)
        {
            var edits = new List<(SourceRange Range, string NewText)>();
            foreach (var group in _replacements.GroupBy(each => NameOf(each.Key)))
            {
                foreach (var occurrence in NameOccurrences.Find(context, group.Key, [syntax]).Occurrences
                    .Where(each => each.Kind is OccurrenceKind.Name or OccurrenceKind.Declaration && Within(each.Range, body) && each.Binding is { } bound && _replacements.ContainsKey(bound)))
                {
                    var replacement = _replacements[occurrence.Binding!.Value];
                    var declared = text.Of(occurrence.Range);
                    edits.Add((occurrence.Range, occurrence.Kind is OccurrenceKind.Declaration ? replacement + declared[NameLength(declared)..] : replacement));
                }
            }

            Labels(edits);
            var closing = Exits(edits);
            edits.AddRange(_folds);
            var written = text.Of(body);
            var start = text.OffsetOf(body.Start);
            // an edit inside another one (a parameter's name in a folded IsMissing) goes with it.
            foreach (var (range, newText) in edits.DistinctBy(edit => edit.Range)
                .Where(edit => !edits.Any(other => other.Range != edit.Range && Within(edit.Range, other.Range)))
                .OrderByDescending(edit => edit.Range.Start))
            {
                var from = text.OffsetOf(range.Start) - start;
                written = string.Concat(written.AsSpan(0, from), newText, written.AsSpan(text.OffsetOf(range.End) - start));
            }

            return written.TrimEnd('\r', '\n').Split('\n').Select(line => line.TrimEnd('\r').TrimStart()).Concat(closing);
        }

        // each Exit Sub, Function or Property (MS-VBAL §5.4.2.17-19) leaves the inlined body for what follows it: dropped when it is the body's last statement;
        // the end of a top-level block If when it is the last statement of an If that has no other branch, the rest of the body then going in an Else of
        // that If; a GoTo a label after the body anywhere else. The lines written after the body: the End If of each such If, then the label.
        private List<string> Exits(List<(SourceRange Range, string NewText)> edits)
        {
            var closing = new List<string>();
            string? label = null;
            var statements = procedure.Declaration.Children.OfType<StatementNode>().ToArray();
            foreach (var exit in procedure.Instructions.Items.Where(instruction => instruction.Kind is InstructionKind.ExitProcedure).Select(instruction => instruction.Node!))
            {
                if (exit == statements.LastOrDefault())
                {
                    edits.Add(Removal(exit.SourceLocation.Range));
                    continue;
                }

                if (statements.OfType<IfBlockStatementNode>().FirstOrDefault(each => each.ElseIfBlocks.IsEmpty && each.ElseBlock is null && each.Body.Children.LastOrDefault() == exit) is { } guard
                    && procedure.Instructions.Items.Count(instruction => instruction.Kind is InstructionKind.ExitProcedure && Within(instruction.Node!.SourceLocation.Range, guard.SourceLocation.Range)) == 1)
                {
                    edits.Add(Removal(exit.SourceLocation.Range));
                    if (guard != statements[^1])
                    {
                        edits.Add((LineOf(new SourceRange(guard.SourceLocation.Range.End, guard.SourceLocation.Range.End)), "Else" + text.NewLine));
                        closing.Insert(0, "End If");
                    }

                    continue;
                }

                label ??= FreshLabel($"{callee.Name}_exit");
                edits.Add((exit.SourceLocation.Range, $"GoTo {label}"));
            }

            if (label is not null)
            {
                closing.Add($"{label}:");
            }

            return closing;
        }

        private static SourceRange LineOf(SourceRange range) => new(new SourcePosition(range.Start.Line, 0), new SourcePosition(range.End.Line + 1, 0));

        // the edit that removes what a range holds: its whole line when nothing else is written on it, the range alone otherwise.
        private (SourceRange Range, string NewText) Removal(SourceRange range)
        {
            var line = LineOf(range);
            var rest = string.Concat(text.Of(new SourceRange(line.Start, range.Start)), text.Of(new SourceRange(range.End, line.End)));
            return (rest.Trim().Length == 0 ? line : range, string.Empty);
        }

        // the labels of the body, which are unique within the procedure the body goes into (MS-VBAL §5.4.1.1): a named label renamed to a fresh label; a
        // line number removed, unless a jump goes to it and no named label is where it is, when it becomes a fresh label. Each jump is renamed with the
        // label it goes to, which the instruction list resolved. The inlined lines are then unnumbered: Erl after an error in them reports the line of the
        // caller it reported before.
        private void Labels(List<(SourceRange Range, string NewText)> edits)
        {
            var instructions = procedure.Instructions;
            var targets = instructions.Items.Where(item => item.Kind is InstructionKind.Jump).Select(item => item.Target)
                .Concat(instructions.Items.Where(item => item.Kind is InstructionKind.JumpTable).SelectMany(item => item.Targets)).OfType<int>().ToHashSet();
            var labels = procedure.Declaration.Descendants().OfType<LineLabelNode>().Where(label => instructions.TryGetLabelOffset(label.Name, out _)).ToArray();
            var named = new Dictionary<int, string>();
            foreach (var label in labels.Where(label => label is not LineNumberNode).Concat(labels.Where(label => label is LineNumberNode)))
            {
                instructions.TryGetLabelOffset(label.Name, out var offset);
                if (label is not LineNumberNode)
                {
                    var name = FreshLabel(label.Name);
                    named.TryAdd(offset, name);
                    edits.Add((label.NameRange ?? label.SourceLocation.Range, name));
                }
                else if (targets.Contains(offset) && !named.ContainsKey(offset))
                {
                    var name = FreshLabel($"{callee.Name}_{label.Name.TrimStart('-')}");
                    named.Add(offset, name);
                    edits.Add((label.SourceLocation.Range, $"{name}:"));
                }
                else
                {
                    edits.Add(Removal(label.SourceLocation.Range));
                }
            }

            foreach (var item in instructions.Items)
            {
                switch (item.Node)
                {
                    case GoToStatementNode jump when item.Kind is InstructionKind.Jump && item.Target is { } offset && named.TryGetValue(offset, out var name):
                        // `If x Then 10` goes to line 10 with a line number alone, which a label can't be written as.
                        edits.Add(jump.LabelExpression.SourceLocation.Range == jump.SourceLocation.Range
                            ? (jump.SourceLocation.Range, $"GoTo {name}")
                            : (jump.LabelExpression.SourceLocation.Range, name));
                        break;
                    case OnGoToStatementNode table when item.Kind is InstructionKind.JumpTable:
                        for (var index = 0; index < table.Labels.Length && index < item.Targets.Length; index++)
                        {
                            if (item.Targets[index] is { } offset && named.TryGetValue(offset, out var target))
                            {
                                edits.Add((table.Labels[index].SourceLocation.Range, target));
                            }
                        }

                        break;
                }
            }
        }

        // a label no line of the caller has, and no name it writes (MS-VBAL §5.4.1.1: labels are unique within a procedure).
        private string FreshLabel(string stem)
        {
            var labels = NameOccurrences.Walk(syntax.Syntax).Select(each => each.Node).OfType<LineLabelNode>()
                .Where(each => Within(each.SourceLocation.Range, CallerRange())).Select(each => each.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (NewNames.ValidityOf(stem) is not NewNameValidity.Valid)
            {
                stem = $"{callee.Name}_label";
            }

            var name = stem;
            for (var index = 2; labels.Contains(name) || _taken.Contains(name) || NewNames.ValidityOf(name) is not NewNameValidity.Valid; index++)
            {
                name = $"{stem}{index}";
            }

            _taken.Add(name);
            return name;
        }

        private static string NameOf(SemanticId id) => id.Uri.Fragment[(id.Uri.Fragment.LastIndexOf('.') + 1)..];

        private static int NameLength(string written) => written.TakeWhile(character => char.IsLetterOrDigit(character) || character == '_').Count();

        private static bool Within(SourceRange range, SourceRange body) => body.Start <= range.Start && range.End <= body.End;

        // the variable an argument names, alone: a local, a parameter or a field.
        private Symbol? VariableOf(ExpressionNode argument)
            => argument is SimpleNameExpressionNode && _bindings(argument) is { } id && NameOccurrences.SymbolOf(context, id) is { } symbol
               && symbol is VBLocalVariableSymbol or VBParameterSymbol or VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol
                ? symbol
                : null;

        // an element of an array or a field of a Type that a reference can be to, which no variable stands for at the call.
        private bool IsStorage(ExpressionNode argument)
            => _bindings(argument) is not { } id
               || NameOccurrences.SymbolOf(context, id) is not (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol or VBExternalFunctionMemberSymbol);

        private VBType? TypeOf(ExpressionNode argument, Symbol? variable) => argument switch
        {
            LiteralExpressionNode literal => literal.StaticValue?.TypeInfo,
            _ => (variable as ITypedSymbol)?.ResolvedType,
        };

        private static bool SameType(VBType given, VBType? type)
            => type is not null && given.GetType() == type.GetType() && given.Name.Equals(type.Name, StringComparison.OrdinalIgnoreCase);

        private static string NameOfType(VBType type) => type is VBFixedStringType ? "String" : type.Name;

        // the statement that gives a local its type's default value (MS-VBAL §5.4.3.1); none for a type that has none to give.
        private static string? DefaultOf(VBType? type, string name) => type switch
        {
            VBClassType or VBObjectType => $"Set {name} = Nothing",
            VBStringType => $"{name} = vbNullString",
            VBBooleanType => $"{name} = False",
            VBVariantType => $"{name} = Empty",
            VBDateType or VBNumericType => $"{name} = 0",
            _ => null,
        };

        // the name of the callee's, or the name followed by a number: one that nothing at the call, or in the body, means already.
        private string Fresh(string name)
        {
            var candidate = name;
            for (var index = 2; !IsFree(candidate); index++)
            {
                candidate = $"{name}{index}";
            }

            _taken.Add(candidate);
            return candidate;
        }

        private bool IsFree(string name)
            => !_taken.Contains(name)
               && NewNames.ValidityOf(name) is NewNameValidity.Valid
               && !_callerScope.SelfAndAncestors().Any(scope => scope.DeclaredAs(name).Any())
               && NewNames.RebindingOf(context, syntax, _callerScope, name) is null;
    }
}
