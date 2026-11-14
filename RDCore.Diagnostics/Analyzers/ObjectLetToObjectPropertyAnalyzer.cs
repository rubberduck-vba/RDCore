using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11047</c>: a <c>Let</c> gives an object to a property whose value parameter is a class or <c>Object</c>. MS-VBA passes the object itself, as a
/// <c>Set</c> would, where <strong>MS-VBAL §5.5.1.1</strong> makes the assignment a type mismatch, and <strong>§5.4.3.8</strong> with
/// <strong>§5.5.1.2.13</strong> evaluates the object's default member (error 424, 91 for <c>Nothing</c>).
/// </summary>
/// <remarks>
/// The analyzer finds the <c>Let</c> in the syntax tree, and reads the <c>ExpressionFactDto</c> of the property its target names (the callee of an index, or
/// the target itself): a property bound early, outside the procedure the <c>Let</c> is in, whose <c>Property Let</c> takes a class or <c>Object</c>
/// (<c>LetValueTypeKind</c>). The value's fact is of a class or <c>Object</c>, not an array. A value declared <c>Variant</c> is not reported: whether it holds
/// an object is known only when it runs.
/// </remarks>
internal sealed class ObjectLetToObjectPropertyAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => ObjectLetsToProperties.Find(context, kind => kind is DeclaredTypeKind.Class or DeclaredTypeKind.Object)
            .Select(target => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectLetToObjectProperty.ToCode(),
                target.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObjectLetToObjectProperty_Message, SymbolUri.NameOf(target.Binding!))));
}
