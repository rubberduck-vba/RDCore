using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using Location = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.Semantics.Runtime;

public abstract class SymbolOperationTests
{
    internal abstract IRuntimeSemantics Semantics { get; }
    internal abstract IEnumerable<VBType> EffectiveTypes { get; }

    protected void AssertVBRuntimeErrorException(VBRuntimeErrorId expected, Exception exception)
    {
        if (exception is VBRuntimeErrorException vbError)
        {
            Assert.AreEqual((int)expected, vbError.VBErrorNumber);
        }
        else
        {
            Assert.Fail();
        }
    }

    internal static IVBExecutionContext CreateContext(bool is64bit = true) => new VBExecutionContext(default!) { Is64Bit = true };
    /// <summary>
    /// For the sake of a test involving a binary operator, the location of the LHS symbol or
    /// in the case of a unary operator, the location of the expression.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal static Location TestLocationLHS { get; } = new() { Uri = "file:///a:/test/file#rhs", Range = new Range(1, 1, 1, 1) };
    /// <summary>
    /// For the sake of a test, the location of the operator symbol.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal static Location TestLocation { get; } = new() { Uri = "file:///a:/test/file#op", Range = new Range(1, 2, 1, 2) };
    /// <summary>
    /// For the sake of a test involving a binary operator, the location of the RHS symbol.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal static Location TestLocationRHS { get; } = new() { Uri = "file:///a:/test/file#lhs", Range = new Range(1, 3, 1, 3) };

    private static VBUserDefinedTypeValue GetUDT()
    {
        var name = "TestUDT";
        var symbol = new VBUserDefinedTypeMemberSymbol(
            ScopeKind.Module,
            TestUri.TestModuleUserDefinedTypeUri(name),
            name, Accessibility.Private,
            SymbolOperationTests.TestLocation?.Range,
            SymbolOperationTests.TestLocation?.Range,
            TestUri.WorkspaceRoot());
        var udt = new VBUserDefinedType(symbol, []);
        var value = new VBUserDefinedTypeValue(udt, symbol);
        return value;
    }

    internal static VBTypedValue WrapVBTypedValue(object? value, Location location)
    {
        VBTypedValue? dateHelper(string s) => s.StartsWith("#") && s.EndsWith("#") ?
            DateTime.TryParse(s.TrimStart("#").TrimEnd("#"), out var dateValue)
            ? new VBDateValue(GlobalSymbols.ExtensionSymbols.VBDateZeroValue).WithValue(dateValue)
            : null : null;

        return value switch
        {
            VBTypedValue typedValue => typedValue,
            "UDT" =>  GetUDT(),
            "DateTime.Now" => VBDateType.Zero.WithValue(43452),
            "VBErrorValue" => VBErrorType.TypeInfo.DefaultValue,
            Tokens.Empty => VBEmptyValue.Empty,
            null => VBNullValue.Null,
            bool boolValue => VBBooleanValue.False.WithValue(boolValue),
            byte byteValue => VBByteType.Zero.WithValue(byteValue),
            int intValue => VBIntegerType.Zero.WithValue(intValue),
            long longValue => VBLongLongType.Zero.WithValue(longValue),
            double doubleValue => VBDoubleType.Zero.WithValue(doubleValue),

            // keep string last!
            string s => dateHelper(s) ?? new VBStringValue(GlobalSymbols.StaticSymbols.VBEmptyString).WithValue(s),
            _ => throw new NotSupportedException()
        };
    }

    internal static LiteralExpression WrapLiteralExpression(object? value, Location location)
    {
        var typedValue = WrapVBTypedValue(value, location);
        return new LiteralExpression(location, typedValue);
    }

    //[TestMethod]
    //[DataRow(-1.0, -1.0, -1)]   // True And True -> True
    //[DataRow(-1.0, 0.0, 0)]   // True And False -> False
    //[DataRow(0.0, 0.0, 0)]    // False And False -> False
    //[DataRow(1.0, 2.0, 0)]    // 1 And 2 -> False (in Boolean context)
    //public void EvaluateBinaryBitwiseAnd_BooleanContext_MatchesVBAL(double lhs, double rhs, int expected)
    //{
    //    var context = CreateContext();

    //    var lhsValue = new VBDoubleValue().WithValue(lhs);
    //    var rhsValue = new VBDoubleValue().WithValue(rhs);

    //    var expression = new VBBinaryOperatorExpression(GlobalSymbols.BitwiseAnd, null!, null!, TestLocation);

    //    var result = SymbolOperation.EvaluateBinaryBitwiseAnd(context, expression, lhsValue, rhsValue);

    //    Assert.IsInstanceOfType<VBDoubleValue>(result);
    //    Assert.AreEqual(expected, ((VBDoubleValue)result).Value);
    //}

    //[TestMethod]
    //public void EvaluateBinaryBitwiseAnd_PureBoolean_ReturnsBoolean()
    //{
    //    var context = CreateContext();
    //    var lhsValue = new LiteralExpression(TestLocation) { ResultValue = new VBBooleanValue().WithValue(true) }; // -1
    //    var rhsValue = new LiteralExpression(TestLocation) { ResultValue = new VBBooleanValue().WithValue(true) }; // -1

    //    var expression = new VBBinaryOperatorExpression(GlobalSymbols.BitwiseAnd, lhsValue, rhsValue, TestLocation);

    //    var result = SymbolOperation.EvaluateBinaryBitwiseAnd(context, expression, lhsValue.ResultValue, rhsValue.ResultValue);

    //    // MS-VBAL: Bool And Bool -> Bool
    //    Assert.IsInstanceOfType<VBBooleanValue>(result);
    //    Assert.IsTrue(((VBBooleanValue)result).Value);
    //}
}
