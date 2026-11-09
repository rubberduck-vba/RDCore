using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.StdLib;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.37 StrConv</strong>: a String cased, or read from or written to the environment's ANSI code page,
/// as a sum of <see cref="VBStrConv"/> values says, and between narrow and wide characters, hiragana and katakana, in the locales
/// those conversions apply to.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.37 StrConv")]
public sealed class StrConvTests
{
    private const int InvalidProcedureCall = (int)VBRuntimeErrorId.InvalidProcedureCallOrArgument;

    private static VBStrConv Conversion(int value) => (VBStrConv)value;

    [TestMethod]
    [DataRow("hello wORLD-foo bar_baz o'neil 1st x.y", 1, "HELLO WORLD-FOO BAR_BAZ O'NEIL 1ST X.Y")]
    [DataRow("hello wORLD-foo bar_baz o'neil 1st x.y", 2, "hello world-foo bar_baz o'neil 1st x.y")]
    [DataRow("ß", 1, "ß")]
    public void StrConv_CasesUpOrDown(string text, int conversion, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), Conversion(conversion))));

    [TestMethod]
    [DataRow("hello wORLD-foo bar_baz o'neil 1st x.y", "Hello World-foo Bar_baz O'neil 1st X.y")]
    [DataRow("hELLO wORLD", "Hello World")]
    [DataRow("o'neil-smith", "O'neil-smith")]
    [DataRow("a(b)c.d", "A(b)c.d")]
    [DataRow("mcdonald's", "Mcdonald's")]
    [DataRow("a\0b\tc\nd\ve\ff\rg h", "A\0B\tC\nD\vE\fF\rG H")]
    public void StrConv_ProperCase_StartsAWordAfterTheSeparatorsOnly(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), VBStrConv.VBProperCase)));

    [TestMethod]
    [DataRow("a\u00A0b", "A\u00A0b")]
    public void StrConv_ProperCase_StartsNoWordAfterAnotherSpace(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), VBStrConv.VBProperCase)));

    // measured in MS-VBA (Excel, 64-bit, en-US, Windows-1252).
    [TestMethod]
    [DataRow("abc　def", 3, 0, "Abc Def")]
    [DataRow("abc　def", 3, 1033, "Abc Def")]
    [DataRow("abc def", 3, 0, "Abc Def")]
    [DataRow("abc def", 3, 1041, "Abc?def")]
    [DataRow("αβ γδ", 1, 0, "Aß ?D")]
    [DataRow("ΑΒ ΓΔ", 2, 0, "?? g?")]
    [DataRow("αβ γδ", 3, 0, "Aß ?d")]
    [DataRow("αβ γδ", 3, 1032, "Αβ Γδ")]
    [DataRow("αβ γδ", 1, 1032, "ΑΒ ΓΔ")]
    [DataRow("αβ γδ", 3, 1041, "Αβ Γδ")]
    [DataRow("αβ γδ", 3, 1049, "?? ??")]
    [DataRow("αβ γδ क", 3, 1081, "Aß ?d ?")]
    [DataRow("аб в", 3, 1049, "Аб В")]
    [DataRow("āb āc", 3, 0, "Ab Ac")]
    [DataRow("āb", 1, 0, "AB")]
    [DataRow("😀ab", 3, 0, "??ab")]
    [DataRow("ﬁx yz", 3, 0, "?x Yz")]
    [DataRow("﻿a", 3, 0, "?a")]
    [DataRow("a", 3, 0, "?a")]
    [DataRow("€ab", 3, 0, "€ab")]
    [DataRow("\u0080ab", 3, 0, "?ab")]
    [DataRow("\u009Fa", 3, 0, "?a")]
    [DataRow("\u0081ab", 3, 0, "\u0081ab")]
    [DataRow("\u008Da", 3, 0, "\u008Da")]
    [DataRow("ａa", 3, 1033, "Aa")]
    [DataRow("あい ab", 3, 1033, "?? Ab")]
    [DataRow("あい ab", 3, 1041, "あい Ab")]
    [DataRow("가 ab", 3, 1042, "가 Ab")]
    [DataRow("中 abα", 3, 2052, "中 Abα")]
    [DataRow("ǆa", 3, 1050, "?a")]
    [DataRow("a α", 4, 1041, "ａ？α")]
    [DataRow("あ ā", 16, 1041, "ア??")]
    [DataRow("ａ ā", 8, 1041, "a??")]
    [DataRow("ab cd", 3 + 4, 1041, "Ａｂ？ｃｄ")]
    public void StrConv_ConvertsInTheAnsiCodePageOfItsLocale(string text, int conversion, int lcid, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), Conversion(conversion), lcid == 0 ? null : L(lcid))));

    // a data row cannot hold a lone surrogate: the test runner's serialization replaces it.
    [TestMethod]
    public void StrConv_ALoneSurrogate_IsAQuestionMark()
        => Assert.AreEqual("?ab", TextOf(Module().StrConv(V("\uD83Dab"), VBStrConv.VBProperCase)));

    // measured in MS-VBA: a character cases only to one that cases back to it, whatever the locale.
    [TestMethod]
    [DataRow("iKIı", 1, 1055, "IKIı")]
    [DataRow("IİKi", 2, 1055, "iİki")]
    [DataRow("iKI", 3, 1055, "Iki")]
    [DataRow("iKI", 3, 1033, "Iki")]
    [DataRow("İA", 2, 1033, "ia")]
    [DataRow("µa", 1, 0, "µA")]
    [DataRow("µa", 3, 0, "µa")]
    [DataRow("ÿ", 1, 0, "Ÿ")]
    [DataRow("Ÿ", 2, 0, "ÿ")]
    [DataRow("ÿa", 3, 0, "Ÿa")]
    [DataRow("šœ", 1, 0, "ŠŒ")]
    [DataRow("ßa", 3, 0, "ßa")]
    [DataRow("ςΐ", 1, 1032, "ςΐ")]
    [DataRow("ΣΣ", 3, 1032, "Σσ")]
    [DataRow("ａｂ cd", 3, 1041, "Ａｂ Cd")]
    public void StrConv_CasesOnlyToACharacterThatCasesBack(string text, int conversion, int lcid, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), Conversion(conversion), lcid == 0 ? null : L(lcid))));

    [TestMethod]
    public void StrConv_OfZero_IsTheStringUnchanged()
    {
        Assert.AreEqual("abc", TextOf(Module().StrConv(V("abc"), Conversion(0))));
        CollectionAssert.AreEqual(new byte[] { 0x61, 0x00, 0x62 }, BytesOf(Module().StrConv(V(Bytes(0x61, 0x00, 0x62)), Conversion(0))), "a trailing byte stays");
        CollectionAssert.AreEqual(new byte[] { 0x41, 0x00 }, BytesOf(Module().StrConv(V(Bytes(0x61, 0x00, 0x62)), VBStrConv.VBUpperCase)), "a case conversion reads the characters");
    }

    [TestMethod]
    public void StrConv_Unicode_ReadsTheBytesAsTheAnsiCodePagesText()
    {
        var widened = Module().StrConv(V("abc"), VBStrConv.VBUnicode);
        Assert.AreEqual("a\0b\0c\0", TextOf(widened));
        Assert.AreEqual(12, StringOf(widened).ByteLength);

        Assert.AreEqual("abc", TextOf(Module().StrConv(V(Bytes(0x61, 0x62, 0x63)), VBStrConv.VBUnicode)), "a trailing byte is read too");
        Assert.AreEqual("€", TextOf(Module().StrConv(V(Bytes(0x80)), VBStrConv.VBUnicode)));
        Assert.AreEqual("\u0081", TextOf(Module().StrConv(V(Bytes(0x81)), VBStrConv.VBUnicode)));
        Assert.AreEqual("あ", TextOf(Module(codePage: 932).StrConv(V(Bytes(0x82, 0xA0)), VBStrConv.VBUnicode)), "a lead and a trail byte");
    }

    [TestMethod]
    public void StrConv_FromUnicode_WritesTheCharactersAsTheAnsiCodePagesBytes()
    {
        var narrowed = Module().StrConv(V("abc"), VBStrConv.VBFromUnicode);
        CollectionAssert.AreEqual(new byte[] { 0x61, 0x62, 0x63 }, BytesOf(narrowed));
        Assert.AreEqual(1, StringOf(narrowed).Length, "two bytes to a character, and a trailing byte");

        CollectionAssert.AreEqual(new byte[] { 0x80 }, BytesOf(Module().StrConv(V("€"), VBStrConv.VBFromUnicode)));
        CollectionAssert.AreEqual(new byte[] { 0x3F }, BytesOf(Module().StrConv(V("あ"), VBStrConv.VBFromUnicode)), "a character the code page lacks");
        CollectionAssert.AreEqual(new byte[] { 0x82, 0xA0 }, BytesOf(Module(codePage: 932).StrConv(V("あ"), VBStrConv.VBFromUnicode)));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(256)]
    [DataRow(512)]
    [DataRow(1024)]
    [DataRow(64 + 128)]
    [DataRow(64 + 1)]
    [DataRow(128 + 2)]
    [DataRow(128 + 3)]
    [DataRow(64 + 4)]
    [DataRow(128 + 16)]
    [DataRow(128 + 8)]
    [DataRow(4 + 8)]
    [DataRow(16 + 32)]
    public void StrConv_AnUndefinedOrMutuallyExclusiveConversion_IsError5(int conversion)
        => Assert.AreEqual(InvalidProcedureCall, ErrorOf(Module().StrConv(V("abc"), Conversion(conversion))));

    [TestMethod]
    [DataRow(4, 0)]
    [DataRow(8, 0)]
    [DataRow(16, 0)]
    [DataRow(32, 0)]
    [DataRow(4, 1033)]
    [DataRow(8, 1036)]
    [DataRow(16, 2052)]
    [DataRow(32, 1042)]
    public void StrConv_TheEastAsianConversions_AreError5_WhereTheyDoNotApply(int conversion, int lcid)
        => Assert.AreEqual(InvalidProcedureCall, ErrorOf(Module().StrConv(V("abc"), Conversion(conversion), lcid == 0 ? null : L(lcid))));

    private const int Japanese = 1041;

    // a Japanese environment's ANSI code page: a conversion reads the String in it.
    private const int JapaneseCodePage = 932;

    [TestMethod]
    [DataRow("abc 123!~", "ａｂｃ　１２３！～")]
    [DataRow("ｶﾞｷﾞﾊﾟﾝ ｳﾞ", "ガギパン　ヴ")]
    [DataRow("｢ｱｲｳ｣､｡･ｰ", "「アイウ」、。・ー")]
    [DataRow("ﾞ", "゛")]
    [DataRow("¥¢", "\\￠")]
    [DataRow("¦¯", "｜￣")]
    [DataRow("\\", "\\")]
    [DataRow("ひらがな漢字", "ひらがな漢字")]
    public void StrConv_Wide_WidensTheNarrowCharacters(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module(Japanese, JapaneseCodePage).StrConv(V(text), VBStrConv.VBWide)));

    [TestMethod]
    [DataRow("ａｂｃ　１２３！～", "abc 123!~")]
    [DataRow("ガギパン　ヴ", "ｶﾞｷﾞﾊﾟﾝ ｳﾞ")]
    [DataRow("「アイウ」、。・ー", "｢ｱｲｳ｣､｡･ｰ")]
    [DataRow("ひらがな漢字", "ひらがな漢字")]
    public void StrConv_Narrow_NarrowsTheWideCharacters(string text, string expected)
        => Assert.AreEqual(expected, TextOf(Module(Japanese, JapaneseCodePage).StrConv(V(text), VBStrConv.VBNarrow)));

    // measured in MS-VBA: the backslash and the fullwidth one stay as they are in every East Asian locale.
    [TestMethod]
    [DataRow("\\", 4, 2052, "\\")]
    [DataRow("\\", 4, 1042, "\\")]
    [DataRow("\\¥", 4, 1028, "\\￥")]
    [DataRow("＼", 8, 2052, "＼")]
    [DataRow("＼￥", 8, 1041, "＼\\")]
    public void StrConv_TheBackslash_HasNoWideForm(string text, int conversion, int lcid, string expected)
        => Assert.AreEqual(expected, TextOf(Module().StrConv(V(text), Conversion(conversion), L(lcid))));

    [TestMethod]
    public void StrConv_KatakanaAndHiragana_ConvertBetweenTheSyllabaries()
    {
        Assert.AreEqual("ヒラガナ ヴヽ", TextOf(Module(Japanese, JapaneseCodePage).StrConv(V("ひらがな ゔゝ"), VBStrConv.VBKatakana)));
        Assert.AreEqual("ヒ ヴヽ", TextOf(Module(Japanese, JapaneseCodePage).StrConv(V("ひ ゔゝ"), VBStrConv.VBKatakana)), "932 has no ゔ, and reads it as ヴ");
        Assert.AreEqual("かたかな ヴゞ ?", TextOf(Module(Japanese, JapaneseCodePage).StrConv(V("カタカナ ヴヾ ヷ"), VBStrConv.VBHiragana)));
        Assert.AreEqual("ｶﾀｶﾅ", TextOf(Module(Japanese, JapaneseCodePage).StrConv(V("ｶﾀｶﾅ"), VBStrConv.VBHiragana)), "a halfwidth katakana has no hiragana");
    }

    [TestMethod]
    public void StrConv_TheConversions_AreMadeInOneOrder()
    {
        Assert.AreEqual("かたかな", TextOf(Module(Japanese, JapaneseCodePage).StrConv(V("ｶﾀｶﾅ"), VBStrConv.VBWide | VBStrConv.VBHiragana)), "widened, then a hiragana");
        Assert.AreEqual("ﾋﾗｶﾞﾅ", TextOf(Module(Japanese, JapaneseCodePage).StrConv(V("ひらがな"), VBStrConv.VBKatakana | VBStrConv.VBNarrow)), "a katakana, then narrowed");
        Assert.AreEqual("ＡＢＣ", TextOf(Module(Japanese, JapaneseCodePage).StrConv(V("abc"), VBStrConv.VBUpperCase | VBStrConv.VBWide)));
    }

    [TestMethod]
    public void StrConv_TheLocale_IsLocaleIDsOrTheEnvironments()
    {
        Assert.AreEqual("ａｂｃ", TextOf(Module().StrConv(V("abc"), VBStrConv.VBWide, L(2052))), "Chinese is an East Asian locale");
        Assert.AreEqual("ㄱ", TextOf(Module().StrConv(V("\uFFA1"), VBStrConv.VBWide, L(1042))), "so is Korean, whose halfwidth Hangul widens");
        Assert.AreEqual(InvalidProcedureCall, ErrorOf(Module(Japanese, JapaneseCodePage).StrConv(V("abc"), VBStrConv.VBWide, L(1033))));
    }

    [TestMethod]
    public void StrConv_ProperCase_InAnEastAsianLocale_StartsAWordAfterAnIdeographicSpace()
    {
        Assert.AreEqual("Abc\u3000Def", TextOf(Module(Japanese, JapaneseCodePage).StrConv(V("abc\u3000def"), VBStrConv.VBProperCase)));
        Assert.AreEqual("Abc\u3000Def", TextOf(Module().StrConv(V("abc\u3000def"), VBStrConv.VBProperCase, L(2052))));
        Assert.AreEqual("Abc\u3000Def", TextOf(Module().StrConv(V("abc\u3000def"), VBStrConv.VBProperCase, L(1042))));
        Assert.AreEqual("Abc Def", TextOf(Module(1033).StrConv(V("abc\u3000def"), VBStrConv.VBProperCase)), "Windows-1252 has no ideographic space");
    }

    [TestMethod]
    public void StrConv_OfNullEmptyOrANumber()
    {
        Assert.IsTrue(IsNull(Module().StrConv(VNull, VBStrConv.VBUpperCase)));
        Assert.IsTrue(IsNull(Module().StrConv(VNull, Conversion(-1))), "a Null String before Conversion's check");
        Assert.AreEqual(string.Empty, TextOf(Module().StrConv(VEmpty, VBStrConv.VBUpperCase)));
        Assert.AreEqual("123", TextOf(Module().StrConv(V(123), VBStrConv.VBUpperCase)));
    }

    [TestMethod]
    public void StrConv_OfAValueWithNoStringForm_IsError13_BeforeConversionsCheck()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().StrConv(VMissing, Conversion(-1))));

    [TestMethod]
    public void StrConv_ConvertsInLocaleIDsCodePage_OrTheEnvironments()
    {
        var greek = Module(codePage: 1253);
        Assert.AreEqual("Α", TextOf(greek.StrConv(V("α"), VBStrConv.VBUpperCase)), "the environment's, left out");
        Assert.AreEqual("Α", TextOf(greek.StrConv(V("α"), VBStrConv.VBUpperCase, L(0))), "and at 0");
        Assert.AreEqual("A", TextOf(greek.StrConv(V("α"), VBStrConv.VBUpperCase, L(1033))));
        Assert.AreEqual("A", TextOf(Module(lcid: 1032).StrConv(V("α"), VBStrConv.VBUpperCase)), "the environment's code page, not its locale's");
    }

    [TestMethod]
    [DataRow(12345)]
    [DataRow(1024)]
    [DataRow(2048)]
    [DataRow(-1)]
    public void StrConv_WithALocaleIDThatNamesNoLocale_IsError5(int lcid)
        => Assert.AreEqual(InvalidProcedureCall, ErrorOf(Module().StrConv(V("abc"), VBStrConv.VBUpperCase, L(lcid))));

    [TestMethod]
    public void StrConv_WithALocaleID_ConvertsInTheEnvironmentsCodePage()
    {
        // "the default code page of the system": a Japanese LocaleID does not make it 932.
        CollectionAssert.AreEqual(new byte[] { 0x80 }, BytesOf(Module().StrConv(V("€"), VBStrConv.VBFromUnicode, L(1041))));
        Assert.AreEqual("€", TextOf(Module().StrConv(V(Bytes(0x80)), VBStrConv.VBUnicode, L(1041))));
    }

    [TestMethod]
    public void StrConv_FromSource_WithTheLocaleLeftOut()
        => CollectionAssert.AreEqual(new[] { "Hello World", "ABC", "String", "Null" }, Run(
            "Debug.Print StrConv(\"hELLO wORLD\", vbProperCase)",
            "Debug.Print StrConv(\"abc\", vbUpperCase)",
            "Debug.Print TypeName(StrConv(\"a\", vbUpperCase))",
            "Debug.Print StrConv(Null, vbUpperCase)"));

    [TestMethod]
    public void StrConv_FromSource_RoundTripsThroughAByteArray()
        => CollectionAssert.AreEqual(new[] { "3", "abc", "2", "abc" }, RunWith([Field("B", VBResizableByteArrayType.TypeInfo)],
            "Debug.Print LenB(StrConv(\"abc\", vbFromUnicode))",
            "Debug.Print StrConv(StrConv(\"abc\", vbFromUnicode), vbUnicode)",
            "B = StrConv(\"abc\", vbFromUnicode)",
            "Debug.Print UBound(B)",
            "Debug.Print StrConv(B, vbUnicode)"));

    [TestMethod]
    public void StrConv_FromSource_Errors()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print StrConv(\"abc\", Null)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print StrConv(\"abc\", vbUpperCase, Null)"));
        Assert.AreEqual(InvalidProcedureCall, RunToError("Debug.Print StrConv(\"abc\", vbNarrow)"));
        Assert.AreEqual(InvalidProcedureCall, RunToError("Debug.Print StrConv(\"abc\", vbUnicode + vbFromUnicode)"));
    }
}
