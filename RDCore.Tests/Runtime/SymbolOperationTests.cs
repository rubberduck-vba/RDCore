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
    /// <summary>
    /// For the sake of a test involving a binary operator, the location of the LHS symbol or
    /// in the case of a unary operator, the location of the expression.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal Location TestLocationLHS { get; } = new() { Uri = "file:///a:/test/file#rhs", Range = new Range(1, 1, 1, 1) };
    /// <summary>
    /// For the sake of a test, the location of the operator symbol.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal Location TestLocation { get; } = new() { Uri = "file:///a:/test/file#op", Range = new Range(1, 2, 1, 2) };
    /// <summary>
    /// For the sake of a test involving a binary operator, the location of the RHS symbol.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal Location TestLocationRHS { get; } = new() { Uri = "file:///a:/test/file#lhs", Range = new Range(1, 3, 1, 3) };

    internal ValuedExpression Wrap(object? val, Location location)
    {
        if (val is ValuedExpression exp)
        {
            return exp;
        }

        ValuedExpression? dateHelper(string s) => s.StartsWith("#") && s.EndsWith("#") ?
            DateTime.TryParse(s.TrimStart("#").TrimEnd("#"), out var dateValue)
            ? new LiteralExpression<VBDateValue>(location).WithResultValue(new VBDateValue().WithValue(dateValue)) 
            : null : null;

        // Helper to turn MSTest DataRow objects into RDCore VBTypedValues
        return val switch
        {
            VBTypedValue value => new LiteralExpression<VBTypedValue>(location).WithResultValue(value),
            "UDT" => new LiteralExpression<VBUserDefinedTypeValue>(location).WithResultValue(VBLongPtrValue.Zero),
            "DateTime.Now" => new LiteralExpression<VBDateValue>(location).WithResultValue(VBDateValue.FromSerial(43452)),
            "VBErrorValue" => new LiteralExpression<VBErrorValue>(location).WithResultValue(VBErrorType.TypeInfo.DefaultValue),
            "Empty" => new LiteralExpression<VBEmptyValue>(location).WithResultValue(VBEmptyValue.Empty),
            bool b => new LiteralExpression<VBBooleanValue>(location).WithResultValue(new VBBooleanValue().WithValue(b)),
            byte v => new LiteralExpression<VBByteValue>(location).WithResultValue(new VBByteValue().WithValue(v)),
            string s => dateHelper(s) ?? new LiteralExpression<VBStringValue>(location).WithResultValue(new VBStringValue().WithValue(s)),
            int i => new LiteralExpression<VBIntegerValue>(location).WithResultValue(new VBIntegerValue().WithValue(i)),
            long i => new LiteralExpression<VBLongLongValue>(location).WithResultValue(new VBLongLongValue().WithValue(i)),
            double d => new LiteralExpression<VBDoubleValue>(location).WithResultValue(new VBDoubleValue().WithValue(d)),
            null => new LiteralExpression<VBNullValue>(location).WithResultValue(VBNullValue.Null),
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Asserts that the specified <c>RDCodeDiagnosticId</c> was issued in the specified execution context.
    /// </summary>
    /// <remarks>
    /// Specify <c>assertMissing:true</c> to assert that the specified diagnostic was specifically <strong>not</strong> issued.
    /// Specify a <c>location</c> to assert that the specified diagnostic was issued for that specific location.
    /// </remarks>
    internal static void AssertDiagnostic(VBExecutionContext context, RDCoreDiagnosticId id, Range? location = default, bool assertMissing = false)
    {
        var code = id.ToDiagnosticCode();
        var diagnostics = context.Diagnostics.Where(e => e.Code == code);

        if (!assertMissing && location is not null)
        {
            Assert.IsTrue(diagnostics.Any(e => e.Range.Equals(location)));
        }
        else
        {
            Assert.AreEqual(!assertMissing, diagnostics.Any());
        }
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
