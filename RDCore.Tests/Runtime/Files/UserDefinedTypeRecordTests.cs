using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// <strong>MS-VBAL §5.4.5.11-12</strong> a <c>Put</c> or <c>Get</c> whose data is a user-defined type — the
/// one-line binary serialization a UDT and these two statements exist together for.
/// </summary>
/// <remarks>
/// "If <c>data</c> is a UDT, then the value of each member of the UDT is written to the file... in the order
/// in which the members are declared in the UDT", and a <c>Get</c> reads the same members back in the same
/// order. Nothing pads between them, which is what makes <c>Len</c> of a UDT "the size as it will be written
/// to the file" and not the in-memory size <c>LenB</c> reports.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.11 Put")]
[TestCategory("MS-VBAL 5.4.5.12 Get")]
public sealed class UserDefinedTypeRecordTests
{
    private const string Root = "/ws";
    private const string Path = $"{Root}/records.dat";

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

    private static (MockFileSystem Files, RuntimeExecutionOutcome Outcome, VBUserDefinedTypeValue Record) Run(
        byte[] content, VBUserDefinedType type, params string[] body)
    {
        var variable = Variable("Rec", type);
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem, [variable], body);

        return (fileSystem, outcome, (VBUserDefinedTypeValue)type.CreateValue(session.Symbols.Resolver.GetValue(variable)));
    }

    private static int FieldOf(VBUserDefinedTypeValue record, string name)
        => Convert.ToInt32(record[name]!.Handle.Value.BoxedValue);

    private static string TextOf(VBUserDefinedTypeValue record, string name)
        => record[name]!.Handle.Value.BoxedValue as string ?? string.Empty;

    [TestMethod]
    public void AFieldAssignment_ReachesTheField()
    {
        // the prerequisite for any of this being usable: a program has to be able to fill the record before it
        // can serialize one. A UDT field is not an addressable symbol, so the assignment writes the cell on the
        // value rather than going through the "__let_op" operator a variable target does.
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));

        var (_, outcome, record) = Run([], type, "Rec.X = 3", "Rec.Y = 4");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(3, FieldOf(record, "X"));
        Assert.AreEqual(4, FieldOf(record, "Y"));
    }

    [TestMethod]
    public void AFieldRead_YieldsWhatWasAssigned()
    {
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));

        var (_, outcome, record) = Run([], type, "Rec.X = 3", "Rec.Y = Rec.X + 1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(4, FieldOf(record, "Y"));
    }

    [TestMethod]
    public void AFieldAssignment_CoercesToTheFieldsDeclaredType()
    {
        // a field's declared type is the destination of the same let-coercion an assignment to a variable of
        // that type applies - 2.67 rounds to 3 by MS-VBAL 5.5.1.2.1.1's banker's rounding.
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo));

        var (_, outcome, record) = Run([], type, "Rec.X = 2.67");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(3, FieldOf(record, "X"));
    }

    [TestMethod]
    public void Put_WritesEachMemberInDeclarationOrder_WithNoPadding()
    {
        // a Byte then a Long is 8 bytes in memory (LenB) and 5 in the file (Len): the record is the
        // concatenation of the members, and the alignment padding is not part of it.
        var type = Udt("TMixed", ("B", VBByteType.TypeInfo), ("L", VBLongType.TypeInfo));

        var (files, outcome, _) = Run([], type,
            "Rec.B = 1",
            "Rec.L = 2",
            $"Open \"{Path}\" For Binary As #1",
            "Put #1, 1, Rec",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreSequenceEqual<byte>([0x01, 0x02, 0x00, 0x00, 0x00], files.File.ReadAllBytes(Path));
    }

    [TestMethod]
    public void Get_FillsEachMemberInDeclarationOrder()
    {
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));

        var (_, outcome, record) = Run(
            [0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00], type,
            $"Open \"{Path}\" For Binary As #1",
            "Get #1, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(3, FieldOf(record, "X"));
        Assert.AreEqual(4, FieldOf(record, "Y"));
    }

    [TestMethod]
    public void PutThenGet_RoundTripsARecordInOneLineEach()
    {
        // the use case: one statement writes the whole record and one statement reads it back.
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));

        var (_, outcome, record) = Run([], type,
            "Rec.X = 3",
            "Rec.Y = 4",
            $"Open \"{Root}/out.dat\" For Binary As #1",
            "Put #1, 1, Rec",
            "Close #1",
            "Rec.X = 0",
            "Rec.Y = 0",
            $"Open \"{Root}/out.dat\" For Binary As #2",
            "Get #2, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(3, FieldOf(record, "X"));
        Assert.AreEqual(4, FieldOf(record, "Y"));
    }

    [TestMethod]
    public void PutThenGet_RoundTripsAStringMember()
    {
        // a String member is framed by its own row of the format, so in Random mode it carries a two-byte
        // length and reads back at whatever length it was written - which is the case Len cannot predict
        // ("Len might not be able to determine the actual number of storage bytes required when used with
        // variable-length strings in user-defined data types").
        var type = Udt("TNamed", ("Id", VBLongType.TypeInfo), ("Name", VBStringType.TypeInfo));

        var (_, outcome, record) = Run([], type,
            "Rec.Id = 7",
            "Rec.Name = \"Ducky\"",
            $"Open \"{Root}/out.dat\" For Random As #1 Len = 64",
            "Put #1, 1, Rec",
            "Close #1",
            "Rec.Name = \"\"",
            $"Open \"{Root}/out.dat\" For Random As #2 Len = 64",
            "Get #2, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(7, FieldOf(record, "Id"));
        Assert.AreEqual("Ducky", TextOf(record, "Name"));
    }

    [TestMethod]
    public void PutThenGet_RoundTripsEveryFixedWidthMemberType()
    {
        var type = Udt("TAll",
            ("I", VBIntegerType.TypeInfo), ("L", VBLongType.TypeInfo), ("D", VBDoubleType.TypeInfo),
            ("T", VBDateType.TypeInfo), ("B", VBBooleanType.TypeInfo), ("Y", VBByteType.TypeInfo));

        var (_, outcome, record) = Run([], type,
            "Rec.I = 7",
            "Rec.L = 70000",
            "Rec.D = 1.5",
            "Rec.T = #2020-01-15#",
            "Rec.B = True",
            "Rec.Y = 255",
            $"Open \"{Root}/out.dat\" For Binary As #1",
            "Put #1, 1, Rec",
            "Close #1",
            "Rec.I = 0",
            "Rec.L = 0",
            "Rec.D = 0",
            "Rec.T = 0",
            "Rec.B = False",
            "Rec.Y = 0",
            $"Open \"{Root}/out.dat\" For Binary As #2",
            "Get #2, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(7, FieldOf(record, "I"));
        Assert.AreEqual(70000, FieldOf(record, "L"));
        Assert.AreEqual(1.5, Convert.ToDouble(record["D"]!.Handle.Value.BoxedValue));
        Assert.AreEqual(new DateTime(2020, 1, 15), DateTime.FromOADate(Convert.ToDouble(record["T"]!.Handle.Value.BoxedValue)));
        Assert.IsTrue(Convert.ToBoolean(record["B"]!.Handle.Value.BoxedValue));
        Assert.AreEqual(255, FieldOf(record, "Y"));
    }

    [TestMethod]
    public void PutThenGet_RoundTripsANestedRecord()
    {
        // "the value of each member" goes all the way down: a nested UDT member is its own members, in their
        // own declaration order, with nothing marking where one record ends and the next begins.
        var inner = Udt("TInner", ("A", VBLongType.TypeInfo), ("B", VBLongType.TypeInfo));
        var outer = Udt("TOuter", ("Id", VBLongType.TypeInfo), ("Nested", inner));

        var (_, outcome, record) = Run([], outer,
            "Rec.Id = 1",
            "Rec.Nested.A = 2",
            "Rec.Nested.B = 3",
            $"Open \"{Root}/out.dat\" For Binary As #1",
            "Put #1, 1, Rec",
            "Close #1",
            "Rec.Nested.A = 0",
            "Rec.Nested.B = 0",
            $"Open \"{Root}/out.dat\" For Binary As #2",
            "Get #2, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var nested = (VBUserDefinedTypeValue)record["Nested"]!;
        Assert.AreEqual(2, FieldOf(nested, "A"));
        Assert.AreEqual(3, FieldOf(nested, "B"));
    }

    [TestMethod]
    public void Put_SuccessiveRecords_ThenGetByRecordNumber()
    {
        // what Random mode is for: a record number addresses the n-th record, so a file is an indexable table.
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));

        var (_, outcome, record) = Run([], type,
            "Rec.X = 10",
            "Rec.Y = 11",
            $"Open \"{Root}/out.dat\" For Random As #1 Len = 8",
            "Put #1, 1, Rec",
            "Rec.X = 20",
            "Rec.Y = 21",
            "Put #1, 2, Rec",
            "Close #1",
            $"Open \"{Root}/out.dat\" For Random As #2 Len = 8",
            "Get #2, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(10, FieldOf(record, "X"), "record 1 is the first one written, not the last");
        Assert.AreEqual(11, FieldOf(record, "Y"));
    }

    [TestMethod]
    public void Put_ARecordWiderThanTheRecordLength_IsBadRecordLength()
    {
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));

        var (_, outcome, _) = Run([], type,
            $"Open \"{Path}\" For Random As #1 Len = 4",
            "Put #1, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordLength, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Get_PastTheEndOfTheFile_IsInputPastEndOfFile()
    {
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));

        var (_, outcome, _) = Run([0x01, 0x00, 0x00, 0x00], type,
            $"Open \"{Path}\" For Binary As #1",
            "Get #1, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InputPastEndOfFile, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Get_FillsTheVariableInPlace_KeepingItsIdentity()
    {
        // a UDT variable has location identity, so a Get fills the record the program already holds rather
        // than replacing it - which is what lets the statement be a one-liner with no assignment at all.
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo));
        var variable = Variable("Rec", type);
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(new byte[] { 0x09, 0x00, 0x00, 0x00 }) });

        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem, [variable],
            $"Open \"{Path}\" For Binary As #1",
            "Get #1, 1, Rec");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);

        var before = (VBUserDefinedTypeValue)type.CreateValue(session.Symbols.Resolver.GetValue(variable));
        var after = (VBUserDefinedTypeValue)type.CreateValue(session.Symbols.Resolver.GetValue(variable));
        Assert.AreSame(before, after, "the variable holds one UDT instance, not a fresh one per read");
        Assert.AreEqual(9, FieldOf(after, "X"));
    }
}
