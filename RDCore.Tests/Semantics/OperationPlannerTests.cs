using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Plans;

namespace RDCore.Tests.Semantics;

/// <summary>
/// The plan of an operation: decided from the types of the operands alone, once, by the one table the binder and the interpreter both consult.
/// What each operation yields is pinned by <c>ArithmeticOperatorsGoldenMasterTests</c> and <c>OtherOperatorsGoldenMasterTests</c>; this is what a plan is.
/// </summary>
[TestClass]
public sealed class OperationPlannerTests
{
    [TestMethod]
    public void APlan_IsMadeOnce_ForEachOperatorAndPairOfTypes()
    {
        var first = OperationPlanner.ForValues(Tokens.AdditionOp, VBIntegerType.TypeInfo, VBLongType.TypeInfo);
        var second = OperationPlanner.ForValues(Tokens.AdditionOp, VBIntegerType.TypeInfo, VBLongType.TypeInfo);

        Assert.AreSame(first, second);
        Assert.AreEqual(VBLongType.TypeInfo, first.EffectiveType);
        CollectionAssert.AreEqual(new SDK.Model.Types.Abstract.VBType[] { VBIntegerType.TypeInfo, VBLongType.TypeInfo }, first.Operands.ToArray());
    }

    [TestMethod]
    public void APlan_FoundByTheIdentityOfItsTypes_IsThePlanFoundByTheirValue()
    {
        // two fixed-length strings of the same length are the same type, in two instances: the second is found by value, and then by identity.
        var first = OperationPlanner.ForValues(Tokens.ConcatOp, new VBFixedStringType(5), VBStringType.TypeInfo);
        var other = new VBFixedStringType(5);

        Assert.AreSame(first, OperationPlanner.ForValues(Tokens.ConcatOp, other, VBStringType.TypeInfo));
        Assert.AreSame(first, OperationPlanner.ForValues(Tokens.ConcatOp, other, VBStringType.TypeInfo));
        Assert.AreSame(OperationPlanner.ForValue(Tokens.LogicalNotOp, other), OperationPlanner.ForValue(Tokens.LogicalNotOp, new VBFixedStringType(5)));
    }

    [TestMethod]
    public void ThePlan_OfValues_IsFinal_WhateverTheTypes()
    {
        var plan = OperationPlanner.ForValues(Tokens.AdditionOp, VBObjectType.TypeInfo, VBLongType.TypeInfo);
        Assert.IsFalse(plan.IsDeferred, "Nothing reaches an operator as an Object value, which no row of the table takes");
        Assert.IsTrue(plan.IsTypeMismatch);
    }

    [TestMethod]
    public void ThePlan_OfAnOperandDeclaredVariantOrObject_IsDeferred()
    {
        foreach (var declared in new SDK.Model.Types.Abstract.VBType[] { VBVariantType.TypeInfo, VBObjectType.TypeInfo })
        {
            var plan = OperationPlanner.ForDeclared(Tokens.MultiplicationOp, declared, VBLongType.TypeInfo);
            Assert.IsTrue(plan.IsDeferred);
            Assert.IsNull(plan.EffectiveType);
            Assert.IsFalse(plan.IsTypeMismatch, "a deferred plan is not a mismatch: the value decides");
            Assert.IsTrue(OperationPlanner.ForDeclared(Tokens.NegationOp, declared).IsDeferred);
        }
    }

    [TestMethod]
    public void ThePlan_OfALateBoundOperand_IsDeferred()
    {
        // an operand whose type is not known statically is known when the operation is evaluated.
        Assert.IsTrue(OperationPlanner.ForDeclared(Tokens.AdditionOp, VBUnknownType.TypeInfo, VBLongType.TypeInfo).IsDeferred);
        Assert.IsTrue(OperationPlanner.ForDeclared(Tokens.LogicalNotOp, VBUnknownType.TypeInfo).IsDeferred);
    }

    [TestMethod]
    public void ThePlan_OfOtherDeclaredTypes_IsThePlanOfTheirValues()
    {
        Assert.AreSame(
            OperationPlanner.ForValues(Tokens.DivisionOp, VBIntegerType.TypeInfo, VBLongType.TypeInfo),
            OperationPlanner.ForDeclared(Tokens.DivisionOp, VBIntegerType.TypeInfo, VBLongType.TypeInfo));

        // a fixed-length string holds a String.
        Assert.AreSame(
            OperationPlanner.ForValues(Tokens.AdditionOp, VBStringType.TypeInfo, VBStringType.TypeInfo),
            OperationPlanner.ForDeclared(Tokens.AdditionOp, new VBFixedStringType(3), VBStringType.TypeInfo));
    }

    [TestMethod]
    public void ADateOperation_IsEvaluatedInDouble_AndItsResultIsADate()
    {
        var plan = OperationPlanner.ForValues(Tokens.AdditionOp, VBDateType.TypeInfo, VBIntegerType.TypeInfo);
        Assert.AreEqual(VBDateType.TypeInfo, plan.EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, plan.OperandDestination);
    }

    [TestMethod]
    public void ANullOperation_CoercesNothing()
    {
        var plan = OperationPlanner.ForValues(Tokens.SubtractionOp, VBNullType.TypeInfo, VBLongType.TypeInfo);
        Assert.AreEqual(VBNullType.TypeInfo, plan.EffectiveType);
        Assert.IsNull(plan.OperandDestination);
    }

    [TestMethod]
    public void AnOperationTheTableHasNoRowFor_IsATypeMismatch_AndCoercesNothing()
    {
        var plan = OperationPlanner.ForValues(Tokens.AdditionOp, VBErrorType.TypeInfo, VBLongType.TypeInfo);
        Assert.IsTrue(plan.IsTypeMismatch);
        Assert.IsNull(plan.OperandDestination);
    }

    [TestMethod]
    public void NegationAndSubtraction_ShareAToken_AndAreToldApartByHowManyOperandsTheyHave()
    {
        Assert.AreEqual(VBIntegerType.TypeInfo, OperationPlanner.ForValue(Tokens.NegationOp, VBByteType.TypeInfo).EffectiveType, "a Byte has no negative values");
        Assert.AreEqual(VBByteType.TypeInfo, OperationPlanner.ForValues(Tokens.SubtractionOp, VBByteType.TypeInfo, VBByteType.TypeInfo).EffectiveType);
    }

    [TestMethod]
    public void EachOperator_HasItsOwnRows_OverTheArithmeticTable()
    {
        // '+' of two strings concatenates them; '/' of two integral values is a Double; '\' of a Double is a Long; '^' is a Double; '*' of a Date is a Double.
        Assert.AreEqual(VBStringType.TypeInfo, OperationPlanner.ForValues(Tokens.AdditionOp, VBStringType.TypeInfo, VBStringType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.SubtractionOp, VBStringType.TypeInfo, VBStringType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.DivisionOp, VBIntegerType.TypeInfo, VBIntegerType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongType.TypeInfo, OperationPlanner.ForValues(Tokens.IntegerDivisionOp, VBDoubleType.TypeInfo, VBIntegerType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongType.TypeInfo, OperationPlanner.ForValues(Tokens.ModuloOp, VBDoubleType.TypeInfo, VBIntegerType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.PowerOp, VBIntegerType.TypeInfo, VBIntegerType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.MultiplicationOp, VBDateType.TypeInfo, VBIntegerType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDateType.TypeInfo, OperationPlanner.ForValues(Tokens.SubtractionOp, VBDateType.TypeInfo, VBIntegerType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.SubtractionOp, VBDateType.TypeInfo, VBDateType.TypeInfo).EffectiveType, "the days between two dates");
    }

    [TestMethod]
    public void ABoolean_IsAnOperand_OfEveryRowAnIntegralValueIsAnOperandOf()
    {
        // MS-VBAL's tables list Boolean beside Byte and Integer: True + 2.5 is a Double, True = 2.5 compares Doubles, True And 7& is a Long.
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.AdditionOp, VBBooleanType.TypeInfo, VBDoubleType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongLongType.TypeInfo, OperationPlanner.ForValues(Tokens.SubtractionOp, VBLongLongType.TypeInfo, VBBooleanType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBNullType.TypeInfo, OperationPlanner.ForValues(Tokens.MultiplicationOp, VBBooleanType.TypeInfo, VBNullType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareEqualOp, VBBooleanType.TypeInfo, VBDoubleType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDateType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareLessThanOp, VBDateType.TypeInfo, VBBooleanType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongType.TypeInfo, OperationPlanner.ForValues(Tokens.LogicalAndOp, VBBooleanType.TypeInfo, VBLongType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongType.TypeInfo, OperationPlanner.ForValues(Tokens.LogicalOrOp, VBStringType.TypeInfo, VBBooleanType.TypeInfo).EffectiveType);
    }

    [TestMethod]
    public void ASingle_AndALongLong_AreComparedAsDoubles()
    {
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareEqualOp, VBSingleType.TypeInfo, VBLongLongType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareLessThanOp, VBLongLongType.TypeInfo, VBSingleType.TypeInfo).EffectiveType);
    }

    [TestMethod]
    public void EveryOperator_IsPlanned()
    {
        string[] binary =
        [
            Tokens.AdditionOp, Tokens.SubtractionOp, Tokens.MultiplicationOp, Tokens.DivisionOp, Tokens.IntegerDivisionOp, Tokens.ModuloOp, Tokens.PowerOp,
            Tokens.ConcatOp, Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp,
            Tokens.CompareGreaterThanOp, Tokens.CompareGreaterThanOrEqualOp, Tokens.CompareLikeOp, Tokens.CompareIsOp,
            Tokens.LogicalAndOp, Tokens.LogicalOrOp, Tokens.LogicalXOrOp, Tokens.LogicalEqvOp, Tokens.LogicalImpOp,
        ];
        foreach (var token in binary)
        {
            Assert.IsNotNull(OperationPlanner.ForValues(token, VBLongType.TypeInfo, VBLongType.TypeInfo).EffectiveType, token);
        }

        foreach (var token in new[] { Tokens.NegationOp, Tokens.LogicalNotOp })
        {
            Assert.IsNotNull(OperationPlanner.ForValue(token, VBLongType.TypeInfo).EffectiveType, token);
        }
    }

    // a unary + is no operator (RD-VBAL §5.6.9.3), and no operation of one operand is planned for it.
    [TestMethod]
    public void AUnaryPlus_IsNotPlanned()
        => Assert.ThrowsExactly<NotSupportedException>(() => OperationPlanner.ForValue(Tokens.AdditionOp, VBLongType.TypeInfo));

    [TestMethod]
    public void AConversion_HasNoPlan_AndIsAMistakeToAskFor()
    {
        // a let-assignment and a pair of parentheses evaluate as operators, and are conversions: there is no table of effective types for either.
        Assert.ThrowsExactly<NotSupportedException>(() => OperationPlanner.ForValues(OperatorSymbolNames.BinaryAssignmentValueOp, VBLongType.TypeInfo, VBLongType.TypeInfo));
        Assert.ThrowsExactly<NotSupportedException>(() => OperationPlanner.ForValue(OperatorSymbolNames.UnaryLetCoerceOp, VBLongType.TypeInfo));
    }

    [TestMethod]
    public void TheRelationalOperators_ShareATable()
    {
        foreach (var token in new[] { Tokens.CompareNotEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareGreaterThanOp, Tokens.CompareGreaterThanOrEqualOp })
        {
            foreach (var (left, right) in new (VBType, VBType)[]
                {
                    (VBIntegerType.TypeInfo, VBStringType.TypeInfo), (VBLongType.TypeInfo, VBSingleType.TypeInfo), (VBDateType.TypeInfo, VBStringType.TypeInfo),
                    (VBNullType.TypeInfo, VBLongType.TypeInfo), (VBErrorType.TypeInfo, VBLongType.TypeInfo),
                })
            {
                Assert.AreEqual(
                    OperationPlanner.ForValues(Tokens.CompareEqualOp, left, right).EffectiveType,
                    OperationPlanner.ForValues(token, left, right).EffectiveType, $"{left.Name} {token} {right.Name}");
            }
        }
    }

    [TestMethod]
    public void AComparison_HasItsOwnTable_AndComparesDatesAsDoubles()
    {
        // a Boolean is compared to a String as a Boolean, a Long to a Single as a Double, two Errors by their values; a Date comparison compares Doubles.
        Assert.AreEqual(VBBooleanType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareEqualOp, VBBooleanType.TypeInfo, VBStringType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareLessThanOp, VBLongType.TypeInfo, VBSingleType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBErrorType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareEqualOp, VBErrorType.TypeInfo, VBErrorType.TypeInfo).EffectiveType);

        var date = OperationPlanner.ForValues(Tokens.CompareGreaterThanOp, VBDateType.TypeInfo, VBStringType.TypeInfo);
        Assert.AreEqual(VBDateType.TypeInfo, date.EffectiveType);
        Assert.AreEqual(VBDoubleType.TypeInfo, date.OperandDestination);
    }

    [TestMethod]
    public void Like_MatchesAnythingButNull_AsStrings()
    {
        Assert.AreEqual(VBStringType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareLikeOp, VBLongType.TypeInfo, VBDateType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBNullType.TypeInfo, OperationPlanner.ForValues(Tokens.CompareLikeOp, VBStringType.TypeInfo, VBNullType.TypeInfo).EffectiveType);
    }

    [TestMethod]
    public void Is_ComparesObjectReferences_AsTheyAre()
    {
        var plan = OperationPlanner.ForValues(Tokens.CompareIsOp, VBObjectType.TypeInfo, VBLongType.TypeInfo);
        Assert.AreEqual(VBBooleanType.TypeInfo, plan.EffectiveType, "an operand that is not an object is the evaluation's error, not the plan's");
        Assert.IsNull(plan.OperandDestination, "nothing is let-coerced to the Boolean the comparison yields");
    }

    [TestMethod]
    public void AConcatenation_IsAString_OfAnythingThatConvertsToOne()
    {
        Assert.AreEqual(VBStringType.TypeInfo, OperationPlanner.ForValues(Tokens.ConcatOp, VBBooleanType.TypeInfo, VBDateType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBStringType.TypeInfo, OperationPlanner.ForValues(Tokens.ConcatOp, VBNullType.TypeInfo, VBLongType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBNullType.TypeInfo, OperationPlanner.ForValues(Tokens.ConcatOp, VBNullType.TypeInfo, VBNullType.TypeInfo).EffectiveType);
        Assert.IsTrue(OperationPlanner.ForValues(Tokens.ConcatOp, VBObjectType.TypeInfo, VBStringType.TypeInfo).IsTypeMismatch);
    }

    [TestMethod]
    public void TwoByteArrays_AreConcatenated_AsTheStringsTheyAreTheDataOf()
    {
        Assert.AreEqual(VBStringType.TypeInfo, OperationPlanner.ForValues(Tokens.ConcatOp, VBResizableByteArrayType.TypeInfo, VBResizableByteArrayType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBStringType.TypeInfo,
            OperationPlanner.ForDeclared(Tokens.ConcatOp, new VBFixedSizeArrayType(VBByteType.TypeInfo), new VBResizableArrayType(VBByteType.TypeInfo)).EffectiveType,
            "a fixed-size Byte array is the data of a String as well");

        var longs = new VBResizableArrayType(VBLongType.TypeInfo);
        Assert.IsTrue(OperationPlanner.ForValues(Tokens.ConcatOp, longs, longs).IsTypeMismatch);
    }

    [TestMethod]
    public void ALogicalOperation_IsIntegral_OrBoolean()
    {
        // a Boolean operation is a Boolean; a floating-point, fixed-point, string or date operand is a Long, unless the other is a LongLong.
        Assert.AreEqual(VBBooleanType.TypeInfo, OperationPlanner.ForValues(Tokens.LogicalAndOp, VBBooleanType.TypeInfo, VBBooleanType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBIntegerType.TypeInfo, OperationPlanner.ForValues(Tokens.LogicalOrOp, VBBooleanType.TypeInfo, VBByteType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongType.TypeInfo, OperationPlanner.ForValues(Tokens.LogicalXOrOp, VBDoubleType.TypeInfo, VBIntegerType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongLongType.TypeInfo, OperationPlanner.ForValues(Tokens.LogicalEqvOp, VBStringType.TypeInfo, VBLongLongType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBByteType.TypeInfo, OperationPlanner.ForValues(Tokens.LogicalImpOp, VBByteType.TypeInfo, VBNullType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBNullType.TypeInfo, OperationPlanner.ForValues(Tokens.LogicalAndOp, VBNullType.TypeInfo, VBNullType.TypeInfo).EffectiveType);
        Assert.IsTrue(OperationPlanner.ForValues(Tokens.LogicalAndOp, VBErrorType.TypeInfo, VBLongType.TypeInfo).IsTypeMismatch);
    }

    [TestMethod]
    public void Not_NegatesTheBitsOfAnIntegralValue_InItsOwnType()
    {
        // unlike the negation of a Byte, which is an Integer, the bits of a Byte are negated in a Byte; anything that is not integral is a Long.
        Assert.AreEqual(VBByteType.TypeInfo, OperationPlanner.ForValue(Tokens.LogicalNotOp, VBByteType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongLongType.TypeInfo, OperationPlanner.ForValue(Tokens.LogicalNotOp, VBLongLongType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongType.TypeInfo, OperationPlanner.ForValue(Tokens.LogicalNotOp, VBCurrencyType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBLongType.TypeInfo, OperationPlanner.ForValue(Tokens.LogicalNotOp, VBDateType.TypeInfo).EffectiveType);
        Assert.AreEqual(VBNullType.TypeInfo, OperationPlanner.ForValue(Tokens.LogicalNotOp, VBNullType.TypeInfo).EffectiveType);
        Assert.IsTrue(OperationPlanner.ForValue(Tokens.LogicalNotOp, VBErrorType.TypeInfo).IsTypeMismatch);
    }

    [TestMethod]
    public void TwoPlans_AreTheSame_WhenTheyAgree()
    {
        var made = OperationPlanner.ForValues(Tokens.ModuloOp, VBLongType.TypeInfo, VBLongType.TypeInfo);
        var copy = new OperationPlan(Tokens.ModuloOp, [VBLongType.TypeInfo, VBLongType.TypeInfo], VBLongType.TypeInfo);

        Assert.AreEqual(made, copy);
        Assert.AreEqual(made.GetHashCode(), copy.GetHashCode());
        Assert.AreNotEqual(made, copy with { EffectiveType = VBDoubleType.TypeInfo });
    }
}
