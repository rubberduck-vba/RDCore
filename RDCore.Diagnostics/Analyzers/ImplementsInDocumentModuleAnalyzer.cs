using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11024</c>: an <c>Implements</c> directive in a document module, which <strong>MS-VBAL §5.2.4.2</strong> says cannot occur in an extension module, and
/// <strong>MS-VBA</strong> compiles and runs in a worksheet's.
/// </summary>
/// <remarks>
/// A document module is an extension module of its host's extensible module (<strong>MS-VBAL §4.2.1</strong>). RD-VBA does as MS-VBA does (recorded in Excel):
/// the directive is checked as any class module's, and this stands for what the specification says of it.
/// </remarks>
internal sealed class ImplementsInDocumentModuleAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.Semantics?.Document is not null && context.ParseResult.SyntaxTree is { } module
            ? module.GetImplementedInterfaceNames().Zip(module.GetImplementedInterfaceRanges()).Select(directive => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplementsInDocumentModule,
                directive.Second,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplementsInDocumentModule_Message, context.Semantics.Module.Fragment.TrimStart('#'), directive.First)))
            : [];
}
