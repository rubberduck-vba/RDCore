using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11028</c>: <c>New</c> of a class whose objects are never created (<c>New ErrObject</c>), and a variable declared <c>As New</c> of one
/// (<c>Dim e As New ErrObject</c>), which MS-VBA compiles.
/// </summary>
/// <remarks>
/// The class's library says it can be created, so <strong>MS-VBAL §5.6.8</strong> and <strong>§5.2.3.1.4</strong> make <c>New</c> of it valid; nothing
/// creates one, and creating it raises run-time error -2147221164 <em>Class not registered</em> (recorded in Excel: <c>oracle\s62</c> X04, X06-X09). That
/// holds of every path, whether or not the run-time facts reach the code, and it is reported wherever it is written, at Warning severity.
/// </remarks>
internal sealed class NewOfAClassNotCreatableAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var declarations = (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration.AutoInstantiationAlwaysFails)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.NewOfAClassNotCreatable,
                declaration.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.NewOfAClassNotCreatable_AsNew_Message, declaration.Name)));

        var expressions = (context.Semantics?.Procedures ?? []).SelectMany(procedure => procedure.Expressions
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.NewAlwaysFails))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.NewOfAClassNotCreatable,
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.NewOfAClassNotCreatable_New_Message, NameOf(fact)))));

        return declarations.Concat(expressions);
    }

    // the name of the class created: the last part of the address of the class the expression is bound to.
    private static string NameOf(ExpressionFactDto fact)
        => fact.Binding?.Fragment.TrimStart('#') is { Length: > 0 } address ? address[(address.LastIndexOf('.') + 1)..] : string.Empty;
}
