using RDCore.Runtime.Execution.External.Native;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// What an argument of a <c>Declare</c>d procedure's <c>As Any</c> parameter is passed as, from what it is stored as (<strong>MS-VBAL §5.2.3.5</strong>):
/// a <c>String</c> is the runtime value that holds its data, so <c>vbNullString</c> is the <c>String</c> the provider passes as a null pointer, and
/// <c>""</c> is a zero-length string. These hold on any platform, where the calls that show it are made on Windows only.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
public sealed class NativeValuesTests
{
    [TestMethod]
    public void TypedOf_TheNullString_IsTheNullString()
        // its managed value is null, which no case of the managed value takes.
        => Assert.IsTrue(Assert.IsInstanceOfType<VBStringValue>(NativeValues.TypedOf(VBStringValue.VBNullString.RuntimeValue)).IsNullString);

    [TestMethod]
    public void TypedOf_AZeroLengthString_IsAZeroLengthString_NotTheNullString()
    {
        var text = Assert.IsInstanceOfType<VBStringValue>(NativeValues.TypedOf(new VBStringValue(string.Empty).RuntimeValue));

        Assert.IsFalse(text.IsNullString);
        Assert.AreEqual(string.Empty, text.Value);
    }
}
