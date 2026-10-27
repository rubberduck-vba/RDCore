using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11022</c>: a <c>ReDim</c> of a name the procedure used before it, with no declaration, so that the name is a variable declared implicitly where it
/// was first used (<strong>MS-VBAL §5.6.10</strong>), and the <c>ReDim</c> re-dimensions it.
/// </summary>
/// <remarks>
/// MS-VBAL makes such a variable a <c>Variant</c> (or the type a <c>Def</c><em>Type</em> directive gives the name), which a <c>ReDim</c> re-dimensions, as
/// vb6.exe does. MS-VBA refuses the <c>ReDim</c> at compile time instead, "Invalid ReDim", whether the earlier use writes the name, reads it, or is on a line
/// that never runs (recorded in Excel: oracle\findings-labels R01-R10). RD-VBA runs it as the specification and vb6.exe do, and warns, by the author's ruling
/// (HexIDE's findings desk #9). That the name was declared implicitly is a fact of the declaration the host vouches for
/// (<see cref="SDK.Semantics.DeclarationFact.DeclaredBy"/>); a name used only after its first <c>ReDim</c> is the array that <c>ReDim</c> declares
/// (<c>RDC00114</c>), and nothing is reported here.
/// </remarks>
internal sealed class ReDimOfAnImplicitVariantAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            return [];
        }

        var implicitLocals = (context.Semantics?.Declarations ?? []).Where(declaration => declaration.DeclaredBy is LocalDeclarationKind.Implicit).ToArray();
        return module.Descendants()
            .OfType<MemberDeclarationNode>()
            .SelectMany(procedure => procedure.Descendants()
                .OfType<RedimDeclarationNode>()
                .Where(redim => redim.IsSimpleName && implicitLocals.Any(local =>
                    string.Equals(local.Name, redim.Name, StringComparison.OrdinalIgnoreCase)
                    && Within(procedure.SourceLocation.Range, local.Location.Range.Start)
                    && local.Location.Range.Start.CompareTo(redim.SourceLocation.Range.Start) < 0)))
            .Select(redim => new AnalyzerFinding(
                RDCoreDiagnosticId.ReDimOfAnImplicitVariant,
                redim.Target.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ReDimOfAnImplicitVariant_Message, redim.Name)));
    }

    private static bool Within(SourceRange range, SourcePosition position)
        => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;
}
