using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// A <c>Function</c> made a <c>Sub</c> (<strong>MS-VBAL §5.3.1</strong>), for a function no caller uses the value of: a call statement calls a <c>Sub</c> as it
/// calls a <c>Function</c> (<strong>MS-VBAL §5.4.2.1</strong>).
/// </summary>
internal static class FunctionsMadeSubs
{
    /// <summary>
    /// The fix that makes a <c>Function</c> a <c>Sub</c>, which keeps what the code does: its keyword, its <c>End Function</c> and its <c>Exit Function</c>
    /// statements written for a <c>Sub</c>, its <c>As</c> clause removed, and the statements that assign its return value removed.
    /// </summary>
    /// <remarks>
    /// Offered when every reference the host counts discards the value (<see cref="DeclarationFactDto.ReferencedFrom"/>), and the body uses its return value
    /// only as the target of a statement of its own line, given a literal or the value of a variable: none of them has an effect that removing it would
    /// lose. Not offered for a function written with a type character, or with a header in each branch of a <c>#If</c> block, nor where the tree does not
    /// say where a keyword is written.
    /// </remarks>
    /// <param name="context">The module the function is declared in.</param>
    /// <param name="declaration">The fact of the function's declaration.</param>
    /// <returns>The fix; <see langword="null"/> when it is not offered.</returns>
    public static EditFix? Fix(ModuleAnalysisContext context, DeclarationFactDto declaration)
    {
        var parse = context.ParseResult;
        if (context.Semantics is not { } semantics
            || parse.SyntaxTree?.Children.OfType<MemberDeclarationNode>().FirstOrDefault(member
                => member.NameRange?.Start.Line == declaration.Location.Range.Start.Line
                    && string.Equals(member.Name, declaration.Name, StringComparison.OrdinalIgnoreCase)) is not
                { MemberKind: MemberKind.Function, TypeHint: null, NameRange: { } name, EndRange: { } end } function
            || !function.HeaderAlternatives.IsDefaultOrEmpty
            || declaration.ReferencedFrom is not { } sites || !sites.All(site => site.DiscardsResult)
            || KeywordOf(function, name) is not { } keyword || SyntaxEdits.Width(end.Start, end.End) != Tokens.EndFunction.Length)
        {
            return null;
        }

        var edits = new List<TextEdit> { SyntaxEdits.Replace(keyword, Tokens.Sub), SyntaxEdits.Replace(end, Tokens.EndSub) };
        if (function.Children.OfType<AsTypeExpressionNode>().FirstOrDefault() is { } asType)
        {
            var from = function.ParameterListRange?.End ?? name.End;
            if (SyntaxEdits.Width(from, asType.SourceLocation.Range.End) < 0)
            {
                return null;
            }

            edits.Add(SyntaxEdits.Remove(new SourceRange(from, asType.SourceLocation.Range.End)));
        }

        var body = function.Children.Where(child => child is not (ParameterDeclarationNode or AsTypeExpressionNode)).SelectMany(DescendantsAndSelf).ToArray();
        foreach (var exit in body.OfType<KeywordStatementNode>().Where(statement => statement.Token == Tokens.ExitFunction))
        {
            var range = exit.SourceLocation.Range;
            if (SyntaxEdits.Width(range.Start, range.End) != Tokens.ExitFunction.Length)
            {
                return null;
            }

            edits.Add(SyntaxEdits.Replace(range, Tokens.ExitSub));
        }

        // the return value assigned is removed with its statement; it is named nowhere else, but to call the procedure.
        var assignments = body.OfType<AssignmentStatementNode>().Where(statement => IsTheResult(statement.Target, function)).ToArray();
        var called = body.OfType<CallStatementNode>().Select(statement => statement.Callee.Identity).ToHashSet();
        if (body.OfType<SimpleNameExpressionNode>()
            .Any(each => IsTheResult(each, function) && !called.Contains(each.Identity)
                && !assignments.Any(statement => statement.Target.Identity == each.Identity)))
        {
            return null;
        }

        foreach (var assignment in assignments)
        {
            if (!HasNoEffect(assignment, semantics) || LineEdits.WholeLinesOf(parse, assignment) is not { } lines)
            {
                return null;
            }

            edits.Add(SyntaxEdits.Remove(lines));
        }

        return new EditFix { Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.FunctionMadeASub_Fix, function.Name), Edits = edits };
    }

    // the Function keyword, written right before the name, after the access modifier and the Static keyword, each followed by one space.
    private static SourceRange? KeywordOf(MemberDeclarationNode function, SourceRange name)
    {
        var header = (function.AccessModifier is AccessModifier.Implicit ? 0 : function.AccessModifier.ToString().Length + 1)
            + (function.IsStatic ? Tokens.Static.Length + 1 : 0);
        return SyntaxEdits.Width(function.SourceLocation.Range.Start, name.Start) == header + Tokens.Function.Length + 1
            ? SyntaxEdits.Preceding(new SourcePosition(name.Start.Line, name.Start.Character - 1), Tokens.Function.Length)
            : null;
    }

    private static bool IsTheResult(ExpressionNode expression, MemberDeclarationNode function)
        => expression is SimpleNameExpressionNode { IdentifierName: var name } && string.Equals(name, function.Name, StringComparison.OrdinalIgnoreCase);

    // a literal, or a variable whose value is read without a call: of a type that has no default member for a Let, of any type for a Set.
    private static bool HasNoEffect(AssignmentStatementNode assignment, ModuleSemanticsDto semantics)
    {
        if (assignment.Kind is not (AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet or AssignmentKind.Set))
        {
            return false;
        }

        if (assignment.Value is LiteralExpressionNode)
        {
            return true;
        }

        var range = assignment.Value.SourceLocation.Range;
        return assignment.Value is SimpleNameExpressionNode
            && semantics.Procedures.SelectMany(procedure => procedure.Expressions).FirstOrDefault(fact => fact.Location.Range == range) is
                { Error: null, Classification: ExpressionClassification.Variable } fact
            && (assignment.Kind is AssignmentKind.Set || fact.TypeKind is not (DeclaredTypeKind.Class or DeclaredTypeKind.Object or DeclaredTypeKind.Variant));
    }

    private static IEnumerable<SyntaxNode> DescendantsAndSelf(SyntaxNode node) => [node, .. node.Descendants()];
}
