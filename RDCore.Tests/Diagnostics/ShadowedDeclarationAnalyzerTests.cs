using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00208</c>: a local or a parameter that hides what its name means in the rest of the module, and a declaration of the module that hides what its name
/// means in the project and its libraries (<strong>MS-VBAL §5.6.10</strong>).
/// </summary>
[TestClass]
public sealed class ShadowedDeclarationAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string program, params (string Name, string Source)[] modules)
    {
        var source = $"Attribute VB_Name = \"Program\"\r\n{program.Replace(" ;; ", "\r\n")}\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source, modules: modules);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ShadowedDeclarationAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model))];
    }

    [TestMethod]
    [DataRow("Private Count As Long ;; Public Sub Main() ;; Dim Count As Long ;; End Sub", "Count", DisplayName = "a variable of the module")]
    [DataRow("Private Const Limit = 3 ;; Public Sub Main() ;; Const Limit = 4 ;; End Sub", "Limit", DisplayName = "a constant of the module")]
    [DataRow("Private Sub Report() ;; End Sub ;; Public Sub Main() ;; Dim Report As String ;; End Sub", "Report", DisplayName = "a procedure of the module")]
    [DataRow("Private Count As Long ;; Public Sub Main(ByVal Count As Long) ;; End Sub", "Count", DisplayName = "a parameter")]
    [DataRow("Public Sub Main() ;; Dim Left As String ;; End Sub", "Left", DisplayName = "a function of the library")]
    [DataRow("Public Sub Main(ByVal Now As Date) ;; End Sub", "Now", DisplayName = "a parameter, a function of the library")]
    [DataRow("Public Enum Color ;; Red ;; End Enum ;; Public Sub Main() ;; Dim Red As Long ;; End Sub", "Red", DisplayName = "a member of an enumeration")]
    public async Task ALocalOrAParameterThatHidesAName_IsReported(string program, string name)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(program));

        Assert.AreEqual(RDCoreDiagnosticId.ShadowedDeclaration, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        StringAssert.StartsWith(finding.Message, $"'{name}' hides '");
    }

    [TestMethod]
    [DataRow("Totals", DisplayName = "an ASCII name")]
    [DataRow("Données", DisplayName = "a name with an accented letter, named as its code names it")]
    public async Task APublicMemberOfAnotherModule_IsHidden(string module)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(
            "Public Sub Main() ;; Dim Total As Long ;; End Sub", (module, ModuleWorkspace.StandardModule(module, "Public Total As Long"))));

        StringAssert.Contains(finding.Message, $"{module}.Total");
    }

    [TestMethod]
    public async Task APrivateMemberOfAnotherModule_IsNotHidden()
        => Assert.IsEmpty(await FindingsAsync(
            "Public Sub Main() ;; Dim Total As Long ;; End Sub", ("Totals", ModuleWorkspace.StandardModule("Totals", "Private Total As Long"))));

    [TestMethod]
    [DataRow("Public Function Replace(ByVal s As String) As String ;; End Function", "Replace", DisplayName = "a Public function, a function of the library")]
    [DataRow("Private Sub Beep() ;; End Sub", "Beep", DisplayName = "a Private procedure, a procedure of the library")]
    [DataRow("Private Left As String", "Left", DisplayName = "a variable, a function of the library")]
    [DataRow("Public Const Now = 1", "Now", DisplayName = "a constant, a function of the library")]
    [DataRow("Public Property Get Format() As String ;; End Property ;; Public Property Let Format(ByVal value As String) ;; End Property", "Format",
        DisplayName = "a property and its two accessors, a function of the library")]
    public async Task ADeclarationOfTheModuleThatHidesAName_IsReported(string program, string name)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(program));

        Assert.AreEqual(RDCoreDiagnosticId.ShadowedDeclaration, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        StringAssert.StartsWith(finding.Message, $"'{name}' hides '");
        StringAssert.Contains(finding.Message, "wherever");
    }

    [TestMethod]
    [DataRow("Private Total As Long", DisplayName = "a Private variable")]
    [DataRow("Public Total As Long", DisplayName = "a Public variable")]
    [DataRow("Public Function Total() As Long ;; End Function", DisplayName = "a function")]
    public async Task ADeclarationOfTheModule_HidesAPublicMemberOfAnotherModule(string program)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(program, ("Totals", ModuleWorkspace.StandardModule("Totals", "Public Total As Long"))));

        StringAssert.StartsWith(finding.Message, "'Total' hides 'Totals.Total'");
    }

    [TestMethod]
    [DataRow("Private Total As Long", DisplayName = "a variable, Private in the other module")]
    [DataRow("Private Total As Long ;; Public Sub Main() ;; Total = 1 ;; End Sub", DisplayName = "a variable the module's code uses")]
    public async Task ADeclarationOfTheModuleNamedLikeAPrivateMemberOfAnotherModule_IsNotReported(string program)
        => Assert.IsEmpty(await FindingsAsync(program, ("Totals", ModuleWorkspace.StandardModule("Totals", "Private Total As Long"))));

    [TestMethod]
    [DataRow("Public Sub Main() ;; Dim total As Long ;; Dim i As Long ;; End Sub", DisplayName = "names of their own")]
    [DataRow("Private Count As Long ;; Public Function Total() As Long ;; End Function ;; Public Property Get Title() As String ;; End Property",
        DisplayName = "declarations of the module with names of their own")]
    [DataRow("Public Sub Main() ;; Dim x As Long ;; End Sub ;; Public Sub Other() ;; Dim x As Long ;; End Sub", DisplayName = "locals of two procedures")]
    [DataRow("Public Sub Main(ByVal value As Long) ;; End Sub", DisplayName = "a parameter")]
    public async Task ALocalWithANameOfItsOwn_IsNotReported(string program)
        => Assert.IsEmpty(await FindingsAsync(program));
}
