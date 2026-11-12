using System.Globalization;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The parameters of the new procedure as the user edits them: renamed, put in another order, or passed by reference where the plan passes them by value; and
/// the answers that are refused.
/// </summary>
[TestClass]
public sealed class ExtractionParametersTests
{
    // lines 4 and 5 read a and n, which they are given by value, and give b a value read after them, which they are given by reference; t is theirs alone, and
    // factor the module's.
    private const string Body = "Dim a As Long\nDim b As Long\nDim t As Long\na = n + 1\nt = a * factor\nb = t + n\nDebug.Print a; b";
    private const string Declarations = "Private factor As Long";

    // an Optional Variant the selection asks IsMissing of, which the new procedure declares last.
    private const string OptionalHeader = "Public Sub Run(ByVal n As Long, Optional ByVal o As Variant)";
    private const string OptionalBody = "Dim x As Long\nIf IsMissing(o) Then x = n Else x = o\nDebug.Print x";

    private static (System.Collections.Immutable.ImmutableArray<ExtractionParameterChoice> Parameters, ExtractionParameterProblem? Problem) Chosen(
        IReadOnlyList<ExtractionParameterAnswer> answers, string body = Body, int first = 4, int last = 5, string header = "Public Sub Run(ByVal n As Long)")
    {
        var (plan, source, context, module, _) = ExtractionFixture.Naming(body, first, last, Declarations, header);
        return ExtractionParameters.Choose(answers, plan, source, "Extracted", context, module);
    }

    private static ExtractionParameterProblemKind? KindOf(params ExtractionParameterAnswer[] answers) => Chosen(answers).Problem?.Kind;

    private static string[] Names(IEnumerable<ExtractionParameterChoice> parameters) => [.. parameters.Select(parameter => parameter.Name)];

    [TestMethod]
    public void NoAnswer_KeepsThePlansParameters_InTheOrderTheSelectionFirstTouchesThem()
    {
        var (parameters, problem) = Chosen([]);

        Assert.IsNull(problem, problem?.ToMessage());
        CollectionAssert.AreEqual(new[] { "a", "n", "b" }, Names(parameters));
        Assert.IsTrue(parameters.All(parameter => parameter.Names.IsEmpty));
    }

    // a and b are answered, n is not: a and b take each other's places, and n keeps its own.
    [TestMethod]
    public void TheAnsweredParameters_TakeTheirPlacesInTheUsersOrder_AndTheOthersStayWhereTheyAre()
    {
        var (parameters, problem) = Chosen([new("b"), new("a")]);

        Assert.IsNull(problem, problem?.ToMessage());
        CollectionAssert.AreEqual(new[] { "b", "n", "a" }, Names(parameters));
    }

    [TestMethod]
    public void ARenamedParameter_IsRenamedWhereverTheSelectionNamesIt()
    {
        var (parameters, problem) = Chosen([new("a", Name: "doubled")]);

        Assert.IsNull(problem, problem?.ToMessage());
        var renamed = parameters.Single(parameter => parameter.Name == "doubled");
        Assert.AreEqual(1, renamed.Names.Length);
        Assert.IsTrue(parameters.Where(parameter => parameter != renamed).All(parameter => parameter.Names.IsEmpty));
    }

    // the selection names a as n and n as a: each name means the other parameter once both are renamed.
    [TestMethod]
    public void TwoParameters_CanTakeEachOthersNames()
    {
        var (parameters, problem) = Chosen([new("a", Name: "n"), new("n", Name: "a")]);

        Assert.IsNull(problem, problem?.ToMessage());
        CollectionAssert.AreEqual(new[] { "n", "a", "b" }, Names(parameters));
    }

    [TestMethod]
    public void AParameterTheSelectionOnlyReads_CanBePassedByReference()
    {
        var (parameters, problem) = Chosen([new("a", Passing: "byref")]);

        Assert.IsNull(problem, problem?.ToMessage());
        Assert.AreEqual(ExtractionPassing.ByRef, parameters.Single(parameter => parameter.Name == "a").Passing);
    }

    [TestMethod]
    [DataRow("c", null, null, (int)ExtractionParameterProblemKind.NotAParameter, DisplayName = "no parameter")]
    [DataRow("a", "", null, (int)ExtractionParameterProblemKind.Empty, DisplayName = "an empty name")]
    [DataRow("a", "1st", null, (int)ExtractionParameterProblemKind.NotAnIdentifier, DisplayName = "a digit first")]
    [DataRow("a", "Loop", null, (int)ExtractionParameterProblemKind.Reserved, DisplayName = "a keyword")]
    [DataRow("a", "n", null, (int)ExtractionParameterProblemKind.Duplicate, DisplayName = "another parameter's name")]
    [DataRow("a", "t", null, (int)ExtractionParameterProblemKind.Collision, DisplayName = "a local's name")]
    [DataRow("a", "Extracted", null, (int)ExtractionParameterProblemKind.Collision, DisplayName = "the new procedure's name")]
    [DataRow("a", "factor", null, (int)ExtractionParameterProblemKind.Collision, DisplayName = "a name the selection means something else by")]
    [DataRow("a", null, "Copy", (int)ExtractionParameterProblemKind.NotAPassing, DisplayName = "no passing")]
    [DataRow("b", null, "ByVal", (int)ExtractionParameterProblemKind.PassingIsNotAChoice, DisplayName = "by value, written and read after")]
    public void AnAnswerTheNewProcedureCannotDeclare_IsRefused(string id, string? name, string? passing, int kind)
        => Assert.AreEqual((ExtractionParameterProblemKind)kind, KindOf(new ExtractionParameterAnswer(id, name, passing)));

    [TestMethod]
    public void AParameterAnsweredTwice_IsRefused()
        => Assert.AreEqual(ExtractionParameterProblemKind.NotAParameter, KindOf(new("a"), new("A", Name: "x")));

    [TestMethod]
    public void AParameterPassedByReference_IsPassedSoWhenTheAnswerSaysSo()
        => Assert.IsNull(KindOf(new ExtractionParameterAnswer("b", Passing: "ByRef")));

    // n and x are not Optional, o is: o comes last by default, and the user cannot put it before them.
    [TestMethod]
    public void AnOptionalParameter_ComesAfterTheOthers_AndCannotBePutBeforeThem()
    {
        var (parameters, problem) = Chosen([], OptionalBody, 1, 1, OptionalHeader);
        Assert.IsNull(problem, problem?.ToMessage());
        CollectionAssert.AreEqual(new[] { "n", "x", "o" }, Names(parameters));

        var (_, reordered) = Chosen([new("o"), new("n")], OptionalBody, 1, 1, OptionalHeader);
        Assert.AreEqual(ExtractionParameterProblemKind.OptionalBeforeRequired, reordered?.Kind);
    }

    [TestMethod]
    [DataRow("en")]
    [DataRow("fr")]
    public void EveryProblem_IsSaidInTheLanguageOfTheUserInterface(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        foreach (var kind in Enum.GetValues<ExtractionParameterProblemKind>())
        {
            var message = new ExtractionParameterProblem(kind, "a", "x").ToMessage(culture);
            Assert.AreNotEqual(kind.ToString(), message, $"{kind} has no message in {language}");
        }
    }
}
