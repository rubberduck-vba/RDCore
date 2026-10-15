using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Cli;

/// <summary>
/// The Option directives of a class module (MS-VBAL 5.2.1) are what its code is checked and run under, as a standard module's are. The name of a class binds
/// only in the type binding context (MS-VBAL 5.6.4), and that is where its module symbol is found: the one that carries them.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.1 Option Directives")]
public sealed class ClassModuleDirectiveTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    [TestMethod]
    public async Task AClassModule_ThatStatesOptionExplicit_HasTheFactOfIt()
    {
        // what RDC00101 reads: a class that states it is not told that it does not.
        var widget = ("Widget", ModuleWorkspace.ClassModule("Widget", "Option Explicit", "Public Sub Work()", "End Sub"));

        var model = await ModuleWorkspace.ModelAsync([widget], Program("Public Sub Main()", "End Sub"), "Widget");

        Assert.AreEqual(true, model.OptionExplicit);
    }

    [TestMethod]
    public async Task AnUndeclaredName_InAClassModuleThatStatesOptionExplicit_IsAVariableNotDefinedError()
    {
        var widget = ("Widget", ModuleWorkspace.ClassModule("Widget", "Option Explicit", "Public Sub Work()", "undeclared = 1", "End Sub"));

        var errors = await ModuleWorkspace.LoadErrorsAsync([widget], Program("Public Sub Main()", "End Sub"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "Variable not defined");
        StringAssert.Contains(errors[0], "undeclared");
    }

    [TestMethod]
    [DataRow("Option Compare Text", "", "True", DisplayName = "The class's own Option Compare Text")]
    [DataRow("", "Option Compare Text", "False", DisplayName = "Not the Option Compare of the module that calls it")]
    public async Task TheCodeOfAClassModule_ComparesStrings_InItsOwnModulesMode(string classDirective, string programDirective, string expected)
    {
        var widget = ("Widget", ModuleWorkspace.ClassModule("Widget", classDirective, "Public Function Same() As Boolean", "Same = (\"a\" = \"A\")", "End Function"));

        var output = await ModuleWorkspace.RunAsync([widget], Program(programDirective, "Private w As New Widget", "Public Sub Main()", "Debug.Print w.Same", "End Sub"));

        CollectionAssert.AreEqual(new[] { expected }, output);
    }

    [TestMethod]
    public async Task AComparisonOfStringsInAClassModule_IsStated_InItsOwnModulesMode()
    {
        // what the sweep states of a comparison (ComparisonOperatorFact.Comparison), which a diagnostics extension reads, is the mode it compares in.
        var widget = ("Widget", ModuleWorkspace.ClassModule("Widget", "Option Compare Text", "Public Function Same(ByVal s As String) As Boolean", "Same = (s = \"A\")", "End Function"));

        var model = await ModuleWorkspace.ModelAsync([widget], Program("Public Sub Main()", "End Sub"), "Widget");

        var facts = model.Procedures.Single().Runtime;
        Assert.IsNotNull(facts, "the module was loaded, so its code was evaluated");
        var comparison = facts.Operations.OfType<ComparisonOperatorFact>().Single();
        Assert.AreEqual(OptionCompare.Text, comparison.Comparison.Mode);
        Assert.IsTrue(comparison.Flags.HasFlag(ComparisonOperatorSemanticFlags.StringComparisonText), comparison.Flags.ToString());
    }

    [TestMethod]
    [DataRow("Private a() As Long", "ReDim a(3)", "LBound(a)", DisplayName = "ReDim a(3)")]
    [DataRow("Private v As Variant", "v = Array(1, 2)", "LBound(v)", DisplayName = "Array(1, 2)")]
    [DataRow("Private m(3) As Long", "", "LBound(m)", DisplayName = "A field m(3)")]
    public async Task AnArrayOfAClassModule_ThatOmitsItsLowerBound_StartsAtTheClasssOptionBase(string field, string statement, string lowerBound)
    {
        var widget = ("Widget", ModuleWorkspace.ClassModule("Widget", "Option Base 1", field, "Public Function Lower() As Long", statement, $"Lower = {lowerBound}", "End Function"));

        var output = await ModuleWorkspace.RunAsync([widget], Program("Private w As New Widget", "Public Sub Main()", "Debug.Print w.Lower", "End Sub"));

        CollectionAssert.AreEqual(new[] { "1" }, output);
    }
}
