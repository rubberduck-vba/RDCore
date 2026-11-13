using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01011</c>: a variable whose value nothing reads: nothing refers to it, or only to assign it.
/// </summary>
/// <remarks>
/// The host counts the references of a local and of a <c>Private</c> variable of the module (<see cref="SDK.Platform.Protocol.DeclarationFactDto.References"/>);
/// another variable may be read by code the pass does not see, and is not reported. A <c>WithEvents</c> variable is read by the events it handles, and the
/// control variable of a <c>For</c> loop by the loop.
/// </remarks>
internal sealed class VariableNotUsedAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        foreach (var (fact, references) in UnusedDeclarations.Counted(semantics, DeclarationKind.Variable).Where(counted => counted.References is { Reads: 0, PassedAsArguments: 0 }))
        {
            var declaration = UnusedDeclarations.DeclarationOf<VariableDeclarationNode>(module, fact, node => node.NameRange ?? node.SourceLocation.Range);
            if (declaration is { IsWithEvents: true }
                || UnusedDeclarations.ProcedureAt(module, fact.Location.Range.Start) is { } procedure && UnusedDeclarations.IsLoopControl(procedure, fact.Name))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.VariableNotUsed.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(references.Writes > 0 ? RDCoreDiagnosticsResources.VariableNotUsed_Assigned_Message : RDCoreDiagnosticsResources.VariableNotUsed_Message, fact.Name));
        }
    }
}

/// <summary>
/// <c>RDC01012</c>: a variable that is read and never assigned, nor passed as an argument: it always has the value it is initialized with, the default value of
/// its declared type (<strong>MS-VBAL §5.2.3.1.4</strong>).
/// </summary>
/// <remarks>
/// An array, a <c>ReDim</c> of the variable, a <c>WithEvents</c> variable, and a variable of a type that is no intrinsic value type (an object, a user-defined
/// type) are left out: such a variable is given what it holds otherwise than by an assignment. The host counts the references of a local and of a
/// <c>Private</c> variable of the module.
/// </remarks>
internal sealed class VariableNotAssignedAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        var redimensioned = module.Descendants().OfType<RedimDeclarationNode>()
            .Select(redim => redim.Target is SimpleNameExpressionNode name ? name.IdentifierName : null)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Variable)
            .Where(counted => counted.References is { Writes: 0, PassedAsArguments: 0, Reads: > 0 } && !redimensioned.Contains(counted.Fact.Name)))
        {
            if (UnusedDeclarations.DeclarationOf<VariableDeclarationNode>(module, fact, node => node.NameRange ?? node.SourceLocation.Range) is not { IsWithEvents: false } declaration
                || declaration.Children.OfType<ArrayBoundsNode>().Any()
                || !IsOfAnIntrinsicValueType(declaration))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.VariableNotAssigned.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.VariableNotAssigned_Message, fact.Name));
        }
    }

    // no As clause, or one that names an intrinsic type other than Object, unqualified or of the VBA library.
    private static bool IsOfAnIntrinsicValueType(VariableDeclarationNode declaration)
        => declaration.Children.OfType<AsTypeExpressionNode>().FirstOrDefault() is not { } asType
            || !asType.IsArrayDef
                && (asType.QualifierName is null || string.Equals(asType.QualifierName, "VBA", StringComparison.OrdinalIgnoreCase))
                && IntrinsicVBTypes.IsIntrinsic(asType.TypeName)
                && !string.Equals(asType.TypeName, VBTypeNames.VBObject, StringComparison.OrdinalIgnoreCase);
}
