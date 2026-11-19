using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Services.Refactoring;

/// <summary>
/// A line label and the places its procedure names it: where it is defined, and each <c>GoTo</c>, <c>GoSub</c>, <c>On...GoTo</c>, <c>On...GoSub</c>,
/// <c>Resume</c> and <c>On Error GoTo</c> that names it.
/// </summary>
/// <param name="Name">The label, as it is defined, without brackets.</param>
/// <param name="Procedure">The procedure it is a label of.</param>
/// <param name="Here">Where it is named at the position it was found at.</param>
/// <param name="Occurrences">Every place its procedure names it, its definitions included, in the order of the text, and whether it is written in brackets there.</param>
/// <param name="Others">The other labels its procedure defines, each where it is defined.</param>
internal sealed record class LabelOccurrences(
    string Name, MemberDeclarationNode Procedure, SourceRange Here, ImmutableArray<(SourceRange Range, bool IsBracketed)> Occurrences,
    ImmutableArray<(string Name, SourcePosition At)> Others);

// MS-VBAL §5.4.1.1, §5.4.2.12, §5.4.2.13, §5.4.2.14, §5.4.2.16, §5.4.4.1, §5.4.4.2
// a label is not a symbol: it is a name of its procedure only, which the statements that transfer execution to it name. A line number is not a name, and is
// never renamed (Erl reads it).
internal static class LabelRename
{
    private static readonly MemberKind[] Procedures = [MemberKind.Procedure, MemberKind.Function, MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet];

    // the label named at a position of a module, with the places its procedure names it; null when no label is named there.
    public static LabelOccurrences? At(RenameModule module, SourcePosition position)
    {
        if (module.Syntax.Children.OfType<MemberDeclarationNode>()
                .FirstOrDefault(member => Procedures.Contains(member.MemberKind) && SymbolRename.Contains(member.SourceLocation.Range, position)) is not { } procedure)
        {
            return null;
        }

        var named = Named(procedure).ToList();
        if (named.FirstOrDefault(name => SymbolRename.Contains(name.Range, position)) is not { Label: { } label } here)
        {
            return null;
        }

        var same = named.Where(name => string.Equals(name.Label, label, StringComparison.OrdinalIgnoreCase)).ToList();
        var defined = same.FirstOrDefault(name => name.IsDefinition).Label ?? label;
        return new(defined, procedure, here.Range, [.. same.Select(name => (name.Range, name.IsBracketed)).Distinct().OrderBy(name => name.Range.Start)],
            [.. named.Where(name => name.IsDefinition && !string.Equals(name.Label, label, StringComparison.OrdinalIgnoreCase))
                .Select(name => (name.Label, name.Range.Start))]);
    }

    // the labels a range of a module defines, each where it is written.
    public static IEnumerable<SourcePosition> DefinitionsIn(RenameModule module, SourceRange range)
        => module.Syntax.Children.OfType<MemberDeclarationNode>()
            .Where(member => Procedures.Contains(member.MemberKind))
            .SelectMany(procedure => Named(procedure))
            .Where(name => name.IsDefinition && SymbolRename.Contains(range, name.Range.Start))
            .Select(name => name.Range.Start);

    // every label a procedure defines and every one its statements name, without brackets, with the range of the name as it is written.
    private static IEnumerable<(string Label, SourceRange Range, bool IsDefinition, bool IsBracketed)> Named(SyntaxNode procedure)
    {
        foreach (var (node, _) in NameOccurrences.Walk(procedure))
        {
            switch (node)
            {
                // a line number has no name, and so no range of one.
                case LineLabelNode { NameRange: { } name } label:
                    yield return (label.Name, name, true, label.IsBracketed);
                    break;
                case GoToStatementNode { LabelExpression: SimpleNameExpressionNode name }:
                    yield return Reference(name);
                    break;
                case GoSubStatementNode { LabelExpression: SimpleNameExpressionNode name }:
                    yield return Reference(name);
                    break;
                case OnErrorGoToStatementNode { LabelExpression: SimpleNameExpressionNode name }:
                    yield return Reference(name);
                    break;
                case ResumeStatementNode { LabelExpression: SimpleNameExpressionNode name }:
                    yield return Reference(name);
                    break;
                case OnGoToStatementNode onGoTo:
                    foreach (var name in onGoTo.Labels.OfType<SimpleNameExpressionNode>())
                    {
                        yield return Reference(name);
                    }

                    break;
                case OnGoSubStatementNode onGoSub:
                    foreach (var name in onGoSub.Labels.OfType<SimpleNameExpressionNode>())
                    {
                        yield return Reference(name);
                    }

                    break;
            }
        }
    }

    private static (string, SourceRange, bool, bool) Reference(SimpleNameExpressionNode name)
        => (name.IdentifierName, name.Location.Range, false, name.IsBracketed);
}
