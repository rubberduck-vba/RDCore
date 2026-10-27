using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11019</c>: a reserved function of the library written with a type character that does not match its declared type - <c>Len$</c>, <c>CVar$</c>,
/// <c>Abs%</c> - which <strong>MS-VBAL §5.6.10</strong> makes invalid, and <strong>MS-VBA</strong> compiles, the character ignored.
/// </summary>
/// <remarks>
/// RD-VBA does as MS-VBA does (recorded in Excel: oracle\s46 F18, oracle\s44 D01-D18): <c>Len$("abc")</c> is <c>3</c>, a <c>Long</c>. A warning, because the
/// specification refuses the name, and the character says a type the call does not have. The host states it from the declarations
/// (<see cref="ValueExpressionSemanticFlags.IgnoredTypeCharacter"/>).
/// </remarks>
internal sealed class IgnoredTypeCharacterAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? []).SelectMany(procedure => procedure.Expressions
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.IgnoredTypeCharacter))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.IgnoredTypeCharacter,
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.IgnoredTypeCharacter_Message, NameOf(fact), fact.DeclaredType ?? string.Empty))));

    // the name of the function: the last part of the address of the symbol the name is bound to (Module.Name).
    private static string NameOf(SDK.Platform.Protocol.ExpressionFactDto fact)
        => fact.Binding?.Fragment.TrimStart('#') is { Length: > 0 } address ? address[(address.LastIndexOf('.') + 1)..] : string.Empty;
}
