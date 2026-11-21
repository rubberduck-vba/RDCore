using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;
using DeclaredTypeKind = RDCore.SDK.Semantics.DeclaredTypeKind;
using DefaultMemberFact = RDCore.SDK.Semantics.DefaultMemberFact;
using VBIdentifier = RDCore.SDK.Model.VBIdentifier;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

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
    /// An object let-coerced to a value: the outermost expression of an object type in a conversion the runtime pass states, or an expression the static facts
    /// say is let-coerced, and its declared type.
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
        // the target of a Let is what is assigned, and no operand of its conversion.
        var targets = module.Descendants().OfType<AssignmentStatementNode>().Select(statement => statement.Target.SourceLocation.Range).ToHashSet();
        var converted = semantics.Procedures
            .SelectMany(procedure => (procedure.Runtime?.Conversions ?? [])
                .Where(conversion => conversion.Flags.HasFlag(ConversionSemanticFlags.ObjectOperand))
                .Select(conversion => OperandOf(procedure, conversion, targets)))
            .OfType<ExpressionFactDto>();
        // the static facts state it of the code the runtime pass does not reach as well: an operand after an error raised before it (x = CLng("abc") + h),
        // or a procedure it does not analyze fully.
        // an object of a class with no default member has no value to give: RDC11015 reports it.
        return converted.Concat(StaticObjectsReadAsValues(semantics))
            .Where(operand => !objectsAssignedToObjects.Contains(operand.Location.Range))
            .Where(operand => operand is not { TypeKind: DeclaredTypeKind.Class, DefaultMember: null })
            .DistinctBy(operand => operand.Location.Range);
    }

    // the expressions the static facts say are let-coerced, of a class or of Object.
    private static IEnumerable<ExpressionFactDto> StaticObjectsReadAsValues(ModuleSemanticsDto semantics)
        => semantics.Procedures.SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact is { IsLetCoerced: true, Error: null, IsArray: false, TypeKind: DeclaredTypeKind.Object or DeclaredTypeKind.Class });

    /// <summary>
    /// The <c>Let</c> assignments (with or without the keyword) whose target is an object: a class whose default member the value is assigned to, or an
    /// <c>Object</c>.
    /// </summary>
    /// <param name="semantics">The host's model of the module.</param>
    /// <param name="module">The syntax tree of the module.</param>
    public static IEnumerable<(AssignmentStatementNode Statement, ExpressionFactDto Target, string? DefaultMember, bool ValueIsAnObject)> AssignmentsToObjects(
        ModuleSemanticsDto semantics, ModuleNode module)
    {
        var expressions = semantics.Procedures.SelectMany(procedure => procedure.Expressions).ToArray();
        var accesses = MemberAccesses(semantics).ToArray();
        var objectOperands = semantics.Procedures
            .SelectMany(procedure => procedure.Runtime?.Conversions ?? [])
            .Where(conversion => conversion.Flags.HasFlag(ConversionSemanticFlags.ObjectOperand))
            .Select(conversion => conversion.Location.Range)
            .ToHashSet();
        // the static facts also state that the value of a Let is let-coerced where the runtime pass states no conversion of it: a target that is Nothing
        // raises 91 before its default member is found, and the runtime pass has no value of a parameter, only an assumed one.
        objectOperands.UnionWith(StaticObjectsReadAsValues(semantics).Select(fact => fact.Location.Range));

        foreach (var statement in module.Descendants().OfType<AssignmentStatementNode>().Where(statement => statement.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet))
        {
            // the target's fact is the one at the target: of the type an element holds, for an element. A Let of an object writes the default property of the
            // object, which the target is read for (MS-VBAL §5.4.3.8), and is not flagged as written.
            var targetRange = statement.Target.SourceLocation.Range;
            if (expressions.FirstOrDefault(fact => fact.Location.Range == targetRange) is not { } target)
            {
                continue;
            }

            // a class's default member is the member access of the target that the code does not name; an Object has none to state before it runs. A target
            // that names the default member (`label.Caption = text`, `.Value = x` in a With block) assigns that member, and no object: the host states the
            // access of a member that is the default one as a default member's access, named or not.
            var unbound = IsUnbound(target.DeclaredType);
            // the default member of an object given the target's argument list (b(1) = 5) is RDC00121's. Where the runtime pass states no access at the
            // target, as of a target that is Nothing or a parameter, the static facts give the default member of the target's class.
            var found = unbound ? null : accesses.FirstOrDefault(access => access.Location.Range == targetRange
                && access.MemberAccessOpSemanticFlags!.Value is var flags
                && flags.HasFlag(MemberAccessOperationSemanticFlags.DefaultMember) && !flags.HasFlag(MemberAccessOperationSemanticFlags.ArgumentList)
                && !Names(statement.Target, access.Member));
            var defaultMember = found is not null ? found.Member ?? string.Empty
                : unbound || accesses.Any(stated => stated.Location.Range == targetRange) ? null
                : StaticDefaultMemberOf(statement.Target, target);
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
    /// <param name="recursive">
    /// Whether the access of an object of a known class goes through more than one default member: one that takes no arguments gives them to the default
    /// member of what it returns (<see cref="MemberAccessOperationSemanticFlags.Recursive"/>). Ignored when <paramref name="unbound"/> is
    /// <see langword="true"/>: the default member of an <c>Object</c> is known only at run time.
    /// </param>
    public static IEnumerable<OperatorFactDto> ImplicitIndexedAccesses(ModuleSemanticsDto semantics, bool dictionaryAccess, bool unbound, bool recursive = false)
        // a value assigned to an object (h = 5), or to an element that holds one (a(1) = 5), is an implicit access of its default member that is given no
        // argument list: RDC00119 and RDC00120 report it. The host states a fact at the access for each default member it goes through: those facts are
        // read together as one access of the code, named by the first default member, whose fact carries Recursive.
        => MemberAccesses(semantics)
            .Where(access => access.MemberAccessOpSemanticFlags!.Value.HasFlag(MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember))
            .GroupBy(access => access.Location.Range)
            .Select(facts => (
                Access: facts.FirstOrDefault(fact => fact.MemberAccessOpSemanticFlags!.Value.HasFlag(MemberAccessOperationSemanticFlags.Recursive)) ?? facts.First(),
                Flags: facts.Aggregate(default(MemberAccessOperationSemanticFlags), (all, fact) => all | fact.MemberAccessOpSemanticFlags!.Value),
                // the chain from the class the object is declared as: what each default member returns states the rest of it again, from its own class.
                Chain: facts.Select(fact => fact.DefaultMembers).Where(chain => chain is not null).MaxBy(chain => chain!.Value.Length)))
            .Where(each => each.Flags is var flags
                && flags.HasFlag(MemberAccessOperationSemanticFlags.DictionaryAccess) == dictionaryAccess
                && (dictionaryAccess || flags.HasFlag(MemberAccessOperationSemanticFlags.ArgumentList))
                && flags.HasFlag(MemberAccessOperationSemanticFlags.LateBound) == unbound
                && (unbound || flags.HasFlag(MemberAccessOperationSemanticFlags.Recursive) == recursive))
            .Select(each => each.Access with { DefaultMembers = each.Chain });

    /// <summary>
    /// Whether a declared type is <c>Object</c>, whose default member is known only at run time.
    /// </summary>
    /// <param name="declaredType">The declared type, as the host names it.</param>
    public static bool IsUnbound(string? declaredType) => string.Equals(declaredType, VBTypeNames.VBObject, StringComparison.OrdinalIgnoreCase);

    // the outermost expression in the conversion whose declared type holds an object: a class, or Object. The target of an assignment is not one.
    private static ExpressionFactDto? OperandOf(ProcedureSemanticsDto procedure, ConversionFactDto conversion, HashSet<SourceRange> targets)
    {
        var candidates = procedure.Expressions
            .Where(fact => !fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) && !targets.Contains(fact.Location.Range)
                && conversion.Location.Range.Contains(fact.Location.Range)
                && HoldsAnObject(fact.DeclaredType))
            .ToArray();
        return candidates.FirstOrDefault(fact => !candidates.Any(other => other != fact && other.Location.Range.Contains(fact.Location.Range)));
    }

    // MS-VBAL §5.4.3.8: the default member a Let of a value to an object of the class assigns, as the static facts state it. A function that requires no
    // argument is not a property to assign, and raises 438 when it runs: the runtime pass states no access of it.
    private static string? StaticDefaultMemberOf(ExpressionNode target, ExpressionFactDto fact)
        => fact.DefaultMember is { } member && member is not { Classification: ExpressionClassification.Function, RequiredParameters: 0 }
            && !Names(target, member.Name)
            ? member.Name
            : null;

    // whether the target of an assignment is the access of the member, by its name.
    private static bool Names(ExpressionNode target, string? member)
        => target is MemberAccessExpressionNode access && string.Equals(access.Member.IdentifierName, member, StringComparison.OrdinalIgnoreCase);

    private static bool HoldsAnObject(string? declaredType)
        => declaredType is { Length: > 0 } type && (IsUnbound(type) || !IntrinsicVBTypes.IsIntrinsic(type) && !string.Equals(type, "Unknown", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The fix that names a default member after an expression: <c>.</c> and the member's name, which calls the member the code called without naming it,
    /// with the same arguments (<strong>MS-VBAL §5.6.12</strong>). None for a name that brackets cannot hold (<see cref="Written"/>).
    /// </summary>
    /// <param name="after">Where the member's name is written: the end of the expression.</param>
    /// <param name="member">The name of the default member.</param>
    /// <param name="title">What the fix is called, with the member's name in it.</param>
    public static DiagnosticFixes? NameAfter(SourcePosition after, string? member, string title)
        => Written(member) is { } written
            ? new DiagnosticFixes { Edits = [SyntaxEdits.Fix(AnalyzerMessages.Format(title, member!), SyntaxEdits.Insert(after, $".{written}"))] }
            : null;

    /// <summary>
    /// The default members an access goes through, each written after a period as the code names it (<see cref="Written"/>), in the order they are called:
    /// <c>.Inner.Child.Item</c>. A default member that takes no arguments is called by its name alone (<strong>MS-VBAL §5.6.12</strong>), and its result
    /// is the object of the next one. <see langword="null"/> when the chain is not known, and when one of its names cannot be written.
    /// </summary>
    /// <param name="members">The default members, as the host states them.</param>
    public static (string Written, string Shown)? ChainOf(ImmutableArray<DefaultMemberFact>? members)
    {
        if (members is not { Length: > 0 } chain)
        {
            return null;
        }

        var written = chain.Select(member => Written(member.Name)).ToArray();
        return written.Any(name => name is null)
            ? null
            : (string.Concat(written.Select(name => $".{name}")), string.Join(".", chain.Select(member => member.Name)));
    }

    /// <summary>
    /// The fix that writes the default members a dictionary access gives its name to (<strong>MS-VBAL §5.6.14</strong>): <c>b!key</c> becomes
    /// <c>b.Item("key")</c>, and <c>!key</c> in a <c>With</c> block becomes <c>.Item("key")</c>. A name in brackets (<c>b![a key]</c>) is the name inside
    /// them.
    /// </summary>
    /// <param name="bang">The dictionary access.</param>
    /// <param name="key">The name after the <c>!</c>.</param>
    /// <param name="written">The members, each after a period, as the code writes them.</param>
    /// <param name="title">What the fix is called, with the members' names in it.</param>
    /// <param name="shown">The members' names, as the title shows them.</param>
    public static DiagnosticFixes NameBang(DictionaryAccessExpressionNode bang, string key, string written, string title, string shown)
    {
        var from = bang.Owner?.SourceLocation.Range.End ?? bang.SourceLocation.Range.Start;
        var literal = key.Replace("\"", "\"\"", StringComparison.Ordinal);
        return new DiagnosticFixes
        {
            Edits = [SyntaxEdits.Fix(
                AnalyzerMessages.Format(title, shown),
                SyntaxEdits.Replace(new SourceRange(from, bang.SourceLocation.Range.End), $"{written}(\"{literal}\")"))],
        };
    }

    /// <summary>
    /// A member's name as the code writes it after a period: as it is when it is an identifier and no keyword (a letter, then letters, digits and
    /// underscores, <strong>MS-VBAL §3.3.5</strong>), and in brackets otherwise, as a <c>FOREIGN-NAME</c> (<strong>MS-VBAL §3.3.5.3</strong>): a library's
    /// <c>_Default</c> is written <c>[_Default]</c>. <see langword="null"/> for no name, and for one that brackets cannot hold: with a <c>]</c> or a line
    /// break in it.
    /// </summary>
    /// <param name="member">The name of the member.</param>
    public static string? Written(string? member)
        => member is not { Length: > 0 } name ? null
            : char.IsLetter(name[0]) && name.All(character => char.IsLetterOrDigit(character) || character == '_') && !VBIdentifier.IsReserved(name) ? name
            : name.IndexOfAny([']', '\r', '\n']) < 0 ? $"[{name}]"
            : null;
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
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitDefaultMemberAccess.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            return [];
        }

        // the member a value is read through is named only when a read of it needs no argument: one that requires one makes the read a compile error.
        var reads = DefaultMemberAccesses.ObjectsReadAsValues(semantics, module)
            .Where(operand => !DefaultMemberAccesses.IsUnbound(operand.DeclaredType))
            .Select(operand => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitDefaultMemberAccess.ToCode(),
                operand.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitDefaultMemberAccess_Message, operand.DeclaredType ?? string.Empty))
            {
                Fixes = operand.DefaultMember is { RequiredParameters: 0 } member
                    ? DefaultMemberAccesses.NameAfter(operand.Location.Range.End, member.Name, RDCoreDiagnosticsResources.ImplicitDefaultMemberAccess_Fix)
                    : null,
            });
        // a target that is Nothing, a parameter, or what a function or a property returns is assigned through the same member once it is named.
        var writes = DefaultMemberAccesses.AssignmentsToObjects(semantics, module)
            .Where(assignment => assignment is { ValueIsAnObject: false, DefaultMember: not null })
            .Select(assignment => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplicitDefaultMemberAccess.ToCode(),
                assignment.Target.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.ImplicitDefaultMemberAccess_Assigned_Message,
                    assignment.DefaultMember!,
                    assignment.Target.DeclaredType ?? string.Empty))
            {
                Fixes = DefaultMemberAccesses.NameAfter(
                    assignment.Statement.Target.SourceLocation.Range.End, assignment.DefaultMember, RDCoreDiagnosticsResources.ImplicitDefaultMemberAccess_Fix),
            });
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
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitUnboundDefaultMemberAccess.ToCode()];

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
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.IndexedDefaultMemberAccess.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        // the member is named between the object and its argument list: c(1) is c.Item(1), and t() is t.Item().
        var indexes = (context.ParseResult.SyntaxTree?.Descendants().OfType<IndexExpressionNode>() ?? [])
            .ToLookup(index => index.SourceLocation.Range);
        return DefaultMemberAccesses.ImplicitIndexedAccesses(semantics, dictionaryAccess: false, unbound: false)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.IndexedDefaultMemberAccess.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.IndexedDefaultMemberAccess_Message, access.Member ?? string.Empty))
            {
                Fixes = indexes[access.Location.Range].FirstOrDefault() is { } index
                    ? DefaultMemberAccesses.NameAfter(
                        index.Callee.SourceLocation.Range.End, access.Member, RDCoreDiagnosticsResources.IndexedDefaultMemberAccess_Fix)
                    : null,
            });
    }
}

/// <summary>
/// <c>RDC00122</c>: an argument list given to an <c>Object</c>: which default member takes it is known only at run time (<strong>MS-VBAL §5.6.13</strong>).
/// </summary>
internal sealed class IndexedUnboundDefaultMemberAccessAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.IndexedUnboundDefaultMemberAccess.ToCode()];

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
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.BangNotation.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        var accesses = (context.ParseResult.SyntaxTree?.Descendants().OfType<DictionaryAccessExpressionNode>() ?? [])
            .ToLookup(access => access.SourceLocation.Range);
        return DefaultMemberAccesses.ImplicitIndexedAccesses(semantics, dictionaryAccess: true, unbound: false)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.BangNotation.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.BangNotation_Message, access.Member ?? string.Empty))
            {
                Fixes = accesses[access.Location.Range].FirstOrDefault() is { Member.IdentifierName: { } key } bang
                    && DefaultMemberAccesses.Written(access.Member) is { } written
                    ? DefaultMemberAccesses.NameBang(bang, key, $".{written}", RDCoreDiagnosticsResources.BangNotation_Fix, access.Member!)
                    : null,
            });
    }
}

/// <summary>
/// <c>RDC00124</c>: a dictionary access (<c>v!key</c>) of an <c>Object</c>: which default member takes the name is known only at run time
/// (<strong>MS-VBAL §5.6.14</strong>).
/// </summary>
internal sealed class UnboundBangNotationAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.UnboundBangNotation.ToCode()];

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
/// <c>RDC00126</c>: an argument list given to an object of a known class whose default member takes none: it goes to the default member of the object that
/// one returns, so more than one default member is called and none is named (<strong>MS-VBAL §5.6.13</strong>).
/// </summary>
internal sealed class IndexedRecursiveDefaultMemberAccessAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.IndexedRecursiveDefaultMemberAccess.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        // every default member is named between the object and its argument list: t(1) is t.Child.Item(1).
        var indexes = (context.ParseResult.SyntaxTree?.Descendants().OfType<IndexExpressionNode>() ?? [])
            .ToLookup(index => index.SourceLocation.Range);
        return DefaultMemberAccesses.ImplicitIndexedAccesses(semantics, dictionaryAccess: false, unbound: false, recursive: true)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.IndexedRecursiveDefaultMemberAccess.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.IndexedRecursiveDefaultMemberAccess_Message, access.Member ?? string.Empty))
            {
                Fixes = indexes[access.Location.Range].FirstOrDefault() is { } index && DefaultMemberAccesses.ChainOf(access.DefaultMembers) is { } chain
                    ? new DiagnosticFixes
                    {
                        Edits = [SyntaxEdits.Fix(
                            AnalyzerMessages.Format(RDCoreDiagnosticsResources.IndexedRecursiveDefaultMemberAccess_Fix, chain.Shown),
                            SyntaxEdits.Insert(index.Callee.SourceLocation.Range.End, chain.Written))],
                    }
                    : null,
            });
    }
}

/// <summary>
/// <c>RDC00127</c>: a dictionary access (<c>b!key</c>) of an object of a known class whose default member takes no arguments: the name goes to the default
/// member of the object that one returns (<strong>MS-VBAL §5.6.14</strong>, <strong>§5.6.13</strong>).
/// </summary>
internal sealed class RecursiveBangNotationAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.RecursiveBangNotation.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        // d!Name is d.Inner.Child.Item("Name"): the last default member is given the name.
        var accesses = (context.ParseResult.SyntaxTree?.Descendants().OfType<DictionaryAccessExpressionNode>() ?? [])
            .ToLookup(access => access.SourceLocation.Range);
        return DefaultMemberAccesses.ImplicitIndexedAccesses(semantics, dictionaryAccess: true, unbound: false, recursive: true)
            .Select(access => new AnalyzerFinding(
                RDCoreDiagnosticId.RecursiveBangNotation.ToCode(),
                access.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.RecursiveBangNotation_Message, access.Member ?? string.Empty))
            {
                Fixes = accesses[access.Location.Range].FirstOrDefault() is { Member.IdentifierName: { } key } bang
                    && DefaultMemberAccesses.ChainOf(access.DefaultMembers) is { } chain
                    ? DefaultMemberAccesses.NameBang(bang, key, chain.Written, RDCoreDiagnosticsResources.RecursiveBangNotation_Fix, chain.Shown)
                    : null,
            });
    }
}

/// <summary>
/// <c>RDC00125</c>: an object assigned to an object without <c>Set</c> (<strong>MS-VBAL §5.4.3.8</strong>): the value of the default member of one is assigned
/// to the default member of the other, where a forgotten <c>Set</c> is the likelier intent.
/// </summary>
internal sealed class SuspiciousLetAssignmentAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.SuspiciousLetAssignment.ToCode()];

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            return [];
        }

        // the default member of the value's class, by where the value is: what the Let reads.
        var values = semantics.Procedures.SelectMany(procedure => procedure.Expressions).ToLookup(fact => fact.Location.Range);
        return DefaultMemberAccesses.AssignmentsToObjects(semantics, module)
            .Where(assignment => assignment.ValueIsAnObject)
            .Select(assignment => new AnalyzerFinding(
                RDCoreDiagnosticId.SuspiciousLetAssignment.ToCode(),
                assignment.Statement.SourceLocation.Range,
                DiagnosticSeverity.Warning,
                RDCoreDiagnosticsResources.SuspiciousLetAssignment_Message)
            {
                Fixes = FixesOf(assignment.Statement, assignment.Target, assignment.DefaultMember, values[assignment.Statement.Value.SourceLocation.Range]
                    .OrderBy(fact => fact.DefaultMember is null).FirstOrDefault()),
            });
    }

    // the Let as it is, with both default members named: h = h2 is h.Value = h2.Value. It keeps what the code does, and comes first. None for a target
    // declared Object, whose default member is known only at run time, nor for a value whose default member requires an argument. Then the Let made a Set,
    // which assigns the object and changes what the code does.
    private static DiagnosticFixes? FixesOf(AssignmentStatementNode statement, ExpressionFactDto targetFact, string? target, ExpressionFactDto? value)
    {
        var named = DefaultMemberAccesses.Written(target) is { } writtenTarget && value?.DefaultMember is { RequiredParameters: 0 } member
            && DefaultMemberAccesses.Written(member.Name) is { } writtenValue
            ? SyntaxEdits.Fix(
                RDCoreDiagnosticsResources.SuspiciousLetAssignment_Fix,
                SyntaxEdits.Insert(statement.Target.SourceLocation.Range.End, $".{writtenTarget}"),
                SyntaxEdits.Insert(statement.Value.SourceLocation.Range.End, $".{writtenValue}"))
            : null;
        EditFix[] fixes = [.. new[] { named, SetAssignments.Fix(statement, targetFact, value) }.OfType<EditFix>()];
        return fixes is [] ? null : new DiagnosticFixes { Edits = fixes };
    }
}
