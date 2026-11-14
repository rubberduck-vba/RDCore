using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11028</c>: <c>New</c> of a class whose objects are never created (<c>New ErrObject</c>), and a variable declared <c>As New</c> of one
/// (<c>Dim e As New ErrObject</c>), which MS-VBA compiles.
/// </summary>
/// <remarks>
/// The class's library says it can be created, so <strong>MS-VBAL §5.6.8</strong> and <strong>§5.2.3.1.4</strong> make <c>New</c> of it valid; nothing
/// creates one, and creating it raises run-time error -2147221164 <em>Class not registered</em> (recorded in Excel). That holds of every
/// path, whether or not the run-time facts reach the code, and it is reported wherever it is written, at Warning severity. The analyzer reads which classes
/// are not registered off the module's class facts, each <c>New</c> expression off the syntax tree with the class it is bound to, and the class of each
/// variable declared <c>As New</c> off its declaration fact.
/// </remarks>
internal sealed class NewOfAClassNotCreatableAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            return [];
        }

        // by the whole address: Uri equality ignores the fragment, which is what names the class.
        var unregistered = (semantics.Classes ?? []).Where(created => !created.IsRegistered)
            .Select(created => created.Class.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        if (unregistered.Count == 0)
        {
            return [];
        }

        var declarations = semantics.Declarations
            .Where(declaration => declaration.AutoInstantiates is { } created && unregistered.Contains(created.AbsoluteUri))
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.NewOfAClassNotCreatable.ToCode(),
                declaration.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.NewOfAClassNotCreatable_AsNew_Message, declaration.Name)));

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions)
            .GroupBy(fact => fact.Node).ToDictionary(group => group.Key, group => group.First());
        var expressions = (context.ParseResult.SyntaxTree?.Descendants() ?? []).OfType<NewExpressionNode>()
            .Select(created => facts.GetValueOrDefault(created.Identity))
            .Where(fact => fact?.Binding is { } created && unregistered.Contains(created.AbsoluteUri))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.NewOfAClassNotCreatable.ToCode(),
                fact!.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.NewOfAClassNotCreatable_New_Message, NameOf(fact))));

        return declarations.Concat(expressions);
    }

    // the name of the class created, as the address of the class the expression is bound to names it: an address escapes a name that is not ASCII.
    private static string NameOf(ExpressionFactDto fact)
        => fact.Binding is { IsAbsoluteUri: true } binding ? SymbolUri.NameOf(binding) : string.Empty;
}
