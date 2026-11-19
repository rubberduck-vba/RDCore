using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00128</c>: a variable that no statement declares, in a module with no <c>Option Explicit</c>: a name that matches nothing, which declares a local
/// variable where the procedure first uses it (<strong>MS-VBAL §5.6.10</strong>).
/// </summary>
/// <remarks>
/// <para>
/// <c>RDC00101</c> reports the module once; this reports each variable, where it is first used, as Rubberduck's <em>undeclared variable</em> inspection does: a
/// misspelled name (<c>totl</c> for <c>total</c>) is one of them. That the name matched nothing is a fact of the declaration the host vouches for
/// (<see cref="SDK.Semantics.DeclarationFact.DeclaredBy"/> is <see cref="LocalDeclarationKind.Implicit"/>), which it never states under <c>Option Explicit</c>,
/// where the name is a compile error instead. A <c>ReDim</c> that declares its array is <c>RDC00114</c>'s.
/// </para>
/// <para>
/// The fix declares the variable with a <c>Dim</c> before the first statement of its procedure, of the type it already has: the type of the character its
/// first use is written with (<see cref="IntrinsicVBTypes.TryResolveTypeHint"/>), or else of the <c>Def</c><em>Type</em> directive that covers its name, or
/// else <c>Variant</c>. The program does the same: a local exists from the start of its procedure, wherever it is declared. It is not offered for a procedure
/// whose body starts on the line of its declaration.
/// </para>
/// </remarks>
internal sealed class UndeclaredVariableAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.UndeclaredVariable.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var module = context.ParseResult.SyntaxTree;
        return (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration.DeclaredBy is LocalDeclarationKind.Implicit)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.UndeclaredVariable.ToCode(), declaration.Location.Range, DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.UndeclaredVariable_Message, declaration.Name))
            {
                Fixes = module is not null && FixOf(context.ParseResult, module, declaration.Name, declaration.Location.Range) is { } fix
                    ? new DiagnosticFixes { Edits = [fix] }
                    : null,
            });
    }

    private static EditFix? FixOf(ModuleParseResult parse, ModuleNode module, string name, SourceRange firstUse)
    {
        if (module.Descendants().OfType<MemberDeclarationNode>().FirstOrDefault(member => Within(member.SourceLocation.Range, firstUse.Start)) is not { } procedure)
        {
            return null;
        }

        // the first node of the body, past the declaration's parameters and return type, which the Dim goes before.
        var first = procedure.Children
            .Where(child => child is not (ParameterDeclarationNode or AsTypeExpressionNode))
            .OrderBy(child => child.SourceLocation.Range.Start)
            .FirstOrDefault();
        if (first is null || first.SourceLocation.Range.Start.Line <= procedure.SourceLocation.Range.Start.Line)
        {
            return null;
        }

        var hint = procedure.Descendants().OfType<SimpleNameExpressionNode>().FirstOrDefault(use => use.Location.Range.Start.Equals(firstUse.Start))?.TypeHint;
        var type = (!string.IsNullOrEmpty(hint) && IntrinsicVBTypes.TryResolveTypeHint(hint, out var hinted) ? hinted.Name : null)
            ?? TypeDefDirectiveNode.Covering(module.Children.OfType<TypeDefDirectiveNode>(), name)?.TypeName
            ?? VBTypeNames.VBVariant;
        return LineEdits.InsertLinesBefore(parse, first, [$"Dim {name} As {type}"]) is { } declaration
            ? SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.UndeclaredVariable_Fix, name, type), declaration)
            : null;
    }

    private static bool Within(SourceRange range, SourcePosition position)
        => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;
}
