using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Extensibility.Diagnostics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11045</c>: a <c>Let</c> to an object of a specific class, written without an argument list, whose default member has no <c>Property Let</c>
/// (<c>os = 5</c>, <c>Me = 5</c>), which MS-VBA refuses at compile time.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.4.3.8</strong> makes the statement valid. MS-VBA refuses it: "Invalid use of property" for a <c>Property Get</c>, "Type mismatch" for a
/// function (observed in Excel). A warning, because the code does not compile in MS-VBA. The analyzer finds the <c>Let</c> in the syntax tree, and reads the
/// target's <c>ExpressionFactDto</c>: a class, not an array, whose <c>DefaultMember</c> is a function or a <c>Property Get</c> with no <c>Property Let</c> that
/// requires no argument. A target that is a property with a <c>Property Let</c> of its own (<c>Accessors</c>) is assigned by it. Written with an argument list
/// (<c>os() = 5</c>), or through a variable declared <c>Object</c> or <c>Variant</c>, MS-VBA compiles it.
/// </remarks>
internal sealed class LetToADefaultMemberWithNoPropertyLetAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.LetToADefaultMemberWithNoPropertyLet.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => LetThroughDefaultMember.Findings(context, target => target is SimpleNameExpressionNode or MemberAccessExpressionNode or InstanceExpressionNode ? target : null,
            (_, fact) => fact is { Error: null, TypeKind: DeclaredTypeKind.Class, IsArray: false, DefaultMember: { RequiredParameters: 0 } member }
                && !fact.Accessors.HasFlag(PropertyAccessors.Let)
                && (member.Classification == ExpressionClassification.Function
                    || member.Classification == ExpressionClassification.Property && member.Accessors.HasFlag(PropertyAccessors.Get))
                && !member.Accessors.HasFlag(PropertyAccessors.Let),
            RDCoreDiagnosticId.LetToADefaultMemberWithNoPropertyLet, _ => RDCoreDiagnosticsResources.LetToADefaultMemberWithNoPropertyLet_Message);
}

/// <summary>
/// <c>RDC11046</c>: a <c>Let</c> that names a <c>Property Get</c> with no <c>Property Let</c>, or a function, declared <c>Variant</c>, outside its own body
/// (<c>o.Value = 5</c>), which RD-VBA runs as MS-VBA does.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.4.3.8</strong> raises run-time error 451 for the property and 450 for the function. MS-VBA calls it, and let-assigns the default member of
/// the object it returns, or raises 424 when it returns no object (observed in Excel). A warning, because the statement does what neither the code nor the
/// specification says. The analyzer finds the <c>Let</c> in the syntax tree, and reads the <c>ExpressionFactDto</c> of what its target calls (the callee of
/// an index, or the target itself): a function, or a property with a <c>Property Get</c> and no <c>Property Let</c> (<c>Accessors</c>), declared
/// <c>Variant</c>, and bound to a member other than the procedure the statement is in.
/// </remarks>
internal sealed class LetThroughAVariantGetOrFunctionAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.LetThroughAVariantGetOrFunction.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => LetThroughDefaultMember.Findings(context, target => target is IndexExpressionNode { Callee: var callee } ? callee : target,
            (procedure, fact) => fact is { Error: null, TypeKind: DeclaredTypeKind.Variant, IsArray: false, Binding: { } binding }
                && binding.AbsoluteUri != procedure.Procedure.AbsoluteUri
                && (fact.Classification == ExpressionClassification.Function
                    || fact.Classification == ExpressionClassification.Property && fact.Accessors.HasFlag(PropertyAccessors.Get) && !fact.Accessors.HasFlag(PropertyAccessors.Let)),
            RDCoreDiagnosticId.LetThroughAVariantGetOrFunction,
            fact => fact.Classification == ExpressionClassification.Function
                ? RDCoreDiagnosticsResources.LetThroughAVariantFunction_Message
                : RDCoreDiagnosticsResources.LetThroughAVariantGet_Message);
}

/// <summary>
/// <c>RDC11050</c>: a <c>Let</c> to a target declared as a class that has no default member (<c>p = 5</c>, <c>h.Plain = 5</c>), which MS-VBA compiles and
/// which raises run-time error 438 whenever it runs.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.4.3.8</strong> let-assigns the default property of the object the target holds, or that a <c>Property Get</c> or a function the target
/// names returns, and raises 438 when its class has none; MS-VBA does the same, and 91 when the target holds <c>Nothing</c> (observed in Excel). A warning,
/// because the statement fails every time it runs. The analyzer finds the <c>Let</c> in the syntax tree, and reads the target's <c>ExpressionFactDto</c>:
/// a class, not an array, that has no <c>DefaultMember</c>, and no <c>Property Let</c> of its own (<c>Accessors</c>), bound to a member other than the
/// procedure the statement is in.
/// </remarks>
internal sealed class LetToAnObjectWithNoDefaultMemberAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.LetToAnObjectWithNoDefaultMember.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => LetThroughDefaultMember.Findings(context, target => target is SimpleNameExpressionNode or MemberAccessExpressionNode or InstanceExpressionNode or IndexExpressionNode ? target : null,
            (procedure, fact) => fact is { Error: null, TypeKind: DeclaredTypeKind.Class, IsArray: false, DefaultMember: null }
                && !fact.Accessors.HasFlag(PropertyAccessors.Let)
                && fact.Binding?.AbsoluteUri != procedure.Procedure.AbsoluteUri,
            RDCoreDiagnosticId.LetToAnObjectWithNoDefaultMember, _ => RDCoreDiagnosticsResources.LetToAnObjectWithNoDefaultMember_Message,
            // the Let of an object, which has no default member to take it, made a Set.
            (statement, target, procedure) => SetAssignments.Fix(statement, target, procedure.Expressions
                .FirstOrDefault(fact => fact.Location.Range == statement.Value.SourceLocation.Range)) is { } fix ? new DiagnosticFixes { Edits = [fix] } : null);
}

// a finding at the expression of each Let target that the facts say is assigned through a default member, named as the code writes it, with the declared type
// of the target as a second argument of the message, and the fixes of its statement.
internal static class LetThroughDefaultMember
{
    public static IEnumerable<AnalyzerFinding> Findings(ModuleAnalysisContext context, Func<ExpressionNode, ExpressionNode?> judged,
        Func<ProcedureSemanticsDto, ExpressionFactDto, bool> isAssignedThrough, RDCoreDiagnosticId id, Func<ExpressionFactDto, string> message,
        Func<AssignmentStatementNode, ExpressionFactDto, ProcedureSemanticsDto, DiagnosticFixes?>? fixes = null)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var expressions = module.Descendants().OfType<AssignmentStatementNode>()
            .Where(assignment => assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet)
            .Select(assignment => (Expression: judged(assignment.Target), Statement: assignment))
            .Where(judgedOne => judgedOne.Expression is not null)
            .DistinctBy(judgedOne => judgedOne.Expression!.Identity)
            .ToDictionary(judgedOne => judgedOne.Expression!.Identity);
        foreach (var procedure in semantics.Procedures)
        {
            foreach (var fact in procedure.Expressions.Where(fact => expressions.ContainsKey(fact.Node) && isAssignedThrough(procedure, fact)))
            {
                var (expression, statement) = expressions[fact.Node];
                if (NameOf(expression!) is { } name)
                {
                    yield return new AnalyzerFinding(id.ToCode(), fact.Location.Range, DiagnosticSeverity.Warning, AnalyzerMessages.Format(message(fact), name, fact.DeclaredType ?? string.Empty))
                    {
                        Fixes = fixes?.Invoke(statement, fact, procedure),
                    };
                }
            }
        }
    }

    private static string? NameOf(ExpressionNode expression) => expression switch
    {
        SimpleNameExpressionNode name => name.IdentifierName,
        MemberAccessExpressionNode access => access.Member.IdentifierName,
        InstanceExpressionNode => Tokens.Me,
        IndexExpressionNode { Callee: var callee } => NameOf(callee),
        _ => null,
    };
}
