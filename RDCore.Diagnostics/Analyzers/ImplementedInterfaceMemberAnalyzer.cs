using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00411</c>: a class that a module of the project implements as an interface (<strong>MS-VBAL §5.2.4.2</strong>) has code in a member. The implementing
/// module gives each member of the interface a body of its own, and the code of the class runs only on an object created from the class itself.
/// </summary>
/// <remarks>
/// The class is reported once, at the first member that has code: a statement, or a <c>Dim</c>, <c>Static</c> or <c>Const</c> declaration. Comments and line
/// labels are not code. Whether another module implements the class is a fact the host states (<see cref="ClassModuleSemanticFlags.Implemented"/>);
/// without it, the analyzer reports nothing. The finding comes with the fix that removes the code of every member that has some, its title saying what
/// changes: an object created from the class itself then runs none of it, and its functions and <c>Property Get</c> members return the default value of
/// their type.
/// </remarks>
internal sealed class ImplementedInterfaceMemberAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplementedInterfaceMember.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { Class: { } flags } semantics } || !flags.HasFlag(ClassModuleSemanticFlags.Implemented))
        {
            return [];
        }

        var withCode = module.Children.OfType<MemberDeclarationNode>().Where(member => IsProcedure(member) && BodyOf(member).Any(IsCode)).ToArray();
        if (withCode.Length == 0)
        {
            return [];
        }

        var name = SymbolUri.ModuleNameOf(semantics.Module);
        var edits = withCode.Select(member => CodeOf(context.ParseResult, member)).ToArray();
        return
        [
            new AnalyzerFinding(
                RDCoreDiagnosticId.ImplementedInterfaceMember.ToCode(),
                withCode[0].NameRange ?? withCode[0].SourceLocation.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplementedInterfaceMember_Message, name, withCode[0].Name))
            {
                Fixes = edits.All(member => member is not null)
                    ? new DiagnosticFixes
                    {
                        Edit = SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplementedInterfaceMember_Fix, name), [.. edits.SelectMany(member => member!)]),
                    }
                    : null,
            },
        ];
    }

    private static bool IsProcedure(MemberDeclarationNode member)
        => member is not ExternalMemberDeclarationNode
            && member.MemberKind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

    // a declaration is code here: an interface's member declares nothing. A block of a #If is judged by what it holds, whichever branch is live.
    private static bool IsCode(SyntaxNode node) => node switch
    {
        VariableDeclarationNode or ConstantDeclarationNode => true,
        PrecompilerIfBlockStatementNode or PrecompilerElseIfBlockStatementNode or PrecompilerElseBlockStatementNode or PrecompilerInlineIfStatementNode
            => node.Children.Any(IsCode),
        _ => !new[] { node }.HasNoStatement(),
    };

    private static IEnumerable<SyntaxNode> BodyOf(MemberDeclarationNode member)
        => member.Children.SkipWhile(child => child is ParameterDeclarationNode or AsTypeExpressionNode);

    // the whole lines from the member's first statement, label or declaration to the line before its End, less its Attribute lines: the comments above the
    // first statement stay. Null when the analyzer cannot tell those lines from the header and the End: code on the line of either, an Attribute line
    // after the last statement (the member's range then ends after the End), or an #If in the member.
    private static IEnumerable<TextEdit>? CodeOf(ModuleParseResult parse, MemberDeclarationNode member)
    {
        var body = BodyOf(member).ToArray();
        var code = body.Where(child => child is not AttributeDirectiveNode).ToArray();
        var attributes = body.OfType<AttributeDirectiveNode>().ToArray();
        var header = member.Children.Except(body).Select(child => child.SourceLocation.Range.End.Line)
            .Append((member.NameRange ?? member.SourceLocation.Range).End.Line)
            .Max();
        var first = code.Min(child => child.SourceLocation.Range.Start.Line);
        var last = code.Max(child => child.SourceLocation.Range.End.Line);
        var end = member.SourceLocation.Range.End.Line;
        if (first <= header || last >= end
            || attributes.Any(attribute => attribute.SourceLocation.Range.Start.Line > last)
            || parse.PrecompilerTrivia.Any(trivia => trivia.SourceLocation.Range.Start.Line <= end && trivia.SourceLocation.Range.End.Line >= header))
        {
            return null;
        }

        var kept = attributes.SelectMany(attribute => Enumerable.Range(attribute.SourceLocation.Range.Start.Line,
            attribute.SourceLocation.Range.End.Line - attribute.SourceLocation.Range.Start.Line + 1)).ToHashSet();
        var edits = new List<TextEdit>();
        int? from = null;
        for (var line = first; line <= end; line++)
        {
            if (line < end && !kept.Contains(line))
            {
                from ??= line;
            }
            else if (from is { } start)
            {
                edits.Add(SyntaxEdits.Remove(SyntaxEdits.LinesThrough(start, line - 1)));
                from = null;
            }
        }

        return edits;
    }
}
