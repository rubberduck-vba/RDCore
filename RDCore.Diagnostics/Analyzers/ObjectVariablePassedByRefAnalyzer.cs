using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11026</c>: a public variable of an object, named as a member of it (<c>b.Count</c>, <c>Me.Count</c>), or an element of one (<c>b.V(0)</c>), given to
/// a <c>ByRef</c> parameter of a procedure, a <c>ParamArray</c>, a <c>Declare</c>d procedure or a raised event, which <strong>MS-VBAL §5.6.12</strong> and
/// <strong>§5.3.1.11</strong> pass by reference and MS-VBA passes as a copy.
/// </summary>
/// <remarks>
/// The analyzer reads the argument off the argument facts and the syntax tree. It is given to a parameter that takes it by reference, of a procedure that is
/// not a library's; and it is a member access whose owner (or the target of the enclosing <c>With</c> block) is of a class and which is classified as a variable,
/// or an element of such a variable declared <c>Variant</c>. RD-VBA passes the copy, as MS-VBA does (RD-VBAL §5.3.1.11): what the procedure assigns to its
/// parameter is lost, where the specification has it written to the object's variable. A warning, for code that relies on what the specification says. The
/// same copy given to a procedure called late is RDC11029's.
/// </remarks>
internal sealed class ObjectVariablePassedByRefAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (ObjectVariableArguments.Of(context)?.CopiesGivenByRef() ?? [])
            .Select(range => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectVariablePassedByRefIsACopy.ToCode(), range, DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.ObjectVariablePassedByRefIsACopy_Message));
}
