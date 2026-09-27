using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// <strong>MS-VBAL §5.4.5.10</strong> the <c>Input #</c> statement: the fields of a record, read into one
/// variable each.
/// </summary>
/// <remarks>
/// The reading half of <c>Write #</c>, so the record spellings the two agree on — a quoted string,
/// <c>#TRUE#</c>, <c>#NULL#</c>, <c>#ERROR n#</c>, <c>#yyyy-mm-dd#</c> — are what most of these cover, plus
/// the rules that depend on the declared type of the variable being read into rather than on the field.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.10 Input")]
public sealed class InputStatementTests
{
    private const string Root = "/ws";
    private const string Path = $"{Root}/data.txt";

    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    /// <summary>
    /// Runs <paramref name="body"/> against a file holding <paramref name="content"/>, with one module
    /// variable per entry of <paramref name="variables"/>, and reports what each ended up holding.
    /// </summary>
    private static (RuntimeExecutionOutcome Outcome, Dictionary<string, object?> Values) Run(
        string content, (string Name, VBType Type)[] variables, params string[] body)
    {
        var symbols = variables.Select(variable => Variable(variable.Name, variable.Type)).ToArray();
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem, symbols, body);

        return (outcome, symbols.ToDictionary(
            symbol => symbol.Name,
            symbol => session.Symbols.Resolver.GetValue(symbol).Value.BoxedValue));
    }

    private static (RuntimeExecutionOutcome Outcome, object? Value) RunOne(
        string content, VBType type, params string[] extraStatements)
    {
        var (outcome, values) = Run(content, [("V", type)],
            [$"Open \"{Path}\" For Input As #1", "Input #1, V", .. extraStatements]);

        return (outcome, values["V"]);
    }

    [TestMethod]
    public void Input_AQuotedField_ReadsItsCharacters()
    {
        // "neither DQUOTE is included in the sequence of characters."
        var (outcome, value) = RunOne("\"hello\"\r\n", VBStringType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("hello", value);
    }

    [TestMethod]
    public void Input_ACommaInsideAQuotedField_IsPartOfTheValue()
    {
        // the reason Write # quotes strings at all: the separator inside one is not a separator.
        var (outcome, value) = RunOne("\"a,b\"\r\n", VBStringType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("a,b", value);
    }

    [TestMethod]
    public void Input_TwoVariables_ReadTwoFields()
    {
        var (outcome, values) = Run("\"a\",\"b\"\r\n", [("A", VBStringType.TypeInfo), ("B", VBStringType.TypeInfo)],
            $"Open \"{Path}\" For Input As #1",
            "Input #1, A, B");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("a", values["A"]);
        Assert.AreEqual("b", values["B"]);
    }

    [TestMethod]
    public void Input_FieldsOnSuccessiveRecords_ReadAsSuccessiveFields()
    {
        // "Characters are read... until a non-whitespace character is encountered. These whitespace characters
        // are discarded" - and a line terminator is whitespace, so one Input # can span records.
        var (outcome, values) = Run("\"a\"\r\n\"b\"\r\n", [("A", VBStringType.TypeInfo), ("B", VBStringType.TypeInfo)],
            $"Open \"{Path}\" For Input As #1",
            "Input #1, A, B");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("a", values["A"]);
        Assert.AreEqual("b", values["B"]);
    }

    [TestMethod]
    public void Input_ANumericField_ReadsIntoANumericVariable()
    {
        var (outcome, value) = RunOne("42\r\n", VBLongType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public void Input_ADecimalField_ReadsWithTheInvariantSeparator()
    {
        // Write # writes "." whatever the locale, so Input # reads "." whatever the locale.
        var (outcome, value) = RunOne("1.5\r\n", VBDoubleType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(1.5, value);
    }

    [TestMethod]
    public void Input_HashTrue_ReadsAsTrue()
    {
        var (outcome, value) = RunOne("#TRUE#\r\n", VBBooleanType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(true, Convert.ToBoolean(value));
    }

    [TestMethod]
    public void Input_HashFalse_ReadsAsFalse()
    {
        // "it is assigned the value false, unless the sequence of characters read are '#TRUE#'" - so #FALSE#
        // is read as False by the same rule anything that is not #TRUE# is.
        var (outcome, value) = RunOne("#FALSE#\r\n", VBBooleanType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(false, Convert.ToBoolean(value));
    }

    [TestMethod]
    public void Input_ANumericFieldIntoABoolean_IsOverflow()
    {
        // "If the sequence of characters is numeric an 'Overflow' error is generated (error number 6)."
        var (outcome, _) = RunOne("1\r\n", VBBooleanType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Input_ADelimitedDate_ReadsIntoADateVariable()
    {
        var (outcome, value) = RunOne("#2020-01-15#\r\n", VBDateType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(new DateTime(2020, 1, 15), DateTime.FromOADate(Convert.ToDouble(value)));
    }

    [TestMethod]
    public void Input_ADelimitedDateAndTime_ReadsBoth()
    {
        var (outcome, value) = RunOne("#2020-01-15 13:45:00#\r\n", VBDateType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(new DateTime(2020, 1, 15, 13, 45, 0), DateTime.FromOADate(Convert.ToDouble(value)));
    }

    [TestMethod]
    public void Input_AnUndelimitedFieldIntoADate_IsOverflow()
    {
        // "If the first character at file-pointer-position is not '#', then error 6 ('Overflow') is generated."
        var (outcome, _) = RunOne("2020-01-15\r\n", VBDateType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Input_HashNull_ReadsAsNull()
    {
        // "If the sequence of characters read from the file are '#NULL#' then the Null value is Let-coerced
        // into <input-variable>."
        var (outcome, values) = Run("#NULL#\r\n", [("V", VBVariantType.TypeInfo)],
            $"Open \"{Path}\" For Input As #1",
            "Input #1, V");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsInstanceOfType<VBRuntimeNullValue>(values["V"], $"read as {values["V"]}");
    }

    [TestMethod]
    public void Input_HashError_ReadsAsTheErrorNumber()
    {
        // "the error number value is Let-coerced into <input-variable>" - the number is what the variable ends
        // up holding, which is as far as the specification goes: it does not say the Variant keeps an Error
        // subtype, and the coercion it names does not give it one.
        var (outcome, values) = Run("#ERROR 91#\r\n", [("V", VBVariantType.TypeInfo)],
            $"Open \"{Path}\" For Input As #1",
            "Input #1, V");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(91, Convert.ToInt32(values["V"]));
    }

    [TestMethod]
    public void Input_AQuotedFieldIntoANonStringVariable_IsItsDefaultValue()
    {
        // "If the sequence of characters is surrounded by DQUOTEs and the declared type of <input-variable> is
        // not String or Variant, then <input-variable> is set to its default value" - notably not an error.
        var (outcome, value) = RunOne("\"hello\"\r\n", VBLongType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(0, value);
    }

    [TestMethod]
    public void Input_ADelimitedFieldIntoAStringVariable_ReadsItsCharacters()
    {
        // a String variable takes "all characters read from the file until a ',' is encountered", so the
        // delimiters are characters like any other rather than a spelling to interpret.
        var (outcome, value) = RunOne("#TRUE#\r\n", VBStringType.TypeInfo);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("#TRUE#", value);
    }

    [TestMethod]
    public void Input_PastTheEndOfTheFile_IsInputPastEndOfFile()
    {
        var (outcome, _) = RunOne("\"only\"\r\n", VBStringType.TypeInfo, "Input #1, V");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InputPastEndOfFile, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Input_OnAFileNumberThatIsNotOpen_IsBadFileNameOrNumber()
    {
        var (outcome, _) = Run("\"a\"\r\n", [("V", VBStringType.TypeInfo)], "Input #1, V");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileNameOrNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Input_OnAChannelOpenedForOutput_IsBadFileMode()
    {
        // MS-VBAL 5.4.5.1's table: Input # is valid in Input and Binary modes only.
        var (outcome, _) = Run(string.Empty, [("V", VBStringType.TypeInfo)],
            $"Open \"{Path}\" For Output As #1",
            "Input #1, V");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileMode, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void WriteThenInput_RoundTripsARecord()
    {
        // what the pair is for, and the only test here that proves both halves agree: the record is written by
        // Write # and read back by Input #, through the file rather than through a fixture string.
        var (outcome, values) = Run(
            string.Empty,
            [("A", VBStringType.TypeInfo), ("B", VBLongType.TypeInfo), ("C", VBBooleanType.TypeInfo)],
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Write #1, \"a,b\", 42, True",
            "Close #1",
            $"Open \"{Root}/out.txt\" For Input As #2",
            "Input #2, A, B, C");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("a,b", values["A"]);
        Assert.AreEqual(42, values["B"]);
        Assert.AreEqual(true, Convert.ToBoolean(values["C"]));
    }
}
