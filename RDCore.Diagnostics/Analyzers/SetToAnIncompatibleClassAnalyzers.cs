using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11051</c>: a <c>Set</c> of a target declared as a class to a value declared as a class no object of which is an object of the target's class
/// (<c>Set shape = account</c>), which MS-VBA compiles and which raises run-time error 13 whenever it runs with an object.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.4.3.9</strong> Set-coerces the value to the target's declared type, and <strong>MS-VBAL §5.5.2.2.1</strong> raises error 13 for an
/// object whose class is not that class and does not implement it. A variable declared as a class holds an object of that class, or of a class that
/// implements it (<strong>MS-VBAL §5.2.4.2</strong>). A warning, because the statement fails every time it runs with an object. The analyzer reads the
/// value's <c>ExpressionFactDto</c>: a <c>SetCoercion</c> whose <c>Relation</c> is <c>Unrelated</c>. A class that a class of the workspace implements along
/// with the target's is not reported, nor is a class of a library, whose objects answer for themselves when the code runs.
/// </remarks>
internal sealed class SetToAnIncompatibleClassAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.SetToAnIncompatibleClass.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => IncompatibleClass.Findings(context, assigned: true, RDCoreDiagnosticId.SetToAnIncompatibleClass,
            RDCoreDiagnosticsResources.SetToAnIncompatibleClass_Message);
}

/// <summary>
/// <c>RDC11052</c>: an argument declared as a class given to a parameter declared as a class no object of which is an object of the parameter's class
/// (<c>Draw account</c>, <c>Sub Draw(ByVal shape As IShape)</c>), which MS-VBA compiles and which raises run-time error 13 whenever the call is made with an
/// object.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.3.1.11</strong> Set-assigns the argument to a parameter declared as a class: <c>ByVal</c>, or <c>ByRef</c> where the argument is not a
/// variable of the parameter's own class. <strong>MS-VBAL §5.5.2.2.1</strong> raises error 13 for an object whose class is not the parameter's and does not
/// implement it. A warning, because the call fails every time it is made with an object. The analyzer reads the argument's <c>ExpressionFactDto</c>: a
/// <c>SetCoercion</c> whose <c>Relation</c> is <c>Unrelated</c>, for an expression that is not the value of an assignment.
/// </remarks>
internal sealed class ArgumentOfAnIncompatibleClassAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ArgumentOfAnIncompatibleClass.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => IncompatibleClass.Findings(context, assigned: false, RDCoreDiagnosticId.ArgumentOfAnIncompatibleClass,
            RDCoreDiagnosticsResources.ArgumentOfAnIncompatibleClass_Message);
}

// a finding at each expression whose Set-coercion goes to a class no object of its own declared type is an object of: the value of an assignment, or not.
internal static class IncompatibleClass
{
    public static IEnumerable<AnalyzerFinding> Findings(ModuleAnalysisContext context, bool assigned, RDCoreDiagnosticId id, string message)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var values = module.Descendants().OfType<AssignmentStatementNode>().Select(assignment => assignment.Value.Identity).ToHashSet();
        foreach (var fact in semantics.Procedures.SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact is { Error: null, SetCoercion.Relation: ClassRelation.Unrelated } && values.Contains(fact.Node) == assigned)
            .DistinctBy(fact => fact.Node))
        {
            yield return new AnalyzerFinding(id.ToCode(), fact.Location.Range, DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(message, fact.DeclaredType ?? string.Empty, fact.SetCoercion!.Destination));
        }
    }
}
