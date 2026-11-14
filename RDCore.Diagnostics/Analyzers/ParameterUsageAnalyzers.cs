using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01013</c>: a parameter that nothing in its procedure refers to.
/// </summary>
/// <remarks>
/// A procedure whose signature is not its own to choose is left out: an implementation of an interface member, a handler of an event, a <c>Declare</c>, an
/// event, and a <c>Public</c> member of a class module, which may be a member of an interface (<strong>MS-VBAL §5.2.4.2</strong>).
/// </remarks>
internal sealed class ParameterNotUsedAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Parameter).Where(counted => counted.References.Total == 0))
        {
            // the implicit Me of a class member is a parameter fact too, with no declaration in the source.
            if (UnusedDeclarations.ParameterOf(module, fact) is not { } parameter
                || UnusedDeclarations.ProcedureAt(module, fact.Location.Range.Start) is not { } procedure
                || !UnusedDeclarations.ChoosesItsSignature(procedure, semantics))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.ParameterNotUsed.ToCode(),
                parameter.NameRange ?? fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ParameterNotUsed_Message, fact.Name));
        }
    }
}

/// <summary>
/// <c>RDC01014</c>: a parameter passed by value that its procedure assigns (<strong>MS-VBAL §5.3.1.5</strong>): the assignment changes the procedure's own
/// copy, and the caller's argument does not change.
/// </summary>
/// <remarks>
/// The value parameter of a property <c>Let</c> or <c>Set</c> is passed by value whatever it states (<strong>MS-VBAL §5.3.1.7</strong>), and is reported too.
/// </remarks>
internal sealed class AssignedByValParameterAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Parameter).Where(counted => counted.References.Writes > 0))
        {
            if (UnusedDeclarations.ParameterOf(module, fact) is { } parameter && (parameter.ParameterKind is ParameterKind.ExplicitByVal || parameter.IsByRefIgnored))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.AssignedByValParameter.ToCode(),
                    parameter.NameRange ?? fact.Location.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.AssignedByValParameter_Message, fact.Name));
            }
        }
    }
}
