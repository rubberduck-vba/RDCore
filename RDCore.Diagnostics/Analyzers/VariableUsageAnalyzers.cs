using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01011</c>: a variable whose value nothing reads: nothing refers to it, or only to assign it.
/// </summary>
/// <remarks>
/// The host counts the references of a local, of a <c>Private</c> variable of the module, and of a <c>Public</c> one that only the project's code can refer
/// to: of an <c>Option Private Module</c> module, or of a program (<see cref="SDK.Platform.Protocol.DeclarationFactDto.References"/>). Another variable
/// may be read by code the pass does not see, and is not reported. A <c>WithEvents</c> variable is read by the events it handles, and the
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
/// The variable is judged by the kind of its declared type (<see cref="DeclarationFactDto.TypeKind"/>). A dynamic array, a <c>ReDim</c> of the variable, a
/// <c>WithEvents</c> variable, a variable declared <c>As New</c>, a variable of a user-defined type and one whose type does not resolve are left out: such a
/// variable is given what it holds otherwise than by an assignment (<c>As New</c>, its members), or is not known. A variable of a class or declared
/// <c>As Object</c> is judged: it is <c>Nothing</c> until a <c>Set</c> assigns it. So is one of an <c>Enum</c>, a <c>Long</c> that is <c>0</c>
/// (<strong>MS-VBAL §5.2.3.4</strong>). A fixed-size array is judged by its elements' type: an assignment of one of its elements is a write of the array. The
/// host counts the references of the variables that <see cref="VariableNotUsedAnalyzer"/> reads.
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

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Variable)
            .Where(counted => counted.References is { Writes: 0, PassedAsArguments: 0, Reads: > 0 }
                && !givenOtherwise.Contains(counted.Fact.Symbol.ToString()) && !redimensionedNames.Contains(counted.Fact.Name)))
        {
            if (UnusedDeclarations.DeclarationOf<VariableDeclarationNode>(module, fact, node => node.NameRange ?? node.SourceLocation.Range) is not { IsWithEvents: false } declaration
                || declaration.Children.OfType<ArrayBoundsNode>().Any(bounds => bounds.IsResizable)
                || declaration.Children.OfType<AsTypeExpressionNode>().Any(asType => asType.IsArrayDef)
                || !IsGivenItsValueByAnAssignment(fact))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.VariableNotAssigned.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.VariableNotAssigned_Message, fact.Name))
            {
                Fixes = FixOf(context.ParseResult, module, fact, declaration) is { } fix ? new DiagnosticFixes { Edits = [fix] } : null,
            };
        }
    }

    // the local assigned the default value of its type on a line of its own after its declaration: nothing else assigns it, so it holds that value wherever
    // it is read, and the code does what it did. Not offered for a field of the module, which no one procedure starts with; for a fixed-length string, whose
    // default is made of null characters; for an array or an object, which no Let of a literal assigns; nor for a declaration with no As clause or type
    // character in a module whose DefType statements may type it.
    private static EditFix? FixOf(ModuleParseResult parse, ModuleNode module, DeclarationFactDto fact, VariableDeclarationNode declaration)
    {
        if (UnusedDeclarations.ProcedureAt(module, fact.Location.Range.Start) is not { } procedure
            || DefaultOf(declaration, module.Descendants().OfType<TypeDefDirectiveNode>().Any()) is not { } value
            || procedure.Descendants().Prepend(procedure).FirstOrDefault(node => !node.Children.IsDefaultOrEmpty && node.Children.Contains(declaration)) is not { } parent
            || DeclarationStatements.In(parent.Children).FirstOrDefault(declarations => declarations.Contains(declaration)) is not { } declarations)
        {
            return null;
        }

        var statement = DeclarationStatements.RangeOf(declarations);
        if (statement.End.Line >= procedure.SourceLocation.Range.End.Line)
        {
            return null;
        }

        // the assignment is written at the indentation of the declaration.
        var indent = new string(' ', statement.Start.Character);
        return SyntaxEdits.Fix(
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.VariableNotAssigned_Fix, fact.Name, value),
            LineEdits.InsertLinesAfter(parse, statement, $"{indent}{fact.Name} = {value}"));
    }

    // the literal of the default value of the type the declaration states, with its As clause or its type character.
    private static string? DefaultOf(VariableDeclarationNode declaration, bool hasDefType)
    {
        if (declaration.Children.OfType<ArrayBoundsNode>().Any())
        {
            return null;
        }

        if (declaration.Children.OfType<AsTypeExpressionNode>().FirstOrDefault() is { } asType)
        {
            return asType.FixedLength is null
                && !string.Equals(asType.TypeName, VBTypeNames.VBObject, StringComparison.OrdinalIgnoreCase)
                && IntrinsicVBTypes.TryResolve(asType.TypeName, out var declared)
                    ? IntrinsicVBTypes.DefaultLiteralOf(declared)
                    : null;
        }

        return declaration.TypeHint switch
        {
            { } hint => IntrinsicVBTypes.TryResolveTypeHint(hint, out var hinted) ? IntrinsicVBTypes.DefaultLiteralOf(hinted) : null,
            null when !hasDefType => IntrinsicVBTypes.DefaultLiteralOf(VBVariantType.TypeInfo),
            _ => null,
        };
    }

    // a variable, or the elements of an array, that only an assignment gives a value: of an intrinsic type, Variant, Object, an Enum, or a class it is not
    // declared As New of (MS-VBAL §5.2.3.1.4). A user-defined type is written through its members, and a type that did not resolve is not known.
    private static bool IsGivenItsValueByAnAssignment(DeclarationFactDto fact)
        => fact.TypeKind is DeclaredTypeKind.Intrinsic or DeclaredTypeKind.Variant or DeclaredTypeKind.Object or DeclaredTypeKind.Enum
            || fact.TypeKind is DeclaredTypeKind.Class && fact.AutoInstantiates is null;
}
