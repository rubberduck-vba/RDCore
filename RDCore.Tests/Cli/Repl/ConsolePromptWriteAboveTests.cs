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

    // a writer that more than one thread writes to, as the console is.
    private sealed class Console : StringWriter
    {
        public override void Write(char value)
        {
            lock (this)
            {
                base.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (this)
            {
                base.Write(value);
            }
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
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AMessageWrittenWhileALineIsEdited_GoesAboveIt_AndTheLineIsDrawnAgainUnderIt()
    {
        var keys = new WaitingKeys();
        var output = new Console();
        var editor = new ConsolePromptEditor(keys, output, () => 80) { Status = _ => ["[busy]"] };

        var reading = editor.ReadLineAsync(CancellationToken.None);
        keys.Send(Char('a'), Char('b'));
        await WaitUntilAsync(() => output.Text.Contains("ab") && output.Text.Contains("[busy]"));

        // the work is done, and the shell logs something as it is.
        editor.Status = _ => [];
        editor.WriteAbove(() => output.Write("LOGGED\n"));
        await WaitUntilAsync(() => output.Text.LastIndexOf("ab", StringComparison.Ordinal) > output.Text.IndexOf("LOGGED", StringComparison.Ordinal));

        var text = output.Text;
        var logged = text.IndexOf("LOGGED", StringComparison.Ordinal);
        StringAssert.EndsWith(text[..logged], "\r\u001b[J", "the line and what is under it are erased before the message is written");
        Assert.IsFalse(text[logged..].Contains("[busy]", StringComparison.Ordinal), "the progress row isn't drawn again once its work is done");

        keys.Send(Char('c'), Key(ConsoleKey.Enter));
        Assert.AreEqual("abc", await reading, "what is typed is the line's, the message isn't");
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
