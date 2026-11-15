using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00307</c>: a name written with a type-declaration character - <c>x%</c>, <c>name$</c> - where an <c>As</c> clause at the declaration states the type
/// (<strong>MS-VBAL §3.3.5.3</strong>).
/// </summary>
/// <remarks>
/// <para>
/// The declaration of a variable, a constant, a parameter or a function that is written with one is reported where its name is written. So is a reference to a name
/// that the host vouches is bound to a declaration of the project (<see cref="ExpressionFactDto.Binding"/>), of this module or of another, since the declaration is
/// what is changed to get rid of it: a simple name (<c>gCount%</c>), or the name of a member access (<c>Globals.gCount%</c>), reported where the member's name is
/// written.
/// </para>
/// <para>
/// A reference to a member of a library (<see cref="ValueExpressionSemanticFlags.BoundToLibrary"/>), such as <c>Mid$</c> or <c>Error$</c> of the standard library,
/// has no declaration to change: it is not reported, and neither is a reference whose binding is not known.
/// </para>
/// <para>
/// The fix of a declaration takes the character out of the name and writes the <c>As</c> clause it stands for after the declarator; the fix of a reference takes
/// it out of the name, which a declared type allows with or without it. The fix is not offered for a function, whose <c>As</c> clause goes after a parameter list
/// the tree does not place, nor for a name written between brackets.
/// </para>
/// </remarks>
internal sealed class ObsoleteTypeHintAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ObsoleteTypeHint.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        // a reference bound to a declaration of the project: of this module, or of another.
        var boundToTheProject = (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding is not null && !fact.Flags.HasFlag(ValueExpressionSemanticFlags.BoundToLibrary))
            .Select(fact => fact.Node)
            .ToHashSet();
        // the name of a member access is reported with the access, which is what the host binds: Globals.gCount% is one reference.
        var memberNames = module.Descendants().OfType<MemberAccessExpressionNode>().Select(access => access.Member.Identity).ToHashSet();

        foreach (var node in module.Descendants())
        {
            var (name, range, fix) = node switch
            {
                MemberAccessExpressionNode { Member: { TypeHint.Length: > 0 } member } access
                    when boundToTheProject.Contains(access.Identity) || boundToTheProject.Contains(member.Identity)
                    => (member.IdentifierName, member.Location.Range, ReferenceFix(member)),
                SimpleNameExpressionNode reference when memberNames.Contains(reference.Identity) => (null, default, null),
                VariableDeclarationNode { TypeHint.Length: > 0 } variable
                    => (variable.Name, NameOf(variable.NameRange, variable.SourceLocation), DeclarationFix(variable.Name, variable.TypeHint, variable.NameRange, variable.SourceLocation.Range.End)),
                ConstantDeclarationNode { ConstKind: not ConstKind.EnumMember, TypeHint.Length: > 0 } constant
                    => (constant.Name, NameOf(constant.NameRange, constant.Location), DeclarationFix(constant.Name, constant.TypeHint, constant.NameRange, constant.NameRange?.End)),
                ParameterDeclarationNode { TypeHint.Length: > 0 } parameter
                    => (parameter.Name, NameOf(parameter.NameRange, parameter.Location),
                        DeclarationFix(parameter.Name, parameter.TypeHint, parameter.NameRange, parameter.IsArray ? parameter.Location.Range.End : parameter.NameRange?.End)),
                MemberDeclarationNode { TypeHint.Length: > 0 } member => (member.Name, NameOf(member.NameRange, member.SourceLocation), null),
                SimpleNameExpressionNode { TypeHint.Length: > 0 } reference when boundToTheProject.Contains(reference.Identity)
                    => (reference.IdentifierName, reference.Location.Range, ReferenceFix(reference)),
                _ => (null, default, null),
            };

            if (name is not null)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.ObsoleteTypeHint.ToCode(), range, DiagnosticSeverity.Information, AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObsoleteTypeHint_Message, name))
                {
                    Fixes = fix is null ? null : new DiagnosticFixes { Edits = [fix] },
                };
            }
        }
    }

    private static SourceRange NameOf(SourceRange? name, SourceLocation declaration) => name ?? declaration.Range;

    // the character comes out of the name, and its As clause goes where the declarator ends: after the name, or after the bounds of an array.
    private static EditFix? DeclarationFix(string name, string? hint, SourceRange? nameRange, SourcePosition? declaratorEnd)
    {
        if (hint is null || !IntrinsicVBTypes.TryResolveTypeHint(hint, out var type) || nameRange is not { } range || declaratorEnd is not { } end
            || SyntaxEdits.Width(range.Start, range.End) != name.Length + hint.Length)
        {
            return null;
        }

        var character = new SourceRange(new SourcePosition(range.End.Line, range.End.Character - hint.Length), range.End);
        var asClause = $" As {type.Name}";
        var title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObsoleteTypeHint_DeclarationFix, name, type.Name);
        return end == range.End
            ? SyntaxEdits.Fix(title, SyntaxEdits.Replace(character, asClause))
            : SyntaxEdits.Fix(title, SyntaxEdits.Remove(character), SyntaxEdits.Insert(end, asClause));
    }

    private static EditFix? ReferenceFix(SimpleNameExpressionNode reference)
    {
        var range = reference.Location.Range;
        var hint = reference.TypeHint!;
        return SyntaxEdits.Width(range.Start, range.End) == reference.IdentifierName.Length + hint.Length
            ? SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObsoleteTypeHint_ReferenceFix, reference.IdentifierName),
                SyntaxEdits.Remove(new SourceRange(new SourcePosition(range.End.Line, range.End.Character - hint.Length), range.End)))
            : null;
    }
}
