using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A String whose data is an odd number of bytes, from source: <c>S = B</c> with <c>B</c> a <c>Byte()</c> of three
/// elements (<strong>MS-VBAL §5.5.1.2.6</strong>) is the character <c>"A"</c> and a byte left over, and every
/// consumer of a String's bytes sees all three of them.
/// </summary>
/// <remarks>
/// Character consumers - <c>Debug.Print</c>, <c>Like</c>, fitting into a <c>String * length</c> - see the whole
/// characters only. <c>B</c> is given its bytes before the body runs, so that every body starts from the same String.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.6 Let-coercion to and from a resizable Byte array")]
public sealed class ByteStringTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static readonly VBModuleFieldVariableMemberSymbol B = Variable("B", VBResizableByteArrayType.TypeInfo);

    // the variables every body here can use: B, the Byte() it starts from, and the Strings and the Variant it moves into.
    private static Symbol[] Variables() =>
    [
        B,
        Variable("B2", VBResizableByteArrayType.TypeInfo),
        Variable("S", VBStringType.TypeInfo),
        Variable("T", VBStringType.TypeInfo),
        Variable("U", VBStringType.TypeInfo),
        Variable("N", VBStringType.TypeInfo),
        Variable("V", VBVariantType.TypeInfo),
        Variable("F", new VBFixedStringType(2)),
    ];

    // B holds 41 00 42: the character "A", and the byte 42 that makes no character on its own.
    private static void ArrangeTheBytes(IRuntimeSession session)
    {
        var array = new VBResizableByteArrayValue([(0, 2)]);
        byte[] elements = [0x41, 0x00, 0x42];
        for (var index = 0; index < elements.Length; index++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(elements[index])), index);
        }

        session.Symbols.Resolver.GetValue(B).SetValue(
            session.Symbols.Resolver, new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array)));
    }

    private static (IRuntimeSession Session, IReadOnlyList<string> Printed) Run(
        RuntimeSourceHarness.Surroundings surroundings, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (session, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, Variables(), output, standardLibrary: true, ArrangeTheBytes, surroundings, ["S = B", .. body]);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind,
            $"{outcome.ErrorInfo?.ErrorId} {outcome.ErrorInfo?.Description} | {outcome.ErrorInfo?.Verbose}");
        return (session, [.. output.Lines.Select(line => line.Trim())]);
    }

    private static IReadOnlyList<string> Run(params string[] body) => Run(new RuntimeSourceHarness.Surroundings(), body).Printed;

    [TestMethod]
    public void AnOddByteString_SurvivesAssignment_AndAVariant()
        // the Variant holds the whole value, and the String it is assigned back to is "a copy of the source string".
        => CollectionAssert.AreEqual(new[] { "3", "3", "3" },
            Run("T = S", "V = S", "U = V", "Debug.Print LenB(T)", "Debug.Print LenB(V)", "Debug.Print LenB(U)").ToArray());

    [TestMethod]
    public void DebugPrint_AnOddByteString_ShowsItsWholeCharactersOnly()
        => Assert.AreEqual("A", Run("Debug.Print S").Single());

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5 Relational Operators")]
    public void Comparing_AnOddByteString_SeesItsTrailingByte()
        // a relational operator compares every byte; Like matches characters, and the byte is not one.
        => CollectionAssert.AreEqual(new[] { "False", "True", "True" },
            Run("Debug.Print (S = \"A\")", "Debug.Print (S > \"A\")", "Debug.Print (S Like \"A\")").ToArray());

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5 Relational Operators")]
    public void SelectCase_SeesATrailingByte()
        => Assert.AreEqual("other", Run(
            "Select Case S",
            "Case \"A\"",
            "Debug.Print \"A\"",
            "Case Else",
            "Debug.Print \"other\"",
            "End Select").Single());

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.4 & Operator")]
    public void Concatenating_AnOddByteString_PairsTheBytes()
        => CollectionAssert.AreEqual(new[] { "6", "6" }, Run("Debug.Print LenB(S & S)", "Debug.Print LenB(S + S)").ToArray());

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.5 Let-coercion to and from FixedString")]
    public void AssigningAnOddByteString_ToAFixedLengthString_DropsTheByte()
        // MS-VBAL 5.5.1.2.5 fits a String to the length by its characters: "A" padded to two of them, four bytes.
        => Assert.AreEqual("4", Run("F = S", "Debug.Print LenB(F)").Single());

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.6 LSet")]
    public void LSet_FromAnOddByteString_FitsItsCharacters()
        // "the number of characters in" the String is what LSet fits, and the byte left over is not one.
        => CollectionAssert.AreEqual(new[] { "4", "A |" }, Run("F = \"zz\"", "LSet F = S", "Debug.Print LenB(F)", "Debug.Print F & \"|\"").ToArray());

    [TestMethod]
    public void AnOddByteString_RoundTripsThroughAByteArray()
        => Assert.AreEqual("66", Run("B2 = S", "Debug.Print B2(2)").Single());

    [TestMethod]
    [TestCategory("MS-VBAL 6.1.2.3 Conversion")]
    public void CStr_OfAnOddByteString_KeepsTheByte()
        // CStr is its argument Let-coerced to String, and that is a copy of the String it already is.
        => Assert.AreEqual("3", Run("T = CStr(S)", "Debug.Print LenB(T)").Single());

    [TestMethod]
    [TestCategory("MS-VBAL 6.1.2.3 Conversion")]
    public void CStr_OfAByteArrayOfAnOddLength_IsAnOddByteString()
        => Assert.AreEqual("3", Run("T = CStr(B)", "Debug.Print LenB(T)").Single());

    [TestMethod]
    [TestCategory("MS-VBAL 6.1.2.3 Conversion")]
    public void CStr_OfTheNullString_IsTheNullString()
    {
        // N was never assigned, so it is vbNullString; what CStr returns is a copy of it, not a zero-length string.
        var (session, _) = Run(new RuntimeSourceHarness.Surroundings(), "T = \"x\"", "T = CStr(N)");

        var t = Variables().Single(symbol => symbol.Name == "T");
        Assert.IsNull(session.Symbols.Resolver.GetValue(t).Value.BoxedValue);
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.6.6 Parenthesized Expressions")]
    public void AParenthesizedOddByteString_KeepsTheByte()
        // the parentheses Let-coerce the String to its own type, which is a copy of it.
        => Assert.AreEqual("3", Run("Debug.Print LenB((S))").Single());

    [TestMethod]
    public void AnOddByteString_PassedByValue_KeepsTheByte()
    {
        // a ByVal argument is Let-coerced to the parameter's declared type, with or without the parentheses that make
        // the argument a value of its own.
        var stub = new VBProcedureMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "Foo", ScopeKind.Module,
            SymbolKindExt.Procedure, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var p = new VBParameterSymbol(Workspace, stub.Uri, "P", SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal, VBStringType.TypeInfo);
        var foo = stub with { Parameters = [p] };

        var (_, printed) = Run(
            new RuntimeSourceHarness.Surroundings { Procedures = [(foo, ["Debug.Print LenB(P)"])] },
            "Foo S", "Foo (S)");

        CollectionAssert.AreEqual(new[] { "3", "3" }, printed.ToArray());
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.5 Mid/MidB/Mid$/MidB$ Statement")]
    public void MidB_OnAnOddByteString_CountsAndReplacesTheByteLeftOver()
        // the byte left over is the third byte of S, so MidB can start there and replace it: 41 00 5A, still three bytes.
        => CollectionAssert.AreEqual(new[] { "3", "90" }, Run("MidB(S, 3, 1) = \"Z\"", "Debug.Print LenB(S)", "B2 = S", "Debug.Print B2(2)").ToArray());

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.5 Mid/MidB/Mid$/MidB$ Statement")]
    public void MidB_SplittingACharacter_KeepsTheBytesItWrites()
    {
        // U is the character U+00D8, D8 00; written over the second byte of "AB" (41 00 42 00) it makes 41 D8, a lone
        // surrogate, which the String's data holds as it is rather than as the replacement character's FD FF.
        var printed = Run(
            "ReDim B2(1)", "B2(0) = &HD8", "B2(1) = 0", "U = B2", "T = \"AB\"", "MidB(T, 2, 1) = U", "B2 = T",
            "Debug.Print B2(0)", "Debug.Print B2(1)", "Debug.Print LenB(T)");

        CollectionAssert.AreEqual(new[] { "65", "216", "4" }, printed.ToArray());
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.5 Mid/MidB/Mid$/MidB$ Statement")]
    public void Mid_OnAnOddByteString_ReplacesTheCharacter_AndKeepsTheByteLeftOver()
        // "A" is replaced by "Z" as the two bytes it is, and the byte after it is not a character: 5A 00 42.
        => CollectionAssert.AreEqual(new[] { "3", "90", "66" },
            Run("Mid(S, 1, 1) = \"Z\"", "Debug.Print LenB(S)", "B2 = S", "Debug.Print B2(0)", "Debug.Print B2(2)").ToArray());

    [TestMethod]
    [TestCategory("MS-VBAL 5.4.3.5 Mid/MidB/Mid$/MidB$ Statement")]
    public void Mid_OnAnOddByteString_ReplacesWholeCharactersOnly()
        // x is the least of the characters left in S from the start, one, and the characters of "ZQ", two: one character
        // is replaced, and nothing of "Q" is written over the byte left over.
        => CollectionAssert.AreEqual(new[] { "3", "1", "66" },
            Run("Mid(S, 1) = \"ZQ\"", "Debug.Print LenB(S)", "Debug.Print Len(S)", "B2 = S", "Debug.Print B2(2)").ToArray());
}
