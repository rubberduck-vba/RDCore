using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// Where a module calls a default member without naming it, as the host states it: an object let-coerced to a value (<strong>MS-VBAL §5.6.2.2</strong>,
/// a <see cref="ConversionSemanticFlags.ObjectOperand"/> conversion), a value assigned to an object without <c>Set</c> (<strong>MS-VBAL §5.4.3.8</strong>), an
/// argument list given to an object (<strong>MS-VBAL §5.6.13</strong>) and a dictionary access (<strong>MS-VBAL §5.6.14</strong>), the last two as member
/// accesses (<see cref="MemberAccessOperationSemanticFlags.DefaultMember"/>).
/// </summary>
internal static class DefaultMemberAccesses
{
    /// <summary>
    /// An object let-coerced to a value: the outermost expression of an object type in the conversion, and its declared type.
    /// </summary>
    /// <param name="semantics">The host's model of the module.</param>
    /// <param name="module">The syntax tree of the module.</param>
    public static IEnumerable<ExpressionFactDto> ObjectsReadAsValues(ModuleSemanticsDto semantics, ModuleNode module)
    {
        // the object assigned to an object is RDC00125's.
        var objectsAssignedToObjects = AssignmentsToObjects(semantics, module)
            .Where(assignment => assignment.ValueIsAnObject)
            .Select(assignment => assignment.Statement.Value.SourceLocation.Range)
            .ToHashSet();
        return semantics.Procedures
            .SelectMany(procedure => (procedure.Runtime?.Conversions ?? [])
                .Where(conversion => conversion.Flags.HasFlag(ConversionSemanticFlags.ObjectOperand))
                .Select(conversion => OperandOf(procedure, conversion)))
            .OfType<ExpressionFactDto>()
            .Where(operand => !objectsAssignedToObjects.Contains(operand.Location.Range))
            .DistinctBy(operand => operand.Location.Range);
    }

    /// <summary>
    /// The <c>Let</c> assignments (with or without the keyword) whose target is an object: a class whose default member the value is assigned to, or an
    /// <c>Object</c>.
    /// </summary>
    /// <param name="semantics">The host's model of the module.</param>
    /// <param name="module">The syntax tree of the module.</param>
    public static IEnumerable<(AssignmentStatementNode Statement, ExpressionFactDto Target, OperatorFactDto? DefaultMember, bool ValueIsAnObject)> AssignmentsToObjects(
        ModuleSemanticsDto semantics, ModuleNode module)
    {
        var expressions = semantics.Procedures.SelectMany(procedure => procedure.Expressions).ToArray();
        var accesses = MemberAccesses(semantics).ToArray();
        var objectOperands = semantics.Procedures
            .SelectMany(procedure => procedure.Runtime?.Conversions ?? [])
            .Where(conversion => conversion.Flags.HasFlag(ConversionSemanticFlags.ObjectOperand))
            .Select(conversion => conversion.Location.Range)
            .ToHashSet();

        foreach (var statement in module.Descendants().OfType<AssignmentStatementNode>().Where(statement => statement.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet))
        {
            var targetRange = statement.Target.SourceLocation.Range;
            if (expressions.FirstOrDefault(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) && fact.Location.Range == targetRange) is not { } target)
            {
                continue;
            }

            // a class's default member is the member access of the target; an Object has none to state before it runs.
            var unbound = IsUnbound(target.DeclaredType);
            var defaultMember = unbound ? null : accesses.FirstOrDefault(access => access.Location.Range == targetRange
                && access.MemberAccessOpSemanticFlags!.Value.HasFlag(MemberAccessOperationSemanticFlags.DefaultMember));
            if (defaultMember is null && !unbound)
            {
                continue;
            }

            yield return (statement, target, defaultMember, objectOperands.Contains(statement.Value.SourceLocation.Range));
        }
    }

    /// <summary>
    /// The member accesses the host states for the module's procedures.
    /// </summary>
    /// <param name="semantics">The host's model of the module.</param>
    public static IEnumerable<OperatorFactDto> MemberAccesses(ModuleSemanticsDto semantics)
        => semantics.Procedures.SelectMany(procedure => procedure.Runtime?.Operations ?? []).Where(operation => operation.MemberAccessOpSemanticFlags is not null);

    /// <summary>
    /// The member accesses that give an argument list, or a <c>!</c> name, to a default member nothing names.
    /// </summary>
    /// <param name="semantics">The host's model of the module.</param>
    /// <param name="dictionaryAccess">Whether the access is a dictionary access (<c>a!b</c>).</param>
    /// <param name="unbound">Whether the object is an <c>Object</c>, whose default member is known only at run time.</param>
    public static IEnumerable<OperatorFactDto> ImplicitIndexedAccesses(ModuleSemanticsDto semantics, bool dictionaryAccess, bool unbound)
        => MemberAccesses(semantics).Where(access => access.MemberAccessOpSemanticFlags!.Value is var flags
            && flags.HasFlag(MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember)
            && flags.HasFlag(MemberAccessOperationSemanticFlags.DictionaryAccess) == dictionaryAccess
            && flags.HasFlag(MemberAccessOperationSemanticFlags.LateBound) == unbound);

    /// <summary>
    /// Whether a declared type is <c>Object</c>, whose default member is known only at run time.
    /// </summary>
    /// <param name="declaredType">The declared type, as the host names it.</param>
    public static bool IsUnbound(string? declaredType) => string.Equals(declaredType, VBTypeNames.VBObject, StringComparison.OrdinalIgnoreCase);

    // the outermost expression in the conversion whose declared type holds an object: a class, or Object.
    private static ExpressionFactDto? OperandOf(ProcedureSemanticsDto procedure, ConversionFactDto conversion)
    {
        var candidates = procedure.Expressions
            .Where(fact => !fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget)
                && Contains(conversion.Location.Range, fact.Location.Range)
                && HoldsAnObject(fact.DeclaredType))
            .ToArray();
        return candidates.FirstOrDefault(fact => !candidates.Any(other => other != fact && Contains(other.Location.Range, fact.Location.Range)));
    }

    private static bool HoldsAnObject(string? declaredType)
        => declaredType is { Length: > 0 } type && (IsUnbound(type) || !IntrinsicVBTypes.IsIntrinsic(type) && !string.Equals(type, "Unknown", StringComparison.OrdinalIgnoreCase));

    private static bool Contains(SourceRange outer, SourceRange inner) => outer.Start.CompareTo(inner.Start) <= 0 && inner.End.CompareTo(outer.End) <= 0;
}

/// <summary>
/// <c>RDC00119</c>: an object of a known class used as a value, or assigned a value without <c>Set</c>: its default member gives or takes the value, and the
/// code does not name it (<strong>MS-VBAL §5.6.2.2</strong>, <strong>§5.4.3.8</strong>).
/// </summary>
/// <remarks>
/// An object assigned to an object without <c>Set</c> is <c>RDC00125</c> instead.
/// </remarks>
internal sealed class ImplicitDefaultMemberAccessAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            return [];
        }

        var reads = DefaultMemberAccesses.ObjectsReadAsValues(semantics, module)
            .Where(operand => !DefaultMemberAccesses.IsUnbound(operand.DeclaredType))
            .Select(operand => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitDefaultMemberAccess.ToCode(),
                operand.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitDefaultMemberAccess_Message, operand.DeclaredType ?? string.Empty)));
        var writes = DefaultMemberAccesses.AssignmentsToObjects(semantics, module)
            .Where(assignment => assignment is { ValueIsAnObject: false, DefaultMember: not null })
            .Select(assignment => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitDefaultMemberAccess.ToCode(),
                assignment.Target.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitDefaultMemberAccess_Assigned_Message, assignment.DefaultMember!.Member ?? string.Empty, assignment.Target.DeclaredType ?? string.Empty)));
        return reads.Concat(writes);
    }
}

/// <summary>
/// <c>RDC00120</c>: an <c>Object</c> used as a value, or assigned a value without <c>Set</c>: which default member gives or takes the value is known only at
/// run time, and none at all raises error 438 (<strong>MS-VBAL §5.6.2.2</strong>).
/// </summary>
/// <remarks>
/// A <c>Variant</c> that holds an object is not reported: nothing says it holds one before it runs.
/// </remarks>
internal sealed class ImplicitUnboundDefaultMemberAccessAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            return [];
        }

        var reads = DefaultMemberAccesses.ObjectsReadAsValues(semantics, module)
            .Where(operand => DefaultMemberAccesses.IsUnbound(operand.DeclaredType))
            .Select(operand => operand.Location.Range);
        var writes = DefaultMemberAccesses.AssignmentsToObjects(semantics, module)
            .Where(assignment => assignment is { ValueIsAnObject: false, DefaultMember: null })
            .Select(assignment => assignment.Target.Location.Range);
        return reads.Concat(writes).Select(range => new AnalyzerFinding(
            RDCoreDiagnosticId.ImplicitUnboundDefaultMemberAccess.ToCode(),
            range,
            DiagnosticSeverity.Warning,
            RDCoreDiagnosticsResources.ImplicitUnboundDefaultMemberAccess_Message));
    }
}

/// <summary>
/// <c>RDC00121</c>: an argument list given to an object of a known class, which its default member takes without the code naming it
/// (<strong>MS-VBAL §5.6.13</strong>).
/// </summary>
internal sealed class IndexedDefaultMemberAccessAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.Semantics is not { } semantics ? [] : DefaultMemberAccesses.ImplicitIndexedAccesses(semantics, dictionaryAccess: false, unbound: false)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.IndexedDefaultMemberAccess.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.IndexedDefaultMemberAccess_Message, access.Member ?? string.Empty)));
}

/// <summary>
/// <c>RDC00122</c>: an argument list given to an <c>Object</c>: which default member takes it is known only at run time (<strong>MS-VBAL §5.6.13</strong>).
/// </summary>
internal sealed class IndexedUnboundDefaultMemberAccessAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.Semantics is not { } semantics ? [] : DefaultMemberAccesses.ImplicitIndexedAccesses(semantics, dictionaryAccess: false, unbound: true)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.IndexedUnboundDefaultMemberAccess.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.IndexedUnboundDefaultMemberAccess_Message));
}

/// <summary>
/// <c>RDC00123</c>: a dictionary access (<c>b!key</c>) of an object of a known class, which gives the name as a <c>String</c> to its default member
/// (<strong>MS-VBAL §5.6.14</strong>).
/// </summary>
internal sealed class BangNotationAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.Semantics is not { } semantics ? [] : DefaultMemberAccesses.ImplicitIndexedAccesses(semantics, dictionaryAccess: true, unbound: false)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.BangNotation.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.BangNotation_Message, access.Member ?? string.Empty)));
}

/// <summary>
/// <c>RDC00124</c>: a dictionary access (<c>v!key</c>) of an <c>Object</c>: which default member takes the name is known only at run time
/// (<strong>MS-VBAL §5.6.14</strong>).
/// </summary>
internal sealed class UnboundBangNotationAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.Semantics is not { } semantics ? [] : DefaultMemberAccesses.ImplicitIndexedAccesses(semantics, dictionaryAccess: true, unbound: true)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.UnboundBangNotation.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.UnboundBangNotation_Message));
}

/// <summary>
/// <c>RDC00125</c>: an object assigned to an object without <c>Set</c> (<strong>MS-VBAL §5.4.3.8</strong>): the value of the default member of one is assigned
/// to the default member of the other, where a forgotten <c>Set</c> is the likelier intent.
/// </summary>
internal sealed class SuspiciousLetAssignmentAnalyzer : IModuleAnalyzer
{
    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics } ? [] : DefaultMemberAccesses.AssignmentsToObjects(semantics, module)
            .Where(assignment => assignment.ValueIsAnObject)
            .Select(assignment => new AnalyzerFinding(
                RDCoreDiagnosticId.SuspiciousLetAssignment.ToCode(),
                assignment.Statement.SourceLocation.Range,
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.SuspiciousLetAssignment_Message));
}
