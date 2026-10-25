using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.SDK.Client.Progress;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The progress of the language server's work, as the shell draws it: a bar of how much is done, on the line a command's output starts on while the command
/// waits for it, and under the prompt's line while a line is edited.
/// </summary>
[TestClass]
public sealed class ReplProgressTests
{
    [TestMethod]
    public void ABar_IsTheTitle_HowMuchIsDone_AndWhatTheWorkIsDoingNow()
    {
        var line = ConsoleProgressBar.Render(new WorkProgressState("t", "Analyzing Program", "Diagnostics: 1 of 2", 50), 100);

        Assert.AreEqual($"Analyzing Program [{new string('█', 12)}{new string('░', 12)}]  50% Diagnostics: 1 of 2", line);
    }

    [TestMethod]
    public void WorkThatDoesNotSayHowMuchIsDone_HasASegmentThatMovesAlongTheBar_AndNoPercentage()
    {
        var state = new WorkProgressState("t", "Indexing", null, null);
        var (first, next) = (ConsoleProgressBar.Render(state, 100, frame: 0), ConsoleProgressBar.Render(state, 100, frame: 1));

        Assert.AreNotEqual(first, next);
        Assert.IsFalse(first.Contains('%'));
        Assert.AreEqual(6, first.Count(cell => cell == '█'));
    }

    [TestMethod]
    public void ABar_FitsTheConsole_AndIsDrawnInAsciiWhereItMustBe()
    {
        var line = ConsoleProgressBar.Render(new WorkProgressState("t", "Composing the project", "Defining a module with a very long name indeed (3 of 39)", 10), 60, unicode: false);

        Assert.IsTrue(ConsoleText.WidthOf(line) < 60);
        StringAssert.Contains(line, "[##");
    }

    // what the server reports to a token, pushed by the test.
    private sealed class Reported : IObservable<WorkDoneProgress>
    {
        private IObserver<WorkDoneProgress>? _observer;

        public IDisposable Subscribe(IObserver<WorkDoneProgress> observer)
        {
            _observer = observer;
            return new Unsubscribed();
        }

        public void Push(WorkDoneProgress progress) => _observer?.OnNext(progress);

        private sealed class Unsubscribed : IDisposable
        {
            public void Dispose() { }
        }
    }

    [TestMethod]
    public void TheWorkDoneForACommand_IsDrawnWhereItsOutputStarts_AndTheLineIsClearedWhenItIsDone()
    {
        var tracker = new WorkProgressTracker();
        var output = new StringWriter();
        using var display = new ReplProgressDisplay(tracker, output, () => 80, editor: null);
        var reported = new Reported();
        tracker.Track("t", reported);

        reported.Push(new WorkDoneProgressBegin { Title = "Analyzing Program", Percentage = 20 });
        var drawn = output.ToString();
        reported.Push(new WorkDoneProgressEnd());

        StringAssert.Contains(drawn, "Analyzing Program [");
        StringAssert.EndsWith(output.ToString(), "\r\u001b[2K", "the line is the output's again");
    }

    [TestMethod]
    public void TheWorkTheServerDoesOfItsOwn_IsNotDrawnAmongACommandsOutput()
    {
        var tracker = new WorkProgressTracker();
        var output = new StringWriter();
        using var display = new ReplProgressDisplay(tracker, output, () => 80, editor: null);
        var reported = new Reported();
        tracker.Track("t", reported, WorkProgressOrigin.Server);

        reported.Push(new WorkDoneProgressBegin { Title = "Parsing the workspace", Percentage = 20 });

        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(1, display.Rows(80).Count, "it is drawn under the prompt's line instead");
    }

    [TestMethod]
    public async Task WhileALineIsEdited_TheWorkIsDrawnUnderIt()
    {
        var tracker = new WorkProgressTracker();
        var output = new StringWriter();
        var keys = new ScriptedKeys();
        var editor = new ConsolePromptEditor(keys, output, () => 80) { CompleteWords = false };
        using var display = new ReplProgressDisplay(tracker, output, () => 80, editor);
        var reported = new Reported();
        tracker.Track("t", reported, WorkProgressOrigin.Server);
        reported.Push(new WorkDoneProgressBegin { Title = "Parsing the workspace", Message = "Module1 (1 of 2)", Percentage = 0 });

        keys.Type("x").Press(ScriptedKeys.Key(ConsoleKey.Enter));
        var line = await editor.ReadLineAsync(CancellationToken.None);

        Assert.AreEqual("x", line);
        StringAssert.Contains(output.ToString(), "Parsing the workspace [");
        StringAssert.Contains(output.ToString(), "Module1 (1 of 2)");
    }
}
