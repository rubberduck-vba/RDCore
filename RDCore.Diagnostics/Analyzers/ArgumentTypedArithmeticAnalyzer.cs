using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11018</c>: an addition, a subtraction or a multiplication of <c>Byte</c>, <c>Integer</c> or <c>Long</c> values, an operand of which is <c>Abs</c>,
/// <c>Int</c> or <c>Fix</c> of a number, can overflow where <strong>MS-VBAL</strong>'s <c>Variant</c> operation would widen.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §6.1.2.10.1.1</strong>, <strong>§6.1.2.3.1.16</strong> and <strong>§6.1.2.3.1.18</strong> declare <c>Abs</c>, <c>Fix</c> and <c>Int</c>
/// <c>As Variant</c>; MS-VBA types a call of one of a number as the number's type, and RD-VBA does as MS-VBA does
/// (<see cref="RDCore.SDK.Semantics.Static.ArgumentTypedFunctions"/>): <c>Fix(n) * 7000</c> of an <c>Integer</c> raises error 6, where a <c>Variant</c> product
/// would be a <c>Long</c>. A warning, because the program that relies on MS-VBAL's typing overflows. The host states it from the declared types
/// (<see cref="ValueExpressionSemanticFlags.OverflowsWhereVariantWidens"/>).
/// </remarks>
internal sealed class ArgumentTypedArithmeticAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? []).SelectMany(procedure => procedure.Expressions
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.OverflowsWhereVariantWidens))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.ArgumentTypedArithmeticOverflow,
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ArgumentTypedArithmeticOverflow_Message, fact.DeclaredType ?? string.Empty))));
}
