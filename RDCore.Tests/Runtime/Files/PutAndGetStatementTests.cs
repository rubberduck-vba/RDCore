using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// <strong>MS-VBAL §5.4.5.11-12</strong> the <c>Put</c> and <c>Get</c> statements — records of bytes, in the
/// format the specification's <em>Variant Data File Type Descriptors</em> and <em>Binary File Data Formats</em>
/// tables define.
/// </summary>
/// <remarks>
/// A record's bytes are checked directly wherever the format says what they are — a <c>Boolean</c> is
/// <c>FF FF</c>, a <c>Random</c>-mode <c>String</c> carries a two-byte length — because the point of the format
/// is that a file written here is one MS-VBA reads, and a round trip through <c>Put</c> and <c>Get</c> alone
/// would pass just as well on a format of this suite's own invention.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.11 Put")]
[TestCategory("MS-VBAL 5.4.5.12 Get")]
public sealed class PutAndGetStatementTests
{
    private const string Root = "/ws";
    private const string Path = $"{Root}/data.dat";

    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static (MockFileSystem Files, RuntimeExecutionOutcome Outcome, Dictionary<string, object?> Values) Run(
        byte[] content, (string Name, VBType Type)[] variables, params string[] body)
    {
        var symbols = variables.Select(variable => Variable(variable.Name, variable.Type)).ToArray();
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem, symbols, body);

        return (fileSystem, outcome, symbols.ToDictionary(
            symbol => symbol.Name,
            symbol => (object?)session.Symbols.Resolver.GetValue(symbol).Value.BoxedValue));
    }

    [TestMethod]
    public void Put_AnInteger_WritesTwoLittleEndianBytes()
    {
        // "A two byte signed integer output in little-endian form."
        var (files, outcome, _) = Run([], [("N", VBIntegerType.TypeInfo)],
            "N = 258",
            $"Open \"{Path}\" For Binary As #1",
            "Put #1, 1, N",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0x02, 0x01], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Put_ALong_WritesFourBytes()
    {
        var (files, outcome, _) = Run([], [("N", VBLongType.TypeInfo)],
            "N = 1",
            $"Open \"{Path}\" For Binary As #1",
            "Put #1, 1, N",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0x01, 0x00, 0x00, 0x00], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Put_ATrueBoolean_WritesFFFF()
    {
        // "If the data value of the Boolean is True, then the two bytes are FF FF. Otherwise, the two bytes are
        // 00 00" - so it is two bytes, and True is not 01.
        var (files, outcome, _) = Run([], [("B", VBBooleanType.TypeInfo)],
            "B = True",
            $"Open \"{Path}\" For Binary As #1",
            "Put #1, 1, B",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0xFF, 0xFF], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Put_AFalseBoolean_WritesZeroes()
    {
        var (files, outcome, _) = Run([], [("B", VBBooleanType.TypeInfo)],
            "B = False",
            $"Open \"{Path}\" For Binary As #1",
            "Put #1, 1, B",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0x00, 0x00], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Put_AStringInBinaryMode_WritesTheCharactersAlone()
    {
        // "In binary mode there is no two-byte prefix, and the String is stored in ANSI form, without NULL
        // termination."
        var (files, outcome, _) = Run([], [("S", VBStringType.TypeInfo)],
            "S = \"ab\"",
            $"Open \"{Path}\" For Binary As #1",
            "Put #1, 1, S",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0x61, 0x62], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Put_AStringInRandomMode_PrefixesItsLength()
    {
        // "In random mode, the first two bytes are the length of the String."
        var (files, outcome, _) = Run([], [("S", VBStringType.TypeInfo)],
            "S = \"ab\"",
            $"Open \"{Path}\" For Random As #1 Len = 16",
            "Put #1, 1, S",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0x02, 0x00, 0x61, 0x62], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Put_AVariant_PrecedesTheValueWithItsTypeDescriptor()
    {
        // "When outputting a variable whose declared type is Variant, a two byte type descriptor is output
        // before the actual value of the variable" - 02 00 for an Integer, which is what the literal 1 is.
        var (files, outcome, _) = Run([], [("V", VBVariantType.TypeInfo)],
            "V = 1",
            $"Open \"{Path}\" For Binary As #1",
            "Put #1, 1, V",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0x02, 0x00, 0x01, 0x00], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Put_AtARecordNumber_PositionsByRecordLength()
    {
        // "The file-pointer-position is updated to be exactly (<record-number> * <rec-length>) number of bytes
        // from the start" - counted from position 1, which is what MS-VBAL 5.4.5.3 makes a file-pointer-position
        // and what this statement defaults a record number to, so record 3 of a 4-byte record starts at byte 8.
        var (files, outcome, _) = Run([], [("N", VBLongType.TypeInfo)],
            "N = 1",
            $"Open \"{Path}\" For Random As #1 Len = 4",
            "Put #1, 3, N",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var bytes = files.File.ReadAllBytes(Path);
        Assert.AreEqual(12, bytes.Length);
        Assert.AreSequenceEqual<byte>([0x01, 0x00, 0x00, 0x00], bytes[8..]);
    }

    [TestMethod]
    public void Put_WithNoRecordNumber_WritesAtTheCurrentPosition()
    {
        // "If no <record-number> is specified, the effect is as if <record-number> is the current
        // file-pointer-position" - so two consecutive Puts write consecutive records.
        var (files, outcome, _) = Run([], [("N", VBIntegerType.TypeInfo)],
            "N = 1",
            $"Open \"{Path}\" For Binary As #1",
            "Put #1, , N",
            "N = 2",
            "Put #1, , N",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0x01, 0x00, 0x02, 0x00], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Put_MoreBytesThanTheRecordLength_IsBadRecordLength()
    {
        // "If the number of bytes written is more than the specified <rec-length>, an error is generated
        // (#59, 'Bad record length')."
        var (_, outcome, _) = Run([], [("N", VBLongType.TypeInfo)],
            "N = 1",
            $"Open \"{Path}\" For Random As #1 Len = 2",
            "Put #1, 1, N");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordLength, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Put_OnAnInputChannel_IsBadFileMode()
    {
        // MS-VBAL 5.4.5.1's table: Put # is valid in Binary and Random modes only.
        var (_, outcome, _) = Run([], [("N", VBLongType.TypeInfo)],
            $"Open \"{Path}\" For Input As #1",
            "Put #1, 1, N");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileMode, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Get_AnInteger_ReadsTwoLittleEndianBytes()
    {
        var (_, outcome, values) = Run([0x02, 0x01], [("N", VBIntegerType.TypeInfo)],
            $"Open \"{Path}\" For Binary As #1",
            "Get #1, 1, N");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual((short)258, Convert.ToInt16(values["N"]));
    }

    [TestMethod]
    public void Get_ABoolean_ReadsFFFFAsTrue()
    {
        var (_, outcome, values) = Run([0xFF, 0xFF], [("B", VBBooleanType.TypeInfo)],
            $"Open \"{Path}\" For Binary As #1",
            "Get #1, 1, B");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(Convert.ToBoolean(values["B"]));
    }

    [TestMethod]
    public void Get_AStringInRandomMode_ReadsTheLengthPrefixFirst()
    {
        // "Two bytes are read from the file. The data value of these two bytes is the number of bytes to read
        // from the file."
        var (_, outcome, values) = Run([0x02, 0x00, 0x61, 0x62], [("S", VBStringType.TypeInfo)],
            $"Open \"{Path}\" For Random As #1 Len = 16",
            "Get #1, 1, S");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("ab", values["S"]);
    }

    [TestMethod]
    public void Get_AStringInBinaryMode_ReadsAsManyBytesAsTheVariableIsLong()
    {
        // "If the value type of <variable> is String, then the number of bytes to read is the number of
        // characters in <variable>" - the variable's own length is the only thing that says where to stop,
        // there being no prefix in the record.
        var (_, outcome, values) = Run([0x61, 0x62, 0x63, 0x64], [("S", VBStringType.TypeInfo)],
            "S = \"xx\"",
            $"Open \"{Path}\" For Binary As #1",
            "Get #1, 1, S");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("ab", values["S"]);
    }

    [TestMethod]
    public void Get_AVariant_ReadsTheTypeDescriptorFirst()
    {
        var (_, outcome, values) = Run([0x03, 0x00, 0x01, 0x00, 0x00, 0x00], [("V", VBVariantType.TypeInfo)],
            $"Open \"{Path}\" For Binary As #1",
            "Get #1, 1, V");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(1, Convert.ToInt32(values["V"]));
    }

    [TestMethod]
    public void Get_PastTheEndOfTheFile_IsInputPastEndOfFile()
    {
        var (_, outcome, _) = Run([0x01], [("N", VBLongType.TypeInfo)],
            $"Open \"{Path}\" For Binary As #1",
            "Get #1, 1, N");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InputPastEndOfFile, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Get_OnAnOutputChannel_IsBadFileMode()
    {
        var (_, outcome, _) = Run([], [("N", VBLongType.TypeInfo)],
            $"Open \"{Root}/out.dat\" For Output As #1",
            "Get #1, 1, N");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileMode, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void PutThenGet_RoundTripsARecordOfEachFixedWidthType()
    {
        // one record per width the format defines, written and read back through the file - the check that the
        // two halves agree about every row of the table, which the byte-level tests above cannot make.
        var (_, outcome, values) = Run(
            [],
            [
                ("I", VBIntegerType.TypeInfo), ("L", VBLongType.TypeInfo), ("D", VBDoubleType.TypeInfo),
                ("T", VBDateType.TypeInfo), ("B", VBBooleanType.TypeInfo),
            ],
            "I = 7",
            "L = 70000",
            "D = 1.5",
            "T = #2020-01-15#",
            "B = True",
            $"Open \"{Root}/out.dat\" For Binary As #1",
            "Put #1, , I",
            "Put #1, , L",
            "Put #1, , D",
            "Put #1, , T",
            "Put #1, , B",
            "Close #1",
            "I = 0",
            "L = 0",
            "D = 0",
            "T = 0",
            "B = False",
            $"Open \"{Root}/out.dat\" For Binary As #2",
            "Get #2, , I",
            "Get #2, , L",
            "Get #2, , D",
            "Get #2, , T",
            "Get #2, , B");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual((short)7, Convert.ToInt16(values["I"]));
        Assert.AreEqual(70000, Convert.ToInt32(values["L"]));
        Assert.AreEqual(1.5, Convert.ToDouble(values["D"]));
        Assert.AreEqual(new DateTime(2020, 1, 15), DateTime.FromOADate(Convert.ToDouble(values["T"])));
        Assert.IsTrue(Convert.ToBoolean(values["B"]));
    }
}
