using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01021</c>: a read of a local variable that nothing assigns before it, on any code path: the read always finds the default value of the variable's
/// type (<strong>MS-VBAL §2.3</strong>), which is likely not what the code means.
/// </summary>
/// <remarks>
/// The host states the read (<see cref="ProcedureSemanticsDto.InitialValueReads"/>, <see cref="RDCore.SDK.Semantics.Flow.InitialValueReadFact.IsOnlyValue"/>).
/// An array, a user-defined type and a variable declared <c>As New</c> are not reported: an element or a field is assigned on its own, and the first use of an
/// <c>As New</c> variable creates its object. Neither is a read in the argument of <c>Len</c> or <c>LenB</c>, which is how the size of a type is asked.
/// </remarks>
internal sealed class UnassignedVariableUsageAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.UnassignedVariableUsage.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        var sized = module.Descendants().OfType<IndexExpressionNode>()
            .Where(call => call.Callee is SimpleNameExpressionNode { IdentifierName: var name }
                && (string.Equals(name, "Len", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "LenB", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(call => call.Arguments.SelectMany(argument => (IEnumerable<SyntaxNode>)[argument, .. argument.Descendants()]))
            .Select(node => node.SourceLocation.Range)
            .ToHashSet();
        foreach (var read in semantics.Procedures.SelectMany(procedure => procedure.InitialValueReads ?? []).Where(read => read.IsOnlyValue))
        {
            if (VariableFlow.DeclarationOf(semantics, read.Variable)
                    is not { IsArray: false, AutoInstantiates: null, TypeKind: not DeclaredTypeKind.UserDefinedType }
                || sized.Contains(read.Location.Range))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.UnassignedVariableUsage.ToCode(),
                read.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.UnassignedVariableUsage_Message, read.Name));
        }
    }
}

/// <summary>
/// <c>RDC01022</c>: an assignment of a local variable or a parameter whose value no code path reads: it is replaced, or the procedure ends, first.
/// </summary>
/// <remarks>
/// The host states the assignment (<see cref="ProcedureSemanticsDto.UnreadAssignments"/>). A <c>Set</c> to <c>Nothing</c> is not reported: it releases the
/// object where the code says so. Neither is an assignment to an array.
/// </remarks>
internal sealed class AssignmentNotUsedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.AssignmentNotUsed.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        var releases = module.Descendants().OfType<AssignmentStatementNode>()
            .Where(assignment => assignment is { Kind: AssignmentKind.Set } && IsNothing(assignment.Value))
            .Select(assignment => assignment.Target.SourceLocation.Range)
            .ToArray();
        foreach (var assignment in semantics.Procedures.SelectMany(procedure => procedure.UnreadAssignments ?? []))
        {
            if (VariableFlow.DeclarationOf(semantics, assignment.Variable) is not { IsArray: false } declaration
                || declaration is not ({ Kind: DeclarationKind.Variable, DeclaredBy: not null } or { Kind: DeclarationKind.Parameter })
                || releases.Any(target => VariableFlow.Contains(target, assignment.Location.Range)))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.AssignmentNotUsed.ToCode(),
                assignment.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.AssignmentNotUsed_Message, assignment.Name));
        }
    }

    private static bool IsNothing(ExpressionNode value) => value switch
    {
        LiteralExpressionNode { StaticValue: VBNothingValue } => true,
        SimpleNameExpressionNode name => string.Equals(name.IdentifierName, "Nothing", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}

/// <summary>
/// <c>RDC01023</c>: a parameter passed by reference that its procedure neither assigns nor passes on to a procedure that may assign it
/// (<strong>MS-VBAL §5.3.1.5</strong>): passed by value, it does the same, and the caller's variable is plainly the caller's.
/// </summary>
/// <remarks>
/// <para>
/// The host counts the parameter's references (<see cref="DeclarationFactDto.References"/>): no write, and no argument of a call that may assign it. An
/// array, a <c>ParamArray</c> and a user-defined type are passed by reference only, and are not reported; neither is the value parameter of a property
/// <c>Let</c> or <c>Set</c>, which is passed by value whatever it states.
/// </para>
/// <para>
/// A procedure whose signature is another's (an implementation, a handler, a <c>Declare</c>) is not reported. A parameter of an interface member or of an
/// event is reported when the host states that no member bound to it writes its own (<see cref="DeclarationFactDto.IsWrittenByABoundMember"/>). The fix
/// writes <c>ByVal</c> in place of <c>ByRef</c>, or before the name.
/// </para>
/// </remarks>
internal sealed class ParameterCanBeByValAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ParameterCanBeByVal.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Parameter)
            .Where(counted => counted.References is { Writes: 0, PassedAsArguments: 0 }
                && counted.Fact is { IsArray: false, TypeKind: not (null or DeclaredTypeKind.UserDefinedType) }))
        {
            if (UnusedDeclarations.ParameterOf(module, fact) is not { } parameter
                || parameter is not
                {
                    ParameterKind: ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef, IsParamArray: false, IsArray: false, IsByRefIgnored: false,
                }
                || UnusedDeclarations.ProcedureAt(module, fact.Location.Range.Start) is not { } procedure
                || !(UnusedDeclarations.ChoosesItsSignature(procedure, semantics) || IsUnwrittenByItsBoundMembers(procedure, fact, semantics)))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.ParameterCanBeByVal.ToCode(),
                parameter.NameRange ?? fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ParameterCanBeByVal_Message, fact.Name))
            {
                Fixes = FixOf(parameter),
            };
        }
    }

    // an interface member's or an event's parameter: no implementation and no handler writes the parameter at its position.
    private static bool IsUnwrittenByItsBoundMembers(MemberDeclarationNode procedure, DeclarationFactDto fact, ModuleSemanticsDto semantics)
        => procedure is not ExternalMemberDeclarationNode && !UnusedDeclarations.HasAnothersSignature(procedure, semantics)
            && fact.IsWrittenByABoundMember is false;

    private static DiagnosticFixes? FixOf(ParameterDeclarationNode parameter)
    {
        var title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.ParameterCanBeByVal_Fix, parameter.Name);
        var edit = parameter switch
        {
            { ParameterKind: ParameterKind.ExplicitByRef, PassingRange: { } keyword } => SyntaxEdits.Replace(keyword, "ByVal"),
            { ParameterKind: ParameterKind.ImplicitByRef, NameRange: { } name } => SyntaxEdits.Insert(name.Start, "ByVal "),
            _ => null,
        };
        return edit is null ? null : new DiagnosticFixes { Edits = [SyntaxEdits.Fix(title, edit)] };
    }
}

// what the flow facts are told by: the host's fact of the variable, and the ranges they are written at.
internal static class VariableFlow
{
    // the fact of the declaration at an address: Uri equality ignores the fragment, which is what names the variable.
    public static DeclarationFactDto? DeclarationOf(ModuleSemanticsDto semantics, Uri variable)
        => semantics.Declarations.FirstOrDefault(declaration => string.Equals(declaration.Symbol.AbsoluteUri, variable.AbsoluteUri, StringComparison.Ordinal));

    public static bool Contains(SourceRange outer, SourceRange inner) => outer.Start <= inner.Start && inner.End <= outer.End;
}
