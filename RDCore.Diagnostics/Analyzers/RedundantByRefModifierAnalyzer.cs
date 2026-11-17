using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00314</c>: a parameter declared <c>ByRef</c>, which a parameter that states neither is (<strong>MS-VBAL §5.3.1.5</strong>), under the convention that
/// leaves it unwritten (<see cref="DiagnosticsOptions.ByRefModifier"/>).
/// </summary>
/// <remarks>
/// A <c>Declare</c> states how each argument goes to the DLL, and an implementation of an interface member or a handler of an event repeats the declaration it
/// matches: their parameters are not reported. Which procedures those are is a fact the host vouches for, so the analyzer that is not given it has nothing to say.
/// The value parameter of a property <c>Let</c> or <c>Set</c> is always passed by value, and its <c>ByRef</c> is <c>RDC00205</c>.
/// </remarks>
/// <param name="options">The settings of the extension.</param>
internal sealed class RedundantByRefModifierAnalyzer(IOptions<DiagnosticsOptions> options) : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.RedundantByRefModifier.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (options.Value.ByRefModifier is not ByRefConvention.Implicit || context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        foreach (var member in module.Children.OfType<MemberDeclarationNode>().Where(member => member is not ExternalMemberDeclarationNode && !Matches(member, semantics)))
        {
            foreach (var parameter in member.Children.OfType<ParameterDeclarationNode>()
                .Where(parameter => parameter is { ParameterKind: ParameterKind.ExplicitByRef, IsParamArray: false, IsByRefIgnored: false }))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.RedundantByRefModifier.ToCode(),
                    parameter.Location.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.RedundantByRefModifier_Message, parameter.Name))
                {
                    Fixes = FixOf(parameter),
                };
            }
        }
    }

    // the ByRef and what is written between it and the name: the parameter is passed by reference without it.
    private static DiagnosticFixes? FixOf(ParameterDeclarationNode parameter)
        => parameter is { PassingRange.Start: var keyword, NameRange.Start: var name }
            ? new DiagnosticFixes
            {
                Edits = [SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.RedundantByRefModifier_Fix, parameter.Name), SyntaxEdits.Remove(new SourceRange(keyword, name)))],
            }
            : null;

    // an implementation of an interface member, or a handler of an event: its declaration matches another's.
    private static bool Matches(MemberDeclarationNode member, ModuleSemanticsDto semantics)
        => semantics.Declarations.Any(declaration => declaration is { Role: DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler }
            && string.Equals(declaration.Name, member.Name, StringComparison.OrdinalIgnoreCase));
}
