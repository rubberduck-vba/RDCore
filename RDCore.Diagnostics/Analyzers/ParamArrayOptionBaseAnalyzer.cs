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
/// The analyzer reads the syntax tree of the file's module and of each of its blocks' modules, each with its own <c>Option Base</c>. It does not see an
/// <c>Option Base 1</c> that another part of a <c>Partial</c> module states in another file.
/// </remarks>
internal sealed class ParamArrayOptionBaseAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ParamArrayIgnoresOptionBase.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var document = new RDCoreDocument(context.Document, context.ParseResult);
        return document.ModuleUris.Select(document.SyntaxTreeOf).OfType<ModuleNode>()
            .Where(module => module.GetOptionBase() == 1)
            .SelectMany(module => module.Descendants().OfType<ParameterDeclarationNode>())
            .Where(parameter => parameter.IsParamArray)
            .Select(parameter => new AnalyzerFinding(RDCoreDiagnosticId.ParamArrayIgnoresOptionBase.ToCode(), parameter.Location.Range,
                DiagnosticSeverity.Hint, AnalyzerMessages.Format(RDCoreDiagnosticsResources.ParamArrayIgnoresOptionBase_Message, parameter.Name)));
    }
}
