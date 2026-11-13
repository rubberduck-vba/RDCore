using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00214</c>: a <c>Sub</c> that gives its result back through a parameter: its only <c>ByRef</c> parameter is assigned in its body
/// (<strong>MS-VBAL §5.3.1.5</strong>). A <c>Function</c> returns a value (<strong>MS-VBAL §5.3.1.6</strong>), which says so at the call.
/// </summary>
/// <remarks>
/// A <c>ParamArray</c> is not counted as the <c>ByRef</c> parameter. A procedure whose signature is not its own to choose is left out
/// (<see cref="UnusedDeclarations.ChoosesItsSignature"/>). The host's reference counts tell that the parameter is assigned.
/// </remarks>
internal sealed class ProcedureCanBeWrittenAsFunctionAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        var assigned = UnusedDeclarations.Counted(semantics, DeclarationKind.Parameter)
            .Where(counted => counted.References.Writes > 0)
            .Select(counted => UnusedDeclarations.ParameterOf(module, counted.Fact))
            .OfType<ParameterDeclarationNode>()
            .ToHashSet();

        foreach (var procedure in module.Children.OfType<MemberDeclarationNode>().Where(member => member.MemberKind is MemberKind.Procedure))
        {
            var byRef = procedure.HeaderChildren.OfType<ParameterDeclarationNode>()
                .Where(parameter => parameter is { ParameterKind: ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef, IsParamArray: false })
                .ToList();
            if (byRef is [var parameter] && assigned.Contains(parameter) && UnusedDeclarations.ChoosesItsSignature(procedure, semantics))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.ProcedureCanBeWrittenAsFunction.ToCode(),
                    procedure.NameRange ?? procedure.SourceLocation.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.ProcedureCanBeWrittenAsFunction_Message, procedure.Name, parameter.Name));
            }
        }
    }
}

/// <summary>
/// <c>RDC00215</c>: a variable declared <c>As New</c>, an automatic instantiation variable (<strong>MS-VBAL §2.5.1</strong>): its object is created when the
/// variable is first used, and created again when it is used after it was set to <c>Nothing</c>.
/// </summary>
internal sealed class SelfAssignedDeclarationAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<VariableDeclarationNode>()
            .Where(variable => variable.Children.OfType<AsTypeExpressionNode>().Any(type => type.AsAutoObject))
            .Select(variable => new AnalyzerFinding(
                RDCoreDiagnosticId.SelfAssignedDeclaration.ToCode(),
                variable.NameRange ?? variable.SourceLocation.Range,
                DiagnosticSeverity.Hint,
                Say(RDCoreDiagnosticsResources.SelfAssignedDeclaration_Message, variable.Name)));
}

/// <summary>
/// <c>RDC00409</c>: a <c>Public</c> variable of a class module (<strong>MS-VBAL §5.2.3.1</strong>), a member of its objects that any code holding one can
/// assign, with nothing of the class to check the value.
/// </summary>
/// <remarks>
/// A class module is told by the host (<c>ModuleSemanticsDto.Class</c>). A variable that states no access modifier is <c>Private</c>.
/// </remarks>
internal sealed class EncapsulatePublicFieldAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context is { ParseResult.SyntaxTree: { } module, Semantics.Class: not null }
            ? module.Children.OfType<VariableDeclarationNode>()
                .Where(variable => variable.AccessModifier is AccessModifier.Public or AccessModifier.Global)
                .Select(variable => new AnalyzerFinding(
                    RDCoreDiagnosticId.EncapsulatePublicField.ToCode(),
                    variable.NameRange ?? variable.SourceLocation.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.EncapsulatePublicField_Message, variable.Name)))
            : [];
}
