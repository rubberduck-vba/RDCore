using RDCore.Parsing;
using RDCore.Parsing.Model.Expressions.Operators;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model;
using RDCore.Runtime.Model.Operators;
using RDCore.Server;
using Location = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests;

[TestClass]
public class SymbolOperationTests
{
    [TestMethod]
    [DataRow(null, 5)]   // MS-VBAL: Null + Any -> Null
    [DataRow(null, "#1-1-2026#")]   // MS-VBAL: Null + Any -> Null
    [DataRow(null, 1.23d)]   // MS-VBAL: Null + Any -> Null
    [DataRow(5, null)]   // MS-VBAL: Any + Null -> Null
    [DataRow("Empty", null)]   // MS-VBAL: Any + Null -> Null
    [DataRow("#2026-12-31#", null)]   // MS-VBAL: Any + Null -> Null
    public void EvaluateAddition_NullPropagation_ResultIsNull(object lhs, object rhs)
    {
        var result = EvaluateAddition(CreateContext(), GlobalSymbols.Addition, lhs, rhs);

        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [DataRow("1.5", 1, 2.5d)]         // String + Integer -> Double
    [DataRow(32767, 1, "VBR00006")]   // Integer + Integer -> Overflow
    [DataRow(32767, 1.0, 32768.0d)]   // Integer + Double -> Double (Safe)
    public void EvaluateAddition_NumericScenarios_MatchesVBAL(object lhs, object rhs, object expected)
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

        return SymbolOperation.EvaluateAddition(context, expression, lhsValue.ResultValue, rhsValue.ResultValue);
    }

    private VBExecutionContext CreateContext(bool is64bit = true) => new(default!, default!) { Is64Bit = true };
    private Location TestLocation { get; } = new() { Uri = "file:///a:/test/file", Range = new Range(1, 1, 1, 1) };

    private ValuedExpression Wrap(object? val, Location location)
    {
        var dateHelper = (string s) => DateTime.TryParse(s.TrimStart("#").TrimEnd("#"), out var dateValue)
            ? new LiteralExpression<VBDateValue>(location).WithResultValue(new VBDateValue().WithValue(dateValue)) : null;

        // Helper to turn MSTest DataRow objects into RDCore VBTypedValues
        return val switch
        {
            null => new LiteralExpression<VBObjectValue>(location).WithResultValue(VBNullValue.Null),
            "Empty" => new LiteralExpression<VBEmptyValue>(location).WithResultValue(VBEmptyValue.Empty),
            string s => new LiteralExpression<VBStringValue>(location).WithResultValue(new VBStringValue().WithValue(s)),
            int i => new LiteralExpression<VBIntegerValue>(location).WithResultValue(new VBIntegerValue().WithValue(i)),
            double d => new LiteralExpression<VBDoubleValue>(location).WithResultValue(new VBDoubleValue().WithValue(d)),
            _ => throw new NotSupportedException()
        };
    }
}
