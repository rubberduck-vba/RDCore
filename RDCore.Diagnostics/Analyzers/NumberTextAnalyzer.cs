using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11040</c>, <c>RDC11041</c> and <c>RDC11042</c>: a number, or an <c>Error</c> value, whose text MS-VBA writes otherwise than the specification. RD-VBA
/// writes it as MS-VBA does.
/// </summary>
/// <remarks>
/// <para>
/// <c>RDC11040</c>: <c>Str</c> or <c>Str$</c> (<strong>MS-VBAL §6.1.2.3.1.20</strong>) of a number between -1 and 1 has no zero before its decimal point
/// (<c>" .5"</c>), where the specification writes <c>" 0.5"</c>. <c>RDC11042</c>: <c>Write #</c> (<strong>MS-VBAL §5.4.5.9</strong>) writes such a number the
/// same way (<c>.5</c>). A value is found by its declared type: <c>Single</c>, <c>Double</c>, <c>Currency</c>, <c>Decimal</c> or <c>Variant</c>; a literal
/// only when it is such a number.
/// </para>
/// <para>
/// <c>RDC11041</c>: <c>Str</c> or <c>Str$</c> of an <c>Error</c> value raises run-time error 13, where the specification writes <c>"Error "</c> and its code.
/// The argument is found when it is a call to <c>CVErr</c>.
/// </para>
/// </remarks>
internal sealed class NumberTextAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.StrOfAFraction.ToCode(), RDCoreDiagnosticId.StrOfAnErrorValue.ToCode(), RDCoreDiagnosticId.WriteOfAFraction.ToCode()];

    private static readonly HashSet<string> Str = new(StringComparer.OrdinalIgnoreCase) { "Conversion.Str", "Conversion.Str$" };
    private const string CVErr = "Conversion.CVErr";

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            return [];
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
        var findings = new List<AnalyzerFinding>();
        foreach (var call in module.Descendants().OfType<IndexExpressionNode>())
        {
            if (!facts.TryGetValue(call.Callee.Identity, out var callee) || callee.Binding is not { } binding || !Str.Contains(SymbolUri.QualifiedNameOf(binding))
                || Argument(call) is not { } argument)
            {
                continue;
            }

            var name = SymbolUri.NameOf(binding);
            var value = Unparenthesized(argument);
            if (value is IndexExpressionNode inner && facts.TryGetValue(inner.Callee.Identity, out var innerCallee) && IsBoundTo(innerCallee, CVErr))
            {
                findings.Add(Finding(RDCoreDiagnosticId.StrOfAnErrorValue, call.Location.Range, RDCoreDiagnosticsResources.StrOfAnErrorValue_Message, name));
            }
            else if (CanBeAFraction(value, facts))
            {
                findings.Add(Finding(RDCoreDiagnosticId.StrOfAFraction, call.Location.Range, RDCoreDiagnosticsResources.StrOfAFraction_Message, name));
            }
        }

        foreach (var write in module.Descendants().OfType<PrintStatementNode>().Where(statement => statement.Token == Tokens.Write))
        {
            findings.AddRange(write.Items.OfType<PrintOutputItemNode>()
                .Where(item => item.Value is { } value && CanBeAFraction(Unparenthesized(value), facts))
                .Select(item => Finding(RDCoreDiagnosticId.WriteOfAFraction, item.Value!.Location.Range, RDCoreDiagnosticsResources.WriteOfAFraction_Message, Tokens.Write)));
        }

        return findings.OrderBy(finding => finding.Range.Start);
    }

    private static AnalyzerFinding Finding(RDCoreDiagnosticId id, SourceRange range, string message, string name)
        => new(id.ToCode(), range, DiagnosticSeverity.Warning, AnalyzerMessages.Format(message, name));

    private static bool IsBoundTo(ExpressionFactDto fact, string qualifiedName)
        => fact.Binding is { } binding && string.Equals(SymbolUri.QualifiedNameOf(binding), qualifiedName, StringComparison.OrdinalIgnoreCase);

    // the argument given to Str's one parameter: by its name, or the first one.
    private static ExpressionNode? Argument(IndexExpressionNode call)
    {
        var named = call.Arguments.OfType<NamedArgumentNode>().FirstOrDefault(argument => string.Equals(argument.Name, "Number", StringComparison.OrdinalIgnoreCase));
        if (named is not null)
        {
            return named.Value;
        }

        return call.Arguments.Length > 0 && call.Arguments[0] is not (NamedArgumentNode or MissingArgumentNode) ? call.Arguments[0] : null;
    }

    // a literal is one when it is a number between -1 and 1 other than 0; anything else when its declared type can hold such a number.
    private static bool CanBeAFraction(ExpressionNode value, Dictionary<SyntaxNodeId, ExpressionFactDto> facts)
    {
        var operand = value;
        while (operand is VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp } negation)
        {
            operand = Unparenthesized(negation.Operand);
        }

        if (operand is LiteralExpressionNode literal)
        {
            return literal.StaticValue is VBNumericTypedValue number && Math.Abs(number.AsDouble) is > 0 and < 1;
        }

        return facts.TryGetValue(value.Identity, out var fact) && fact.DeclaredType is "Single" or "Double" or "Currency" or "Decimal" or "Variant";
    }

    private static ExpressionNode Unparenthesized(ExpressionNode expression)
    {
        while (expression is VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp } parentheses)
        {
            expression = parentheses.Operand;
        }

        return expression;
    }
}
