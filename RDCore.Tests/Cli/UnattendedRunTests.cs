using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.Runtime.Support.Host;
using RDCore.External.Forms;
using RDCore.LanguageServer.Runtime;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.External;
using RDCore.Tests.Runtime.Libraries;
using System.Text;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program run unattended (<c>rdc --unattended</c>, a client that expects <c>Unattended</c>): nobody is there to answer a dialog, and one the program opens
/// fails the run with <c>VBR50001</c>, which no error handler of the program catches.
/// </summary>
[TestClass]
public sealed class UnattendedRunTests
{
    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static void FailedWithNobodyToAnswer(ExecuteResult result)
    {
        Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
        Assert.AreEqual(("VBR50001", 50001), (result.ErrorCode, result.ErrorNumber), result.ErrorMessage);
        Assert.DoesNotContain("after", result.Output.Select(line => line.Trim()));
    }

    [TestMethod]
    [DataRow("MsgBox \"Done\"", DisplayName = "MsgBox")]
    [DataRow("Dim answer As VbMsgBoxResult: answer = MsgBox(\"Sure?\", vbYesNo)", DisplayName = "MsgBox, its answer read")]
    [DataRow("Dim name As String: name = InputBox(\"Name?\")", DisplayName = "InputBox")]
    public async Task ADialog_FailsTheRun(string dialog)
    {
        var result = await ModuleWorkspace.RunUnattendedAsync([], Program(dialog, "Debug.Print \"after\""));

        FailedWithNobodyToAnswer(result);
    }

    [TestMethod]
    [DataRow("On Error Resume Next", DisplayName = "On Error Resume Next")]
    [DataRow("On Error GoTo Handled", DisplayName = "On Error GoTo")]
    public async Task ADialog_FailsTheRun_WhateverErrorHandlerTheProgramHas(string handler)
    {
        var result = await ModuleWorkspace.RunUnattendedAsync([], Program(
            handler,
            "MsgBox \"Done\"",
            "Debug.Print \"after\"",
            "Exit Sub",
            "Handled:",
            "Debug.Print \"handled \" & Err.Number"));

        FailedWithNobodyToAnswer(result);
        Assert.IsFalse(result.Output.Any(line => line.Contains("handled", StringComparison.Ordinal)), string.Join(" | ", result.Output));
    }

    // the family follows the raiser: the program's own error 50001 is VBA50001, an error of the program's, which its handler catches as any other.
    [TestMethod]
    public async Task AProgramsOwnError50001_IsItsOwn_AndCaught()
    {
        var result = await ModuleWorkspace.RunUnattendedAsync([], Program(
            "On Error Resume Next",
            "Err.Raise 50001",
            "Debug.Print \"caught \" & Err.Number"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "caught 50001" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    // the language server asks its client to answer a dialog only when the client answers them and doesn't run unattended, which wins over answering them.
    [TestMethod]
    [DataRow(true, false, true, DisplayName = "answers dialogs")]
    [DataRow(true, true, false, DisplayName = "answers dialogs, and runs unattended")]
    [DataRow(false, true, false, DisplayName = "runs unattended")]
    [DataRow(false, false, false, DisplayName = "neither")]
    public void TheLanguageServer_AsksItsClient_OnlyWhenItAnswersDialogs_AndDoesNotRunUnattended(bool answers, bool unattended, bool asked)
    {
        var expected = new PlatformClientCapabilitiesService();
        expected.Record(new PlatformInitializeParams
        {
            ExpectedComponent = CoreServerComponent.LanguageServer,
            Expected = new CorePlatformClientCapabilities
            {
                LanguageServer = new LanguageServerCapabilities { SessionInteraction = new SessionInteraction(answers), Unattended = new Unattended(unattended) },
            },
        });
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IPlatformClientCapabilitiesService)).Returns(expected);
        var server = Substitute.For<ILanguageServer>();
        server.Services.Returns(services);

        Assert.AreEqual(asked, new LanguageServerInteractionClient(server).ShowsDialogs);
    }

    // Microsoft Forms 2.0 as the platform ships it (`rdc export-library` of FM20.DLL).
    internal static DirectoryLibrarySource Shipped => new(new System.IO.Abstractions.FileSystem(), ShippedLibrariesTests.Symbols);

    internal static byte[] Fixture(string fileName)
    {
        var assembly = typeof(UnattendedRunTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith($".Fixtures.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // MVVM's ExampleView - its designer and the attributes of the module - with no code behind it but `code`. The file is read with the VBE's CRLF whatever the
    // checkout made of it: git stores it with LF, and checks it out with LF on Linux, where a header split at CRLF has no attributes - no VB_PredeclaredId, no
    // ExampleView.
    internal static (string Name, string Source, FormDescriptor Designer) ExampleView(string code = "")
    {
        var frm = Encoding.UTF8.GetString(Fixture("ExampleView.frm")).ReplaceLineEndings("\r\n");
        var lines = frm.Split("\r\n");
        var header = string.Join("\r\n", lines.Take(Array.FindLastIndex(lines, line => line.StartsWith("Attribute VB_", StringComparison.Ordinal)) + 1));
        return ("ExampleView", header + "\r\nOption Explicit\r\n" + code, FormDesigner.Read(frm, Fixture("ExampleView.frx"))!);
    }

    // forms nobody is to see: the run fails before any of them is made.
    private sealed class NoFormMade : IFormHost
    {
        public List<string> Asked { get; } = [];

        public bool IsAvailable => true;

        public object Create(FormDefinition form) => Ask<object>(nameof(Create));

        public void Show(object form, bool modal, object? owner = null) => Ask<object>(nameof(Show));

        public void Hide(object form) => Ask<object>(nameof(Hide));

        public bool IsShown(object form) => Ask<bool>(nameof(IsShown));

        public string GetCaption(object form) => Ask<string>(nameof(GetCaption));

        public void SetCaption(object form, string caption) => Ask<object>(nameof(SetCaption));

        public FormBounds GetBounds(object form) => Ask<FormBounds>(nameof(GetBounds));

        public void SetBounds(object form, FormBounds bounds) => Ask<object>(nameof(SetBounds));

        public void Leave(object form) => Ask<object>(nameof(Leave));

        public void Destroy(object form) => Ask<object>(nameof(Destroy));

        public void Hold(bool running)
        {
        }

        public void DoEvents()
        {
        }

        private T Ask<T>(string member)
        {
            Asked.Add(member);
            throw new InvalidOperationException($"The form host was asked to {member} a form of a program run unattended.");
        }
    }

    [TestMethod]
    [DataRow("ExampleView.Show", DisplayName = "Show, modal")]
    [DataRow("ExampleView.Show vbModal", DisplayName = "Show vbModal")]
    [DataRow("ExampleView.Show vbModeless", DisplayName = "Show vbModeless")]
    [DataRow("Dim view As Object: Set view = ExampleView: view.Show vbModeless", DisplayName = "Show vbModeless, late bound")]
    public async Task AFormShown_FailsTheRun_BeforeAnyFormIsMade(string shown)
    {
        var forms = new NoFormMade();
        var world = MachineExternal.World with { Forms = forms };

        var result = await ModuleWorkspace.RunUnattendedAsync([], Program(shown, "Debug.Print \"after\""),
            new ModuleWorkspace.WorkspaceLibraries(["MSForms"], Shipped, Outside: world), [ExampleView()]);

        FailedWithNobodyToAnswer(result);
        Assert.IsEmpty(forms.Asked, string.Join(", ", forms.Asked));
    }

    // the forms of a program that an external host hosts (rdc's automation host): the run fails before the host is asked for one.
    [TestMethod]
    public async Task AFormOfTheExternalHost_FailsTheRun_BeforeTheHostIsAsked()
    {
        using var host = ExternalHosts.New();
        var forms = new NoFormMade();
        var world = ExternalHosts.WorldOf(host, _ => forms);

        var result = await ModuleWorkspace.RunUnattendedAsync([], Program("ExampleView.Show vbModeless", "Debug.Print \"after\""),
            new ModuleWorkspace.WorkspaceLibraries(["MSForms"], Shipped, Outside: world), [ExampleView()]);

        FailedWithNobodyToAnswer(result);
        Assert.IsEmpty(forms.Asked, string.Join(", ", forms.Asked));
    }
}
