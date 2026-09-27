using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.4.3.6-7</strong> the <c>LSet</c> and <c>RSet</c> statements.
/// </summary>
/// <remarks>
/// Both fit a value into the width the target already has — the width comes from the target's <em>current
/// value</em>, not from its declared type — and differ only in which end pads. <c>LSet</c> also copies one UDT
/// over another as bytes, which is the case with no <c>RSet</c> counterpart.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.6 LSet")]
[TestCategory("MS-VBAL 5.4.3.7 RSet")]
public sealed class FixedAssignmentTests
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

    private static (RuntimeExecutionOutcome Outcome, Dictionary<string, object?> Values, IRuntimeSessionValues Session) Run(
        (string Name, VBType Type)[] variables, params string[] body)
    {
        var symbols = variables.Select(variable => Variable(variable.Name, variable.Type)).ToArray();

        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem: null, symbols, body);

        return (outcome, symbols.ToDictionary(
                symbol => symbol.Name,
                symbol => session.Symbols.Resolver.GetValue(symbol).Value.BoxedValue),
            new IRuntimeSessionValues(session, symbols));
    }

    /// <summary>Reaches a UDT variable's own value, fields intact, after a run.</summary>
    private sealed class IRuntimeSessionValues(SDK.Runtime.Abstract.Execution.IRuntimeSession session, VBModuleFieldVariableMemberSymbol[] symbols)
    {
        public VBUserDefinedTypeValue Record(string name)
        {
            var symbol = symbols.Single(candidate => candidate.Name == name);
            return (VBUserDefinedTypeValue)symbol.ResolvedType!.CreateValue(session.Symbols.Resolver.GetValue(symbol));
        }
    }

    private static int FieldOf(VBUserDefinedTypeValue record, string name)
        => Convert.ToInt32(record[name]!.Handle.Value.BoxedValue);

    [TestMethod]
    public void LSet_AShorterValue_IsPaddedOnTheRight()
    {
        // "the concatenation of e followed by (qLength - eLength) space characters (U+0020)".
        var (outcome, values, _) = Run([("S", new VBFixedStringType(8))], "LSet S = \"abc\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("abc     ", values["S"]);
    }

    [TestMethod]
    public void RSet_AShorterValue_IsPaddedOnTheLeft()
    {
        // "(qLength - eLength) spaces followed by the data value of <expression>".
        var (outcome, values, _) = Run([("S", new VBFixedStringType(8))], "RSet S = \"abc\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("     abc", values["S"]);
    }

    [TestMethod]
    public void LSet_ALongerValue_IsTruncatedToTheTargetsWidth()
    {
        // "the initial qLength characters of e".
        var (outcome, values, _) = Run([("S", new VBFixedStringType(4))], "LSet S = \"abcdefgh\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("abcd", values["S"]);
    }

    [TestMethod]
    public void RSet_ALongerValue_IsTruncatedFromTheSameEndAsLSet()
    {
        // "the first qLength characters in <expression>" - RSet pads on the left but still truncates on the
        // right, which is easy to assume the other way round.
        var (outcome, values, _) = Run([("S", new VBFixedStringType(4))], "RSet S = \"abcdefgh\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("abcd", values["S"]);
    }

    [TestMethod]
    public void LSet_AnExactFit_IsUnchanged()
    {
        var (outcome, values, _) = Run([("S", new VBFixedStringType(3))], "LSet S = \"abc\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("abc", values["S"]);
    }

    [TestMethod]
    public void LSet_TheWidthComesFromTheTargetsCurrentValue_NotItsDeclaredType()
    {
        // a variable-length String has no declared width at all, so the only width there can be is the one it
        // is holding - which is what makes the statement meaningful on one.
        var (outcome, values, _) = Run([("S", VBStringType.TypeInfo)], "S = \"12345\"", "LSet S = \"ab\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("ab   ", values["S"]);
    }

    [TestMethod]
    public void LSet_IntoAnEmptyString_AssignsNothing()
    {
        // qLength is 0, so every value truncates to nothing. Consistent rather than special-cased.
        var (outcome, values, _) = Run([("S", VBStringType.TypeInfo)], "S = \"\"", "LSet S = \"abc\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(string.Empty, values["S"]);
    }

    [TestMethod]
    public void LSet_ANonStringExpression_IsLetCoercedToStringFirst()
    {
        // "Let e be the data value of <expression> Let-coerced to declared type String."
        var (outcome, values, _) = Run([("S", new VBFixedStringType(6))], "LSet S = 42");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("42    ", values["S"]);
    }

    [TestMethod]
    public void LSet_ARecordOverAnotherOfTheSameShape_CopiesEveryField()
    {
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));
        var (outcome, _, session) = Run([("A", type), ("B", type)],
            "A.X = 3", "A.Y = 4", "LSet B = A");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var target = session.Record("B");
        Assert.AreEqual(3, FieldOf(target, "X"));
        Assert.AreEqual(4, FieldOf(target, "Y"));
    }

    [TestMethod]
    public void LSet_ARecordOverADifferentlyShapedOne_ReinterpretsTheBytes()
    {
        // what the statement is for: VBA has no union, so LSet between two records is how one is read as the
        // other. Two Integers occupy the four bytes one Long does, so X = 0x00040003 reads back as 3 and 4.
        var source = Udt("TLong", ("N", VBLongType.TypeInfo));
        var target = Udt("TPair", ("Lo", VBIntegerType.TypeInfo), ("Hi", VBIntegerType.TypeInfo));

        var (outcome, _, session) = Run([("A", source), ("B", target)],
            "A.N = 262147", "LSet B = A");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var record = session.Record("B");
        Assert.AreEqual(3, FieldOf(record, "Lo"), "the low word of 0x00040003");
        Assert.AreEqual(4, FieldOf(record, "Hi"), "the high word");
    }

    [TestMethod]
    public void LSet_IntoAWiderRecord_LeavesWhatTheSourceDoesNotReachAtItsDefault()
    {
        // "only the bytes both types have": a destination field past the end of the source's image was never
        // copied, so it says so rather than holding half a value.
        var source = Udt("TOne", ("A", VBLongType.TypeInfo));
        var target = Udt("TTwo", ("A", VBLongType.TypeInfo), ("B", VBLongType.TypeInfo));

        var (outcome, _, session) = Run([("S", source), ("D", target)],
            "S.A = 7", "D.B = 99", "LSet D = S");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var record = session.Record("D");
        Assert.AreEqual(7, FieldOf(record, "A"));
        Assert.AreEqual(0, FieldOf(record, "B"), "nothing was copied here, so nothing is claimed here");
    }

    [TestMethod]
    public void LSet_IntoANarrowerRecord_CopiesOnlyWhatFits()
    {
        var source = Udt("TTwo", ("A", VBLongType.TypeInfo), ("B", VBLongType.TypeInfo));
        var target = Udt("TOne", ("A", VBLongType.TypeInfo));

        var (outcome, _, session) = Run([("S", source), ("D", target)],
            "S.A = 7", "S.B = 8", "LSet D = S");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(7, FieldOf(session.Record("D"), "A"));
    }

    [TestMethod]
    public void LSet_ARecordWithAFixedLengthString_CopiesItsCharacters()
    {
        // a fixed-length String field is its characters inline, so it has a byte image like any other field.
        var type = Udt("TNamed", ("Code", new VBFixedStringType(4)));
        var (outcome, _, session) = Run([("A", type), ("B", type)],
            "A.Code = \"abcd\"", "LSet B = A");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("abcd", session.Record("B")["Code"]!.Handle.Value.BoxedValue);
    }

    [TestMethod]
    public void LSet_ARecordWithAVariableLengthString_CopiesTheValue_NotAPointer()
    {
        // the case MS-VBA corrupts a process over: the member is a pointer in memory, and copying its bytes
        // would leave two records owning one allocation. RDCore copies the value across instead, which is the
        // only reading of "copied" that is both meaningful and safe.
        var type = Udt("TNamed", ("Id", VBLongType.TypeInfo), ("Name", VBStringType.TypeInfo));
        var (outcome, _, session) = Run([("A", type), ("B", type)],
            "A.Id = 1", "A.Name = \"Ducky\"", "LSet B = A");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var record = session.Record("B");
        Assert.AreEqual(1, FieldOf(record, "Id"));
        Assert.AreEqual("Ducky", record["Name"]!.Handle.Value.BoxedValue);
    }

    [TestMethod]
    public void LSet_ARecordOntoOneWhoseStringSitsElsewhere_LeavesItAtItsDefault()
    {
        // a reference is carried only when the destination has a field of the same type at the same offset.
        // Reinterpreting one kind of reference as another is the one thing a byte copy must not do.
        var source = Udt("TA", ("Name", VBStringType.TypeInfo), ("Id", VBLongType.TypeInfo));
        var target = Udt("TB", ("Id", VBLongType.TypeInfo), ("Name", VBStringType.TypeInfo));

        var (outcome, _, session) = Run([("S", source), ("D", target)],
            "S.Name = \"Ducky\"", "S.Id = 5", "LSet D = S");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(((VBStringValue)session.Record("D")["Name"]!).IsNullString,
            "an uncopied String field is the null string a String variable starts as, not the source's value");
    }

    [TestMethod]
    public void RSet_ARecord_IsATypeMismatch()
    {
        // §5.4.3.7 admits only String and Variant: RSet has no UDT form at all.
        var type = Udt("TPoint", ("X", VBLongType.TypeInfo));
        var (outcome, _, _) = Run([("A", type), ("B", type)], "RSet B = A");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void LSet_ATargetHoldingNeitherStringNorRecord_IsATypeMismatch()
    {
        // "The value type of <bound-variable-expression> MUST be String or a UDT." The static rule lets a
        // Variant through on its declared type; what it holds is the run-time rule's business.
        var (outcome, _, _) = Run([("N", VBLongType.TypeInfo)], "LSet N = \"abc\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void LSet_AVariantHoldingAString_FitsIt()
    {
        var (outcome, values, _) = Run([("V", VBVariantType.TypeInfo)], "V = \"12345\"", "LSet V = \"ab\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("ab   ", values["V"]);
    }
}
