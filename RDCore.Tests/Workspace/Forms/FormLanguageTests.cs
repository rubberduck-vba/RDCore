using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace.Forms;
using RDCore.Parsing;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using RDCore.Tests.Platform;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Abstractions.TestingHelpers;
using MemberKind = RDCore.SDK.Runtime.Libraries.MemberKind;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A form's controls in the language: each is a <c>Public</c>, <c>WithEvents</c> field of the form's class, of the control's class, which its code refers to
/// and handles the events of; its code declares no member of a control's name (MS-VBA, Excel-measured: <c>oracle\s30</c>).
/// </summary>
[TestClass]
public sealed class FormLanguageTests
{
    // the part of Microsoft Forms 2.0 the tests use, as `rdc export-library` describes FM20.DLL
    private static readonly LibraryDescription MSForms = new()
    {
        Name = "MSForms",
        Classes =
        [
            new ClassDescription
            {
                Name = "UserForm",
                Members =
                [
                    new MemberDescription { Name = "Caption", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription { Name = "Caption", Kind = MemberKind.PropertyLet, Parameters = [new ParameterDescription { Name = "Caption", Type = "String" }] },
                ],
                Events = [new EventDescription { Name = "Click" }],
            },
            new ClassDescription
            {
                Name = "TextBox",
                Members =
                [
                    new MemberDescription { Name = "Text", Kind = MemberKind.PropertyGet, Type = "String", DispId = 0 },
                    new MemberDescription { Name = "Text", Kind = MemberKind.PropertyLet, DispId = 0, Parameters = [new ParameterDescription { Name = "Text", Type = "String" }] },
                ],
                Events = [new EventDescription { Name = "Change" }],
            },
            // the extender every control of a form is wrapped in
            new ClassDescription
            {
                Name = "Control",
                Members =
                [
                    new MemberDescription { Name = "Left", Kind = MemberKind.PropertyGet, Type = "Single" },
                    new MemberDescription { Name = "SetFocus", Kind = MemberKind.Method },
                ],
                Events = [new EventDescription { Name = "Enter" }, new EventDescription { Name = "Exit" }],
            },
            new ClassDescription { Name = "OptionButton", Events = [new EventDescription { Name = "Click" }] },
            new ClassDescription { Name = "Frame", Events = [new EventDescription { Name = "Click" }] },
            new ClassDescription { Name = "MultiPage", Events = [new EventDescription { Name = "Change" }] },
            new ClassDescription { Name = "Page" },
        ],
    };

    private static readonly ModuleWorkspace.WorkspaceLibraries References = new(["MSForms"], new InMemoryLibrarySource(MSForms));

    private static FormControlDescriptor Control(string name, string className, params FormControlDescriptor[] children)
        => new(name, new FormControlClass("MSForms", className), TabIndex: null, Index: null, children);

    // TextBox1; Frame1 holding OptionButton1; MultiPage1, whose Page1 holds TextBox2 - the form s30 measures
    private static readonly FormDescriptor Designer = new(
        "Form1", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
        new Dictionary<string, string> { ["Caption"] = "Form1" },
        [
            Control("TextBox1", "TextBox"),
            Control("Frame1", "Frame", Control("OptionButton1", "OptionButton")),
            Control("MultiPage1", "MultiPage", Control("Page1", "Page", Control("TextBox2", "TextBox"))),
        ]);

    private static string Form(params string[] body)
        => "VERSION 5.00\r\n" +
           "Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Form1 \r\n" +
           "   Caption         =   \"Form1\"\r\n" +
           "   OleObjectBlob   =   \"Form1.frx\":0000\r\n" +
           "End\r\n" +
           "Attribute VB_Name = \"Form1\"\r\n" +
           "Attribute VB_GlobalNameSpace = False\r\n" +
           "Attribute VB_Creatable = False\r\n" +
           "Attribute VB_PredeclaredId = True\r\n" +
           "Attribute VB_Exposed = False\r\n" +
           "Option Explicit\r\n" +
           string.Join("\r\n", body) + "\r\n";

    private static async Task<IReadOnlyList<(VBCompileErrorId Id, int Line)>> DeclarationErrorsAsync(string form, string program = "")
    {
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{program}\r\n", libraries: References, forms: [("Form1", form, Designer)]);
        return [.. payload.Modules.SelectMany(module => module.DeclarationErrors.Concat(module.Procedures.SelectMany(procedure => procedure.CompileErrors)))
            .Select(error => (error.Id, error.Location.Range.Start.Line))];
    }

    // ---- the controls are fields of the form's class ----

    private static VBClassModuleSymbol ComposedForm(string form)
    {
        var root = Wire.FileUri(ModuleWorkspace.Root);
        var formUri = new UriBuilder(root) { Fragment = "Form1" }.Uri;
        var parse = new ModuleParser().Parse(formUri, form) with { Designer = Designer };
        var libraries = new LibrarySymbolProvider(root, ReferencedLibraries.Load(References.Source, ["MSForms"], [])).ProvideSymbols().ToList();
        var resolver = WorkspaceSymbolResolver.Compose(root, [(formUri, ModuleType.ClassModule, parse)], new IntrinsicSymbolResolver(), libraries: libraries);
        return Assert.IsInstanceOfType<VBClassModuleSymbol>(resolver.ResolveType("Form1", ScopeKind.Global, formUri).Symbol);
    }

    [TestMethod]
    public void EveryControl_IsAPublicReadOnlyPropertyOfItsClass_ButTheMultiPagesPages()
    {
        var form = ComposedForm(Form());

        // what the designer owns, the code reads and does not assign: a Property Get, and no Let or Set
        var controls = form.Members.Where(member => member.GetProperty(SymbolProperties.DesignerControl)).ToDictionary(control => control.Name);
        CollectionAssert.AreEquivalent(new[] { "TextBox1", "Frame1", "OptionButton1", "MultiPage1", "TextBox2" }, controls.Keys.ToArray());
        Assert.IsTrue(controls.Values.All(control => control is VBPropertyGetMemberSymbol));
        var textBox = (VBPropertyGetMemberSymbol)controls["TextBox1"];
        Assert.AreEqual(AccessModifier.Public, textBox.AccessModifier);
        Assert.IsTrue(textBox.GetProperty(SymbolProperties.WithEvents));
        Assert.IsTrue(textBox.GetProperty(SymbolProperties.DesignerControl));
        Assert.AreEqual("TextBox", Assert.IsInstanceOfType<VBClassType>(textBox.ResolvedType).Symbol.Name);
    }

    [TestMethod]
    public void TheControls_AreMembersOfTheFormsInterface()
    {
        var form = ComposedForm(Form("Public Sub Refresh()", "End Sub"));

        CollectionAssert.IsSubsetOf(new[] { "TextBox1", "OptionButton1", "TextBox2", "Refresh" }, form.DefaultInterfaceMembers.Select(member => member.Name).ToArray());
    }

    [TestMethod]
    public void ANameTheCodeDeclares_IsTheCodesMember()
    {
        var form = ComposedForm(Form("Private TextBox1 As Long"));

        var field = Assert.ContainsSingle(form.Members.Where(member => member.Name == "TextBox1"));
        Assert.IsFalse(field.GetProperty(SymbolProperties.DesignerControl));
    }

    // ---- what the host checks ----

    [TestMethod]
    public async Task AFormWithItsHandlers_HasNoErrors()
    {
        var errors = await DeclarationErrorsAsync(
            Form("Private Sub TextBox1_Change()", "End Sub", "Private Sub OptionButton1_Click()", "End Sub", "Public Sub Fill()", "    TextBox1.Text = \"a\"", "    Me.TextBox2.Text = \"b\"", "End Sub"),
            "Public Sub Main()\r\n    Debug.Print Form1.TextBox1.Text\r\nEnd Sub");

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    [DataRow("Private TextBox1 As Long")]
    [DataRow("Private Sub TextBox1()\r\nEnd Sub")]
    [DataRow("Public Function OptionButton1() As Long\r\nEnd Function")]
    [DataRow("Private Const TextBox2 = 1")]
    public async Task AMemberOfAControlsName_IsMemberAlreadyExists_AtItsDeclaration(string declaration)
    {
        var errors = await DeclarationErrorsAsync(Form(declaration));

        var error = Assert.ContainsSingle(errors);
        Assert.AreEqual(VBCompileErrorId.MemberAlreadyExistsInDesigner, error.Id);
        Assert.AreEqual(11, error.Line, "the line of the declaration, after the header and Option Explicit");
    }

    [TestMethod]
    public async Task APagesName_IsNoControlsName()
        => Assert.IsEmpty(await DeclarationErrorsAsync(Form("Private Page1 As Long")));

    [TestMethod]
    [DataRow("Private Sub TextBox1_Change(ByVal Value As Long)", "End Sub")]
    [DataRow("Private Function TextBox1_Change() As Long", "End Function")]
    public async Task AControlsHandlerThatDoesNotMatchTheEvent_IsNoError(string declaration, string end)
        // MS-VBA compiles it (s30 C06, C19): the event never calls it, which makes it dead code (RDC00407), not an invalid handler
        => Assert.IsEmpty(await DeclarationErrorsAsync(Form(declaration, end)));

    // ---- RDC00407: a control's handler that does not match the event is dead, unless something calls it ----

    private static async Task<AnalyzerFinding[]> UnmatchedHandlersAsync(params string[] body)
    {
        var form = Form(body);
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n", libraries: References, forms: [("Form1", form, Designer)]);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Form1");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Form1.frm"));
        return [.. new RDCore.Diagnostics.Analyzers.UnmatchedEventHandlerAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, form), model))];
    }

    [TestMethod]
    public async Task AControlsHandlerThatDoesNotMatchTheEvent_IsDeadCode_OverItsWholeDeclaration()
    {
        var findings = await UnmatchedHandlersAsync("Private Sub TextBox1_Change(ByVal Value As Long)", "    Debug.Print Value", "End Sub");

        var finding = Assert.ContainsSingle(findings);
        Assert.AreEqual(RDCoreDiagnosticId.UnmatchedEventHandler, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, finding.Severity);
        CollectionAssert.AreEqual(new[] { DiagnosticTag.Unnecessary }, finding.Tags.ToArray(), "rendered as code that cannot be reached");
        Assert.AreEqual((11, 13), (finding.Range.Start.Line, finding.Range.End.Line), "from Sub to End Sub");
    }

    [TestMethod]
    public async Task AHandlerThatMatches_IsNotDead()
        => Assert.IsEmpty(await UnmatchedHandlersAsync("Private Sub TextBox1_Change()", "End Sub"));

    [TestMethod]
    public async Task AnUnmatchedHandlerThatTheModuleCalls_IsNotDead()
        => Assert.IsEmpty(await UnmatchedHandlersAsync(
            "Private Sub TextBox1_Change(ByVal Value As Long)", "End Sub",
            "Public Sub Refresh()", "    TextBox1_Change 42", "End Sub"));

    [TestMethod]
    public async Task APublicUnmatchedHandler_MayBeCalledThroughTheForm_AndIsNotReported()
        => Assert.IsEmpty(await UnmatchedHandlersAsync("Public Sub TextBox1_Change(ByVal Value As Long)", "End Sub"));

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
        var cached = new ModuleParser().Parse(uri, text) with { Designer = Designer };
        parsing.TryGetCached(uri, out Arg.Any<SDK.Model.AST.ModuleParseResult>()).Returns(call => { call[1] = cached; return true; });
        parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(References.Source, ["MSForms"], [SDK.Runtime.StdLib.StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SDK.Server.Configuration.SdkAppOptions
        {
            Workspace = new SDK.Server.Configuration.SdkWorkspaceOptions { WorkspaceUri = root, Language = "vba" },
        });
        var completion = new CompletionService(
            documents, parsing, new IntrinsicSymbolResolver(), libraries, References.Source, new ClientVerbRegistry(), options,
            NullLogger<CompletionService>.Instance, RDCore.Tests.LanguageServer.OneProject.Registry());

        var list = await completion.CompleteDocumentAsync(uri, position, CancellationToken.None);
        return [.. list.Items.Select(item => item.Label)];
    }

    [TestMethod]
    public async Task MeDot_OffersTheControls_ButNotThePages()
    {
        var labels = await CompletionAtAsync(Form("Private Sub Fill()", "    Me.|", "End Sub"));

        CollectionAssert.IsSubsetOf(new[] { "TextBox1", "Frame1", "OptionButton1", "MultiPage1", "TextBox2" }, labels);
        CollectionAssert.DoesNotContain(labels, "Page1");
    }

    [TestMethod]
    public async Task AControlDot_OffersTheMembersOfItsClass()
    {
        var labels = await CompletionAtAsync(Form("Private Sub Fill()", "    Me.TextBox1.|", "End Sub"));

        CollectionAssert.Contains(labels, "Text");
    }

    [TestMethod]
    [DataRow("    Me.TextBox1.|")]
    [DataRow("    TextBox1.|")]
    public async Task AControlDot_OffersTheMembersOfTheExtenderToo(string line)
        => CollectionAssert.IsSubsetOf(new[] { "Text", "Left", "SetFocus" }, await CompletionAtAsync(Form("Private Sub Fill()", line, "End Sub")));

    [TestMethod]
    public async Task AVariableOfAControlsClass_IsNoControl_AndHasNoExtender()
    {
        var labels = await CompletionAtAsync(Form("Private Sub Fill()", "    Dim t As MSForms.TextBox", "    t.|", "End Sub"));

        CollectionAssert.Contains(labels, "Text");
        CollectionAssert.DoesNotContain(labels, "Left");
    }

    // ---- the extender's events, and Set ----

    [TestMethod]
    public async Task AHandlerOfAnExtendersEvent_IsAHandler_AndUnmatchedIsDead()
    {
        Assert.IsEmpty(await UnmatchedHandlersAsync("Private Sub TextBox1_Enter()", "End Sub"));

        var finding = Assert.ContainsSingle(await UnmatchedHandlersAsync("Private Sub TextBox1_Exit(ByVal Value As Long)", "End Sub"));
        Assert.AreEqual(RDCoreDiagnosticId.UnmatchedEventHandler, finding.Id);
    }

    [TestMethod]
    [DataRow("Set Form1.TextBox1 = Nothing", "")]
    [DataRow("", "Set Me.TextBox1 = Nothing")]
    [DataRow("", "Set TextBox1 = Nothing")]
    public async Task SettingAControl_IsAnInvalidUseOfProperty(string program, string form)
    {
        var errors = await DeclarationErrorsAsync(
            Form("Public Sub Reset()", $"    {form}", "End Sub"), $"Public Sub Main()\r\n    {program}\r\nEnd Sub");

        Assert.AreEqual(VBCompileErrorId.InvalidUseOfProperty, Assert.ContainsSingle(errors).Id);
    }

    // ---- the form's own members and events: the Microsoft Forms UserForm's and the VBA form object's (oracle\s32b, s32c) ----

    [TestMethod]
    public async Task TheFormsOwnMembers_AreMembersOfItsClass_ByMe_ByItsName_AndBare()
        => Assert.IsEmpty(await DeclarationErrorsAsync(
            Form("Public Sub Reset()", "    Me.Caption = \"a\"", "    Debug.Print Caption", "    Me.Left = 10", "    Hide", "End Sub"),
            "Public Sub Main()\r\n    Form1.Caption = \"b\"\r\n    Form1.Move 1, 2\r\n    Form1.Show\r\n    Debug.Print Form1.Name\r\nEnd Sub"));

    [TestMethod]
    [DataRow("Debug.Print Form1.ShowModal", DisplayName = "s32b M14 a designer property, not a member")]
    [DataRow("Debug.Print Form1.Parent", DisplayName = "s32b M19")]
    public async Task WhatTheFormObjectDoesNotHave_IsMethodOrDataMemberNotFound(string statement)
        => Assert.AreEqual(VBCompileErrorId.MethodOrDataMemberNotFound, Assert.ContainsSingle(await DeclarationErrorsAsync(Form(), $"Public Sub Main()\r\n    {statement}\r\nEnd Sub")).Id);

    [TestMethod]
    public async Task MeDot_OffersTheFormsOwnMembers()
        => CollectionAssert.IsSubsetOf(new[] { "Caption", "Show", "Hide", "Left", "Move", "TextBox1" }, await CompletionAtAsync(Form("Private Sub Fill()", "    Me.|", "End Sub")));

    [TestMethod]
    [DataRow("Private Sub UserForm_Initialize()", DisplayName = "the form object's")]
    [DataRow("Private Sub UserForm_QueryClose(Cancel As Integer, CloseMode As Integer)", DisplayName = "the form object's, with its parameters")]
    [DataRow("Private Sub UserForm_Click()", DisplayName = "the Microsoft Forms UserForm's")]
    public async Task AHandlerOfTheFormsOwnEvent_IsAHandler(string declaration)
        => Assert.IsEmpty(await UnmatchedHandlersAsync(declaration, "End Sub"));

    [TestMethod]
    public async Task AnUnmatchedHandlerOfTheFormsOwnEvent_IsDead()
        => Assert.AreEqual(RDCoreDiagnosticId.UnmatchedEventHandler,
            Assert.ContainsSingle(await UnmatchedHandlersAsync("Private Sub UserForm_QueryClose(ByVal Cancel As Long)", "End Sub")).Id);

    [TestMethod]
    public async Task ShowingAForm_WithNothingToHostIt_IsARunTimeError_NotAnInternalOne()
    {
        // a platform with no form host: the machine's would show the form, modal, and wait for it.
        var printed = await ModuleWorkspace.RunAsync([], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\nForm1.Show\r\nDebug.Print Err.Number\r\nEnd Sub\r\n",
            References with { Outside = RDCore.External.ExternalWorld.None }, [("Form1", Form(), Designer)]);

        // 429, as MS-VBA says of a class it cannot get an object of: a member the designer declares is the form host's, not looked for in the workspace's code (51).
        Assert.AreEqual("429", Assert.ContainsSingle(printed));
    }

    [TestMethod]
    public async Task LettingAControl_AssignsItsDefaultMember()
        => Assert.IsEmpty(await DeclarationErrorsAsync(Form("Public Sub Reset()", "    TextBox1 = \"a\"", "End Sub"), "Public Sub Main()\r\n    Form1.TextBox1 = \"b\"\r\nEnd Sub"));

    // ---- the language server reads the companion file ----

    [TestMethod]
    public async Task AFormModule_IsLoadedWithItsFrx_AndOpenedWithItToo()
    {
        var root = Path.Combine(Path.GetTempPath(), "rdcore-form-ws");
        var files = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "Form1.frm")] = new(Form()),
            [Path.Combine(root, "Form1.frx")] = new([1, 2, 3]),
            [Path.Combine(root, "Module1.bas")] = new("Attribute VB_Name = \"Module1\"\r\n"),
        });
        var formUri = new Uri(Path.Combine(root, "Form1.frm"));
        var moduleUri = new Uri(Path.Combine(root, "Module1.bas"));
        var states = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        states.Initialize([new TextDocumentIdentifier(formUri), new TextDocumentIdentifier(moduleUri)]);
        var service = new WorkspaceDocumentService(states, NullLogger<WorkspaceDocumentService>.Instance, files.Path, files.File);
        service.Initialize(root);

        Assert.IsTrue(await service.TryLoadAsync(new TextDocumentIdentifier(formUri)));
        Assert.IsTrue(await service.TryLoadAsync(new TextDocumentIdentifier(moduleUri)));

        Assert.IsTrue(service.TryGetDocument(formUri, out var loaded));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, loaded.Companion.ToArray());
        Assert.IsTrue(service.TryGetDocument(new Uri(Path.Combine(root, "Module1.bas")), out var module));
        Assert.IsTrue(module.Companion.IsEmpty, "a module that is no form has no companion");

        var opened = service.Open(formUri, Form("' edited"), version: 7);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, opened.Companion.ToArray(), "no client sends the .frx: the server reads it");
    }

    // ---- the designer travels with the parse ----

    [TestMethod]
    public void TheDesigner_RoundTripsWithTheParse()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///Form1.frm"), Form()) with { Designer = Designer };

        var travelled = PlatformJson.Deserialize<SDK.Model.AST.ModuleParseResult>(PlatformJson.Serialize(parse))!.Designer!;

        CollectionAssert.AreEqual(Designer.DeclaredControls().Select(control => control.Name).ToArray(), travelled.DeclaredControls().Select(control => control.Name).ToArray());
        Assert.AreEqual("Form1", travelled.Properties["caption"], "property names still compare case-insensitively");
    }
}
