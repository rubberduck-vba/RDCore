using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Model.Values;

/// <summary>
/// <c>vbNullString</c> — the <c>String</c> bound to a null pointer, which is also what
/// <see cref="VBStringType"/> hands out as the default value of a <c>String</c> variable.
/// </summary>
/// <remarks>
/// Reading it used to throw, which made defining <em>any</em> module-scoped <c>String</c> variable throw:
/// allocating storage for one asks the default value for its <see cref="VBStringValue.Size"/>, and that
/// dereferenced the null managed value behind it.
/// </remarks>
[TestClass]
public sealed class VBStringValueNullStringTests
{
    [TestMethod]
    public void VBNullString_ReadsAsAZeroLengthString()
        // "vbNullString = """" is True in MS-VBA: the pointer is what differs, not the characters.
        => Assert.AreEqual(string.Empty, VBStringValue.VBNullString.Value);

    [TestMethod]
    public void VBNullString_HasNoLength()
        => Assert.AreEqual(0, VBStringValue.VBNullString.Length);

    [TestMethod]
    public void VBNullString_OccupiesNoStorage()
        // a null pointer has no string behind it to size, where a zero-length string still has a header.
        => Assert.AreEqual(0, VBStringValue.VBNullString.Size);

    [TestMethod]
    public void ZeroLengthString_OccupiesItsHeader()
        => Assert.AreEqual(2, VBStringValue.ZeroLengthString.Size);

    [TestMethod]
    public void VBNullString_IsTheNullString_AndAZeroLengthStringIsNot()
        // the one thing that tells the two apart, and what Size reads.
        => Assert.IsTrue(VBStringValue.VBNullString.IsNullString
            && !VBStringValue.ZeroLengthString.IsNullString);

    [TestMethod]
    public void StringDefaultValue_IsTheNullString()
        // MS-VBA gives an uninitialized String a null pointer (StrPtr(s) is 0 before anything is assigned),
        // so the default value being vbNullString rather than "" is deliberate.
        => Assert.IsTrue(((VBStringValue)VBStringType.TypeInfo.DefaultValue).IsNullString);
}
