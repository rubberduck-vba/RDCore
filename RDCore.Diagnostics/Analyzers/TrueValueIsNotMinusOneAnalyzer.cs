using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11031</c>: a condition reads a constant that is <c>True</c> with a value other than <c>-1</c> through an operator that works on its bits
/// (<strong>MS-VBAL §5.6.9.8</strong>): <c>#If Not DEV Then</c> with <c>#Const DEV = 1</c> compiles the branch, because <c>Not 1</c> is <c>-2</c>.
/// </summary>
/// <remarks>
/// <para>
/// Which operations are wrong is <see cref="TruthValueOperations"/>'s to say. In code, an operand is read off the syntax tree and the expression facts: a
/// literal, or the binding of the constant a name names (<see cref="ExpressionFactDto.ConstantValue"/>); otherwise its declared type. In a directive, it
/// is read off the module's own <c>#Const</c>s and the predefined constants, which are <c>1</c> where they are true (<see cref="PredefinedConditionalConstants"/>): <c>#If Not Win64</c> is
/// live on 64-bit Windows. Another constant the module does not define, which the project does, is <c>True</c> or <c>False</c> as far as anyone can tell.
/// </para>
/// <para>
/// Only where the operation's truth value is what is tested: the condition of an <c>If</c>, a loop, a <c>Debug.Assert</c>, a <c>#If</c> or a <c>#ElseIf</c>,
/// and the value of a <c>#Const</c>, or an operand of one through <c>Not</c> and the other logical operators. A plain <c>If DEV Then</c> is right, and is not
/// reported; and an operand combined with a number nobody knows (<c>flags And Not MASK</c>) is a mask, which the bitwise-operator hint describes.
/// </para>
/// </remarks>
internal sealed class TrueValueIsNotMinusOneAnalyzer : IModuleAnalyzer
{
    // how many constants a #Const's value may name in turn before its value is no longer looked for.
    private const int ConstantDepth = 8;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => [.. InCode(context), .. InDirectives(context.ParseResult.PrecompilerTrivia)];

    private static IEnumerable<AnalyzerFinding> InCode(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            return [];
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions)
            .GroupBy(fact => fact.Node).ToDictionary(group => group.Key, group => group.First());

        // by the whole address: Uri equality ignores the fragment, which is what names the symbol.
        var constants = semantics.Declarations.Where(declaration => declaration.Kind is DeclarationKind.Constant)
            .Select(declaration => declaration.Symbol.ToString()).ToHashSet(StringComparer.Ordinal);

        // an operand combined with what holds a number nobody knows is a mask: the bits are what is meant.
        bool IsAMask(ExpressionNode sibling)
            => facts.TryGetValue(sibling.Identity, out var fact) && IsNumeric(fact.DeclaredType) && !IsConstant(Unparenthesized(sibling), facts, constants);

        return module.Descendants().Select(ConditionOf).OfType<ExpressionNode>()
            .SelectMany(condition => TruthReads(condition, IsAMask))
            .Where(read => facts.ContainsKey(read.Identity) && MisleadsInCode(read, facts))
            .Select(read => Finding(read, facts[read.Identity].Location.Range));
    }

    // an operation of code that a constant True and not -1 makes wrong: Not, and the logical operators and comparisons that work on its bits.
    private static bool MisleadsInCode(ExpressionNode operation, Dictionary<SyntaxNodeId, ExpressionFactDto> facts) => operation switch
    {
        VBUnaryOperatorExpressionNode { Token: Tokens.LogicalNotOp } not => TruthValueOperations.NotMisleads(OperandInCode(not.Operand, facts)),
        VBBinaryOperatorExpressionNode
        {
            Token: Tokens.LogicalAndOp or Tokens.LogicalXOrOp or Tokens.LogicalEqvOp or Tokens.LogicalImpOp or Tokens.CompareEqualOp or Tokens.CompareNotEqualOp
        } binary => TruthValueOperations.Misleads(binary.Token, OperandInCode(binary.Left, facts), OperandInCode(binary.Right, facts)),
        _ => false,
    };

    // what an operand of code is as a truth value: a literal; a constant, by its binding (a Boolean constant is True or False, whatever number it is given);
    // otherwise what its declared type says it can hold.
    private static TruthOperand OperandInCode(ExpressionNode operand, Dictionary<SyntaxNodeId, ExpressionFactDto> facts)
    {
        switch (operand)
        {
            case VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp } parentheses:
                return OperandInCode(parentheses.Operand, facts);
            case VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp } negation:
                return OperandInCode(negation.Operand, facts).Negated;
            case LiteralExpressionNode literal:
                return TruthOperand.Of(literal.StaticValue);
        }

        facts.TryGetValue(operand.Identity, out var fact);
        if (operand is SimpleNameExpressionNode && fact?.ConstantValue is { } value)
        {
            return TruthOperand.Of(value);
        }

        return fact?.DeclaredType switch
        {
            VBTypeNames.VBBoolean => TruthOperand.AnyBoolean,
            VBTypeNames.VBVariant or VBTypeNames.VBUnknown => TruthOperand.Unknown,
            _ => TruthOperand.Opaque,
        };
    }

    private static IEnumerable<AnalyzerFinding> InDirectives(System.Collections.Immutable.ImmutableArray<SyntaxNode> trivia)
    {
        if (trivia.IsDefaultOrEmpty)
        {
            return [];
        }

        var nodes = trivia.SelectMany(node => (IEnumerable<SyntaxNode>)[node, .. node.Descendants()]).ToList();

        // MS-VBAL §3.4.1: every #Const of the module is processed, those of an excluded branch too; the first of a name is the one (a second is an error).
        var constants = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<ExpressionNode>();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case PrecompilerConstantDeclarationNode { Children: [PrecompilerNameExpressionNode name, ExpressionNode value, ..] }:
                    constants.TryAdd(name.Name, value);
                    roots.Add(value);
                    break;
                case PrecompilerInlineIfStatementNode or PrecompilerElseIfBlockStatementNode when node.Children.OfType<ExpressionNode>().FirstOrDefault() is { } condition:
                    roots.Add(condition);
                    break;
            }
        }

        return roots.SelectMany(root => TruthReads(root, _ => false))
            .Where(read => Misleads(read, constants))
            .Select(read => Finding(read, read.Location.Range));
    }

    private static AnalyzerFinding Finding(ExpressionNode operation, SDK.Model.Source.SourceRange range)
        => new(
            RDCoreDiagnosticId.TrueValueIsNotMinusOne.ToCode(),
            range,
            DiagnosticSeverity.Warning,
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.TrueValueIsNotMinusOne_Message, OperatorOf(operation)));

    private static string OperatorOf(ExpressionNode operation) => operation switch
    {
        VBUnaryOperatorExpressionNode unary => unary.Token,
        VBBinaryOperatorExpressionNode binary => binary.Token,
        _ => string.Empty,
    };

    // what a statement tests as a truth value.
    private static ExpressionNode? ConditionOf(SyntaxNode node) => node switch
    {
        IfBlockStatementNode ifBlock => ifBlock.ConditionExpression,
        ElseIfBlockStatementNode elseIf => elseIf.ConditionExpression,
        InlineIfStatementNode inlineIf => inlineIf.ConditionExpression,
        DoWhileLoopStatementNode loop => loop.ConditionExpression,
        DoUntilLoopStatementNode loop => loop.ConditionExpression,
        DoLoopWhileStatementNode loop => loop.ConditionExpression,
        DoLoopUntilStatementNode loop => loop.ConditionExpression,
        WhileWendStatementNode loop => loop.ConditionExpression,
        DebugAssertStatementNode assert => assert.Condition,
        _ => null,
    };

    // the expressions whose truth value a condition's is made of: the condition, and through parentheses and the logical operators, their operands - an
    // operand of an operation with a mask excepted.
    private static IEnumerable<ExpressionNode> TruthReads(ExpressionNode condition, Func<ExpressionNode, bool> isAMask)
    {
        var pending = new Stack<ExpressionNode>([condition]);
        while (pending.TryPop(out var expression))
        {
            yield return expression;
            switch (expression)
            {
                case VBUnaryOperatorExpressionNode
                {
                    Token: Tokens.LogicalNotOp or OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp,
                } unary:
                    pending.Push(unary.Operand);
                    break;
                case VBBinaryOperatorExpressionNode { Token: Tokens.LogicalAndOp or Tokens.LogicalOrOp or Tokens.LogicalXOrOp or Tokens.LogicalEqvOp or Tokens.LogicalImpOp } binary:
                    if (!isAMask(binary.Right))
                    {
                        pending.Push(binary.Left);
                    }

                    if (!isAMask(binary.Left))
                    {
                        pending.Push(binary.Right);
                    }

                    break;
            }
        }
    }

    // an operation of a directive that a constant True and not -1 makes wrong.
    private static bool Misleads(ExpressionNode operation, Dictionary<string, ExpressionNode> constants) => operation switch
    {
        VBUnaryOperatorExpressionNode { Token: Tokens.LogicalNotOp } not => TruthValueOperations.NotMisleads(OperandOf(not.Operand, constants, 0)),
        VBBinaryOperatorExpressionNode binary => TruthValueOperations.Misleads(binary.Token, OperandOf(binary.Left, constants, 0), OperandOf(binary.Right, constants, 0)),
        _ => false,
    };

    // what an operand of a directive is as a truth value: a literal, a #Const of the module whose value is one, a predefined constant (1 or 0, Win16 always
    // 0), or - a constant the module does not define, which the project does - True or False as far as the module says.
    private static TruthOperand OperandOf(ExpressionNode operand, Dictionary<string, ExpressionNode> constants, int depth) => operand switch
    {
        LiteralExpressionNode literal => TruthOperand.Of(literal.StaticValue),
        VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp } negation => OperandOf(negation.Operand, constants, depth).Negated,
        PrecompilerNameExpressionNode name when constants.TryGetValue(name.Name, out var value) =>
            depth < ConstantDepth ? OperandOf(value, constants, depth + 1) : TruthOperand.Opaque,
        PrecompilerNameExpressionNode name when PredefinedConditionalConstants.CanBeTrue(name.Name) => TruthOperand.OneOrZero,
        PrecompilerNameExpressionNode name when PredefinedConditionalConstants.Names.Contains(name.Name) =>
            new TruthOperand(TruthOperandKind.Number, PredefinedConditionalConstants.False),
        PrecompilerNameExpressionNode => TruthOperand.Unknown,
        VBBinaryOperatorExpressionNode
        {
            Token: Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareLessThanOp or Tokens.CompareLessThanOrEqualOp
                or Tokens.CompareGreaterThanOp or Tokens.CompareGreaterThanOrEqualOp,
        } => TruthOperand.AnyBoolean,
        _ => TruthOperand.Opaque,
    };

    private static ExpressionNode Unparenthesized(ExpressionNode expression)
    {
        while (expression is VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp } parentheses)
        {
            expression = parentheses.Operand;
        }

        return expression;
    }

    private static bool IsConstant(ExpressionNode expression, Dictionary<SyntaxNodeId, ExpressionFactDto> facts, HashSet<string> constants) => expression switch
    {
        LiteralExpressionNode => true,
        VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp } negation => IsConstant(Unparenthesized(negation.Operand), facts, constants),
        SimpleNameExpressionNode name => facts.TryGetValue(name.Identity, out var fact) && fact.Binding is { } binding && constants.Contains(binding.ToString()),
        _ => false,
    };

    private static bool IsNumeric(string? declaredType)
        => declaredType is "Byte" or "Integer" or "Long" or "LongLong" or "LongPtr" or "Single" or "Double" or "Currency" or "Decimal";
}
