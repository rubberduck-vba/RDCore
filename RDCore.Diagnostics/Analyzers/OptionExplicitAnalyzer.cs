using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00101</c>: a module that does not state <c>Option Explicit</c> (<strong>MS-VBAL §5.2.1.3</strong>).
/// </summary>
/// <remarks>
/// <para>
/// Without it a name that is not declared is not an error, and a variable comes into being where it is used, so a misspelled name goes unnoticed. Whether the
/// module states it is a fact the host that loaded the module vouches for, and the host does not issue it for a language that has no such directive (a BASIC has no way
/// to state it): an analyzer that is not given the fact has nothing to say.
/// </para>
/// <para>
/// The fix writes <c>Option Explicit</c> before the first declaration of the module, and declares each variable the module declares implicitly, with a
/// <c>Dim</c> before the first statement of its procedure (<strong>MS-VBAL §5.6.10</strong>): <c>As</c> the type of its type-declaration character where
/// it is written with one, which then comes out of its uses; without a type where a <c>Def&lt;Type&gt;</c> directive gives it one; and <c>As Variant</c>
/// otherwise. Alone, the option would make each of them an error. It is not offered where a procedure starts its body on the line of its declaration, or
/// has a directive in it, which the <c>Dim</c> could not be placed before.
/// </para>
/// </remarks>
internal sealed class OptionExplicitAnalyzer : IModuleAnalyzer
{
    private const string NewLine = "\r\n";

    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitDeclarationsEnabled.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is { OptionExplicit: false } semantics)
        {
            // the module as a whole is what is wrong with it: the first line is where a reader looks for the option.
            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitDeclarationsEnabled.ToCode(),
                new SourceRange(new SourcePosition(0, 0), new SourcePosition(1, 0)),
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.ImplicitDeclarationsEnabled_Message)
            {
                Fixes = context.ParseResult.SyntaxTree is { } module ? FixOf(module, semantics.Declarations, context.ParseResult.PrecompilerTrivia) : null,
            };
        }
    }

    private static DiagnosticFixes? FixOf(ModuleNode module, IEnumerable<SDK.Platform.Protocol.DeclarationFactDto> declarations, IEnumerable<SyntaxNode> trivia)
    {
        // the precompiler trivia is one node over the module: its directives, and what their branches hold, are the nodes under it.
        var directives = trivia.SelectMany(node => node.Descendants()).ToArray();
        var edits = new List<TextEdit> { SyntaxEdits.Insert(new SourcePosition(OptionLine(module), 0), $"Option Explicit{NewLine}") };
        var procedures = module.Children.OfType<MemberDeclarationNode>().ToArray();
        foreach (var group in declarations.Where(declaration => declaration.DeclaredBy is LocalDeclarationKind.Implicit)
            .GroupBy(declaration => procedures.FirstOrDefault(procedure => Contains(procedure.SourceLocation.Range, declaration.Location.Range.Start))))
        {
            if (group.Key is not { } procedure || FirstStatementOf(procedure) is not { } first
                || first.SourceLocation.Range.Start.Line <= procedure.SourceLocation.Range.Start.Line
                || directives.Any(directive => Contains(procedure.SourceLocation.Range, directive.SourceLocation.Range.Start)))
            {
                return null;
            }

            var indent = new string(' ', first.SourceLocation.Range.Start.Character);
            var names = procedure.Descendants().OfType<SimpleNameExpressionNode>().ToArray();
            var declarators = new List<string>();
            foreach (var declaration in group)
            {
                if (Declarator(module, declaration.Name, names, edits) is not { } declarator)
                {
                    return null;
                }

                declarators.Add($"{indent}Dim {declarator}{NewLine}");
            }

            edits.Add(SyntaxEdits.Insert(new SourcePosition(first.SourceLocation.Range.Start.Line, 0), string.Concat(declarators)));
        }

        return new DiagnosticFixes { Edit = SyntaxEdits.Fix(RDCoreDiagnosticsResources.ImplicitDeclarationsEnabled_Fix, [.. edits]) };
    }

    // the name as the implicit declaration typed it: As the type of its type-declaration character, where a use of it has one, which then comes out of each
    // use that has it; nothing, where a DefType directive types it; and As Variant otherwise. Null where a use is not written the way the tree says it is.
    private static string? Declarator(ModuleNode module, string name, SimpleNameExpressionNode[] names, List<TextEdit> edits)
    {
        var hinted = names.Where(each => string.Equals(each.IdentifierName, name, StringComparison.OrdinalIgnoreCase) && each.TypeHint is { Length: > 0 }).ToArray();
        if (hinted.Length == 0)
        {
            return ImplicitTypes.IsVariant(module, name) ? $"{name} As Variant" : name;
        }

        if (!IntrinsicVBTypes.TryResolveTypeHint(hinted[0].TypeHint!, out var type))
        {
            return null;
        }

        foreach (var use in hinted)
        {
            var range = use.Location.Range;
            if (SyntaxEdits.Width(range.Start, range.End) != use.IdentifierName.Length + use.TypeHint!.Length)
            {
                return null;
            }

            edits.Add(SyntaxEdits.Remove(new SourceRange(new SourcePosition(range.End.Line, range.End.Character - use.TypeHint.Length), range.End)));
        }

        return $"{name} As {type.Name}";
    }

    // the line the option goes on: the first of the module's declarations and procedures, after its attributes; the line after them in a module that has none.
    private static int OptionLine(ModuleNode module)
        => module.Children.FirstOrDefault(child => child is not AttributeDirectiveNode) is { } first
            ? first.SourceLocation.Range.Start.Line
            : module.Children.OfType<AttributeDirectiveNode>().Select(attribute => attribute.SourceLocation.Range.End.Line + 1).DefaultIfEmpty(0).Max();

    // the first node of a procedure's body, past its parameters and return type.
    private static SyntaxNode? FirstStatementOf(MemberDeclarationNode procedure)
        => procedure.Children
            .Where(child => child is not (ParameterDeclarationNode or AsTypeExpressionNode))
            .OrderBy(child => child.SourceLocation.Range.Start)
            .FirstOrDefault();

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;
}
