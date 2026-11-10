using System.Globalization;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The name of the new procedure: suggested from what the code does, and validated as the user writes it.
/// </summary>
[TestClass]
public sealed class ExtractionNameTests
{
    private const string Loop = "Dim i As Long\nDo While i < 10\nIf i = n Then Exit Do\nIf i > n Then Exit Sub\ni = i + 1\nLoop";

    private static ExtractionNameProblem[] Problems(string name, string body = "Debug.Print n", int first = 0, int last = 0, string declarations = "")
    {
        var (plan, source, context, module, _) = ExtractionFixture.Naming(body, first, last, declarations);
        return [.. ExtractionName.Validate(name, plan, source, context, module)];
    }

    private static ExtractionNameProblemKind[] KindsOf(string name, string body = "Debug.Print n", int first = 0, int last = 0, string declarations = "")
        => [.. Problems(name, body, first, last, declarations).Select(problem => problem.Kind)];

    [TestMethod]
    public void AFreeName_IsValid()
        => Assert.IsEmpty(Problems("PrintIt"));

    [TestMethod]
    [DataRow("", (int)ExtractionNameProblemKind.Empty, DisplayName = "empty")]
    [DataRow("1st", (int)ExtractionNameProblemKind.NotAnIdentifier, DisplayName = "a digit first")]
    [DataRow("Print It", (int)ExtractionNameProblemKind.NotAnIdentifier, DisplayName = "a space")]
    [DataRow("Loop", (int)ExtractionNameProblemKind.Reserved, DisplayName = "a keyword")]
    public void ANameCodeCannotDeclare_IsRefused(string name, int kind)
        => CollectionAssert.AreEqual(new[] { (ExtractionNameProblemKind)kind }, KindsOf(name));

    [TestMethod]
    public void TwoHundredFiftyFiveCharacters_IsTheLongestAName()
    {
        Assert.IsEmpty(Problems(new string('a', 255)));
        CollectionAssert.AreEqual(new[] { ExtractionNameProblemKind.TooLong }, KindsOf(new string('a', 256)));
    }

    [TestMethod]
    [DataRow("Bump", DisplayName = "a procedure of the module")]
    [DataRow("field", DisplayName = "a variable of the module")]
    [DataRow("RUN", DisplayName = "the source procedure, in any case")]
    [DataRow("n", DisplayName = "a parameter of the source procedure")]
    [DataRow("t", DisplayName = "a local of the source procedure")]
    public void ANameTheModuleOrTheSourceDeclares_IsACollision(string name)
    {
        var problem = Problems(name, "Dim t As Long\nt = n\nDebug.Print t", 1, 1).Single();

        Assert.AreEqual(ExtractionNameProblemKind.Collision, problem.Kind);
        Assert.IsFalse(problem.IsWarning);
    }

    // the exit codes are module names of their own: StepExit is the enumeration's.
    [TestMethod]
    public void ANameWhoseExitCodesTheModuleDeclares_IsACollision()
    {
        var problem = Problems("Step2", Loop, 2, 4, declarations: "Private Step2Exit As Long").Single();

        Assert.AreEqual(ExtractionNameProblemKind.Collision, problem.Kind);
        Assert.AreEqual("Step2Exit", problem.Name);
        Assert.IsEmpty(Problems("Step2", Loop, 2, 4), "only when the module declares it");
    }

    // a library's Format, written in the module: once the new procedure is declared there, it is what Format means.
    [TestMethod]
    public void ANameTheModuleWritesForSomethingElse_Rebinds()
    {
        var problem = Problems("Format", "Debug.Print Format(n)\nDebug.Print n", 1, 1).Single();

        Assert.AreEqual(ExtractionNameProblemKind.Rebinds, problem.Kind);
        Assert.IsFalse(problem.IsWarning);
    }

    [TestMethod]
    public void ANameOfALibraryTheModuleDoesNotWrite_Shadows_AWarning()
    {
        var problem = Problems("Format").Single();

        Assert.AreEqual(ExtractionNameProblemKind.Shadows, problem.Kind);
        Assert.IsTrue(problem.IsWarning);
    }

    [TestMethod]
    public void AProblem_SaysWhatItIs_InEnglishAndInFrench()
    {
        var problem = Problems("Bump").Single();

        StringAssert.StartsWith(problem.ToMessage(culture: CultureInfo.GetCultureInfo("en-CA")), "'Bump' already names Main.Bump at Main ");
        StringAssert.StartsWith(problem.ToMessage(culture: CultureInfo.GetCultureInfo("fr-CA")), "« Bump » nomme déjà Main.Bump à Main ");
        Assert.AreEqual("A name is at most 255 characters long.", Problems(new string('a', 256)).Single().ToMessage(culture: CultureInfo.GetCultureInfo("en-CA")));
    }

    [TestMethod]
    public void TheSuggestion_IsGetAndTheVariableGivenBack()
    {
        var (plan, source, context, module, analyzed) = ExtractionFixture.Naming("Dim total As Long\ntotal = n * 2\nDebug.Print total", 1, 1);

        Assert.AreEqual("GetTotal", ExtractionName.Suggest(plan, source, context, module));
        Assert.AreEqual("GetTotal", ExtractionName.Suggest(plan, source, context, module, analyzed.Variable("total")));
    }

    [TestMethod]
    public void TheSuggestion_IsTheVerbOfTheFirstCall_AndWhatItIsCalledWith()
    {
        var (plan, source, context, module, _) = ExtractionFixture.Naming("Dim x As Long\nBump x\nBump x\nDebug.Print n", 3, 3);
        var (called, calledSource, calledContext, calledModule, _) = ExtractionFixture.Naming("Dim x As Long\nx = n\nBump x\nDebug.Print n", 2, 3);

        Assert.AreEqual("ExtractedMethod", ExtractionName.Suggest(plan, source, context, module), "no call");
        Assert.AreEqual("BumpX", ExtractionName.Suggest(called, calledSource, calledContext, calledModule));
    }

    [TestMethod]
    public void ATakenSuggestion_IsNumbered()
    {
        var (plan, source, context, module, _) = ExtractionFixture.Naming("Debug.Print n", 0, 0, declarations: "Private ExtractedMethod As Long");

        Assert.AreEqual("ExtractedMethod2", ExtractionName.Suggest(plan, source, context, module));
    }
}
