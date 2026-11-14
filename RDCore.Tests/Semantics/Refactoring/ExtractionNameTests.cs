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

    // a standard module beside Main: its public members are the project's.
    private static readonly (string Name, string Source)[] Other =
        [("Other", "Attribute VB_Name = \"Other\"\r\nOption Explicit\r\n\r\nPublic StepExit As Long\r\n\r\nPublic Sub Tally()\r\nEnd Sub\r\n\r\nPublic Sub Show()\r\n    Debug.Print Format(1)\r\nEnd Sub\r\n")];

    private static ExtractionNameProblem[] Problems(
        string name, ExtractionAccessibility accessibility, string body = "Debug.Print n", int first = 0, int last = 0, bool classModule = false,
        IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        var (plan, source, context, module, _) = ExtractionFixture.Naming(body, first, last, classes: classes, classModule: classModule, modules: Other);
        return [.. ExtractionName.Validate(name, plan, source, context, module, accessibility)];
    }

    // a Public procedure of a standard module is the project's: a public member of another module named so is a collision, which a Private one only hides.
    [TestMethod]
    public void APublicName_InAStandardModule_CollidesWithAPublicMemberOfAnotherModule()
    {
        var problem = Problems("Tally", ExtractionAccessibility.Public).Single();

        Assert.AreEqual(ExtractionNameProblemKind.Collision, problem.Kind);
        Assert.AreEqual("Other.Tally", problem.Other);
        Assert.AreEqual(ExtractionNameProblemKind.Shadows, Problems("Tally", ExtractionAccessibility.Private).Single().Kind);
    }

    // Other writes the library's Format: once a Public Format is the project's, it is what Other's Format means.
    [TestMethod]
    public void APublicName_RebindsWhatAnotherModuleWritesForSomethingElse()
    {
        var problem = Problems("Format", ExtractionAccessibility.Public).Single();

        Assert.AreEqual(ExtractionNameProblemKind.Rebinds, problem.Kind);
        Assert.AreEqual("Other", problem.Module!.Fragment.TrimStart('#'));
        Assert.AreEqual(ExtractionNameProblemKind.Shadows, Problems("Format", ExtractionAccessibility.Private).Single().Kind);
    }

    // Other's Helper means its own Private procedure, nearer than a Public one of the project.
    [TestMethod]
    public void APublicName_ThatAnotherModuleWritesForItsOwnDeclaration_IsValid()
    {
        (string, string)[] own = [("Other", "Attribute VB_Name = \"Other\"\r\nOption Explicit\r\n\r\nPrivate Sub Helper()\r\nEnd Sub\r\n\r\nPublic Sub Show()\r\n    Helper\r\nEnd Sub\r\n")];
        var (plan, source, context, module, _) = ExtractionFixture.Naming("Debug.Print n", 0, 0, modules: own);

        Assert.IsEmpty(ExtractionName.Validate("Helper", plan, source, context, module, ExtractionAccessibility.Public));
    }

    // a Public member of a class module is a member of each of its objects, which no unqualified name of another module means.
    [TestMethod]
    public void APublicName_InAClassModule_IsCheckedAsAPrivateOne()
        => CollectionAssert.AreEqual(new[] { ExtractionNameProblemKind.Shadows },
            Problems("Format", ExtractionAccessibility.Public, classModule: true).Select(problem => problem.Kind).ToArray());

    // a Public Enum of a class module is the project's, which Other's StepExit is already; a Private one only hides it.
    [TestMethod]
    public void TheExitCodesOfAPublicMemberOfAClassModule_AreTheProjects()
    {
        ExtractionNameProblem[] ProblemsOf(ExtractionAccessibility accessibility) => Problems("Step", accessibility, Loop, 2, 4, classModule: true);

        var problem = ProblemsOf(ExtractionAccessibility.Public).Single();

        Assert.AreEqual(ExtractionNameProblemKind.Collision, problem.Kind);
        Assert.AreEqual("StepExit", problem.Name);
        Assert.AreEqual(ExtractionNameProblemKind.Shadows, ProblemsOf(ExtractionAccessibility.Private).Single().Kind);
    }

    // another class that implements the class would have a member of its interface it does not implement.
    [TestMethod]
    public void APublicMember_OfAClassAnotherImplements_IsRefused()
    {
        (string, string)[] implementer = [("Impl", "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Impl\"\r\nOption Explicit\r\nImplements Main\r\n")];

        var problem = Problems("PrintIt", ExtractionAccessibility.Public, classModule: true, classes: implementer).Single();

        Assert.AreEqual(ExtractionNameProblemKind.Implemented, problem.Kind);
        Assert.AreEqual("Impl", problem.Other);
        Assert.IsFalse(problem.IsWarning);
        Assert.IsEmpty(Problems("PrintIt", ExtractionAccessibility.Private, classModule: true, classes: implementer));
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
