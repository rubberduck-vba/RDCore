using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01006</c> and <c>RDC01007</c>: a document module whose host's class is not known - RD-VBA does not know the host its project names, or the project names
/// none for a <c>.doccls</c> - or is not referenced, so that none of its members are names of the module's code.
/// </summary>
/// <remarks>
/// What the project says of the module is its <c>Super</c> (<see cref="SDK.Workspace.RDCoreModule.Super"/>), and nothing else says it: a <c>.doccls</c> that the
/// project says nothing of is loaded as the module its text makes it, and the extension of its file only says that it was exported from a host.
/// </remarks>
internal sealed class DocumentModuleHostAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics || context.ParseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        var name = semantics.Module.Fragment.TrimStart('#');
        switch (semantics.Document)
        {
            case { HostClass: null }:
                yield return Finding(RDCoreDiagnosticId.UnsupportedDocumentModule, module,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.UnsupportedDocumentModule_Message, name));
                break;

            case { HostClass: { } host, HostClassUri: null }:
                yield return Finding(RDCoreDiagnosticId.HostLibraryNotReferenced, module,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.HostLibraryNotReferenced_Message, name, $"{host.Library}.{host.ClassName}", host.Library));
                break;

            case null when context.Document.AbsolutePath.EndsWith(".doccls", StringComparison.OrdinalIgnoreCase):
                yield return Finding(RDCoreDiagnosticId.UnsupportedDocumentModule, module,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.UnsupportedDocumentModule_NoSuper_Message, name));
                break;
        }
    }

    private static AnalyzerFinding Finding(RDCoreDiagnosticId id, ModuleNode module, string message)
        => new(id, FirstLineOf(module), DiagnosticSeverity.Error, message);

    private static SourceRange FirstLineOf(ModuleNode module)
        => module.Children.FirstOrDefault()?.SourceLocation.Range is { } first
            ? new SourceRange(first.Start, first.Start.Line == first.End.Line ? first.End : first.Start)
            : SourceRange.Empty;
}
