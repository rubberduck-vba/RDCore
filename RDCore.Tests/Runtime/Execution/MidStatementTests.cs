using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Errors;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.4.3.5</strong> the <c>Mid</c>, <c>MidB</c>, <c>Mid$</c> and <c>MidB$</c> statements.
/// </summary>
/// <remarks>
/// A replacement never changes the length of the string: it is the least of the length asked for, what is left of the
/// target, and what the value has to give. The <c>B</c> forms count in bytes, two to a character.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.5 Mid Statement")]
public sealed class MidStatementTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    // the value of S after `S = <initial>` and the statement, with S a String.
    private static string Mid(string initial, string statement, VBType? type = null)
    {
        var (outcome, values) = Run(type ?? VBStringType.TypeInfo, initial, statement);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return (string)values["S"]!;
    }

    private static (RuntimeExecutionOutcome Outcome, Dictionary<string, object?> Values) Run(VBType type, string initial, string statement)
    {
        var symbol = Variable("S", type);
        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [symbol], $"S = \"{initial}\"", statement);
        return (outcome, new() { ["S"] = session.Symbols.Resolver.GetValue(symbol).Value.BoxedValue });
    }

    [TestMethod]
    public void ASpanOfTheTarget_IsReplaced()
        => Assert.AreEqual("aXYZef", Mid("abcdef", "Mid(S, 2, 3) = \"XYZ\""));

    [TestMethod]
    public void WithNoLength_TheReplacementRunsThroughTheValue()
        => Assert.AreEqual("ab12ef", Mid("abcdef", "Mid(S, 3) = \"12\""));

    [TestMethod]
    public void ALongerValue_IsCutToTheLength()
        => Assert.AreEqual("aXYdef", Mid("abcdef", "Mid(S, 2, 2) = \"XYZ\""));

    [TestMethod]
    public void AShorterValue_ReplacesOnlyWhatItHas()
        => Assert.AreEqual("aXcdef", Mid("abcdef", "Mid(S, 2, 3) = \"X\""));

    [TestMethod]
    public void TheReplacement_StopsAtTheEndOfTheTarget_AndNeverLengthensIt()
        => Assert.AreEqual("abcdWX", Mid("abcdef", "Mid(S, 5) = \"WXYZ\""));

    [TestMethod]
    public void AnEmptyValue_ChangesNothing()
        => Assert.AreEqual("abcdef", Mid("abcdef", "Mid(S, 2) = \"\""));

    [TestMethod]
    public void ALengthOfZero_ChangesNothing()
        => Assert.AreEqual("abcdef", Mid("abcdef", "Mid(S, 2, 0) = \"XYZ\""));

    [TestMethod]
    public void TheDollarForm_IsTheSameStatement()
        => Assert.AreEqual("aXYZef", Mid("abcdef", "Mid$(S, 2, 3) = \"XYZ\""));

    [TestMethod]
    public void TheKeyword_IsCaseInsensitive()
        => Assert.AreEqual("aXYZef", Mid("abcdef", "MID(S, 2, 3) = \"XYZ\""));

    [TestMethod]
    public void AValueThatIsNotAString_IsLetCoercedToOne()
        => Assert.AreEqual("19945", Mid("12345", "Mid(S, 2, 2) = 99"));

    [TestMethod]
    public void AVariantTarget_HoldingAString_IsReplacedInPlace()
        => Assert.AreEqual("aXcdef", Mid("abcdef", "Mid(S, 2, 1) = \"X\"", VBVariantType.TypeInfo));

    [TestMethod]
    public void AFixedLengthTarget_KeepsItsWidth()
        => Assert.AreEqual("aXYdef", Mid("abcdef", "Mid(S, 2, 2) = \"XYZ\"", new VBFixedStringType(6)));

    [TestMethod]
    public void MidB_CountsBytes_TwoToACharacter()
    {
        // "abcd" is 61 00 62 00 63 00 64 00: the two bytes from byte 3 are the whole of "b", and "X" is two bytes.
        Assert.AreEqual("aXcd", Mid("abcd", "MidB(S, 3, 2) = \"X\""));
    }

    [TestMethod]
    public void MidB_StartingOnAnOddByte_SplitsACharacter()
    {
        // bytes 2-3 of 61 00 62 00 63 00 64 00 become 58 00: 61 58 | 00 00 | 63 00 | 64 00.
        Assert.AreEqual("塡\0cd", Mid("abcd", "MidB(S, 2, 2) = \"X\""));
    }

    [TestMethod]
    public void MidB_WithNoLength_StopsAtTheEndOfTheTargetInBytes()
        => Assert.AreEqual("abXY", Mid("abcd", "MidB$(S, 5) = \"XYZ\""));

    [TestMethod]
    [DataRow("Mid(S, 0, 1) = \"X\"", DisplayName = "a start of 0")]
    [DataRow("Mid(S, -1, 1) = \"X\"", DisplayName = "a negative start")]
    [DataRow("Mid(S, 7, 1) = \"X\"", DisplayName = "a start past the end of the string")]
    [DataRow("Mid(S, 2, -1) = \"X\"", DisplayName = "a negative length")]
    [DataRow("MidB(S, 13, 1) = \"X\"", DisplayName = "a byte start past the end of the string, in bytes")]
    public void ASpanThatIsNotInTheString_IsError5(string statement)
    {
        var (outcome, _) = Run(VBStringType.TypeInfo, "abcdef", statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void TheLastCharacter_IsInTheString()
        => Assert.AreEqual("abcdeX", Mid("abcdef", "Mid(S, 6, 1) = \"X\""));

    [TestMethod]
    public void MidB_AtTheLastCharactersFirstByte_ReplacesIt()
        => Assert.AreEqual("abcdeX", Mid("abcdef", "MidB(S, 11, 2) = \"X\""));
}
