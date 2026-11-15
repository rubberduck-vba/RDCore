using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00104</c>: a parameter that states neither <c>ByRef</c> nor <c>ByVal</c>, and is passed by reference (<strong>MS-VBAL §5.3.1.5</strong>).
/// </summary>
/// <remarks>
/// A <c>ParamArray</c> cannot state either, and the value parameter of a property <c>Let</c> or <c>Set</c> is always passed by value, so neither is reported.
/// Nor is a parameter of an implementation of an interface member or of a handler of an event, which is the member's or the event's
/// (<see cref="UnusedDeclarations.HasAnothersSignature"/>). Under the convention that leaves <c>ByRef</c> unwritten
/// (<see cref="DiagnosticsOptions.ByRefModifier"/>), nothing is reported: <c>RDC00314</c> reports the opposite.
/// The fix writes <c>ByRef</c> before the name, after <c>Optional</c>: the parameter is passed as it was.
/// </remarks>
/// <param name="options">The settings of the extension.</param>
internal sealed class ImplicitByRefModifierAnalyzer(IOptions<DiagnosticsOptions> options) : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitByRefModifier.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => options.Value.ByRefModifier is not ByRefConvention.Implicit && context.ParseResult.SyntaxTree is { } module
            ? module.Children
                .Where(member => member is not MemberDeclarationNode procedure || !UnusedDeclarations.HasAnothersSignature(procedure, context.Semantics))
                .SelectMany(member => member.Descendants())
                .OfType<ParameterDeclarationNode>()
                .Where(parameter => parameter is { ParameterKind: ParameterKind.ImplicitByRef, IsParamArray: false })
                .Select(parameter => new AnalyzerFinding(
                    RDCoreDiagnosticId.ImplicitByRefModifier.ToCode(),
                    parameter.Location.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitByRefModifier_Message, parameter.Name)) { Fixes = FixOf(parameter) })
            : [];

    private static DiagnosticFixes? FixOf(ParameterDeclarationNode parameter)
        => parameter.NameRange is { } name
            ? new DiagnosticFixes { Edits = [SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitByRefModifier_Fix, parameter.Name), SyntaxEdits.Insert(name.Start, "ByRef "))] }
            : null;
}
