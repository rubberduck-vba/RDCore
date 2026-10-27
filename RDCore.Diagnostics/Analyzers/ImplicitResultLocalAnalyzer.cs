using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Flags;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00115</c>: a function or a property getter that reads its own return value - the function result variable (<strong>MS-VBAL §5.3.1</strong>) - as the
/// local variable it implicitly is.
/// </summary>
/// <remarks>
/// <c>Total = Total + Item</c> in the body of <c>Total</c> works: the name of the procedure is a variable of its body, which holds what it returns. It reads as a
/// call of the procedure, though, which it is anywhere else, and which <c>Total(1)</c> is in a function that takes an argument. A local declared for the work,
/// assigned to the return value once at the end, says what the code does. Each read is reported where it is: the host binds the name to the procedure and does
/// not flag it as what is assigned (<see cref="ValueExpressionSemanticFlags.AssignmentTarget"/>), and the syntax tree says it is not the procedure called.
/// </remarks>
internal sealed class ImplicitResultLocalAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).GroupBy(fact => fact.Node).ToDictionary(group => group.Key, group => group.First());
        foreach (var procedure in module.Descendants().OfType<MemberDeclarationNode>().Where(member => member.MemberKind is MemberKind.Function or MemberKind.PropertyGet))
        {
            var body = procedure.Descendants().ToArray();
            var called = body.OfType<IndexExpressionNode>().Select(call => call.Callee.Identity).ToHashSet();
            foreach (var name in body.OfType<SimpleNameExpressionNode>())
            {
                if (string.Equals(name.IdentifierName, procedure.Name, StringComparison.OrdinalIgnoreCase)
                    && !called.Contains(name.Identity)
                    && facts.TryGetValue(name.Identity, out var fact)
                    && fact.Classification is ExpressionClassification.Function or ExpressionClassification.Property
                    && !fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget))
                {
                    yield return new AnalyzerFinding(
                        RDCoreDiagnosticId.ImplicitResultLocal,
                        name.Location.Range,
                        DiagnosticSeverity.Information,
                        AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitResultLocal_Message, procedure.Name));
                }
            }
        }
    }
}
