using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.External.Automation;
using RDCore.External.Client;
using RDCore.External.Forms;
using RDCore.External.Hosting;
using RDCore.External.Interaction;
using RDCore.SDK.Platform.Channels;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.IO.Pipes;

namespace RDCore.Tests.External;

/// <summary>
/// What an external host is told before it makes a program's first window, the program having said it while the host was not running: whether the program
/// runs, and the theme of its windows, before the first form or the first dialog.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class ExternalHostFirstWindowTests
{
    // an external host in the test's own process: the external host's service, on a pipe of its own, with the forms and dialogs the test gives it.
    private sealed class InProcessExternalHost(IFormHost forms, IUserInteraction dialogs) : IExternalHostProcess
    {
        private CallChannel? _served;

        public int ProcessId => Environment.ProcessId;

        public Stream Start(Action onExited)
        {
            var name = $"RDCore.Tests.{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            // the server's pipe is there once it is made, so the client connects to it before the server takes the connection.
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            client.Connect(TimeSpan.FromSeconds(30));
            server.WaitForConnection();
            _served = new CallChannel(server);
            new ExternalAutomationService(UnavailableAutomationServer.Instance, forms, dialogs).ServeOn(_served);
            _served.Start();
            return client;
        }

        public void Stop(TimeSpan timeout) => _served?.Dispose();

        public void Dispose() => _served?.Dispose();
    }

    private static ExternalHost HostOf(IFormHost forms, IUserInteraction dialogs)
        => new(() => new InProcessExternalHost(forms, dialogs), NullLogger<ExternalHost>.Instance);

    private static FormDefinition Form() => new("Form1", ReadOnlyMemory<byte>.Empty);

    // a program whose first window is a MsgBox: the dialog is shown in the theme the program said, as a form is made in it.
    [TestMethod]
    public void AProgramsFirstWindow_ADialog_IsShownInTheThemeTheProgramSaid()
    {
        var forms = Substitute.For<IFormHost>();
        var dialogs = Substitute.For<IUserInteraction>();
        bool? themeWhenShown = null;
        dialogs.ShowMessage(Arg.Any<MessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            themeWhenShown = forms.KeepSystemTheme;
            return VBMsgBoxResult.VBOK;
        });
        using var host = HostOf(forms, dialogs);
        var servers = new RemoteAutomationServer(host);
        ((IFormHost)servers).KeepSystemTheme = true;
        Assert.IsNull(host.Running, "the theme is said before the external host runs, which is not started to be told it");

        var answer = new RemoteUserInteraction(servers, NoUserInteraction.Instance).ShowMessage(new MessageBoxRequest("Sure?", "T", VBMsgBoxStyle.VBOKOnly));

        Assert.AreEqual(VBMsgBoxResult.VBOK, answer, "the external host showed the dialog");
        Assert.IsTrue(themeWhenShown, "the external host's forms keep the system's theme by the time the dialog is shown");
    }

    // a program whose first window is a form: the form is made in the theme the program said.
    [TestMethod]
    public void AProgramsFirstWindow_AForm_IsMadeInTheThemeTheProgramSaid()
    {
        var forms = Substitute.For<IFormHost>();
        bool? themeWhenMade = null;
        forms.Create(Arg.Any<FormDefinition>()).Returns(_ =>
        {
            themeWhenMade = forms.KeepSystemTheme;
            return new object();
        });
        using var host = HostOf(forms, NoUserInteraction.Instance);
        IFormHost servers = new RemoteAutomationServer(host);
        servers.KeepSystemTheme = true;
        Assert.IsNull(host.Running, "the theme is said before the external host runs, which is not started to be told it");

        _ = servers.Create(Form());

        Assert.IsTrue(themeWhenMade, "the external host's forms keep the system's theme by the time the form is made");
    }

    // a program whose first window is a form, made while it runs: what the form's window posts as it is made waits, as the program's messages do.
    [TestMethod]
    public void AProgramsFirstWindow_AForm_IsMadeWithTheMessagesHeld_WhileTheProgramRuns()
    {
        var forms = Substitute.For<IFormHost>();
        bool? held = null;
        bool? heldWhenMade = null;
        forms.When(each => each.Hold(Arg.Any<bool>())).Do(call => held = call.Arg<bool>());
        forms.Create(Arg.Any<FormDefinition>()).Returns(_ =>
        {
            heldWhenMade = held;
            return new object();
        });
        using var host = HostOf(forms, NoUserInteraction.Instance);
        IFormHost servers = new RemoteAutomationServer(host);
        servers.Hold(running: true);
        Assert.IsNull(host.Running, "the program runs before the external host does, which is not started to be told it");

        _ = servers.Create(Form());

        Assert.IsTrue(heldWhenMade, "the external host holds its forms' messages by the time the form is made");
    }
}
