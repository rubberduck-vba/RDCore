using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00219</c>: a <c>Case</c> block, or the <c>Case Else</c>, of a <c>Select Case</c> statement (<strong>MS-VBAL §5.4.2.10</strong>) that no value of
/// the selector reaches: the clauses of the blocks before it match every value its own clauses match, or every value of the selector's type, or its range
/// clauses each start above where they end.
/// </summary>
/// <remarks>
/// The blocks are tried in order, and the first whose clause matches runs. The analyzer reads the declared type of the selector (its
/// <c>ExpressionFactDto</c>): <c>Byte</c>, <c>Integer</c>, <c>Long</c>, <c>LongLong</c> or an <c>Enum</c>, which is a <c>Long</c>, and <c>Boolean</c>. A
/// clause is known when its values are literals or constants (<c>ConstantValue</c>) of an integral type, negated or not, that the selector's type holds,
/// and, for a <c>Boolean</c> selector, a value clause of <c>True</c> or <c>False</c>. Such a value is compared with the selector as it is. A clause of any
/// other value is converted to the selector's type first, and is not known: it matches nothing the analyzer counts, and a block with one is reported only
/// when the blocks before it match every value of the type. Nothing is reported for a selector of another type.
/// </remarks>
internal sealed class UnreachableCaseAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.UnreachableCase.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).ToLookup(fact => fact.Location.Range);
        foreach (var select in module.Descendants().OfType<SelectCaseStatementNode>())
        {
            if (DomainOf(facts[select.ControlExpression.SourceLocation.Range].FirstOrDefault()) is not { } domain)
            {
                continue;
            }

            var covered = ValueSet.Empty;
            foreach (var block in select.CaseExpressionBlocks)
            {
                var clauses = block.RangeClauses.Select(clause => ValuesOf(clause, domain, facts)).ToArray();
                var message = covered.Covers(domain.All) || clauses.All(clause => clause is { } known && covered.Covers(known.Values))
                    ? clauses.All(clause => clause is { IsInverted: true })
                        ? RDCoreDiagnosticsResources.UnreachableCase_Inverted_Message
                        : RDCoreDiagnosticsResources.UnreachableCase_Message
                    : null;
                if (message is not null && block.RangeClauses is [.., var last])
                {
                    yield return Finding(new SourceRange(block.Location.Range.Start, last.SourceLocation.Range.End), message);
                }

                covered = clauses.OfType<Clause>().Aggregate(covered, (set, clause) => set.With(clause.Values));
            }

            if (select.CaseElseBlock is { } caseElse && covered.Covers(domain.All))
            {
                yield return Finding(SyntaxEdits.Following(caseElse.SourceLocation.Range.Start, Tokens.Case.Length),
                    RDCoreDiagnosticsResources.UnreachableCase_Else_Message);
            }
        }
    }

    private static AnalyzerFinding Finding(SourceRange range, string message)
        => new(RDCoreDiagnosticId.UnreachableCase.ToCode(), range, DiagnosticSeverity.Warning, message);

    // the values of the selector's declared type, and whether it is a Boolean, whose values are True and False only.
    private sealed record class Domain(ValueSet All, bool IsBoolean);

    // what a clause matches, and whether it is a range that starts above where it ends.
    private sealed record class Clause(ValueSet Values, bool IsInverted = false);

    private static Domain? DomainOf(ExpressionFactDto? selector) => selector switch
    {
        null or { IsArray: true } or { Error: not null } => null,
        { TypeKind: DeclaredTypeKind.Enum } => Integral(int.MinValue, int.MaxValue),
        { TypeKind: DeclaredTypeKind.Intrinsic, DeclaredType: var type } => type switch
        {
            _ when Is(type, Tokens.Byte) => Integral(byte.MinValue, byte.MaxValue),
            _ when Is(type, Tokens.Integer) => Integral(short.MinValue, short.MaxValue),
            _ when Is(type, Tokens.Long) => Integral(int.MinValue, int.MaxValue),
            _ when Is(type, Tokens.LongLong) => Integral(long.MinValue, long.MaxValue),
            _ when Is(type, Tokens.Boolean) => new Domain(ValueSet.Of(-1, -1).With(ValueSet.Of(0, 0)), IsBoolean: true),
            _ => null,
        },
        _ => null,
    };

    private static bool Is(string? type, string name) => string.Equals(type, name, StringComparison.OrdinalIgnoreCase);

    private static Domain Integral(Int128 min, Int128 max) => new(ValueSet.Of(min, max), IsBoolean: false);

    private static Clause? ValuesOf(CaseRangeClauseNode clause, Domain domain, ILookup<SourceRange, ExpressionFactDto> facts)
    {
        if (domain.IsBoolean)
        {
            return clause is CaseValueRangeClauseNode { Value: var value } && BooleanOf(value, facts) is { } truth
                ? new Clause(ValueSet.Of(truth, truth))
                : null;
        }

        var (min, max) = domain.All.Bounds;
        switch (clause)
        {
            case CaseValueRangeClauseNode { Value: var value } when IntegralOf(value, domain, facts) is { } single:
                return new Clause(ValueSet.Of(single, single));
            case CaseToRangeClauseNode { Start: var start, End: var end }
                when IntegralOf(start, domain, facts) is { } from && IntegralOf(end, domain, facts) is { } to:
                return from <= to ? new Clause(ValueSet.Of(from, to)) : new Clause(ValueSet.Empty, IsInverted: true);
            case CaseComparisonRangeClauseNode { ComparisonOperator: var comparison, Value: var value } when IntegralOf(value, domain, facts) is { } bound:
                return comparison switch
                {
                    Tokens.CompareEqualOp => new Clause(ValueSet.Of(bound, bound)),
                    Tokens.CompareNotEqualOp => new Clause(ValueSet.Of(min, bound - 1).With(ValueSet.Of(bound + 1, max))),
                    Tokens.CompareLessThanOp => new Clause(ValueSet.Of(min, bound - 1)),
                    Tokens.CompareLessThanOrEqualOp => new Clause(ValueSet.Of(min, bound)),
                    Tokens.CompareGreaterThanOp => new Clause(ValueSet.Of(bound + 1, max)),
                    Tokens.CompareGreaterThanOrEqualOp => new Clause(ValueSet.Of(bound, max)),
                    _ => null,
                };
            default:
                return null;
        }
    }

    // a literal or a constant of an integral type, negated or not, that the selector's type holds: compared with the selector as it is.
    private static Int128? IntegralOf(ExpressionNode expression, Domain domain, ILookup<SourceRange, ExpressionFactDto> facts)
    {
        var value = expression is VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp or OperatorSymbolNames.UnaryArithmeticNegationOp } negation
            ? -IntegralOf(negation.Operand, facts)
            : IntegralOf(expression, facts);
        var (min, max) = domain.All.Bounds;
        return value is { } known && known >= min && known <= max ? known : null;
    }

    private static Int128? IntegralOf(ExpressionNode expression, ILookup<SourceRange, ExpressionFactDto> facts)
        => ValueOf(expression, facts) switch
        {
            VBByteValue { IsIndeterminate: false } value => value.Value,
            VBIntegerValue { IsIndeterminate: false } value => value.Value,
            VBLongValue { IsIndeterminate: false } value => value.Value,
            VBLongLongValue { IsIndeterminate: false } value => value.Value,
            _ => null,
        };

    // True is -1, and False is 0.
    private static Int128? BooleanOf(ExpressionNode expression, ILookup<SourceRange, ExpressionFactDto> facts)
        => ValueOf(expression, facts) is VBBooleanValue { IsIndeterminate: false } value ? value.Value.StoredValue : null;

    private static VBTypedValue? ValueOf(ExpressionNode expression, ILookup<SourceRange, ExpressionFactDto> facts)
        => expression is LiteralExpressionNode literal
        ? literal.StaticValue
        : facts[expression.SourceLocation.Range].FirstOrDefault()?.ConstantValue;

    // a set of whole numbers, as the ranges it is made of, in order, none of them touching another.
    private sealed record class ValueSet(IReadOnlyList<(Int128 From, Int128 To)> Ranges)
    {
        public static ValueSet Empty { get; } = new([]);

        public static ValueSet Of(Int128 from, Int128 to) => from <= to ? new([(from, to)]) : Empty;

        public (Int128 Min, Int128 Max) Bounds => (Ranges[0].From, Ranges[^1].To);

        public ValueSet With(ValueSet other)
        {
            var merged = new List<(Int128 From, Int128 To)>();
            foreach (var range in Ranges.Concat(other.Ranges).OrderBy(range => range.From))
            {
                if (merged.Count > 0 && range.From <= merged[^1].To + 1)
                {
                    merged[^1] = (merged[^1].From, Int128.Max(merged[^1].To, range.To));
                }
                else
                {
                    merged.Add(range);
                }
            }

            return new(merged);
        }

        public bool Covers(ValueSet other) => other.Ranges.All(range => Ranges.Any(own => own.From <= range.From && range.To <= own.To));
    }
}
