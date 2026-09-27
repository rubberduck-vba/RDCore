using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Tests.Runtime;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.22</strong> <c>Len</c> and <c>LenB</c> — "the number of characters in a string
/// or the number of bytes required to store a variable on the current platform".
/// </summary>
/// <remarks>
/// The two functions that make a UDT's two sizes observable to a program: <c>Len</c> "returns the size as it
/// will be written to the file", <c>LenB</c> "the in-memory size, including any implementation-specific
/// padding between elements". Everything else answers the same for both, except a string, whose characters are
/// two bytes each.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.22 Len / LenB")]
public sealed class LenAndLenBTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBUserDefinedType Udt(string name, params (string Name, VBType Type)[] fields)
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri(name);
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [.. fields.Select(field =>
        {
            var fieldUri = TestUri.TestUserDefinedTypeMemberUri(field.Name, name);
            return (VBTypeMemberSymbol)new VBUserDefinedTypeFieldSymbol(
                fieldUri, fieldUri, field.Name, field.Type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        })]);
    }

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    /// <summary>Runs a body with the standard library available, and reports what it printed.</summary>
    private static IReadOnlyList<string> Run((string Name, VBType Type)[] variables, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var symbols = variables.Select(variable => Variable(variable.Name, variable.Type)).ToArray();

        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, symbols, output, standardLibrary: true, body);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind,
            $"{outcome.ErrorInfo?.ErrorId} {outcome.ErrorInfo?.Description} | {outcome.ErrorInfo?.Verbose}");
        return output.Lines;
    }

    [TestMethod]
    public void Len_OfAStringLiteral_CountsItsCharacters()
        => Assert.Contains("5", Run([], "Debug.Print Len(\"Ducky\")")[0]);

    [TestMethod]
    public void LenB_OfAString_CountsTwoBytesPerCharacter()
        // "LenB can return different values than Len for Unicode strings... LenB returns the number of bytes
        // used to represent that string."
        => Assert.Contains("10", Run([], "Debug.Print LenB(\"Ducky\")")[0]);

    [TestMethod]
    public void Len_OfAnEmptyString_IsZero()
        => Assert.Contains("0", Run([], "Debug.Print Len(\"\")")[0]);

    [TestMethod]
    public void Len_OfAnInteger_IsTheBytesItOccupies()
        // "the number of bytes required to store a variable on the current platform".
        => Assert.Contains("2", Run([("N", VBIntegerType.TypeInfo)], "Debug.Print Len(N)")[0]);

    [TestMethod]
    public void Len_OfALong_IsFour()
        => Assert.Contains("4", Run([("N", VBLongType.TypeInfo)], "Debug.Print Len(N)")[0]);

    [TestMethod]
    public void Len_OfADouble_IsEight()
        => Assert.Contains("8", Run([("D", VBDoubleType.TypeInfo)], "Debug.Print Len(D)")[0]);

    [TestMethod]
    public void LenB_OfALong_IsTheSameAsLen()
        // "LenB will return the same value as Len, except for strings or UDTs."
        => Assert.Contains("4", Run([("N", VBLongType.TypeInfo)], "Debug.Print LenB(N)")[0]);

    [TestMethod]
    public void Len_OfNull_IsNull()
    {
        // "If Expression contains the data value Null, Null is returned" - not 0, and not an error.
        var output = Run([], "Debug.Print Len(Null)");

        Assert.Contains("Null", output[0]);
    }

    [TestMethod]
    public void Len_OfAUserDefinedType_IsTheSizeAsWrittenToTheFile()
    {
        // a Byte then a Long: 5 bytes concatenated, which is what a Put writes.
        var type = Udt("TMixed", ("B", VBByteType.TypeInfo), ("L", VBLongType.TypeInfo));

        Assert.Contains("5", Run([("Rec", type)], "Debug.Print Len(Rec)")[0]);
    }

    [TestMethod]
    public void LenB_OfAUserDefinedType_IsTheInMemorySize_PaddingIncluded()
    {
        // the same record in memory: the Long is aligned to 4, so 5 bytes of data occupy 8.
        var type = Udt("TMixed", ("B", VBByteType.TypeInfo), ("L", VBLongType.TypeInfo));

        Assert.Contains("8", Run([("Rec", type)], "Debug.Print LenB(Rec)")[0]);
    }

    [TestMethod]
    public void LenAndLenB_OfARecordThatNeedsNoPadding_Agree()
    {
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));

        var output = Run([("Rec", type)], "Debug.Print Len(Rec)", "Debug.Print LenB(Rec)");

        Assert.Contains("8", output[0]);
        Assert.Contains("8", output[1]);
    }

    [TestMethod]
    public void Len_OfARecordWithAStringMember_CountsTheCharactersItHolds()
    {
        // the case the specification warns about - "Len might not be able to determine the actual number of
        // storage bytes required when used with variable-length strings in user-defined data types" - because
        // the answer changes with the content: 4 bytes of Long plus however long the string currently is.
        var type = Udt("TNamed", ("Id", VBLongType.TypeInfo), ("Name", VBStringType.TypeInfo));

        var output = Run([("Rec", type)], "Rec.Name = \"Ducky\"", "Debug.Print Len(Rec)");

        Assert.Contains("9", output[0]);
    }

    [TestMethod]
    public void LenB_OfARecordWithAStringMember_CountsThePointer()
    {
        // in memory the member is a pointer, whatever it points at - so LenB does not move when the string does.
        var type = Udt("TNamed", ("Id", VBLongType.TypeInfo), ("Name", VBStringType.TypeInfo));

        var output = Run([("Rec", type)], "Rec.Name = \"Ducky\"", "Debug.Print LenB(Rec)");

        Assert.Contains("8", output[0]);
    }

    [TestMethod]
    public void Len_OfAFixedLengthStringMember_IsItsDeclaredLength()
    {
        var type = Udt("TFixed", ("Code", new VBFixedStringType(4)));

        Assert.Contains("4", Run([("Rec", type)], "Debug.Print Len(Rec)")[0]);
    }

    [TestMethod]
    public void LenB_OfAFixedLengthStringMember_IsTwoBytesPerCharacter()
    {
        var type = Udt("TFixed", ("Code", new VBFixedStringType(4)));

        Assert.Contains("8", Run([("Rec", type)], "Debug.Print LenB(Rec)")[0]);
    }
}
