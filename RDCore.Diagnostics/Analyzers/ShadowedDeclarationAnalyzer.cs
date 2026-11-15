using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00208</c>: a declaration named like something else, which the code where the declaration is in scope does not see by that name
/// (<strong>MS-VBAL §5.6.10</strong>): a local or a parameter named like a member of its module, a <c>Public</c> member of another module, a member of a
/// library or a module; a declaration of the module named like a <c>Public</c> member of another module, a member of a library, or a member of the host's
/// class in a document module.
/// </summary>
/// <remarks>
/// A simple name is first matched in its procedure, then in its module, then in the project and the libraries it references: <c>Dim Left As Long</c> makes
/// <c>Left(s, 1)</c> an index of the variable in that procedure, and a module's <c>Public Function Replace</c> is what <c>Replace</c> calls where
/// <c>VBA.Strings.Replace</c> was. What the name would mean without the declaration is a fact the host vouches for
/// (<see cref="SDK.Semantics.DeclarationFact.Hides"/>): only the host resolves names across modules and libraries.
/// </remarks>
internal sealed class ShadowedDeclarationAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ShadowedDeclaration.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration.Hides is not null)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.ShadowedDeclaration.ToCode(), declaration.Location.Range, DiagnosticSeverity.Information,
                AnalyzerMessages.Format(
                    IsLocal(context, declaration) ? RDCoreDiagnosticsResources.ShadowedDeclaration_Message : RDCoreDiagnosticsResources.ShadowedDeclaration_ModuleMessage,
                    declaration.Name, declaration.Hides!.QualifiedName)));

    // a parameter, or a variable or a constant that a procedure declares.
    private static bool IsLocal(ModuleAnalysisContext context, DeclarationFactDto declaration)
        => declaration.Kind is DeclarationKind.Parameter
            || declaration.Kind is DeclarationKind.Variable or DeclarationKind.Constant
                && context.ParseResult.SyntaxTree is { } module
                && UnusedDeclarations.ProcedureAt(module, declaration.Location.Range.Start) is not null;
}
