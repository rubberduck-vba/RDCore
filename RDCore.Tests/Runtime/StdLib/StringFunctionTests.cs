using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.38 String</strong>: "a String containing a repeating character string of the length specified",
/// the character being a String's first or a character code's, "character Mod 256" above 255. Its C# members are
/// <c>Repeat</c> and <c>RepeatString</c>.
/// </summary>
/// <remarks>
/// The Character argument is a <c>Variant</c>, so the member coerces it itself: as a <c>Long</c> parameter would be, before any
/// other check, then a Null Character is Null whatever the Number, then the values are checked in the order they are declared.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.38 String")]
[TestCategory("MS-VBAL 6.1.2.11.1.39 String$")]
public sealed class StringFunctionTests
{
    [TestMethod]
    [DataRow(5, "*", "*****")]
    [DataRow(10, "ABC", "AAAAAAAAAA")]
    [DataRow(3, "65", "666")]
    [DataRow(3, "\u3042", "\u3042\u3042\u3042")]
    public void Repeat_OfAString_RepeatsItsFirstCharacter(int number, string character, string expected)
        => Assert.AreEqual(expected, TextOf(Module().Repeat(L(number), V(character))));

    [TestMethod]
    public void Repeat_OfASurrogatePair_RepeatsItsFirstCodeUnit()
    {
        var pair = char.ConvertFromUtf32(0x1F600);

        Assert.AreEqual(new string(pair[0], 3), TextOf(Module().Repeat(L(3), V(pair))));
    }

    [TestMethod]
    [DataRow(42, "*")]
    [DataRow(128, "\u20AC")]
    [DataRow(255, "\u00FF")]
    [DataRow(129, "\u0081")]
    public void Repeat_OfACharacterCode_RepeatsThatCharacter(int code, string character)
        => Assert.AreEqual(character + character + character, TextOf(Module().Repeat(L(3), V(code))));

    [TestMethod]
    public void Repeat_OfACodeThatNamesNoCharacter_OnADoubleByteCodePage_IsError5()
    {
        var module = Module(codePage: 932);

        // a lone lead byte is no character, whatever the Number; a single-byte code page reads &H81 as its character (above).
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Repeat(L(2), V(0x81))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Repeat(L(0), V(0x81))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Repeat(L(2), V(0x181))), "&H181 Mod 256");
        Assert.AreEqual("\uFF71\uFF71", TextOf(module.Repeat(L(2), V(0xB1))), "a single-byte character of the double-byte code page");
    }

    [TestMethod]
    [DataRow(321, "A")]
    [DataRow(256, "\0")]
    [DataRow(1000, "\u00E8")]
    [DataRow(65535, "\u00FF")]
    [DataRow(65536, "\0")]
    public void Repeat_OfACodeAbove255_TakesItModulo256(int code, string character)
        => Assert.AreEqual(character + character + character, TextOf(Module().Repeat(L(3), V(code))));

    [TestMethod]
    [DataRow(65.5, "B")]
    [DataRow(2.5, "\u0002")]
    public void Repeat_RoundsACharacterCode(double code, string character)
        => Assert.AreEqual(character + character + character, TextOf(Module().Repeat(L(3), V(code))));

    [TestMethod]
    public void Repeat_OfZero_IsAZeroLengthString()
        => Assert.AreEqual(string.Empty, TextOf(Module().Repeat(L(0), V("a"))));

    [TestMethod]
    public void Repeat_OfEmpty_RepeatsTheNullChar()
        // Empty is 0 wherever a number is wanted.
        => Assert.AreEqual("\0\0\0", TextOf(Module().Repeat(L(3), VEmpty)));

    [TestMethod]
    [DataRow(-1)]
    [DataRow(-65)]
    [DataRow(-256)]
    [DataRow(-32768)]
    public void Repeat_OfANegativeCharacterCode_IsError5(int code)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Repeat(L(3), V(code))));

    [TestMethod]
    public void Repeat_Errors()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Repeat(L(-1), V("a"))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Repeat(L(3), V(string.Empty))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Repeat(L(3), V(true))), "True is the code -1");
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Repeat(L(3), V(new VBErrorValue(5)))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Repeat(L(-1), V(new VBErrorValue(5)))),
            "the Character's coercion comes before the Number's check");
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Module().Repeat(L(3), V(3e9))));
        Assert.AreEqual((int)VBRuntimeErrorId.OutOfStringSpace, ErrorOf(Module().Repeat(L(int.MaxValue), V("a"))));
    }

    [TestMethod]
    public void Repeat_OfNothing_IsError91()
        // Nothing, Let-coerced to a Long, is error 91 (MS-VBAL 5.5.1.2.13).
        => Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, ErrorOf(Module().Repeat(L(3), V(VBObjectValue.Nothing))));

    [TestMethod]
    public void Repeat_OfAnObject_IsError13()
        // 🚧 an object that is not Nothing: its default member is not read.
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Repeat(L(3), V(new VBObjectValue(new VBRuntimeObjectId())))));

    [TestMethod]
    public void Repeat_OfNull_IsNull_WhateverTheNumber()
    {
        Assert.IsTrue(IsNull(Module().Repeat(L(3), VNull)));
        Assert.IsTrue(IsNull(Module().Repeat(L(-1), VNull)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().RepeatString(L(3), VNull)));
    }

    [TestMethod]
    public void String_FromSource()
    {
        CollectionAssert.AreEqual(new[] { "*****", "*****", "[]" }, Run(
            "Debug.Print String(5, \"*\")",
            "Debug.Print String(5, 42)",
            "Debug.Print \"[\" & String(V, \"a\") & \"]\""));
        // the table's "If number contains the data value Null, Null is returned" yields to Number's declared type, Long.
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print String(Null, \"a\")"));
    }

    [TestMethod]
    public void StringDollar_FromSource()
    {
        CollectionAssert.AreEqual(new[] { "aaa", "bbb" }, Run("Debug.Print String$(3, \"a\")", "Debug.Print String$(3, 98)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print String$(3, Null)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print String$(-1, \"a\")"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print String$(3, \"\")"));
    }
}
