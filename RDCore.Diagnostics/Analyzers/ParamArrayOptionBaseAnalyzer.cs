using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Workspace;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00220</c>: a <c>ParamArray</c> parameter in a module with <c>Option Base 1</c>. The param array is a new array with a lower bound of 0
/// (<strong>MS-VBAL §5.3.1.11</strong>), whatever the lower bound <c>Option Base</c> gives the module's other arrays (<strong>MS-VBAL §5.2.1.2</strong>).
/// </summary>
/// <remarks>
/// The analyzer reads the syntax tree of the file's module and of each of its blocks' modules. The file's module is under the <c>Option Base</c> of its
/// semantics, which any of its parts may state, in any file; a block's module is under the <c>Option Base</c> its own tree states.
/// </remarks>
internal sealed class ParamArrayOptionBaseAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ParamArrayIgnoresOptionBase.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var document = new RDCoreDocument(context.Document, context.ParseResult);
        return document.ModuleUris.Where(uri => OptionBaseOf(context, document, uri) == 1)
            .Select(document.SyntaxTreeOf).OfType<ModuleNode>()
            .SelectMany(module => module.Descendants().OfType<ParameterDeclarationNode>())
            .Where(parameter => parameter.IsParamArray)
            .Select(parameter => new AnalyzerFinding(RDCoreDiagnosticId.ParamArrayIgnoresOptionBase.ToCode(), parameter.Location.Range,
                DiagnosticSeverity.Hint, AnalyzerMessages.Format(RDCoreDiagnosticsResources.ParamArrayIgnoresOptionBase_Message, parameter.Name)));
    }

    // the semantics are the file's module's only: a block has the options its own tree states.
    private static int OptionBaseOf(ModuleAnalysisContext context, RDCoreDocument document, Uri module)
        => module == document.File && context.Semantics?.Directives is { } directives
            ? directives.Base
            : document.SyntaxTreeOf(module)?.GetOptionBase() ?? 0;
}
