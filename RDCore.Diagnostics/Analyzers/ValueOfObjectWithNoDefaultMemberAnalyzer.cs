using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11015</c>: an object of a class that has no default member is where a value is expected, which MS-VBA compiles and <strong>MS-VBAL §5.5.1.1</strong>
/// makes statically invalid.
/// </summary>
/// <remarks>
/// The analyzer reads the expression facts: a value that is Let-coerced (the value of a <c>Let</c> assignment, an operand of an operator other than <c>Is</c>,
/// an argument of a parameter declared with a value type), of a class, not an array, that has no default member. RD-VBA runs it as MS-VBA does (error 438,
/// <em>Object doesn't support this property or method</em>, when it runs; 91 when the object is <c>Nothing</c>), and this warning stands in for the type mismatch the
/// specification makes it at compile time.
/// </remarks>
internal sealed class ValueOfObjectWithNoDefaultMemberAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? []).SelectMany(procedure => procedure.Expressions
            .Where(fact => fact is { IsLetCoerced: true, TypeKind: DeclaredTypeKind.Class, IsArray: false, DefaultMember: null })
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.ValueOfObjectWithNoDefaultMember.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ValueOfObjectWithNoDefaultMember_Message, fact.DeclaredType ?? string.Empty))));
}
