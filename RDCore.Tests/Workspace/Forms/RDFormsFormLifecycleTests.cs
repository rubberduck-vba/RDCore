using RDCore.Forms.Xaml;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A form ported to RDForms, whose code is a <c>.rdform</c> module and whose controls are in its <c>.xaml</c>, keeps a Visual Basic 6.0 form's lifecycle:
/// a member of its form loads it, its handlers are <c>Form_</c>'s, and <c>Show</c> shows it modeless. The forms are hosted by RDForms, headless.
/// </summary>
[TestClass]
public sealed class RDFormsFormLifecycleTests
{
    private const string Document =
        "<Form xmlns=\"https://rubberduckvba.ca/schemas/rdforms\" FormatVersion=\"1\" Name=\"Form2\" Caption=\"Ported\">\n" +
        "  <TextBox Name=\"Text1\" Left=\"8\" Top=\"8\" Width=\"81\" Height=\"19\" Text=\"Text1\" TabIndex=\"0\" />\n" +
        "</Form>\n";

    // the .rdform: the attributes and the code, with no VERSION header and no designer block.
    private static readonly string Code = string.Join("\r\n", [
        "Attribute VB_Name = \"Form2\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        "Private Sub Form_Initialize()", "    Debug.Print \"Initialize\"", "End Sub",
        "Private Sub Form_Load()", "    Debug.Print \"Load\"", "End Sub",
        "Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)", "    Debug.Print \"QueryUnload \" & UnloadMode", "End Sub",
        "Private Sub Form_Unload(Cancel As Integer)", "    Debug.Print \"Unload\"", "End Sub",
        string.Empty]);

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    // the form alone: its own members are the form host's, whatever library describes its controls.
    private const string FormOnly =
        "<Form xmlns=\"https://rubberduckvba.ca/schemas/rdforms\" FormatVersion=\"1\" Name=\"Form2\" Caption=\"Ported\" />\n";

    private static Task<string[]> RunAsync(string document, params string[] body)
        => ModuleWorkspace.RunAsync([], Program(body), Libraries, [("Form2", Code, FormXamlDocumentReader.DescriptorOf(document))], SupportedLanguages.VB6);

    [TestMethod]
    public void APortedFormsModule_IsAClassModule_AndItsDocumentDeclaresAFormOfVisualBasic6sModel()
    {
        var designer = FormXamlDocumentReader.DescriptorOf(Document);

        Assert.AreEqual(SDK.Model.AST.Declarations.ModuleType.ClassModule, ModuleKinds.Of(null, Code, "Forms/Form2.rdform"));
        Assert.AreEqual(FormDesignerKind.RDForms, designer.Kind);
        Assert.IsTrue(designer.HasVisualBasic6FormModel);
    }

    [TestMethod]
    public void APortedForm_IsRecordedAsAFormOfVisualBasic6sModel_AndAUserFormIsNot()
    {
        var forms = RDCore.Runtime.Execution.External.Forms.SessionForms.Of(NSubstitute.Substitute.For<RDCore.SDK.Runtime.Abstract.Execution.IRuntimeSession>());
        var userForm = new FormDescriptor("UserForm1", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm"), new Dictionary<string, string>(), []);

        forms.Declare(new Dictionary<string, FormDescriptor> { ["Form2"] = FormXamlDocumentReader.DescriptorOf(Document), ["UserForm1"] = userForm });

        var root = new Uri("file:///c:/ws/");
        Assert.IsTrue(forms.IsVisualBasic6(new SDK.Model.Symbols.VBClassModuleSymbol(root, new Uri(root, "Forms/Form2.rdform"), "Form2")));
        Assert.IsFalse(forms.IsVisualBasic6(new SDK.Model.Symbols.VBClassModuleSymbol(root, new Uri(root, "UserForm1.frm"), "UserForm1")));
    }

    private const string NoRDFormsLibrary = "RDForms describes no RDForms library, so the RDForms.* classes of a ported form and of its controls have no declared type.";

    [TestMethod]
    [Ignore(NoRDFormsLibrary)]
    [DataRow(new[] { "Debug.Print Form2.Caption", "Unload Form2" }, new[] { "Initialize", "Load", "Ported", "QueryUnload 1", "Unload" },
        DisplayName = "a member of its form loads it, and Unload asks it")]
    [DataRow(new[] { "Form2.Show", "Debug.Print Form2.Visible", "Unload Form2" }, new[] { "Initialize", "Load", "True", "QueryUnload 1", "Unload" },
        DisplayName = "Show shows it modeless")]
    public async Task APortedForm_KeepsAVisualBasic6FormsLifecycle(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(FormOnly, body));

    [TestMethod]
    [Ignore(NoRDFormsLibrary)]
    public async Task APortedFormsControl_LoadsItsForm()
        => CollectionAssert.AreEqual(new[] { "Initialize", "Load", "Text1", "QueryUnload 1", "Unload" },
            await RunAsync(Document, "Debug.Print Form2.Text1.Text", "Unload Form2"));
}
