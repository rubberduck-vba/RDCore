using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11043</c>: a <c>Let</c> gives an object to a property whose value parameter is a <c>Variant</c>. MS-VBA passes the object itself, where
/// <strong>MS-VBAL §5.4.3.8</strong> passes its default member's value.
/// </summary>
/// <remarks>
/// The analyzer finds the <c>Let</c> in the syntax tree, and reads the <c>ExpressionFactDto</c> of the property its target names (the callee of an index, or
/// the target itself): a property bound early, outside the procedure the <c>Let</c> is in, whose <c>Property Let</c> takes a <c>Variant</c>
/// (<c>LetValueTypeKind</c>). The value's fact is of a class or <c>Object</c>, not an array. A value declared <c>Variant</c> is not reported: whether it holds
/// an object is known only when it runs.
/// </remarks>
internal sealed class ObjectLetToVariantPropertyAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => ObjectLetsToProperties.Find(context, kind => kind is DeclaredTypeKind.Variant)
            .Select(target => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectLetToVariantProperty.ToCode(),
                target.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObjectLetToVariantProperty_Message, SymbolUri.NameOf(target.Binding!))));
}
