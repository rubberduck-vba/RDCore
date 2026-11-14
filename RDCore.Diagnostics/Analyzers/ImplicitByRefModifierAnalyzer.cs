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
/// (<see cref="UnusedDeclarations.HasAnothersSignature"/>).
/// </remarks>
internal sealed class ImplicitByRefModifierAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.ParseResult.SyntaxTree is { } module
            ? module.Children
                .Where(member => member is not MemberDeclarationNode procedure || !UnusedDeclarations.HasAnothersSignature(procedure, context.Semantics))
                .SelectMany(member => member.Descendants())
                .OfType<ParameterDeclarationNode>()
                .Where(parameter => parameter is { ParameterKind: ParameterKind.ImplicitByRef, IsParamArray: false })
                .Select(parameter => new AnalyzerFinding(
                    RDCoreDiagnosticId.ImplicitByRefModifier.ToCode(),
                    parameter.Location.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitByRefModifier_Message, parameter.Name)))
            : [];
}
