using RDCore.SDK.Model;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.Tests.Semantics;

/// <summary>
/// The plan of an operation (IR lowering §3.2): decided from the types of the operands alone, once, by the one table the binder and the interpreter both consult.
/// What each arithmetic operation yields is pinned by <c>ArithmeticOperatorsGoldenMasterTests</c>; this is what a plan is.
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
    }

    [TestMethod]
    public void AnOperatorThatIsNotPlannedYet_IsAMistakeToAskFor()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => OperationPlanner.ForValues(Tokens.ConcatOp, VBStringType.TypeInfo, VBStringType.TypeInfo));
        Assert.ThrowsExactly<NotSupportedException>(() => OperationPlanner.ForValue(Tokens.LogicalNotOp, VBBooleanType.TypeInfo));
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
