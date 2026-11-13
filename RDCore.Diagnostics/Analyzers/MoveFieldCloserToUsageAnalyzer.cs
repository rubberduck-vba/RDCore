using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00413</c>: a <c>Private</c> variable of the module whose every reference is in one procedure: it can be a local of that procedure.
/// </summary>
/// <remarks>
/// The host states where each reference is (<see cref="RDCore.SDK.Platform.Protocol.DeclarationFactDto.ReferencedFrom"/>). A <c>WithEvents</c> variable is
/// used by the events it handles as well, and is not reported; nor is a variable that the declarations of the module refer to (the bounds of an array).
/// </remarks>
internal sealed class MoveFieldCloserToUsageAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.MoveFieldCloserToUsage.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Variable))
        {
            if (fact.Access is not (AccessModifier.Private or AccessModifier.Implicit) || fact.ReferencedFrom is not { Length: > 0 } sites
                || sites[0].Procedure is not { } procedure || !sites.All(site => site.Procedure is { } other && other.AbsoluteUri == procedure.AbsoluteUri)
                || UnusedDeclarations.ProcedureAt(module, fact.Location.Range.Start) is not null
                || UnusedDeclarations.DeclarationOf<VariableDeclarationNode>(module, fact, node => node.NameRange ?? node.SourceLocation.Range) is { IsWithEvents: true })
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.MoveFieldCloserToUsage.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.MoveFieldCloserToUsage_Message, fact.Name, SymbolUri.NameOf(procedure)));
        }
    }
}
