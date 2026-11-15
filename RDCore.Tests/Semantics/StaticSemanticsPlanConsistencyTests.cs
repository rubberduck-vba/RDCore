using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.Runtime.LanguageCore.Plans;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Tests.Semantics.Runtime;

namespace RDCore.Tests.Semantics;

/// <summary>
/// The static semantics and the plans agree: what the static semantics accept for operands of declared types that are not Variants or
/// objects, the plan made for the same declared types does not reject.
/// </summary>
/// <remarks>
/// <para>
/// The two are not one table, and are not meant to be. The static semantics give an operation its <em>declared type</em> (a comparison's is Boolean, MS-VBAL
/// 5.6.9.5) and say which let-coercions are valid at all (5.5.1.1); a plan gives an operation its <em>effective type</em>, and a let-coercion its rule, for
/// the types its values are. The static rules are stricter where the specification makes them so - an implicit let-coercion from a LongLong to anything but a
/// LongLong or a Variant is invalid, though the same value let-coerced at run time from a Variant is not - so a plan that accepts what the static semantics
/// reject is no disagreement.
/// </para>
/// <para>
/// The other way around it is: a program the static semantics accept, whose plan for its declared types is a type mismatch, compiles, and fails whenever it
/// runs. That is a bug of one table or the other - but for where the specification itself says so (<see cref="IsSpecifiedToFailWheneverItRuns"/>).
/// </para>
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.9 Operators")]
public sealed class StaticSemanticsPlanConsistencyTests : LetCoercionRuntimeSemanticsTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();

    private static readonly string[] BinaryTokens =
    [
        Tokens.AdditionOp, Tokens.SubtractionOp, Tokens.MultiplicationOp, Tokens.DivisionOp, Tokens.IntegerDivisionOp, Tokens.ModuloOp, Tokens.PowerOp,
        Tokens.ConcatOp,
        Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareGreaterThanOp,
        Tokens.CompareGreaterThanOrEqualOp, Tokens.CompareLikeOp,
        Tokens.LogicalAndOp, Tokens.LogicalOrOp, Tokens.LogicalXOrOp, Tokens.LogicalEqvOp, Tokens.LogicalImpOp,
    ];

    // the declared types whose value type is the declared type: no Variant, no object, whose values decide.
    private static VBType[] DeclaredTypes() =>
    [
        VBByteType.TypeInfo, VBBooleanType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBLongPtrType_x64.TypeInfo,
        VBSingleType.TypeInfo, VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo, VBDateType.TypeInfo,
        VBStringType.TypeInfo, new VBFixedStringType(3),
        VBErrorType.TypeInfo, VBNullType.TypeInfo, VBEmptyType.TypeInfo,
        VBResizableByteArrayType.TypeInfo, new VBResizableArrayType(VBLongType.TypeInfo), new VBFixedSizeArrayType(VBLongType.TypeInfo),
        Colour(), Point(),
    ];

    private static VBEnumType Colour() => new(
        new VBEnumMemberSymbol(Root, TestUri.TestModuleUri(), "Colour", ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), members: []);

    private static VBUserDefinedType Point()
    {
        var uri = new Uri($"{Root}#Point");
        return new VBUserDefinedType(new VBUserDefinedTypeMemberSymbol(uri, uri, "Point", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), []);
    }

    private static LiteralExpressionNode Operand() => new(NodeId, TestLocations.TestLocation, new VBLongValue(1));

    // where the specification accepts statically what it makes a type mismatch whenever it runs:
    // - MS-VBAL 5.6.9.5: a comparison is invalid only "if the declared type of any operand is an array or UDT", and an Error operand has an effective type
    //   only with another Error;
    // - MS-VBAL 5.5.1.1 lists no Byte() source as invalid but for a fixed-size array or a resizable array of another element type, and 5.5.1.2.6 let-coerces a
    //   Byte() to nothing but a Byte() and a String.
    private static bool IsSpecifiedToFailWheneverItRuns(VBType left, string? token, VBType right) => token is null
        ? left is VBResizableByteArrayType && right is INumericType or VBBooleanType or VBDateType or VBEnumType or VBLongPtrType_x64
        : token is Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareLessThanOp or Tokens.CompareLessThanOrEqualOp
            or Tokens.CompareGreaterThanOp or Tokens.CompareGreaterThanOrEqualOp
            && (left is VBErrorType) != (right is VBErrorType);

    [TestMethod]
    public void AnOperation_TheStaticSemanticsAccept_IsNoTypeMismatch_ByItsPlan()
    {
        var disagreements = new List<string>();
        foreach (var token in BinaryTokens)
        {
            var rule = ExpressionStaticSemanticsEvaluator.ResolveBinaryOperatorRule(token)!;
            foreach (var left in DeclaredTypes())
            {
                foreach (var right in DeclaredTypes())
                {
                    var (l, r) = (ExpressionStaticSemanticsEvaluator.AsOperand(left), ExpressionStaticSemanticsEvaluator.AsOperand(right));
                    var expression = new VBBinaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, Operand(), Operand());
                    if (rule.DetermineDeclaredType(default, expression, l, r) is { IsSuccess: true, Result: var declared }
                        && OperationPlanner.ForDeclared(token, l, r) is { IsTypeMismatch: true }
                        && !IsSpecifiedToFailWheneverItRuns(left, token, right))
                    {
                        disagreements.Add($"{left.Name} {token} {right.Name}: statically {declared?.Name}, a type mismatch by its plan");
                    }
                }
            }
        }

        Assert.IsEmpty(disagreements, string.Join(Environment.NewLine, disagreements));
    }

    [TestMethod]
    public void ALetCoercion_TheStaticSemanticsAccept_IsNoTypeMismatch_ByItsPlan()
    {
        var disagreements = new List<string>();
        foreach (var destination in DeclaredTypes())
        {
            foreach (var source in DeclaredTypes())
            {
                if (LetCoercionStaticSemantics.Instance.DetermineDeclaredType(default, Operand(), source, destination).IsSuccess
                    && ConversionPlanner.ForDeclared(source, destination) is { IsTypeMismatch: true }
                    && !IsSpecifiedToFailWheneverItRuns(source, null, destination))
                {
                    disagreements.Add($"{source.Name} -> {destination.Name}: statically valid, a type mismatch by its plan");
                }
            }
        }

        Assert.IsEmpty(disagreements, string.Join(Environment.NewLine, disagreements));
    }
}
