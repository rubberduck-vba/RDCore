using RDCore.Parsing.Model.Expressions.Operators;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model;
using RDCore.Runtime.Model.Operators;
using RDCore.Server;
using Location = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests;

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

    /// <summary>
    /// Asserts that the specified <c>RDCodeDiagnosticId</c> was issued in the specified execution context.
    /// </summary>
    /// <remarks>
    /// Specify <c>assertMissing:true</c> to assert that the specified diagnostic was specifically <strong>not</strong> issued.
    /// </remarks>
    internal void AssertDiagnostic(VBExecutionContext context, RDCoreDiagnosticId id, bool assertMissing = false)
    {
        var code = id.ToDiagnosticCode();
        Assert.AreEqual(!assertMissing, context.Diagnostics.Any(e => e.Code == code));
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
