using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00413</c>: a variable of the module whose every reference is in one procedure: it can be a local of that procedure.
/// </summary>
/// <remarks>
/// The host states where each reference is (<see cref="RDCore.SDK.Platform.Protocol.DeclarationFactDto.ReferencedFrom"/>): for a <c>Private</c> or
/// <c>Dim</c> variable, and for a <c>Public</c> one that only the project's code can refer to (of a program, or of an <c>Option Private Module</c> module).
/// A <c>Public</c> variable of a standard module can be a local of a procedure of another module. One of a class is reported only when that procedure is the
/// class's own: elsewhere, it is a member of an object, which no local stands for. A <c>WithEvents</c> variable is used by the events it handles as well, and
/// is not reported; nor is a variable that the declarations of the module refer to.
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
            if (fact.ReferencedFrom is not { Length: > 0 } sites
                || sites[0].Procedure is not { } procedure || !sites.All(site => site.Procedure is { } other && other.AbsoluteUri == procedure.AbsoluteUri)
                || !CanBeALocal(fact, IsOfItsModule(fact, procedure), semantics.Class is not null)
                || UnusedDeclarations.ProcedureAt(module, fact.Location.Range.Start) is not null
                || UnusedDeclarations.DeclarationOf<VariableDeclarationNode>(module, fact, node => node.NameRange ?? node.SourceLocation.Range) is { IsWithEvents: true })
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.MoveFieldCloserToUsage.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.MoveFieldCloserToUsage_Message, fact.Name,
                    IsOfItsModule(fact, procedure) ? SymbolUri.NameOf(procedure) : SymbolUri.QualifiedNameOf(procedure)));
        }
    }

    // a Public variable of a class that another module's code uses is a member of the class's objects.
    private static bool CanBeALocal(DeclarationFactDto fact, bool usedByItsModule, bool isClass) => fact.Access switch
    {
        AccessModifier.Private or AccessModifier.Implicit => true,
        AccessModifier.Public or AccessModifier.Global => usedByItsModule || !isClass,
        _ => false,
    };

    private static bool IsOfItsModule(DeclarationFactDto fact, Uri procedure)
        => string.Equals(SymbolUri.ModuleNameOf(fact.Symbol), SymbolUri.ModuleNameOf(procedure), StringComparison.OrdinalIgnoreCase);
}
