using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11012</c>: a <c>Set</c> statement whose target is a call - of a function, or of a property that has no <c>Property Set</c> - that raises a run-time
/// error whenever it runs, which MS-VBA compiles.
/// </summary>
/// <remarks>
/// <para>
/// A function or a property with no <c>Property Set</c> named as the target of a <c>Set</c> is called, and what it returns is Set through its default member
/// (<strong>MS-VBAL §5.4.3.9</strong>). When it is declared as a class whose default member has no <c>Property Set</c>, the object has nothing to Set, and the
/// statement raises error 438 whenever it runs (<see cref="ValueExpressionSemanticFlags.SetHasNoDefaultMemberToSet"/>). The host states it from the declared
/// types alone, so it holds of every path, whether or not the run-time facts reach the statement; it is reported here, at Error severity, rather than left
/// to the moment it is raised. Where the run-time facts already state it at the statement, as the <c>VBR</c> diagnostic of the run-time phase, it is not
/// reported twice.
/// </para>
/// <para>
/// One of an <c>Object</c> or a <c>Variant</c> fails, or not, by what it returns: error 424 when that is no object. Only the run-time facts can say that.
/// </para>
/// </remarks>
internal sealed class SetThroughACallAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? []).SelectMany(procedure => procedure.Expressions
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.SetHasNoDefaultMemberToSet))
            .Where(fact => !(procedure.Runtime?.Errors ?? []).Any(error => error.Location.Range.Start.Line == fact.Location.Range.Start.Line && error.ErrorId == 438))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.SetThroughACallFails,
                fact.Location.Range,
                DiagnosticSeverity.Error,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.SetThroughACallFails_NoDefaultMemberToSet_Message, NameOf(fact)))));

    // the name of what is called: the last part of the address of the symbol the callee is bound to (Module.Name)
    private static string NameOf(ExpressionFactDto fact)
        => fact.Binding?.Fragment.TrimStart('#') is { Length: > 0 } address ? address[(address.LastIndexOf('.') + 1)..] : string.Empty;
}
