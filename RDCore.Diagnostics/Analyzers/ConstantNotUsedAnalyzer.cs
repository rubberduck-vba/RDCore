using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01015</c>: a constant nothing refers to.
/// </summary>
/// <remarks>
/// The host counts the references of a constant (<see cref="SDK.Platform.Protocol.DeclarationFactDto.References"/>). A constant of a procedure is counted over
/// the code of the procedure: its statements, the values of its other constants and the bounds of its arrays. A <c>Private</c> constant of the module,
/// declared with no access keyword too, is counted over the code and the declarations of the module. A <c>Public</c> constant has no count, and is not
/// reported. The finding comes with the fix that removes the declaration.
/// </remarks>
internal sealed class ConstantNotUsedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ConstantNotUsed.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            yield break;
        }

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Constant).Where(counted => counted.References is { Reads: 0, PassedAsArguments: 0 }))
        {
            var declaration = context.ParseResult.SyntaxTree is { } module
                ? UnusedDeclarations.DeclarationOf<ConstantDeclarationNode>(module, fact, node => node.NameRange ?? node.SourceLocation.Range)
                : null;
            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.ConstantNotUsed.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ConstantNotUsed_Message, fact.Name))
            {
                Fixes = declaration is not null
                    && UnusedDeclarations.RemovalOf(context.ParseResult, declaration, AnalyzerMessages.Format(RDCoreDiagnosticsResources.ConstantNotUsed_Fix, fact.Name)) is { } fix
                    ? new DiagnosticFixes { Edits = [fix] }
                    : null,
            };
        }
    }
}
