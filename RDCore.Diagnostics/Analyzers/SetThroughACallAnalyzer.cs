using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Extensibility.Diagnostics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11012</c>: a <c>Set</c> statement whose target is a call - of a function, or of a property that has no <c>Property Set</c> - that raises a run-time
/// error whenever it runs, which MS-VBA compiles.
/// </summary>
/// <remarks>
/// <para>
/// A function or a property with no <c>Property Set</c> named as the target of a <c>Set</c> outside its own body is called, and what it returns is Set through
/// its default member (<strong>MS-VBAL §5.4.3.9</strong>). When it is declared as a class whose default member has no <c>Property Set</c>, the object has
/// nothing to Set, and the statement raises error 438 whenever it runs. The analyzer finds the <c>Set</c> in the syntax tree, and reads the callee's
/// <c>ExpressionFactDto</c>: its classification, its <c>Accessors</c>, its <c>TypeKind</c>, and the <c>DefaultMember</c> of the class it is declared as. The
/// declared types alone say it, so it holds of every path, whether or not the run-time facts reach the statement; it is reported here, at Error severity,
/// rather than left to the moment it is raised. Where the run-time facts already state it at the statement, as the <c>VBR</c> diagnostic of the run-time
/// phase, it is not reported twice.
/// </para>
/// <para>
/// One of an <c>Object</c> or a <c>Variant</c> fails, or not, by what it returns: error 424 when that is no object. Only the run-time facts can say that.
/// </para>
/// </remarks>
internal sealed class SetThroughACallAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            return [];
        }

        // what a Set names as its target is called when it is a call: the callee of an index, or the name itself.
        var callees = module.Descendants().OfType<AssignmentStatementNode>()
            .Where(assignment => assignment.Kind == AssignmentKind.Set)
            .Select(assignment => assignment.Target is IndexExpressionNode { Callee: var called } ? called.Identity : assignment.Target.Identity)
            .ToHashSet();

        return semantics.Procedures.SelectMany(procedure => procedure.Expressions
            .Where(fact => callees.Contains(fact.Node) && SetsNothing(procedure, fact))
            .Where(fact => !(procedure.Runtime?.Errors ?? []).Any(error => error.Location.Range.Start.Line == fact.Location.Range.Start.Line && error.ErrorId == 438))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.SetThroughACallFails.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Error,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.SetThroughACallFails_NoDefaultMemberToSet_Message, NameOf(fact)))));
    }

    // a function, or a property with a Property Get and no Property Set, of a class whose default member has no Property Set (or that has none). The name of
    // a function in its own body is its result, which is not a call.
    private static bool SetsNothing(ProcedureSemanticsDto procedure, ExpressionFactDto callee)
        => callee is { Error: null, TypeKind: DeclaredTypeKind.Class, IsArray: false, Binding: { } binding }
            && binding.AbsoluteUri != procedure.Procedure.AbsoluteUri
            && (callee.Classification == ExpressionClassification.Function
                || callee.Classification == ExpressionClassification.Property
                    && callee.Accessors.HasFlag(PropertyAccessors.Get) && !callee.Accessors.HasFlag(PropertyAccessors.Set))
            && !(callee.DefaultMember?.Accessors ?? PropertyAccessors.None).HasFlag(PropertyAccessors.Set);

    // the name of what is called, as the address of the symbol the callee is bound to has it (Module.Name)
    private static string NameOf(ExpressionFactDto fact)
        => fact.Binding is { } binding ? SymbolUri.NameOf(binding) : string.Empty;
}
