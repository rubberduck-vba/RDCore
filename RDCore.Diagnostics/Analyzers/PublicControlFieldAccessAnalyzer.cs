using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00412</c>: code outside a form uses one of the form's controls (<c>UserForm1.TextBox1.Text</c> in a standard module). A control is a public member of
/// the form's class, so any module can reach it; the form's own code is what should read and change its controls.
/// </summary>
/// <remarks>
/// The host states that a binding is a control (<see cref="ValueExpressionSemanticFlags.BoundToDesignerControl"/>): the designer declares it, and the code
/// has no declaration of it. A control reached through a form object (<c>f.TextBox1</c>) is found the same way as one reached through the form's name.
/// </remarks>
internal sealed class PublicControlFieldAccessAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.PublicControlFieldAccess.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        var module = SymbolUri.ModuleNameOf(semantics.Module);
        return semantics.Procedures
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding is { } binding && fact.Flags.HasFlag(ValueExpressionSemanticFlags.BoundToDesignerControl)
                && !string.Equals(SymbolUri.ModuleNameOf(binding), module, StringComparison.OrdinalIgnoreCase))
            .GroupBy(fact => (Binding: fact.Binding!.AbsoluteUri, fact.Location.Range.Start))
            .Select(access => access.MinBy(fact => fact.Location.Range.End)!)
            .OrderBy(access => access.Location.Range.Start)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.PublicControlFieldAccess.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.PublicControlFieldAccess_Message,
                    SymbolUri.NameOf(access.Binding!), SymbolUri.ModuleNameOf(access.Binding!))));
    }
}
