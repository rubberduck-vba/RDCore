using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00112</c>: a value is converted implicitly into a type that holds less of it (<strong>MS-VBAL §5.5.1.2</strong>).
/// </summary>
/// <remarks>
/// That the code asks for the conversion is a fact the language core states about it, with the types on both sides and what the conversion does to the value
/// (<see cref="RDCore.SDK.Platform.Protocol.ConversionFactDto"/>); the analyzer reads it, and does not work out the types. A conversion that is written, with a
/// conversion function or the explicit let-coercion operator, is the author saying so and is not reported. Nothing is reported for a procedure that was not evaluated.
/// <para>
/// A value that is known - a literal, or a constant - and that fits the type it is converted to without losing anything is not reported either: <c>b = 10</c> into a
/// <c>Byte</c> needs no conversion function to say it is meant. One that is known and loses its fraction is. One that is known and cannot fit at all is not a
/// warning: the conversion raises a run-time error whenever it runs, which the language reports as such (<c>VBR00006</c>, from
/// <see cref="RDCore.SDK.Platform.Protocol.RuntimeFactsDto.Errors"/>).
/// </para>
/// </remarks>
internal sealed class ImplicitNarrowingConversionAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitNarrowingConversion.ToCode()];

    private const ConversionSemanticFlags Reported = ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Narrowing;

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Conversions ?? [])
            .Where(conversion => conversion.Flags.HasFlag(Reported) && !conversion.Flags.HasFlag(ConversionSemanticFlags.Explicit))
            .Where(conversion => conversion.Error is null && (!conversion.IsValueKnown || conversion.Flags.HasFlag(ConversionSemanticFlags.Lossy)))
            .Select(conversion => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitNarrowingConversion.ToCode(),
                conversion.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    conversion.Flags.HasFlag(ConversionSemanticFlags.Lossy)
                        ? RDCoreDiagnosticsResources.ImplicitNarrowingConversion_Lossy_Message
                        : RDCoreDiagnosticsResources.ImplicitNarrowingConversion_Message,
                    conversion.Source,
                    conversion.Destination)));
}
