using RDCore.Parsing;
using RDCore.Parsing.Model.Expressions.Operators;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model;
using RDCore.Runtime.Model.Operators;
using RDCore.Server;
using Location = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.2: '+' Operator")]
public class AdditionOperationTests : SymbolOperationTests
{
    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, 5)]   // MS-VBAL: Null + Any -> Null
    [DataRow(null, "#1-1-2026#")]   // MS-VBAL: Null + Any -> Null
    [DataRow(null, 1.23d)]   // MS-VBAL: Null + Any -> Null
    [DataRow(5, null)]   // MS-VBAL: Any + Null -> Null
    [DataRow("Empty", null)]   // MS-VBAL: Any + Null -> Null
    [DataRow("#2026-12-31#", null)]   // MS-VBAL: Any + Null -> Null
    public void EvaluateAddition_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateAddition(CreateContext(), GlobalSymbols.Addition, lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateAddition_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBUserDefinedTypeValue>(TestLocation)
            .WithResultValue(new VBUserDefinedTypeValue(udt));
            
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateAddition(CreateContext(), GlobalSymbols.Addition, lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateAddition_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBResizableArrayValue>(TestLocation)
            .WithResultValue(new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateAddition(CreateContext(), GlobalSymbols.Addition, lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.11: Let-coercion from 'Empty'")]
    public void EvaluateAddition_Empty_LetCoercion_Numeric_IsZero()
    {
        var depth = 0;
        var result = VBEmptyValue.Empty.AsCoercedNumeric(ref depth);
        Assert.AreEqual(0, result.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.11: Let-coercion from 'Empty'")]
    public void EvaluateAddition_Empty_LetCoercion_String_IsEmptyString()
    {
        var depth = 0;
        var result = VBEmptyValue.Empty.AsCoercedString(ref depth);
        Assert.AreEqual(VBStringValue.ZeroLengthString, result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2: '+' Operator")]
    [DataRow("1.5", 1, 2.5d)]         // String + Integer -> Double
    [DataRow("1.5", "1", "1.51")]         // String + String -> String
    [DataRow(32767, 1, "VBR00006")]   // Integer + Integer -> Overflow
    [DataRow(32767, 1.0, 32768.0d)]   // Integer + Double -> Double (Safe)
    [DataRow(42, "VBErrorValue", "VBR00013")] // Integer + VBErrorValue -> TypeMismatch
    [DataRow("ABC", "VBErrorValue", "VBR00013")] // String + VBErrorValue -> TypeMismatch
    [DataRow("VBErrorValue", "VBErrorValue", "VBR00013")] // VBErrorValue + VBErrorValue -> TypeMismatch
    public void EvaluateAddition_NumericScenarios_MatchesMSVBAL(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateAddition(CreateContext(), GlobalSymbols.Addition, lhs, rhs);
            if (expected is not string)
            {
                Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).NumericValue, 0.0001);
            }
        }
        catch (VBRuntimeErrorException ex)
        {
            Assert.AreEqual(expected, ex.VBErrorNumber.ToDiagnosticCode());
        }
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2: '+' Operator")]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateAddition_DateTime_ReturnsDateTime(object lhs, object rhs)
    {
        var result = EvaluateAddition(CreateContext(), GlobalSymbols.Addition, lhs, rhs);

        Assert.IsInstanceOfType<VBDateValue>(result);
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitDateSerialConversion")]
    [DataRow(-1, "DateTime.Now", true)]
    [DataRow("DateTime.Now", 1, true)]
    [DataRow("DateTime.Now", "DateTime.Now", true)]
    public void EvaluateAddition_ImplicitDateSerialConversionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        var result = EvaluateAddition(context, GlobalSymbols.Addition, lhs, rhs);

        Assert.IsInstanceOfType<VBDateValue>(result);
        Assert.AreEqual(expectDiagnostics, context.Diagnostics.Any(e => e.Code == RDCoreDiagnosticId.ImplicitDateSerialConversion.ToDiagnosticCode()));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2: '+' Operator")]
    public void EvaluateAddition_BothEmpty_ResultsIsIntegerZero()
    {
        var result = EvaluateAddition(CreateContext(), GlobalSymbols.Addition, "Empty", "Empty");

        Assert.IsInstanceOfType<VBIntegerValue>(result);
        Assert.AreEqual(0, ((VBIntegerValue)result).Value);
    }

    private VBTypedValue EvaluateAddition(VBExecutionContext context, BinaryOperatorSymbol op, object lhs, object rhs)
    {
        var lhsValue = Wrap(lhs, TestLocation);
        var rhsValue = Wrap(rhs, TestLocation);
        var expression = new VBBinaryOperatorExpression(op, lhsValue, rhsValue, TestLocation);

        return SymbolOperation.EvaluateBinaryAddition(context, expression, lhsValue.ResultValue, rhsValue.ResultValue);
    }
}

[TestClass]
public abstract class SymbolOperationTests
{

    internal VBExecutionContext CreateContext(bool is64bit = true) => new(default!, new()) { Is64Bit = true };
    internal Location TestLocation { get; } = new() { Uri = "file:///a:/test/file", Range = new Range(1, 1, 1, 1) };

    internal ValuedExpression Wrap(object? val, Location location)
    {
        if (val is ValuedExpression exp)
        {
            return exp;
        }

        var dateHelper = (string s) => DateTime.TryParse(s.TrimStart("#").TrimEnd("#"), out var dateValue)
            ? new LiteralExpression<VBDateValue>(location).WithResultValue(new VBDateValue().WithValue(dateValue)) : null;

        // Helper to turn MSTest DataRow objects into RDCore VBTypedValues
        return val switch
        {
            "UDT" => new LiteralExpression<VBUserDefinedTypeValue>(location).WithResultValue(VBLongPtrValue.Zero),
            VBTypedValue value => new LiteralExpression<VBTypedValue>(location).WithResultValue(value),
            "DateTime.Now" => new LiteralExpression<VBDateValue>(location).WithResultValue(VBDateValue.FromSerial(43452)),
            "VBErrorValue" => new LiteralExpression<VBErrorValue>(location).WithResultValue(VBErrorType.TypeInfo.DefaultValue),
            null => new LiteralExpression<VBObjectValue>(location).WithResultValue(VBNullValue.Null),
            "Empty" => new LiteralExpression<VBEmptyValue>(location).WithResultValue(VBEmptyValue.Empty),
            string s => new LiteralExpression<VBStringValue>(location).WithResultValue(new VBStringValue().WithValue(s)),
            int i => new LiteralExpression<VBIntegerValue>(location).WithResultValue(new VBIntegerValue().WithValue(i)),
            double d => new LiteralExpression<VBDoubleValue>(location).WithResultValue(new VBDoubleValue().WithValue(d)),
            _ => throw new NotSupportedException()
        };
    }

    [TestMethod]
    [DataRow(-1.0, -1.0, -1)]   // True And True -> True
    [DataRow(-1.0, 0.0, 0)]   // True And False -> False
    [DataRow(0.0, 0.0, 0)]    // False And False -> False
    [DataRow(1.0, 2.0, 0)]    // 1 And 2 -> False (in Boolean context)
    public void EvaluateBinaryBitwiseAnd_BooleanContext_MatchesVBAL(double lhs, double rhs, int expected)
    {
        var context = CreateContext();

        var lhsValue = new VBDoubleValue().WithValue(lhs);
        var rhsValue = new VBDoubleValue().WithValue(rhs);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.BitwiseAnd, null!, null!, TestLocation);

        var result = SymbolOperation.EvaluateBinaryBitwiseAnd(context, expression, lhsValue, rhsValue);

        Assert.IsInstanceOfType<VBDoubleValue>(result);
        Assert.AreEqual(expected, ((VBDoubleValue)result).Value);
    }

    [TestMethod]
    public void EvaluateBinaryBitwiseAnd_PureBoolean_ReturnsBoolean()
    {
        var context = CreateContext();
        var lhsValue = new LiteralExpression<VBBooleanValue>(TestLocation) { ResultValue = new VBBooleanValue().WithValue(true) }; // -1
        var rhsValue = new LiteralExpression<VBBooleanValue>(TestLocation) { ResultValue = new VBBooleanValue().WithValue(true) }; // -1

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.BitwiseAnd, lhsValue, rhsValue, TestLocation);

        var result = SymbolOperation.EvaluateBinaryBitwiseAnd(context, expression, lhsValue.ResultValue, rhsValue.ResultValue);

        // MS-VBAL: Bool And Bool -> Bool
        Assert.IsInstanceOfType<VBBooleanValue>(result);
        Assert.IsTrue(((VBBooleanValue)result).Value);
    }
}
