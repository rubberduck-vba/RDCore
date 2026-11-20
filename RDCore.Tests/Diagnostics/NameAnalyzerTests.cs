using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;
using System.Text;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The members that exist on another's account (<c>RDC00405</c>) and the names a module chooses (<c>RDC01001</c>, <c>RDC01002</c>).
/// </summary>
[TestClass]
public sealed class NameAnalyzerTests
{
    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source)
    {
        var uri = TestUri.TestModuleUri();
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, null)).OrderBy(finding => finding.Range.Start)];
    }

    private static string[] Names(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Message.Split('\'')[1])];

    private static IOptions<DiagnosticsOptions> Options(string[]? meaningful = null, string[]? hungarian = null)
        => Microsoft.Extensions.Options.Options.Create(new DiagnosticsOptions
        {
            MeaningfulNames = new NameRuleOptions { AllowedNames = meaningful ?? [] },
            HungarianNotation = new NameRuleOptions { AllowedNames = hungarian ?? [] },
        });

    #region RDC00405 implementations should be private

    private static readonly (string, string) Shape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Sub Draw()", "End Sub", "Public Function Area() As Long", "End Function"));
    private static readonly (string, string) Button = ("Button", ModuleWorkspace.ClassModule("Button", "Public Event Click()"));

    private static async Task<AnalyzerFinding[]> ImplementationsAsync(params string[] lines)
    {
        var source = ModuleWorkspace.ClassModule("Implementer", ["Implements IShape", "Private WithEvents Source As Button", .. lines]);
        var payload = await ModuleWorkspace.SemanticsAsync([Shape, Button, ("Implementer", source)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Implementer");

        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Implementer.cls"));
        return [.. new ImplementationsShouldBePrivateAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model))];
    }

    [TestMethod]
    public async Task APublicImplementationOfAnInterfaceMember_AndAPublicEventHandler_ShouldBePrivate()
    {
        var findings = await ImplementationsAsync(
            "Public Sub IShape_Draw()", "End Sub",
            "Public Function IShape_Area() As Long", "End Function",
            "Public Sub Source_Click()", "End Sub");

        CollectionAssert.AreEqual(new[] { "IShape_Draw", "IShape_Area", "Source_Click" }, Names(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.ImplementationsShouldBePrivate, Severity: DiagnosticSeverity.Information }));
        Assert.AreNotEqual(findings[0].Message.Replace("IShape_Draw", string.Empty), findings[2].Message.Replace("Source_Click", string.Empty), "an implementation is not a handler");
    }

    [TestMethod]
    public async Task AMemberThatStatesNoAccess_IsPublic_AndIsReportedToo()
        => CollectionAssert.AreEqual(new[] { "IShape_Draw" }, Names(await ImplementationsAsync("Sub IShape_Draw()", "End Sub")));

    [TestMethod]
    public async Task APrivateImplementation_AndAPublicMemberThatOnlyLooksLikeOne_AreFine()
        => Assert.IsEmpty(await ImplementationsAsync(
            "Private Sub IShape_Draw()", "End Sub",
            "Private Function IShape_Area() As Long", "End Function",
            "Private Sub Source_Click()", "End Sub",
            "Public Sub IShape_Helper()", "End Sub",
            "Public Sub Source_Other()", "End Sub"));

    // the RDC00405 fix of the one finding in the Implementer class: the class with the fix made, or null when none is offered. Main is the program's entry
    // point, with the given lines.
    private static async Task<string?> MadePrivateAsync(string[] implementer, string[]? main = null, ProjectBuild? build = null)
    {
        var source = ModuleWorkspace.ClassModule("Implementer", ["Implements IShape", "Private WithEvents Source As Button", .. implementer]);
        var program = ModuleWorkspace.StandardModule("Program", ["Public Sub Main()", .. main ?? [], "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync([Shape, Button, ("Implementer", source)], program, build: build);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Implementer");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Implementer.cls"));

        var context = new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model);
        var finding = Assert.ContainsSingle(new ImplementationsShouldBePrivateAnalyzer().Analyze(context));
        if (finding.Fixes?.Edits.SingleOrDefault() is not { } fix)
        {
            return null;
        }

        StringAssert.Contains(fix.Title, "Private");
        return fix.Edits.ApplyTo(source);
    }

    [TestMethod]
    [DataRow("Public Sub IShape_Draw()", "Private Sub IShape_Draw()", DisplayName = "Public")]
    [DataRow("Sub IShape_Draw()", "Private Sub IShape_Draw()", DisplayName = "no access stated")]
    [DataRow("Public Static Sub IShape_Draw()", "Private Static Sub IShape_Draw()", DisplayName = "Public Static")]
    [DataRow("Static Sub IShape_Draw()", "Private Static Sub IShape_Draw()", DisplayName = "Static, no access stated")]
    [DataRow("Public Sub Source_Click()", "Private Sub Source_Click()", DisplayName = "an event handler")]
    public async Task TheFix_MakesTheMemberPrivate(string declaration, string fixedDeclaration)
    {
        var made = await MadePrivateAsync([declaration, "End Sub"]);

        Assert.IsNotNull(made, "the fix is offered");
        StringAssert.Contains(made, $"\r\n{fixedDeclaration}\r\n");
    }

    [TestMethod]
    public async Task TheFix_MakesEveryAccessorOfAPropertyPrivate()
    {
        var sized = ModuleWorkspace.ClassModule("ISized",
            "Public Property Get Size() As Long", "End Property", "Public Property Let Size(ByVal value As Long)", "End Property");
        var source = ModuleWorkspace.ClassModule("Implementer", "Implements ISized",
            "Public Property Get ISized_Size() As Long", "End Property", "Property Let ISized_Size(ByVal value As Long)", "End Property");
        var program = ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([("ISized", sized), ("Implementer", source)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Implementer");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Implementer.cls"));

        var context = new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model);
        var finding = Assert.ContainsSingle(new ImplementationsShouldBePrivateAnalyzer().Analyze(context));

        var made = finding.Fixes!.Edits.Single().Edits.ApplyTo(source);
        StringAssert.Contains(made, "\r\nPrivate Property Get ISized_Size() As Long\r\n");
        StringAssert.Contains(made, "\r\nPrivate Property Let ISized_Size(ByVal value As Long)\r\n");
    }

    // a call through the interface binds to the interface's member, RaiseEvent calls the handler, and a call of the class's own code stays in the module:
    // none stops the fix, and with both members made Private the program compiles and prints what it printed.
    [TestMethod]
    public async Task AMemberCalledThroughTheInterface_ByRaiseEvent_OrByItsOwnModule_IsMadePrivate_AndRunsAsBefore()
    {
        (string, string) drawable = ("IDrawable", ModuleWorkspace.ClassModule("IDrawable", "Public Sub Draw()", "End Sub"));
        (string, string) pad = ("Pad", ModuleWorkspace.ClassModule("Pad", "Public Event Tapped()", "Public Sub Tap()", "RaiseEvent Tapped", "End Sub"));
        var canvas = ModuleWorkspace.ClassModule("Canvas",
            "Implements IDrawable",
            "Private WithEvents mPad As Pad",
            "Public Sub IDrawable_Draw()", "Debug.Print \"drawn\"", "End Sub",
            "Public Sub mPad_Tapped()", "Debug.Print \"tapped\"", "IDrawable_Draw", "End Sub",
            "Private Sub Class_Initialize()", "Set mPad = New Pad", "End Sub",
            "Public Sub Tap()", "mPad.Tap", "End Sub");
        var program = ModuleWorkspace.StandardModule("Program",
            "Public Sub Main()", "Dim c As New Canvas", "Dim d As IDrawable", "Set d = c", "d.Draw", "c.Tap", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([drawable, pad, ("Canvas", canvas)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Canvas");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Canvas.cls"));

        var context = new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, canvas), model);
        var findings = new ImplementationsShouldBePrivateAnalyzer().Analyze(context).ToArray();

        CollectionAssert.AreEqual(new[] { "IDrawable_Draw", "mPad_Tapped" }, Names(findings));
        var made = findings.SelectMany(finding => finding.Fixes?.Edits.Single().Edits ?? []).ApplyTo(canvas);
        StringAssert.Contains(made, "\r\nPrivate Sub IDrawable_Draw()\r\n");
        StringAssert.Contains(made, "\r\nPrivate Sub mPad_Tapped()\r\n");
        Assert.IsEmpty(await ModuleWorkspace.LoadErrorsAsync([drawable, pad, ("Canvas", made)], program));
        var before = await ModuleWorkspace.RunAsync([drawable, pad, ("Canvas", canvas)], program);
        CollectionAssert.AreEqual(new[] { "drawn", "tapped", "drawn" }, before.Where(line => line.Length > 0).ToArray());
        CollectionAssert.AreEqual(before, await ModuleWorkspace.RunAsync([drawable, pad, ("Canvas", made)], program));
    }

    [TestMethod]
    [DataRow("Dim i As New Implementer", "i.IShape_Draw", DisplayName = "early-bound, by its own name")]
    [DataRow("Dim o As Object", "Set o = New Implementer: o.IShape_Draw", DisplayName = "late-bound")]
    [DataRow("Dim o As Object", "Set o = New Implementer: CallByName o, \"IShape\" & \"_Draw\", VbMethod", DisplayName = "CallByName")]
    public async Task AMemberAnotherModuleMayCall_IsNotMadePrivate(string declaration, string call)
        => Assert.IsNull(await MadePrivateAsync(["Public Sub IShape_Draw()", "End Sub"], [declaration, call]));

    // a referencing project can call a library's Public member by its own name: the host does not count its references.
    [TestMethod]
    public async Task AMemberOfALibrary_IsNotMadePrivate()
        => Assert.IsNull(await MadePrivateAsync(["Public Sub IShape_Draw()", "End Sub"], build: new ProjectBuild { OutputType = ProjectOutputType.Library }));

    [TestMethod]
    public void WithNoFactsFromTheHost_TheAnalyzerWillNotGuessFromAnUnderscore()
        => Assert.IsEmpty(Run(new ImplementationsShouldBePrivateAnalyzer(), "Implements IShape\nPublic Sub IShape_Draw()\nEnd Sub"));

    #endregion

    #region RDC01001 meaningful names

    private const string Declarations = """
        Private x As Long
        Private total1 As Long
        Private tmp As Long
        Private Oui As Boolean
        Private été As Long
        Private Total As Long
        Public Const PI2 = 3
        Public Sub Process(ByVal a As Long, ByVal count As Long)
            Dim i As Long
            Dim result As Long
        End Sub
        Public Sub Handler_Click()
        End Sub
        """;

    [TestMethod]
    public void ANameThatIsTooShort_EndsWithADigit_OrHasNoVowelOrNoConsonant_DoesNotSayWhatItIs()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Declarations);

        CollectionAssert.AreEqual(new[] { "x", "total1", "tmp", "Oui", "PI2", "a", "i" }, Names(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.UseMeaningfulIdentifierNames, Severity: DiagnosticSeverity.Information }));
    }

    [TestMethod]
    public void EachRuleHasAMessageOfItsOwn()
    {
        var messages = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Declarations)
            .ToDictionary(finding => finding.Message.Split('\'')[1], finding => finding.Message.Replace($"'{finding.Message.Split('\'')[1]}'", "''"));

        Assert.AreNotEqual(messages["x"], messages["total1"]);
        Assert.AreNotEqual(messages["total1"], messages["tmp"]);
        Assert.AreNotEqual(messages["x"], messages["tmp"]);
        Assert.AreNotEqual(messages["tmp"], messages["Oui"]);
        Assert.AreEqual(messages["x"], messages["a"], "the same rule, the same message");
    }

    [TestMethod]
    public void AccentedVowels_AreVowels()
        => Assert.IsFalse(Names(Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Declarations)).Contains("été"));

    [TestMethod]
    public void AnApprovedName_IsNotReported_WhateverItsCase()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options(meaningful: ["I", "X", "tmp"])), Declarations);

        CollectionAssert.AreEqual(new[] { "total1", "Oui", "PI2", "a" }, Names(findings));
    }

    // a live setting: the client says the names while the extension runs, and the analyzer that is built already reads them when it analyzes.
    [TestMethod]
    public void TheApprovedNames_AreTheOnesTheSettingsSayWhenAModuleIsAnalyzed()
    {
        var options = Options();
        var meaningful = new UseMeaningfulIdentifierNamesAnalyzer(options);
        var hungarian = new HungarianNotationAnalyzer(options);
        var before = Names(Run(meaningful, Declarations));

        options.Value.MeaningfulNames.AllowedNames = ["I", "X", "tmp"];
        options.Value.HungarianNotation.AllowedNames = [.. Names(Run(hungarian, Declarations))];

        CollectionAssert.Contains(before, "tmp");
        CollectionAssert.AreEqual(new[] { "total1", "Oui", "PI2", "a" }, Names(Run(meaningful, Declarations)));
        Assert.IsEmpty(Run(hungarian, Declarations));
    }

    private static readonly (string, string) Mover = ("Mover", ModuleWorkspace.ClassModule("Mover", "Public Event Moved(ByVal X As Single, ByVal Y As Single)"));

    // the analyzer, given a class that implements an interface and handles events, as the host models it.
    private static async Task<AnalyzerFinding[]> ChosenAsync(IModuleAnalyzer analyzer, params string[] lines)
    {
        var source = ModuleWorkspace.ClassModule("Implementer", ["Implements IShape", "Private WithEvents Source As Button", "Private WithEvents Pad As Mover", .. lines]);
        var payload = await ModuleWorkspace.SemanticsAsync([Shape, Button, Mover, ("Implementer", source)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Implementer");

        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Implementer.cls"));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)).OrderBy(finding => finding.Range.Start)];
    }

    private static readonly string[] Chosen =
    [
        "Private Sub IShape_Draw()", "End Sub",
        "Private Function IShape_Area() As Long", "End Function",
        "Private Sub Source_Click()", "End Sub",
        "Private Sub Pad_Moved(ByVal X As Single, ByVal Y As Single)", "End Sub",
        "Private Sub Class_Initialize()", "End Sub",
        "Public Sub Test_1()", "End Sub",
        "Public Function Run_2() As Long", "End Function",
    ];

    // Rubberduck's meaningful name inspection skips what an event names (the parameters of a handler), and not a name for its underscore.
    [TestMethod]
    public async Task TheNamesOfAnImplementationOrAHandler_AreTheInterfaceOrTheEventToChoose_AndAnUnderscoreSaysNothing()
        => CollectionAssert.AreEqual(new[] { "Test_1", "Run_2" }, Names(await ChosenAsync(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Chosen)));

    [TestMethod]
    public async Task TheNamesOfAnImplementationOrAHandler_AreNotHungarianEither()
        => CollectionAssert.AreEqual(
            new[] { "strTest_1" },
            Names(await ChosenAsync(new HungarianNotationAnalyzer(Options()), [.. Chosen, "Private Sub strTest_1()", "End Sub"])));

    [TestMethod]
    public void WithNoFactsFromTheHost_AnUnderscoreDoesNotMakeAHandler()
        => CollectionAssert.AreEqual(
            new[] { "Test_1", "Run_2" },
            Names(Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), "Public Sub Test_1()\nEnd Sub\nPublic Function Run_2() As Long\nEnd Function\nPublic Sub Handler_Click()\nEnd Sub")));

    [TestMethod]
    [DataRow("Module1")]
    [DataRow("Class1")]
    [DataRow("UserForm1")]
    public void TheNameOfTheModule_IsOneItChooses_AndHasNoRename(string name)
    {
        var finding = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), $"Attribute VB_Name = \"{name}\"\nPrivate customer As Long").Single();

        Assert.AreEqual(name, Names([finding]).Single());
        Assert.AreEqual(0, finding.Range.Start.Line);
        Assert.IsNull(finding.Fixes, "a module is renamed where the project names it");
    }

    [TestMethod]
    public void AModuleNamedForWhatItDoes_IsMeaningful()
        => Assert.IsEmpty(Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), "Attribute VB_Name = \"Invoices\"\nPrivate customer As Long"));

    [TestMethod]
    public void TheAccessorsOfAProperty_AreOneName()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()),
            "Public Property Get Zz() As Long\nEnd Property\nPublic Property Let Zz(ByVal value As Long)\nEnd Property");

        CollectionAssert.AreEqual(new[] { "Zz" }, Names(findings));
    }

    [TestMethod]
    public void TheNamesOfTheMembersOfAnEnumAndAType_AreChosenByTheModule()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), "Enum Colors\n    Red\n    B\nEnd Enum\nType Pt\n    X As Long\n    Label As String\nEnd Type");

        CollectionAssert.AreEqual(new[] { "B", "Pt", "X" }, Names(findings));
    }

    #endregion

    #region RDC01002 Hungarian notation

    private const string Prefixed = """
        Private strName As String
        Private lngCount As Long
        Private stringent As Boolean
        Private strategy As Long
        Private colWidth As Long
        Private rsData As Object
        Private Str As String
        Public Sub Work(ByVal intValue As Long, ByVal valueInt As Long)
            Dim dblRate As Double
        End Sub
        """;

    [TestMethod]
    public void ANameThatBeginsWithAPrefixOfItsType_AndThenAWord_IsHungarian()
    {
        var findings = Run(new HungarianNotationAnalyzer(Options()), Prefixed);

        CollectionAssert.AreEqual(new[] { "strName", "lngCount", "colWidth", "rsData", "intValue", "dblRate" }, Names(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.HungarianNotation, Severity: DiagnosticSeverity.Information }));
    }

    [TestMethod]
    public void AnApprovedName_IsNotHungarian()
        => CollectionAssert.AreEqual(
            new[] { "strName", "lngCount", "rsData", "intValue", "dblRate" },
            Names(Run(new HungarianNotationAnalyzer(Options(hungarian: ["COLWIDTH"])), Prefixed)));

    [TestMethod]
    [DataRow("strName", true)]
    [DataRow("str", false)]
    [DataRow("stringent", false)]
    [DataRow("Str", false)]
    [DataRow("strName1", true)]
    [DataRow("nameStr", false)]
    [DataRow("str1", true)]
    [DataRow("sName", true)]
    [DataRow("iRow", true)]
    [DataRow("oSheet", true)]
    [DataRow("mCount", true)]
    [DataRow("gConfig", true)]
    [DataRow("bDone", true)]
    [DataRow("lRow", true)]
    [DataRow("nItems", true)]
    [DataRow("txtName", true)]
    [DataRow("cmdOK", true)]
    [DataRow("lblTitle", true)]
    [DataRow("frmMain", true)]
    [DataRow("clsCustomer", true)]
    [DataRow("index", false)]
    [DataRow("item", false)]
    [DataRow("s", false)]
    [DataRow("isDone", false)]
    public void TheRule_IsAPrefixAndThenANewWord(string name, bool hungarian)
        => Assert.AreEqual(hungarian, HungarianNotationAnalyzer.IsHungarian(name));

    // the names Rubberduck's inspection reports, and not isDone.
    [TestMethod]
    public void TheNamesRubberducksInspectionReports_AreHungarian()
        => CollectionAssert.AreEquivalent(
            new[] { "mCount", "sName", "iRow", "oSheet", "str1" },
            Names(Run(new HungarianNotationAnalyzer(Options()), "Private mCount As Long\nPublic Sub Work(ByVal sName As String)\n    Dim iRow As Long, oSheet As Object, str1 As String, isDone As Boolean\nEnd Sub")));

    [TestMethod]
    [DataRow("clsCustomer")]
    [DataRow("basUtils")]
    [DataRow("frmMain")]
    public void TheNameOfAModule_CanBeHungarian(string name)
    {
        var finding = Run(new HungarianNotationAnalyzer(Options()), $"Attribute VB_Name = \"{name}\"\nPrivate customer As Long").Single();

        Assert.AreEqual(name, Names([finding]).Single());
        Assert.IsNull(finding.Fixes, "a module is renamed where the project names it");
    }

    [TestMethod]
    public void AHungarianName_IsFixedByARename_ToTheWordAfterItsPrefix()
    {
        var findings = Run(new HungarianNotationAnalyzer(Options()), "Private strName As String\nPrivate strNext As String");

        CollectionAssert.AreEqual(new[] { "Name" }, findings[0].Fixes?.Rename?.Suggestions.ToArray());
        Assert.IsNotNull(findings[1].Fixes?.Rename, "a rename still fixes it");
        Assert.IsEmpty(findings[1].Fixes!.Rename!.Suggestions, "Next is a keyword, which no name can be");
    }

    [TestMethod]
    public void ANameThatDoesNotSayWhatItIs_IsFixedByARenameToANameTheUserChooses()
    {
        var finding = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), "Private x As Long").Single();

        Assert.IsNotNull(finding.Fixes?.Rename);
        Assert.IsEmpty(finding.Fixes.Rename.Suggestions);
    }

    [TestMethod]
    public void TheFixesOfAFinding_TravelInTheDataOfItsDiagnostic()
    {
        var finding = Run(new HungarianNotationAnalyzer(Options()), "Private strName As String").Single();

        var diagnostic = new DiagnosticFactory().FromAnalyzerFinding(finding);
        var sent = Newtonsoft.Json.JsonConvert.DeserializeObject<Diagnostic>(Newtonsoft.Json.JsonConvert.SerializeObject(diagnostic))!;

        Assert.AreEqual("""{"rename":{"suggestions":["Name"]}}""", diagnostic.Data!.ToString(Newtonsoft.Json.Formatting.None));
        CollectionAssert.AreEqual(new[] { "Name" }, DiagnosticFixes.Of(sent)?.Rename?.Suggestions.ToArray());
        Assert.IsNull(DiagnosticFixes.Of(new Diagnostic { Data = Newtonsoft.Json.Linq.JToken.FromObject(new ErrorDiagnosticMetadata(13, "")) }), "the metadata of an error is no fix");
    }

    #endregion

    #region configuration

    private static DiagnosticsOptions Bind(string json)
        => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build()
            .GetSection("Configuration:Diagnostics").Get<DiagnosticsOptions>()!;

    [TestMethod]
    public void TheAllowLists_AreBoundFromTheDiagnosticsSection()
    {
        var options = Bind("""
            { "Configuration": { "Diagnostics": {
                "MeaningfulNames": { "AllowedNames": [ "i", "j" ] },
                "HungarianNotation": { "AllowedNames": [ "colWidth" ] } } } }
            """);

        CollectionAssert.AreEqual(new[] { "i", "j" }, options.MeaningfulNames.AllowedNames);
        CollectionAssert.AreEqual(new[] { "colWidth" }, options.HungarianNotation.AllowedNames);
    }

    [TestMethod]
    public void WithNothingConfigured_NoNameIsApproved()
    {
        var options = new DiagnosticsOptions();

        Assert.IsEmpty(options.MeaningfulNames.AllowedNames);
        Assert.IsEmpty(options.HungarianNotation.AllowedNames);
    }

    [TestMethod]
    public void TheSettingsFileTheExtensionShips_IsValid_AndApprovesNothing()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory);
        var shipped = new ConfigurationBuilder().AddJsonFile(Path.Combine(directory.FullName, "RDCore.Diagnostics", "appsettings.json")).Build()
            .GetSection("Configuration:Diagnostics").Get<DiagnosticsOptions>();

        Assert.IsNotNull(shipped);
        Assert.IsEmpty(shipped.MeaningfulNames.AllowedNames);
        Assert.IsEmpty(shipped.HungarianNotation.AllowedNames);
    }

    #endregion

    #region line labels

    private const string Labels = """
        Public Sub Process()
        L1:
            Debug.Print 1
        Retry:
            Debug.Print 2
        strAgain:
            Debug.Print 3
        10  Debug.Print 4
        End Sub
        """;

    [TestMethod]
    public void ALineLabel_IsANameTheProcedureChooses_AndALineNumberIsNot()
    {
        var findings = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Labels);

        CollectionAssert.AreEqual(new[] { "L1" }, Names(findings));
        Assert.AreEqual(1, findings[0].Range.Start.Line);
        Assert.IsNotNull(findings[0].Fixes?.Rename);
    }

    [TestMethod]
    public void ALineLabel_IsReportedAtItsName_WithoutTheColon()
    {
        var finding = Run(new UseMeaningfulIdentifierNamesAnalyzer(Options()), Labels).Single();

        Assert.AreEqual(new SourceRange(new SourcePosition(1, 0), new SourcePosition(1, 2)), finding.Range);
    }

    [TestMethod]
    public void AHungarianLineLabel_IsFixedByARename_ToTheWordAfterItsPrefix()
    {
        var finding = Run(new HungarianNotationAnalyzer(Options()), Labels).Single();

        Assert.AreEqual("strAgain", Names([finding])[0]);
        Assert.AreEqual(5, finding.Range.Start.Line);
        CollectionAssert.AreEqual(new[] { "Again" }, finding.Fixes?.Rename?.Suggestions.ToArray());
    }

    #endregion
}
