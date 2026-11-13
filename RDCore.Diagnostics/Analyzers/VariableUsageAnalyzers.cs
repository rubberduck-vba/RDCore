using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01011</c>: a variable whose value nothing reads: nothing refers to it, or only to assign it.
/// </summary>
/// <remarks>
/// The host counts the references of a local and of a <c>Private</c> variable of the module (<see cref="SDK.Platform.Protocol.DeclarationFactDto.References"/>);
/// another variable may be read by code the pass does not see, and is not reported. A <c>WithEvents</c> variable is read by the events it handles, and the
/// control variable of a <c>For</c> loop by the loop. A variable nothing refers to comes with the fix that removes its declaration, unless a <c>ReDim</c> names
/// it: without the declaration, the <c>ReDim</c> would declare an array of its own.
/// </remarks>
internal sealed class VariableNotUsedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.VariableNotUsed.ToCode()];

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
                AnalyzerMessages.Format(references.Writes > 0 ? RDCoreDiagnosticsResources.VariableNotUsed_Assigned_Message : RDCoreDiagnosticsResources.VariableNotUsed_Message, fact.Name))
            {
                Fixes = references.Writes == 0 && declaration is not null && !redimensioned.Contains(fact.Name)
                    && UnusedDeclarations.RemovalOf(context.ParseResult, declaration, AnalyzerMessages.Format(RDCoreDiagnosticsResources.VariableNotUsed_Fix, fact.Name)) is { } fix
                    ? new DiagnosticFixes { Edits = [fix] }
                    : null,
            };
        }
    }
}

/// <summary>
/// <c>RDC01012</c>: a variable that is read and never assigned, nor passed as an argument: it always has the value it is initialized with, the default value of
/// its declared type (<strong>MS-VBAL §5.2.3.1.4</strong>).
/// </summary>
/// <remarks>
/// A dynamic array, a <c>ReDim</c> of the variable, a <c>WithEvents</c> variable, and a variable of a class or a user-defined type are left out: such a variable is
/// given what it holds otherwise than by an assignment (<c>As New</c>, its members). A variable declared <c>As Object</c> is judged: it is <c>Nothing</c> until
/// a <c>Set</c> assigns it. A fixed-size array is judged: an assignment of one of its elements is a write of the array. The host counts the references of a
/// local and of a <c>Private</c> variable of the module.
/// </remarks>
internal sealed class VariableNotAssignedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.VariableNotAssigned.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        // the variables a ReDim names, by the symbol its target is bound to: a variable of another procedure with the same name is another variable. A target
        // that is bound to nothing leaves out every variable of its name.
        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
        var givenOtherwise = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var redimensionedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var redim in module.Descendants().OfType<RedimDeclarationNode>())
        {
            if (facts.TryGetValue(redim.Target.Identity, out var target) && target.Binding is { } binding)
            {
                givenOtherwise.Add(binding.ToString());
            }
            else if (redim.Target is SimpleNameExpressionNode name)
            {
                redimensionedNames.Add(name.IdentifierName);
            }
        }

        // an element of an array passed where the procedure may take it by reference: the argument is the element, which no declaration is bound to, and the
        // procedure may assign it.
        foreach (var element in module.Descendants().OfType<IndexExpressionNode>())
        {
            if (facts.TryGetValue(element.Identity, out var passed) && passed.Flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument)
                && facts.TryGetValue(element.Callee.Identity, out var array) && array.Binding is { } binding)
            {
                givenOtherwise.Add(binding.ToString());
            }
        }

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Variable)
            .Where(counted => counted.References is { Writes: 0, PassedAsArguments: 0, Reads: > 0 }
                && !givenOtherwise.Contains(counted.Fact.Symbol.ToString()) && !redimensionedNames.Contains(counted.Fact.Name)))
        {
            if (UnusedDeclarations.DeclarationOf<VariableDeclarationNode>(module, fact, node => node.NameRange ?? node.SourceLocation.Range) is not { IsWithEvents: false } declaration
                || declaration.Children.OfType<ArrayBoundsNode>().Any(bounds => bounds.IsResizable)
                || !IsOfAnIntrinsicType(declaration))
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

    // no As clause, or one that names an intrinsic type, Object included, unqualified or of the VBA library: no user-defined type has such a name. A class
    // can't be told from a user-defined type by its name alone, so neither is judged.
    private static bool IsOfAnIntrinsicType(VariableDeclarationNode declaration)
        => declaration.Children.OfType<AsTypeExpressionNode>().FirstOrDefault() is not { } asType
            || !asType.IsArrayDef
                && (asType.QualifierName is null || string.Equals(asType.QualifierName, "VBA", StringComparison.OrdinalIgnoreCase))
                && IntrinsicVBTypes.IsIntrinsic(asType.TypeName);
}
