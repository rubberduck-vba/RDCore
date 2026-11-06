using System.Collections.Concurrent;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A question the language server asks the shell's user (<c>window/showMessageRequest</c>) is asked when the console is the shell's to ask it at
/// (<see cref="ReplServerQuestions"/>): one that comes while a line is typed waits for the line to be entered, and the line keeps its keys; one that comes at a
/// prompt where nothing is typed is asked at once, and the prompt is drawn again after it.
/// </summary>
[TestClass]
public sealed class ReplServerQuestionsTests
{
    // cancelled at the test's timeout: what a test waits for is waited for with it, so that a question never asked fails the test rather than hangs it.
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    // keys that come when the test sends them, so that a question can come while the line is edited. A read of the nth key says that every key before it
    // was handled: the reader asks for the next key only once it is done with the last.
    private sealed class WaitingKeys : IConsoleKeys
    {
        private readonly BlockingCollection<ConsoleKeyInfo> _keys = [];
        private readonly List<(int Count, TaskCompletionSource Read)> _waiting = [];
        private int _reads;

        public bool IsInteractive => true;

        public void Send(params ConsoleKeyInfo[] keys)
        {
            foreach (var key in keys)
            {
                _keys.Add(key);
            }
        }

        // completes when the reader asks for its nth key.
        public Task ReadingAsync(int nth)
        {
            lock (_waiting)
            {
                if (_reads >= nth)
                {
                    return Task.CompletedTask;
                }

                var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting.Add((nth, read));
                return read.Task;
            }
        }

        public ConsoleKeyInfo ReadKey(CancellationToken token)
        {
            lock (_waiting)
            {
                _reads++;
                foreach (var waiting in _waiting.Where(waiting => waiting.Count <= _reads).ToList())
                {
                    waiting.Read.SetResult();
                    _waiting.Remove(waiting);
                }
            }

            return _keys.Take(token);
        }

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

    // the prompt, and the questions it asks: the shell has read its first line, so a question waits to be asked from now on.
    private static (WaitingKeys Keys, Console Output, ReplServerQuestions Questions, ConsolePromptEditor Editor) Prompt()
    {
        var keys = new WaitingKeys();
        var output = new Console();
        var questions = new ReplServerQuestions(keys);
        var editor = new ConsolePromptEditor(keys, output, () => 80) { Interjections = questions };
        questions.Editor = editor;
        questions.AskWaiting(keys);
        return (keys, output, questions, editor);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AQuestionThatComesWhileALineIsTyped_IsAskedAfterTheLineIsEntered_AndTheLineKeepsItsKeys()
    {
        var (keys, output, questions, editor) = Prompt();
        var reading = editor.ReadLineAsync(Token);
        keys.Send(Char('a'), Char('b'));
        // the line asks for its third key: "a" and "b" are typed.
        await keys.ReadingAsync(3).WaitAsync(Token);
        StringAssert.Contains(output.Text, "ab");

        var read = new List<char>();
        var answer = questions.AskAsync((at, token) =>
        {
            output.Write("QUESTION\n");
            read.Add(at.ReadKey(token).KeyChar);
            return 1;
        }, Token);
        keys.Send(Char('c'), Key(ConsoleKey.Enter));

        Assert.AreEqual("abc", await reading.WaitAsync(Token), "every key typed is the line's");
        Assert.IsFalse(answer.IsCompleted, "not asked while the line was typed");
        Assert.IsEmpty(read);

        // the shell, before it draws its next prompt.
        keys.Send(Char('y'));
        questions.AskWaiting(keys);

        Assert.AreEqual(1, await answer.WaitAsync(Token));
        CollectionAssert.AreEqual(new[] { 'y' }, read);
        Assert.IsGreaterThan(output.Text.LastIndexOf("abc", StringComparison.Ordinal), output.Text.IndexOf("QUESTION", StringComparison.Ordinal), "asked after the line");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AQuestionThatComesAtAPromptWhereNothingIsTyped_IsAskedAtOnce_AndThePromptIsDrawnAgain()
    {
        var (keys, output, questions, editor) = Prompt();
        var reading = editor.ReadLineAsync(Token);

        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = questions.AskAsync((at, token) =>
        {
            output.Write("QUESTION\n");
            asking.SetResult();
            return at.ReadKey(token).KeyChar == 'y' ? 0 : 1;
        }, Token);

        // the key is pressed once the question is asked: it is the question's, though the prompt was the one waiting for it.
        await asking.Task.WaitAsync(Token);
        keys.Send(Char('y'));
        Assert.AreEqual(0, await answer.WaitAsync(Token));

        keys.Send(Char('z'), Key(ConsoleKey.Enter));
        Assert.AreEqual("z", await reading.WaitAsync(Token), "the key that answered the question is not the line's");

        var text = output.Text;
        var asked = text.IndexOf("QUESTION", StringComparison.Ordinal);
        StringAssert.EndsWith(text[..asked], "\r\u001b[J", "the prompt's line, and what is under it, are erased before the question is asked");
        StringAssert.Contains(text[asked..], "\rz", "the prompt is drawn again under the question");
    }

    [TestMethod]
    public async Task AQuestionBeforeTheShellReadsItsFirstLine_IsAskedAsItComes()
    {
        var keys = new WaitingKeys();
        keys.Send(Char('y'));
        var questions = new ReplServerQuestions(keys);

        Assert.AreEqual(0, await questions.AskAsync((at, token) => at.ReadKey(token).KeyChar == 'y' ? 0 : 1, CancellationToken.None));
    }

    [TestMethod]
    public async Task AQuestionTheShellNeverGotToAsk_IsAnsweredWithNoChoice_WhenTheShellEnds()
    {
        var keys = new WaitingKeys();
        var questions = new ReplServerQuestions(keys);
        questions.AskWaiting(keys);
        var answer = questions.AskAsync((_, _) => 0, CancellationToken.None);

        questions.Close();

        Assert.IsNull(await answer);
        Assert.IsNull(await questions.AskAsync((_, _) => 0, CancellationToken.None), "and nothing more is asked");
    }
}
