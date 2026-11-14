using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01003</c>: a branch of an <c>If</c> statement that has no statement (<strong>MS-VBAL §5.4.2.8</strong>, <strong>§5.4.2.9</strong>).
/// </summary>
/// <remarks>
/// The <c>If</c> or <c>ElseIf</c> branch of a block <c>If</c>, or the <c>Then</c> of a single-line <c>If</c> that has only an <c>Else</c>. A comment, a line label and a
/// declaration are no statement. The finding is the header of the branch, its keyword and its condition.
/// <para>
/// A block <c>If</c> with nothing at all in it, no <c>ElseIf</c> and no <c>Else</c>, on lines of its own, comes with the fix that removes it, where evaluating its
/// condition can neither call a procedure nor raise an error: the program then does the same without it. Such a condition reads only literals and the variables
/// and parameters of the procedure that are declared with a numeric, <c>Boolean</c> or <c>Date</c> type, compares them, and combines the comparisons with
/// <c>Not</c>, <c>And</c>, <c>Or</c>, <c>Xor</c>, <c>Eqv</c>, <c>Imp</c> and parentheses; or tests a variable of an object type with <c>Is</c>.
/// </para>
/// </remarks>
internal sealed class EmptyIfBlockAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        var nodes = module.Descendants().ToArray();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case IfBlockStatementNode block when block.Body.HasNoStatement():
                    yield return Finding(EmptyBlocks.Header(block, block.ConditionExpression), "If", RemovalOf(context.ParseResult, module, block));
                    break;
                case ElseIfBlockStatementNode branch when branch.Body.HasNoStatement():
                    yield return Finding(EmptyBlocks.Header(branch, branch.ConditionExpression), "ElseIf", null);
                    break;
                case InlineIfStatementNode inline when inline.ThenBody.HasNoStatement():
                    yield return Finding(EmptyBlocks.Header(inline, inline.ConditionExpression), "If", null);
                    break;
            }
        }
    }

    private static AnalyzerFinding Finding(SourceRange range, string keyword, EditFix? fix) => new(
        RDCoreDiagnosticId.EmptyIfBlock.ToCode(),
        range,
        DiagnosticSeverity.Information,
        AnalyzerMessages.Format(RDCoreDiagnosticsResources.EmptyIfBlock_Message, keyword))
    {
        Fixes = fix is null ? null : new DiagnosticFixes { Edit = fix },
    };

    // the lines of the block, when nothing else is written on them and its condition is inert.
    private static EditFix? RemovalOf(ModuleParseResult parse, ModuleNode module, IfBlockStatementNode block)
    {
        var range = block.SourceLocation.Range;
        if (!block.Body.Children.IsDefaultOrEmpty || !block.ElseIfBlocks.IsDefaultOrEmpty || block.ElseBlock is not null
            || module.Children.OfType<MemberDeclarationNode>().FirstOrDefault(member => Encloses(member.SourceLocation.Range, range)) is not { } procedure)
        {
            return null;
        }

        // a comment in the If is the user's, and is not removed with it.
        if (parse.Trivia.Any(trivia => Encloses(range, trivia.SourceLocation.Range)) || LineEdits.WholeLinesOf(parse, block) is not { } lines)
        {
            return null;
        }

        return IsValue(Evaluate(block.ConditionExpression, TypesOf(procedure)))
            ? SyntaxEdits.Fix(RDCoreDiagnosticsResources.EmptyIfBlock_Fix, SyntaxEdits.Remove(lines))
            : null;
    }

    private static bool Encloses(SourceRange outer, SourceRange inner) => outer.Start <= inner.Start && outer.End >= inner.End;

    // what reading a name or an operation gives, when it neither calls nor raises: a Boolean (or Null), another Value (a number, a Date, Empty), or an Object
    // reference.
    private enum Kind { None, Value, Boolean, Object }

    // the variables and parameters of the procedure, by what reading them gives; an array, a Variant, a String, a constant and a name with neither an As
    // clause nor a type hint (a Variant, or the type of a DefType) are left out, and so is a name declared twice.
    private static Dictionary<string, Kind> TypesOf(MemberDeclarationNode procedure)
    {
        var types = new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, AsTypeExpressionNode? asType, string? typeHint, bool isArray)
            => types[name] = types.ContainsKey(name) || isArray || asType is { IsArrayDef: true } ? Kind.None : KindOf(asType, typeHint);

        foreach (var parameter in procedure.Children.OfType<ParameterDeclarationNode>())
        {
            Add(parameter.Name, parameter.Children.IsDefault ? null : parameter.Children.OfType<AsTypeExpressionNode>().FirstOrDefault(), parameter.TypeHint,
                parameter.IsArray || parameter.IsParamArray);
        }

        foreach (var variable in procedure.Descendants().OfType<VariableDeclarationNode>())
        {
            Add(variable.Name, variable.Children.OfType<AsTypeExpressionNode>().FirstOrDefault(), variable.TypeHint,
                variable.Children.Any(child => child is not AsTypeExpressionNode));
        }

        foreach (var constant in procedure.Descendants().OfType<ConstantDeclarationNode>())
        {
            types[constant.Name] = Kind.None;
        }

        return types;
    }

    private static Kind KindOf(AsTypeExpressionNode? asType, string? typeHint)
    {
        if (asType is null)
        {
            return typeHint is "%" or "&" or "^" or "!" or "#" or "@" ? Kind.Value : Kind.None;
        }

        if (asType is { QualifierName: null or "" })
        {
            switch (asType.TypeName.ToUpperInvariant())
            {
                case "BOOLEAN":
                    return Kind.Boolean;
                case "BYTE" or "INTEGER" or "LONG" or "LONGLONG" or "LONGPTR" or "SINGLE" or "DOUBLE" or "CURRENCY" or "DECIMAL" or "DATE":
                    return Kind.Value;
                case "STRING" or "VARIANT" or "ANY":
                    return Kind.None;
            }
        }

        // a class, Object, or a type of a library: a reference, which Is compares without calling anything.
        return asType is { AsAutoObject: false, FixedLength: null } ? Kind.Object : Kind.None;
    }

    private static bool IsValue(Kind kind) => kind is Kind.Value or Kind.Boolean;

    private static Kind Evaluate(ExpressionNode expression, Dictionary<string, Kind> types) => expression switch
    {
        LiteralExpressionNode { StaticValue: VBNothingValue } => Kind.Object,
        LiteralExpressionNode { StaticValue: VBBooleanValue or VBNullValue } => Kind.Boolean,
        LiteralExpressionNode { StaticValue: VBStringValue or VBFixedStringValue or VBObjectValue or VBErrorValue or VBUnknownValue } => Kind.None,
        LiteralExpressionNode => Kind.Value,
        SimpleNameExpressionNode name when string.Equals(name.IdentifierName, "Nothing", StringComparison.OrdinalIgnoreCase) => Kind.Object,
        SimpleNameExpressionNode name => types.GetValueOrDefault(name.IdentifierName),
        VBBinaryOperatorExpressionNode { Token: Tokens.CompareIsOp } operation
            => Evaluate(operation.Left, types) is Kind.Object && Evaluate(operation.Right, types) is Kind.Object ? Kind.Boolean : Kind.None,
        VBBinaryOperatorExpressionNode
        {
            Token: Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareLessThanOp or Tokens.CompareLessThanOrEqualOp
                or Tokens.CompareGreaterThanOp or Tokens.CompareGreaterThanOrEqualOp,
        } operation => IsValue(Evaluate(operation.Left, types)) && IsValue(Evaluate(operation.Right, types)) ? Kind.Boolean : Kind.None,
        // Not, And, Or... convert a number to a Long, which a Single, a Double, a Currency or a Decimal can overflow: their operands are Booleans.
        VBBinaryOperatorExpressionNode { Token: Tokens.LogicalAndOp or Tokens.LogicalOrOp or Tokens.LogicalXOrOp or Tokens.LogicalEqvOp or Tokens.LogicalImpOp } operation
            => Evaluate(operation.Left, types) is Kind.Boolean && Evaluate(operation.Right, types) is Kind.Boolean ? Kind.Boolean : Kind.None,
        VBUnaryOperatorExpressionNode { Token: Tokens.LogicalNotOp } operation => Evaluate(operation.Operand, types) is Kind.Boolean ? Kind.Boolean : Kind.None,
        VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp } operation
            => Evaluate(operation.Operand, types),
        _ => Kind.None,
    };
}
