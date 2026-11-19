using RDCore.External.Interaction;
using System.Collections.Concurrent;
using RDCore.CLI.App.Console;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A message written while a line is edited at the prompt goes above the line, and the line is drawn again under it (<see cref="ConsolePromptEditor.WriteAbove"/>):
/// a message the shell logged while the workspace loaded landed in what was typed, and the progress bar drawn under the line stayed on the console.
/// </summary>
[TestClass]
public sealed class ConsolePromptWriteAboveTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    // keys that come when the test sends them, so that something can happen while the line is edited.
    private sealed class WaitingKeys : IConsoleKeys
    {
        private readonly BlockingCollection<ConsoleKeyInfo> _keys = [];

        public bool IsInteractive => true;

        public void Send(params ConsoleKeyInfo[] keys)
        {
            foreach (var key in keys)
            {
                _keys.Add(key);
            }
        }

        public ConsoleKeyInfo ReadKey(CancellationToken token) => _keys.Take(token);

        public string? ReadLine(CancellationToken token) => null;
    }

    // a writer that more than one thread writes to, as the console is, and that says when it is written to.
    private sealed class Console : StringWriter
    {
        private TaskCompletionSource _wrote = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Write(char value) => Written(() => base.Write(value));

        public override void Write(string? value) => Written(() => base.Write(value));

        public override void Write(char[] buffer, int index, int count) => Written(() => base.Write(buffer, index, count));

        public override void Write(ReadOnlySpan<char> buffer)
        {
            var text = buffer.ToString();
            Written(() => base.Write(text));
        }

        public string Text
        {
            get
            {
                lock (this)
                {
                    return ToString();
                }
            }
        }

        // completed by the next write.
        public Task NextWrite
        {
            get
            {
                lock (this)
                {
                    return _wrote.Task;
                }
            }
        }

        private void Written(Action write)
        {
            TaskCompletionSource wrote;
            lock (this)
            {
                write();
                (wrote, _wrote) = (_wrote, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            wrote.TrySetResult();
        }
    }

    // the cap only keeps a write that never comes from hanging the run: the condition is looked at again each time the console is written to.
    private static async Task WaitUntilAsync(Console output, Func<bool> condition)
    {
        while (true)
        {
            var next = output.NextWrite;
            if (condition())
            {
                return;
            }

            await next.WaitAsync(SafetyCap);
        }
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AMessageWrittenWhileALineIsEdited_GoesAboveIt_AndTheLineIsDrawnAgainUnderIt()
    {
        var keys = new WaitingKeys();
        var output = new Console();
        var editor = new ConsolePromptEditor(keys, output, () => 80) { Status = _ => ["[busy]"] };

        var reading = editor.ReadLineAsync(CancellationToken.None);
        keys.Send(Char('a'), Char('b'));
        await WaitUntilAsync(output, () => output.Text.Contains("ab") && output.Text.Contains("[busy]"));

        // the work is done, and the shell logs something as it is.
        editor.Status = _ => [];
        editor.WriteAbove(() => output.Write("LOGGED\n"));
        await WaitUntilAsync(output, () => output.Text.LastIndexOf("ab", StringComparison.Ordinal) > output.Text.IndexOf("LOGGED", StringComparison.Ordinal));

        var text = output.Text;
        var logged = text.IndexOf("LOGGED", StringComparison.Ordinal);
        StringAssert.EndsWith(text[..logged], "\r\u001b[J", "the line and what is under it are erased before the message is written");
        Assert.IsFalse(text[logged..].Contains("[busy]", StringComparison.Ordinal), "the progress row isn't drawn again once its work is done");

        keys.Send(Char('c'), Key(ConsoleKey.Enter));
        Assert.AreEqual("abc", await reading, "what is typed is the line's, the message isn't");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AKeyPressedWhileAMessageIsWritten_IsDrawnOnRowsMadeAgainUnderTheLine()
    {
        var keys = new WaitingKeys();
        var output = new Console();
        var editor = new ConsolePromptEditor(keys, output, () => 80) { Status = _ => ["[busy]"] };

        var reading = editor.ReadLineAsync(CancellationToken.None);
        keys.Send(Char('a'), Char('b'));
        await WaitUntilAsync(output, () => output.Text.Contains("ab") && output.Text.Contains("[busy]"));

        // the key comes while the message is written: the line may take it before it is told it was erased, and draw it first. Whichever comes first,
        // the draw that first shows the key makes the erased row under the line again, rather than moving the cursor down to where it was: the row is
        // made before the line is drawn with the key a second time, if it is.
        editor.WriteAbove(() =>
        {
            output.Write("LOGGED\n");
            keys.Send(Char('c'));
        });
        string After() => output.Text[output.Text.IndexOf("LOGGED", StringComparison.Ordinal)..];
        await WaitUntilAsync(output, () => After() is var after && after.Contains("\rabc", StringComparison.Ordinal) && after.Contains("\n\u001b[1A", StringComparison.Ordinal));

        var text = After();
        var again = text.IndexOf("\rabc", text.IndexOf("\rabc", StringComparison.Ordinal) + 1, StringComparison.Ordinal);
        Assert.IsLessThan(again < 0 ? int.MaxValue : again, text.IndexOf("\n\u001b[1A", StringComparison.Ordinal),
            "the erased row under the line is made by the draw that first shows the key");

        keys.Send(Key(ConsoleKey.Enter));
        Assert.AreEqual("abc", await reading);
    }

    [TestMethod]
    public void AMessageWrittenWhileNoLineIsEdited_IsWrittenAsItComes()
    {
        var output = new Console();
        var editor = new ConsolePromptEditor(new WaitingKeys(), output, () => 80);

        editor.WriteAbove(() => output.Write("LOGGED\n"));

        Assert.AreEqual("LOGGED\n", output.Text);
    }
}
