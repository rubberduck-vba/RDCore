using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

/// <summary>
/// Where a program starts when nothing names the module: a <c>Sub Main</c> of a standard module that the project can call, which takes no argument or one
/// <c>ParamArray</c>.
/// </summary>
[TestClass]
public sealed class ProgramEntryPointTests
{
    private static ModuleNode Parse(string source)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Attribute VB_Name = \"Prog\"\r\n" + source);
        Assert.IsNotNull(result.SyntaxTree, "expected a parsed module");
        return result.SyntaxTree;
    }

    [TestMethod]
    [DataRow("Public Sub Main()\r\nEnd Sub\r\n", DisplayName = "Public Sub Main()")]
    [DataRow("Sub Main()\r\nEnd Sub\r\n", DisplayName = "Sub Main(), Public by default")]
    [DataRow("Global Sub Main()\r\nEnd Sub\r\n", DisplayName = "Global Sub Main()")]
    [DataRow("Public Sub main()\r\nEnd Sub\r\n", DisplayName = "the name in another case")]
    [DataRow("Public Sub Main(ParamArray Args())\r\nEnd Sub\r\n", DisplayName = "Sub Main(ParamArray Args())")]
    [DataRow("Public Sub Main(ParamArray Args() As Variant)\r\nEnd Sub\r\n", DisplayName = "Sub Main(ParamArray Args() As Variant)")]
    [DataRow("Private Sub Helper()\r\nEnd Sub\r\nPublic Sub Main()\r\nEnd Sub\r\n", DisplayName = "among other procedures")]
    public void ASubMainTheProjectCanCall_ThatTakesNoArgumentOrAParamArray_IsWhereTheProgramStarts(string source)
        => Assert.IsTrue(ProgramEntryPoint.IsDeclaredBy(ModuleType.StdModule, Parse(source)));

    [TestMethod]
    [DataRow("Private Sub Main()\r\nEnd Sub\r\n", DisplayName = "Private Sub Main()")]
    [DataRow("Public Sub Main(ByVal Count As Long)\r\nEnd Sub\r\n", DisplayName = "a parameter")]
    [DataRow("Public Sub Main(Optional ByVal Count As Long)\r\nEnd Sub\r\n", DisplayName = "an optional parameter")]
    [DataRow("Public Sub Main(ByVal Name As String, ParamArray Args())\r\nEnd Sub\r\n", DisplayName = "a parameter before the ParamArray")]
    [DataRow("Public Function Main()\r\nEnd Function\r\n", DisplayName = "a Function")]
    [DataRow("Public Property Get Main()\r\nEnd Property\r\n", DisplayName = "a Property Get")]
    [DataRow("Public Sub Start()\r\nEnd Sub\r\n", DisplayName = "another name")]
    [DataRow("Public Main As Long\r\n", DisplayName = "a variable")]
    public void AnythingElse_IsNot(string source)
        => Assert.IsFalse(ProgramEntryPoint.IsDeclaredBy(ModuleType.StdModule, Parse(source)));

    [TestMethod]
    [DataRow(ModuleType.ClassModule)]
    [DataRow(ModuleType.DocumentModule)]
    public void ASubMainOfAClassOrADocument_IsNot(ModuleType kind)
        => Assert.IsFalse(ProgramEntryPoint.IsDeclaredBy(kind, Parse("Public Sub Main()\r\nEnd Sub\r\n")));

    [TestMethod]
    public void AnEntryPointOfAnotherName_IsTheSubOfThatName()
    {
        var module = Parse("Public Sub Start()\r\nEnd Sub\r\n");

        Assert.IsTrue(ProgramEntryPoint.IsDeclaredBy(ModuleType.StdModule, module, "Start"));
        Assert.IsFalse(ProgramEntryPoint.IsDeclaredBy(ModuleType.StdModule, module));
    }

    [TestMethod]
    public void ASubMainWhoseHeaderIsWrittenInEachBranchOfAnIf_IsWhereTheProgramStarts_WhenOneOfItsHeadersIs()
    {
        var module = Parse("#If VBA7 Then\r\nPrivate Sub Main(ByVal Count As Long)\r\n#Else\r\nPublic Sub Main()\r\n#End If\r\nEnd Sub\r\n");

        Assert.IsTrue(ProgramEntryPoint.IsDeclaredBy(ModuleType.StdModule, module));
    }
}
