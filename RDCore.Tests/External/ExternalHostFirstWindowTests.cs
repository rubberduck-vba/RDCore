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
/// runs, the theme of its windows, and its name, before the first form or the first dialog.
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

    /// <summary>
    /// The first window a program opens in the external host.
    /// </summary>
    public enum FirstWindow
    {
        /// <summary>
        /// A <c>MsgBox</c>.
        /// </summary>
        Dialog,

        /// <summary>
        /// A form.
        /// </summary>
        Form,
    }

    // what the external host's forms had been told when the program's first window was made: the theme, whether a program runs (none told), the name.
    private sealed record Told(bool Theme, bool? Held, string Name);

    // the program says what say says to the environment host's client while the external host is not running, then opens its first window there.
    private static Told WhenTheFirstWindowIsMade(FirstWindow window, Action<IFormHost> say)
    {
        var forms = Substitute.For<IFormHost>();
        var dialogs = Substitute.For<IUserInteraction>();
        bool? held = null;
        Told? told = null;
        forms.When(each => each.Hold(Arg.Any<bool>())).Do(call => held = call.Arg<bool>());
        Told Now() => new(forms.KeepSystemTheme, held, forms.ApplicationName);
        forms.Create(Arg.Any<FormDefinition>()).Returns(_ =>
        {
            told = Now();
            return new object();
        });
        dialogs.ShowMessage(Arg.Any<MessageBoxRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            told = Now();
            return VBMsgBoxResult.VBOK;
        });
        using var host = HostOf(forms, dialogs);
        var servers = new RemoteAutomationServer(host);
        say(servers);
        Assert.IsNull(host.Running, "the program says it before the external host runs, which is not started to be told it");

        if (window == FirstWindow.Dialog)
        {
            var answer = new RemoteUserInteraction(servers, NoUserInteraction.Instance)
                .ShowMessage(new MessageBoxRequest("Sure?", "T", VBMsgBoxStyle.VBOKOnly));
            Assert.AreEqual(VBMsgBoxResult.VBOK, answer, "the external host showed the dialog");
        }
        else
        {
            _ = ((IFormHost)servers).Create(Form());
        }

        return told ?? throw new AssertFailedException("the external host made no window");
    }

    [TestMethod]
    [DataRow(FirstWindow.Dialog, DisplayName = "a MsgBox")]
    [DataRow(FirstWindow.Form, DisplayName = "a form")]
    public void AProgramsFirstWindow_IsMadeInTheThemeTheProgramSaid(FirstWindow window)
        => Assert.IsTrue(WhenTheFirstWindowIsMade(window, servers => servers.KeepSystemTheme = true).Theme,
            "the external host's forms keep the system's theme by the time the window is made");

    // made while the program runs: what the window posts as it is made waits, as the program's messages do.
    [TestMethod]
    [DataRow(FirstWindow.Dialog, DisplayName = "a MsgBox")]
    [DataRow(FirstWindow.Form, DisplayName = "a form")]
    public void AProgramsFirstWindow_IsMadeWithTheMessagesHeld_WhileTheProgramRuns(FirstWindow window)
        => Assert.AreEqual(true, WhenTheFirstWindowIsMade(window, servers => servers.Hold(running: true)).Held,
            "the external host holds its forms' messages by the time the window is made");

    // the first window of the external host names the application of its windows (App.Title).
    [TestMethod]
    [DataRow(FirstWindow.Dialog, DisplayName = "a MsgBox")]
    [DataRow(FirstWindow.Form, DisplayName = "a form")]
    public void AProgramsFirstWindow_IsMadeUnderTheProgramsName(FirstWindow window)
        => Assert.AreEqual("Payroll", WhenTheFirstWindowIsMade(window, servers => servers.ApplicationName = "Payroll").Name,
            "the external host's forms have the program's name by the time the window is made");
}
