using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Parsing;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using RDCore.Tests.Platform;
using System.Globalization;
using System.IO.Abstractions;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 form in the language: its class is composed with the <c>VB.Form</c> of the VB library (VB6.OLB) as a UserForm's is with Microsoft Forms'
/// <c>UserForm</c>; its controls are fields of their VB class; its own events are handled by <c>Form_EventName</c>, and a control array's by handlers that take
/// the control's index first. A VB6 project references the VB library without listing it.
/// </summary>
[TestClass]
public sealed class VisualBasicFormLanguageTests
{
    private static string Symbols
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
            return Path.Combine(directory.FullName, "Symbols");
        }
    }

    // the libraries that ship (Symbols/*.json): a VB6 project that lists none references VBRUN and VB, and one may list Microsoft Forms besides.
    private static ModuleWorkspace.WorkspaceLibraries Libraries(params string[] listed) => new(listed, new DirectoryLibrarySource(new FileSystem(), Symbols));

    // Command1, Text1, and the control array Command2 (two controls of one name, each with its Index)
    private static string Form(params string[] body) => Designed("VB.Form", body);

    private static string Designed(string designerClass, params string[] body)
        => "VERSION 5.00\r\n" +
           $"Begin {designerClass} Form1 \r\n" +
           "   Caption         =   \"Form1\"\r\n" +
           "   ClientHeight    =   3195\r\n" +
           "   Begin VB.CommandButton Command1 \r\n" +
           "      Caption         =   \"Command1\"\r\n" +
           "      TabIndex        =   0\r\n" +
           "   End\r\n" +
           "   Begin VB.TextBox Text1 \r\n" +
           "      TabIndex        =   1\r\n" +
           "      Text            =   \"Text1\"\r\n" +
           "   End\r\n" +
           "   Begin VB.CommandButton Command2 \r\n" +
           "      Index           =   0\r\n" +
           "      TabIndex        =   2\r\n" +
           "   End\r\n" +
           "   Begin VB.CommandButton Command2 \r\n" +
           "      Index           =   1\r\n" +
           "      TabIndex        =   3\r\n" +
           "   End\r\n" +
           "End\r\n" +
           "Attribute VB_Name = \"Form1\"\r\n" +
           "Attribute VB_GlobalNameSpace = False\r\n" +
           "Attribute VB_Creatable = False\r\n" +
           "Attribute VB_PredeclaredId = True\r\n" +
           "Attribute VB_Exposed = False\r\n" +
           "Option Explicit\r\n" +
           string.Join("\r\n", body) + "\r\n";

    private static (string, string, FormDescriptor) FormModule(string source) => ("Form1", source, FormDesigner.Read(source)!);

    private static async Task<SemanticsPayload> SemanticsAsync(string form, string program = "", params string[] listed)
        => await ModuleWorkspace.SemanticsAsync(
            [], $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{program}\r\n", language: SupportedLanguages.VB6, libraries: Libraries(listed),
            forms: [FormModule(form)]);

    private static async Task<IReadOnlyList<string>> ErrorsAsync(string form, string program = "")
    {
        var payload = await SemanticsAsync(form, program);
        return [.. payload.Modules.SelectMany(module => module.DeclarationErrors.Concat(module.Procedures.SelectMany(procedure => procedure.CompileErrors)))
            .Select(error => $"{error.Id} {error.Location.Range.Start.Line}: {error.Verbose}")];
    }

    private static async Task<DeclarationRole> RoleOfAsync(string member, string form, params string[] listed)
    {
        var payload = await SemanticsAsync(form, listed: listed);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Form1");
        return model.Declarations.First(declaration => declaration.Name == member).Role;
    }

    // ---- the form's class ----

    private static VBClassModuleSymbol ComposedForm(string form)
    {
        var root = Wire.FileUri(ModuleWorkspace.Root);
        var formUri = new UriBuilder(root) { Fragment = "Form1" }.Uri;
        var parse = new ModuleParser().Parse(formUri, form) with { Designer = FormDesigner.Read(form) };
        var libraries = new LibrarySymbolProvider(root, LibrarySymbolProvider.Load(Libraries().Source, [], SupportedLanguages.VB6)).ProvideSymbols().ToList();
        var resolver = WorkspaceSymbolResolver.Compose(root, [(formUri, ModuleType.ClassModule, parse)], new IntrinsicSymbolResolver(), libraries: libraries);
        return Assert.IsInstanceOfType<VBClassModuleSymbol>(resolver.ResolveType("Form1", ScopeKind.Global, formUri).Symbol);
    }

    [TestMethod]
    public void EveryControl_IsAMemberOfTheFormsClass_OfItsVBClass()
    {
        var form = ComposedForm(Form());

        var controls = form.Members.Where(member => member.GetProperty(SymbolProperties.DesignerControl)).ToDictionary(control => control.Name);
        CollectionAssert.AreEquivalent(new[] { "Command1", "Text1", "Command2" }, controls.Keys.ToArray(), "a control array is one field");
        Assert.AreEqual("CommandButton", Assert.IsInstanceOfType<VBClassType>(((VBPropertyGetMemberSymbol)controls["Command1"]).ResolvedType).Symbol.Name);
        Assert.AreEqual("TextBox", Assert.IsInstanceOfType<VBClassType>(((VBPropertyGetMemberSymbol)controls["Text1"]).ResolvedType).Symbol.Name);
    }

    [TestMethod]
    public void TheFormsClass_HasTheMembersOfTheVBForm()
    {
        var members = ComposedForm(Form("Public Sub Refresh2()", "End Sub")).DefaultInterfaceMembers.Select(member => member.Name).ToArray();

        CollectionAssert.IsSubsetOf(new[] { "Show", "Hide", "Caption", "Move", "Controls", "Command1", "Refresh2" }, members);
    }

    // ---- what the host checks ----

    [TestMethod]
    public async Task AFormWithItsHandlers_AndItsMembersByMe_ByItsName_AndBare_HasNoErrors()
    {
        var errors = await ErrorsAsync(
            Form(
                "Private Sub Form_Load()", "    Me.Caption = \"a\"", "    Caption = \"b\"", "    Me.Command1.Caption = \"c\"", "    Text1.Text = \"d\"", "End Sub",
                "Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)", "End Sub",
                "Private Sub Command1_Click()", "    Me.Hide", "End Sub",
                "Private Sub Text1_Change()", "End Sub"),
            "Public Sub Main()\r\n    Form1.Show\r\n    Form1.Text1.Text = \"e\"\r\n    Form1.Move 1, 2\r\n    Debug.Print Form1.Caption\r\nEnd Sub");

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    [DataRow("    Command2(0).Caption = \"a\"", DisplayName = "a control of the array, by its index")]
    [DataRow("    Debug.Print Me.Command2(1).Caption", DisplayName = "another, through Me")]
    [DataRow("    Debug.Print Command2(0).Index", DisplayName = "the index a control of the array has")]
    [DataRow("    Debug.Print Command2.Count", DisplayName = "how many controls the array has")]
    [DataRow("    Debug.Print Command2.LBound", DisplayName = "the least index")]
    [DataRow("    Debug.Print Command2.UBound", DisplayName = "the greatest index")]
    public async Task AControlArray_HasItsControlsByTheirIndex_AndItsBounds(string statement)
    {
        var errors = await ErrorsAsync(Form("Private Sub Form_Load()", statement, "End Sub"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    public async Task AControlThatIsNoControlArray_HasNoIndex()
    {
        var errors = await ErrorsAsync(Form("Private Sub Form_Load()", "    Command1(0).Caption = \"a\"", "End Sub"));

        Assert.ContainsSingle(errors);
    }

    [TestMethod]
    public void AControlArray_IsOfItsControlsClass_WhichItsHoverNames()
    {
        var array = (VBPropertyGetMemberSymbol)ComposedForm(Form()).Members.Single(member => member.Name == "Command2");
        var hover = SymbolHover.Of(array);
        var count = Assert.IsInstanceOfType<VBClassType>(array.ResolvedType).Symbol.Members.Single(member => member.Name == "Count");

        Assert.AreEqual("CommandButton", Assert.IsInstanceOfType<VBClassType>(FormControlArray.ElementOf(array.ResolvedType)).Symbol.Name);
        Assert.AreEqual("Control array of VB.CommandButton", hover.ToPlainText(CultureInfo.InvariantCulture).Split('\n').Last());
        Assert.IsFalse(hover.Declaration.Contains(" As ", StringComparison.Ordinal), $"the array's class has no name a declaration writes: {hover.Declaration}");
        Assert.IsNull(SymbolHover.Of(count).Container, "a member of the array's class is not said to be of a class by its name");
    }

    [TestMethod]
    public async Task WhatTheVBFormDoesNotHave_IsMethodOrDataMemberNotFound()
    {
        var errors = await ErrorsAsync(Form(), "Public Sub Main()\r\n    Debug.Print Form1.ShowModal\r\nEnd Sub");

        StringAssert.StartsWith(Assert.ContainsSingle(errors), nameof(VBCompileErrorId.MethodOrDataMemberNotFound));
    }

    [TestMethod]
    public async Task AMemberOfAControlsName_IsMemberAlreadyExists()
        => StringAssert.StartsWith(Assert.ContainsSingle(await ErrorsAsync(Form("Private Text1 As Long"))), nameof(VBCompileErrorId.MemberAlreadyExistsInDesigner));

    // ---- what each member is there for ----

    [TestMethod]
    [DataRow("Private Sub Form_Load()", "Form_Load", DisplayName = "VB.Form's Load")]
    [DataRow("Private Sub Form_Initialize()", "Form_Initialize", DisplayName = "VB.Form's Initialize")]
    [DataRow("Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)", "Form_QueryUnload", DisplayName = "with its parameters")]
    [DataRow("Private Sub Command1_Click()", "Command1_Click", DisplayName = "a control's")]
    [DataRow("Private Sub Text1_Validate(Cancel As Boolean)", "Text1_Validate", DisplayName = "a control's extender event, which its VB class declares")]
    [DataRow("Private Sub Command2_Click(Index As Integer)", "Command2_Click", DisplayName = "a control array's, with the index first")]
    [DataRow("Private Sub Command2_KeyPress(Index As Integer, KeyAscii As Integer)", "Command2_KeyPress", DisplayName = "a control array's, then the event's")]
    public async Task AHandlerOfAnEventOfTheFormOrOfAControl_IsAnEventHandler(string declaration, string name)
        => Assert.AreEqual(DeclarationRole.EventHandler, await RoleOfAsync(name, Form(declaration, "End Sub")));

    [TestMethod]
    [DataRow("Private Sub Form_Load(ByVal Value As Long)", "Form_Load", DisplayName = "the form's")]
    [DataRow("Private Sub Command1_Click(ByVal Value As Long)", "Command1_Click", DisplayName = "a control's")]
    [DataRow("Private Sub Command2_Click()", "Command2_Click", DisplayName = "a control array's, without the index")]
    public async Task AHandlerThatDoesNotMatchItsEvent_IsUnmatched(string declaration, string name)
        => Assert.AreEqual(DeclarationRole.UnmatchedEventHandler, await RoleOfAsync(name, Form(declaration, "End Sub")));

    [TestMethod]
    [DataRow("Private Sub Command1_Change()", "Command1_Change", DisplayName = "an event the control's class does not have")]
    [DataRow("Private Sub UserForm_Initialize()", "UserForm_Initialize", DisplayName = "a UserForm's handler")]
    [DataRow("Private Sub Form1_Load()", "Form1_Load", DisplayName = "by the form's own name")]
    public async Task AProcedureThatOnlyBeginsLikeAHandler_IsAProcedureLikeAnyOther(string declaration, string name)
        => Assert.AreEqual(DeclarationRole.None, await RoleOfAsync(name, Form(declaration, "End Sub")));

    [TestMethod]
    public async Task AVB6FormsControl_HasNoMicrosoftFormsExtender_EvenWhenTheProjectReferencesMicrosoftForms()
        => Assert.AreEqual(DeclarationRole.None, await RoleOfAsync("Command1_Enter", Form("Private Sub Command1_Enter()", "End Sub"), "MSForms"));

    [TestMethod]
    public async Task AnMDIForm_HandlesItsEventsByMDIFormEventName()
    {
        var form = Designed("VB.MDIForm", "Private Sub MDIForm_Load()", "End Sub", "Private Sub Form_Load()", "End Sub");

        Assert.AreEqual(DeclarationRole.EventHandler, await RoleOfAsync("MDIForm_Load", form));
        Assert.AreEqual(DeclarationRole.None, await RoleOfAsync("Form_Load", form));
    }

    [TestMethod]
    public async Task AnUnmatchedHandler_IsDeadCode()
    {
        var form = Form("Private Sub Form_Load(ByVal Value As Long)", "End Sub");
        var payload = await SemanticsAsync(form);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Form1");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Form1.frm"));

        var findings = new RDCore.Diagnostics.Analyzers.UnmatchedEventHandlerAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, form), model));

        Assert.AreEqual(RDCoreDiagnosticId.UnmatchedEventHandler, Assert.ContainsSingle(findings).Id);
    }

    // ---- running it ----

    [TestMethod]
    public async Task ShowingAVB6Form_WithNothingToHostIt_IsATrappableRunTimeError_NotAnInternalOne()
    {
        // no host runs a VB6 form: what error a host-less Show is in VB6's terms is the runtime's to decide, and any trappable one is not an internal error.
        var printed = await ModuleWorkspace.RunAsync(
            [], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\nForm1.Show\r\nDebug.Print Err.Number & \"|\" & Err.Description\r\nEnd Sub\r\n",
            Libraries() with { Outside = RDCore.External.ExternalWorld.None }, [FormModule(Form())], SupportedLanguages.VB6);

        var error = Assert.ContainsSingle(printed);
        Assert.AreNotEqual("0", error.Split('|')[0], error);
    }

    // the outside world of an external host that hosts its VB6 forms with RDForms, headless, and has no other server: the composition rdc's external host makes.
    private static RDCore.External.ExternalWorld RDFormsWorld(RDCore.External.Forms.RDForms.VisualBasicFormsHost host = RDCore.External.Forms.RDForms.VisualBasicFormsHost.RDForms)
    {
        var (automation, forms) = RDCore.External.Forms.RDForms.RDFormsHosting.Compose(
            RDCore.External.Automation.UnavailableAutomationServer.Instance,
            RDCore.External.Forms.UnavailableFormHost.Instance,
            new RDCore.External.Forms.RDForms.FormsOptions { VB = host, Headless = true });
        return new RDCore.External.ExternalWorld(automation, RDCore.External.Native.UnavailableNativeLibraryHost.Instance) { Forms = forms };
    }

    private static Task<string[]> RunWithRDFormsAsync(string main, RDCore.External.ExternalWorld? world = null, string? form = null)
        => ModuleWorkspace.RunAsync(
            [], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{main}\r\nEnd Sub\r\n",
            Libraries() with { Outside = world ?? RDFormsWorld() }, [FormModule(form ?? Form())], SupportedLanguages.VB6);

    [TestMethod]
    public async Task AVB6Form_HostedByRDForms_IsShownModelessByAShowThatSaysNothing_AndHidden()
    {
        var printed = await RunWithRDFormsAsync("Form1.Show\r\nDebug.Print Form1.Visible\r\nForm1.Hide\r\nDebug.Print Form1.Visible\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { "True", "False" }, printed);
    }

    [TestMethod]
    public async Task AVB6FormsCaption_HostedByRDForms_IsItsDesignersUntilTheProgramSetsIt()
    {
        var printed = await RunWithRDFormsAsync("Debug.Print Form1.Caption\r\nForm1.Caption = \"Hello\"\r\nDebug.Print Form1.Caption\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { "Form1", "Hello" }, printed);
    }

    [TestMethod]
    public async Task AVB6FormsOwnProperty_HostedByRDForms_IsReadAndWrittenOnItsModel()
    {
        var printed = await RunWithRDFormsAsync("Form1.Tag = \"tagged\"\r\nDebug.Print Form1.Tag\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { "tagged" }, printed);
    }

    [TestMethod]
    public async Task AVB6FormsControls_HostedByRDForms_HaveTheirDesignersProperties()
    {
        var printed = await RunWithRDFormsAsync("Debug.Print Form1.Command1.Caption\r\nDebug.Print Form1.Text1.Text\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { "Command1", "Text1" }, printed);
    }

    [TestMethod]
    public async Task AVB6FormsControl_HostedByRDForms_KeepsWhatTheProgramSetsIt()
    {
        var printed = await RunWithRDFormsAsync("Form1.Text1.Text = \"typed\"\r\nDebug.Print Form1.Text1.Text\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { "typed" }, printed);
    }

    [TestMethod]
    public async Task AVB6FormsControlsPosition_HostedByRDForms_IsInTwips_OrInTheFormsScale()
    {
        var printed = await RunWithRDFormsAsync(
            "Form1.Text1.Left = 360\r\nForm1.Text1.Width = 1500\r\nDebug.Print Form1.Text1.Left\r\nDebug.Print Form1.Text1.Width\r\n" +
            "Form1.ScaleMode = 3\r\nDebug.Print Form1.Text1.Left\r\nDebug.Print Form1.Text1.Width\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { "360", "1500", "24", "100" }, printed);
    }

    [TestMethod]
    public async Task AVB6FormsControlsEvent_HostedByRDForms_IsHandledByTheFormsHandler()
    {
        // setting a TextBox's Text from code raises its Change, in VB6 as in a UserForm.
        var form = Form("Private Sub Text1_Change()", "Debug.Print \"changed \" & Text1.Text", "End Sub");
        var printed = await RunWithRDFormsAsync("Form1.Text1.Text = \"typed\"\r\nUnload Form1", form: form);

        CollectionAssert.AreEqual(new[] { "changed typed" }, printed);
    }

    [TestMethod]
    public async Task AVB6Form_WithRDFormsTurnedOff_IsATrappableRunTimeError_AsWithNothingToHostIt()
    {
        var printed = await RunWithRDFormsAsync(
            "On Error Resume Next\r\nForm1.Show\r\nDebug.Print Err.Number",
            RDFormsWorld(RDCore.External.Forms.RDForms.VisualBasicFormsHost.Off));

        Assert.AreNotEqual("0", Assert.ContainsSingle(printed));
    }

    [TestMethod]
    public async Task AnObjectByName_WithRDFormsAndNoAutomationServer_IsError429_AsWithNoFormHost()
    {
        var printed = await RunWithRDFormsAsync("On Error Resume Next\r\nDim x As Object\r\nSet x = CreateObject(\"Scripting.Dictionary\")\r\nDebug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "429" }, printed);
    }

    // a value assigned to a control's property is let-coerced to the property's declared type, early-bound or late-bound, as DispInvoke coerces it.
    [TestMethod]
    [DataRow("Form1.Text1", "Left = Empty", "Left", "0", DisplayName = "Empty to a Single, early-bound, is 0")]
    [DataRow("o", "Left = Empty", "Left", "0", DisplayName = "Empty to a Single, late-bound, is 0")]
    [DataRow("o", "Width = \"1500\"", "Width", "1500", DisplayName = "a String to a Single, late-bound, is its number")]
    [DataRow("o", "Text = True", "Text", "True", DisplayName = "True to a String, late-bound, is True")]
    [DataRow("o", "Text = 2.5", "Text", "2.5", DisplayName = "a Double to a String, late-bound, is its digits")]
    [DataRow("o", "TabIndex = 1.5", "TabIndex", "2", DisplayName = "a Double to an Integer, late-bound, rounds to even")]
    public async Task AValueAssignedToAControlsProperty_IsLetCoercedToItsDeclaredType(string target, string assignment, string read, string printed)
    {
        var lines = await RunWithRDFormsAsync(
            $"Dim o As Object\r\nSet o = Form1.Text1\r\n{target}.{assignment}\r\nDebug.Print {target}.{read}\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { printed }, lines);
    }

    [TestMethod]
    [DataRow("Text = Null", "94", DisplayName = "Null to a String is invalid use of Null")]
    [DataRow("Width = \"wide\"", "13", DisplayName = "a String that is no number, to a Single, is a type mismatch")]
    [DataRow("TabIndex = 70000", "6", DisplayName = "a number too large for an Integer overflows")]
    public async Task AValueThatDoesNotCoerceToAControlsProperty_IsTheCoercionsError(string assignment, string error)
    {
        var printed = await RunWithRDFormsAsync($"On Error Resume Next\r\nDim o As Object\r\nSet o = Form1.Text1\r\no.{assignment}\r\nDebug.Print Err.Number\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { error }, printed);
    }

    [TestMethod]
    public async Task AControlReachedLateBound_IsOfItsVBClass()
    {
        var printed = await RunWithRDFormsAsync("Dim o As Object\r\nSet o = Form1.Text1\r\nDebug.Print TypeName(o)\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { "TextBox" }, printed);
    }

    [TestMethod]
    [Ignore("control arrays are not implemented in the RDForms model")]
    public async Task AControlOfAControlArray_IsReachedByItsIndex()
    {
        var printed = await RunWithRDFormsAsync("Debug.Print Form1.Command2(1).Index\r\nUnload Form1");

        CollectionAssert.AreEqual(new[] { "1" }, printed);
    }

    // ---- completion ----

    private static async Task<string[]> CompletionAtAsync(string form)
    {
        // the cursor is where the `|` is
        var lines = form.Split("\r\n");
        var line = Array.FindIndex(lines, text => text.Contains('|'));
        var position = new Position(line, lines[line].IndexOf('|'));
        var text = form.Replace("|", string.Empty);

        var root = ModuleWorkspace.Root;
        var document = new WorkspaceDocument("Form1.frm", root, text);
        var uri = document.Id.Uri.ToUri();
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.WorkspaceRoot.Returns(root);
        documents.GetAllDocuments().Returns([document]);
        documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });

        // as the language server keeps it: the parse of the document, with what its designer declares
        var parsing = Substitute.For<IParsingClientService>();
        var cached = new ModuleParser().Parse(uri, text) with { Designer = FormDesigner.Read(text) };
        parsing.TryGetCached(uri, out Arg.Any<SDK.Model.AST.ModuleParseResult>()).Returns(call => { call[1] = cached; return true; });
        parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var source = Libraries().Source;
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), LibrarySymbolProvider.Load(source, [new RDCoreReference { Name = "MSForms" }], SupportedLanguages.VB6)).ProvideSymbols()]);
        var options = Options.Create(new SDK.Server.Configuration.SdkAppOptions
        {
            Workspace = new SDK.Server.Configuration.SdkWorkspaceOptions { WorkspaceUri = root, Language = "vb6" },
        });
        var completion = new CompletionService(
            documents, parsing, new IntrinsicSymbolResolver(), libraries, source, new ClientVerbRegistry(), options,
            NullLogger<CompletionService>.Instance, RDCore.Tests.LanguageServer.OneProject.Registry());

        var list = await completion.CompleteDocumentAsync(uri, position, CancellationToken.None);
        return [.. list.Items.Select(item => item.Label)];
    }

    [TestMethod]
    public async Task MeDot_OffersTheControls_AndTheVBFormsMembers()
        => CollectionAssert.IsSubsetOf(
            new[] { "Command1", "Text1", "Command2", "Caption", "Show", "Hide", "Move", "Controls" },
            await CompletionAtAsync(Form("Private Sub Fill()", "    Me.|", "End Sub")));

    [TestMethod]
    [DataRow("    Me.Command1.|")]
    [DataRow("    Command1.|")]
    public async Task AControlDot_OffersTheMembersOfItsVBClass_AndNoMicrosoftFormsExtender(string line)
    {
        var labels = await CompletionAtAsync(Form("Private Sub Fill()", line, "End Sub"));

        CollectionAssert.IsSubsetOf(new[] { "Caption", "Left", "SetFocus", "TabIndex" }, labels);
        CollectionAssert.DoesNotContain(labels, "ControlTipText", "MSForms.Control's, which the project references and a VB6 form does not wrap its controls in");
    }
}
