using RDCore.External.Interaction;
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

    // a question the server asks while a command's work is drawn: its menu line is not cleared by the bar of work that ends while it is asked, nor written over
    // by a bar of work that goes on. The work is drawn as it is once the question is answered.
    [TestMethod]
    public void TheWorkDoneForACommand_IsNotDrawnIntoAQuestion_AndIsDrawnOnceItIsAnswered()
    {
        var tracker = new WorkProgressTracker();
        var output = new StringWriter();
        var messages = new PromptAwareMessageWriter(NSubstitute.Substitute.For<RDCore.CLI.ConsoleIO.IConsoleMessageWriter>());
        using var display = new ReplProgressDisplay(tracker, output, () => 80, editor: null, messages: messages);
        var (ending, going) = (new Reported(), new Reported());
        tracker.Track("ending", ending);
        ending.Push(new WorkDoneProgressBegin { Title = "Analyzing Program", Percentage = 20 });

        var asked = messages.WhileAsking(() =>
        {
            output.Write("QUESTION 1) Yes 2) No");
            var before = output.ToString();
            ending.Push(new WorkDoneProgressEnd());
            tracker.Track("going", going);
            going.Push(new WorkDoneProgressBegin { Title = "Formatting", Percentage = 40 });
            return output.ToString() == before;
        });

        Assert.IsTrue(asked, "nothing is drawn while the question is asked");
        StringAssert.Contains(output.ToString()[output.ToString().IndexOf("QUESTION", StringComparison.Ordinal)..], "Formatting [", "the work that goes on is drawn once it is answered");
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

    // keys that come when the test sends them: the line is edited until then.
    private sealed class WaitingKeys : IConsoleKeys
    {
        private readonly System.Collections.Concurrent.BlockingCollection<ConsoleKeyInfo> _keys = [];

        public bool IsInteractive => true;

        public void Send(ConsoleKeyInfo key) => _keys.Add(key);

        public ConsoleKeyInfo ReadKey(CancellationToken token) => _keys.Take(token);

        public string? ReadLine(CancellationToken token) => null;
    }

    // the console, which says when it is flushed: the editor flushes it once, at the end of each time it draws the line.
    private sealed class FlushedConsole : StringWriter
    {
        private readonly List<(int Count, TaskCompletionSource Done)> _waiting = [];
        private int _flushes;

        public override void Write(char value)
        {
            lock (_waiting)
            {
                base.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_waiting)
            {
                base.Write(value);
            }
        }

        public override void Flush()
        {
            lock (_waiting)
            {
                _flushes++;
                foreach (var waiting in _waiting.Where(waiting => waiting.Count <= _flushes).ToList())
                {
                    waiting.Done.SetResult();
                    _waiting.Remove(waiting);
                }
            }
        }

        public string Text
        {
            get
            {
                lock (_waiting)
                {
                    return ToString();
                }
            }
        }

        // done once the console is flushed for the nth time.
        public Task FlushedAsync(int nth)
        {
            lock (_waiting)
            {
                if (_flushes >= nth)
                {
                    return Task.CompletedTask;
                }

                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting.Add((nth, done));
                return done.Task;
            }
        }
    }

    // cancelled at the test's timeout: what a test waits for is waited for with it, so that a line never drawn fails the test rather than hangs it.
    public TestContext TestContext { get; set; } = null!;

    // the work ends while the line is edited: the row its bar was drawn on, under the line, is cleared as the line is drawn again, not left on the console.
    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task WorkThatEndsWhileALineIsEdited_HasItsRowUnderTheLineCleared()
    {
        var tracker = new WorkProgressTracker();
        var output = new FlushedConsole();
        var keys = new WaitingKeys();
        var editor = new ConsolePromptEditor(keys, output, () => 80) { CompleteWords = false };
        using var display = new ReplProgressDisplay(tracker, output, () => 80, editor);
        var reported = new Reported();
        tracker.Track("t", reported, WorkProgressOrigin.Server);
        reported.Push(new WorkDoneProgressBegin { Title = "Analyzing the workspace", Message = "Program (1 of 1)", Percentage = 0 });

        var reading = editor.ReadLineAsync(TestContext.CancellationToken);
        await output.FlushedAsync(1).WaitAsync(TestContext.CancellationToken);
        var drawn = output.Text.Length;
        StringAssert.Contains(output.Text, "Analyzing the workspace [", "the bar is drawn under the line");

        reported.Push(new WorkDoneProgressEnd());
        await output.FlushedAsync(2).WaitAsync(TestContext.CancellationToken);
        var redrawn = output.Text[drawn..];

        StringAssert.Contains(redrawn, "\u001b[1B\r\u001b[2K", "the row under the line is cleared");
        Assert.IsFalse(redrawn.Contains("Analyzing the workspace", StringComparison.Ordinal), "the bar is not drawn again");

        keys.Send(ScriptedKeys.Key(ConsoleKey.Enter));
        Assert.AreEqual(string.Empty, await reading);
    }
}
