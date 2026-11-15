using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Text;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A golden master of the static semantics of the operators and of let-coercion: the declared type, or the compile error, of every operator over every pair
/// of a broad set of declared types, and whether each let-coercion between them is valid - compared line for line with what was recorded before the static
/// semantics were reconciled with the operation and conversion plans. A line that differs is a change in behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Each operation is asked of the rule <see cref="ExpressionStaticSemanticsEvaluator"/> resolves for its operator, with its operands as the evaluator gives
/// them (an Enum is a <c>Long</c>): an operator that has no rule is recorded as one.
/// </para>
/// <para>
/// The snapshot is <c>GoldenMasters/static-semantics.txt.gz</c>, next to this file, recorded and compared as <see cref="GoldenMaster"/> says.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.9 Operators")]
public sealed class StaticSemanticsGoldenMasterTests : LetCoercionRuntimeSemanticsTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();

    private static readonly string[] BinaryTokens =
    [
        Tokens.AdditionOp, Tokens.SubtractionOp, Tokens.MultiplicationOp, Tokens.DivisionOp, Tokens.IntegerDivisionOp, Tokens.ModuloOp, Tokens.PowerOp,
        Tokens.ConcatOp,
        Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareGreaterThanOp,
        Tokens.CompareGreaterThanOrEqualOp, Tokens.CompareLikeOp, Tokens.CompareIsOp,
        Tokens.LogicalAndOp, Tokens.LogicalOrOp, Tokens.LogicalXOrOp, Tokens.LogicalEqvOp, Tokens.LogicalImpOp,
    ];

    private static readonly string[] UnaryTokens = [Tokens.NegationOp, Tokens.LogicalNotOp, OperatorSymbolNames.UnaryLetCoerceOp];

    // every type a variable, a parameter or a function can be declared as - and Decimal, Error, Null and Empty, which a library's member can be.
    private static VBType[] DeclaredTypes() =>
    [
        VBByteType.TypeInfo, VBBooleanType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBLongPtrType_x64.TypeInfo,
        VBSingleType.TypeInfo, VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo, VBDateType.TypeInfo,
        VBStringType.TypeInfo, new VBFixedStringType(3),
        VBVariantType.TypeInfo, VBObjectType.TypeInfo, VBErrorType.TypeInfo, VBNullType.TypeInfo, VBEmptyType.TypeInfo,
        VBResizableByteArrayType.TypeInfo, new VBResizableArrayType(VBLongType.TypeInfo), new VBResizableArrayType(VBVariantType.TypeInfo),
        new VBFixedSizeArrayType(VBLongType.TypeInfo),
        Colour(), Point(), Widget(),
    ];

    private static VBEnumType Colour() => new(
        new VBEnumMemberSymbol(Root, TestUri.TestModuleUri(), "Colour", ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), members: []);

    private static VBUserDefinedType Point()
    {
        var uri = new Uri($"{Root}#Point");
        return new VBUserDefinedType(new VBUserDefinedTypeMemberSymbol(uri, uri, "Point", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), []);
    }

    private static VBClassType Widget() => new(new VBClassModuleSymbol(Root, Root, "Widget"), []);

    private static LiteralExpressionNode Operand() => new(NodeId, TestLocations.TestLocation, new VBLongValue(1));

    [TestMethod]
    public void TheStaticSemantics_DoWhatTheyWereRecordedToDo() => GoldenMaster.AssertMatches(Record(), "static-semantics.txt.gz");

    private static string Record()
    {
        var text = new StringBuilder();
        foreach (var token in BinaryTokens)
        {
            var rule = ExpressionStaticSemanticsEvaluator.ResolveBinaryOperatorRule(token);
            foreach (var left in DeclaredTypes())
            {
                foreach (var right in DeclaredTypes())
                {
                    var expression = new VBBinaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, Operand(), Operand());
                    var outcome = rule is null ? "no rule" : Outcome(() => rule.DetermineDeclaredType(default, expression,
                        ExpressionStaticSemanticsEvaluator.AsOperand(left), ExpressionStaticSemanticsEvaluator.AsOperand(right)));
                    text.Append($"{GoldenMaster.Describe(left)} {token} {GoldenMaster.Describe(right)} => {outcome}\n");
                }
            }
        }

        foreach (var token in UnaryTokens)
        {
            var rule = ExpressionStaticSemanticsEvaluator.ResolveUnaryOperatorRule(token);
            foreach (var operand in DeclaredTypes())
            {
                var expression = new VBUnaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, [Operand()]);
                var outcome = rule is null ? "no rule" : Outcome(() => rule.DetermineDeclaredType(default, expression, ExpressionStaticSemanticsEvaluator.AsOperand(operand)));
                text.Append($"{token} {GoldenMaster.Describe(operand)} => {outcome}\n");
            }
        }

        // MS-VBAL 5.5.1.1: which let-coercions are valid at all, from one declared type to another.
        foreach (var destination in DeclaredTypes())
        {
            foreach (var source in DeclaredTypes())
            {
                var outcome = Outcome(() => LetCoercionStaticSemantics.Instance.DetermineDeclaredType(default, Operand(), source, destination));
                text.Append($"{GoldenMaster.Describe(source)} -> {GoldenMaster.Describe(destination)} => {outcome}\n");
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string Outcome(Func<StaticSemanticsEvaluationResult> evaluate)
    {
        try
        {
            var result = evaluate();
            return result.ErrorInfo is { } error ? $"compile error {error.VBCompileErrorId}" : result.Result is { } type ? GoldenMaster.Describe(type) : "nothing";
        }
        catch (Exception exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }
}
