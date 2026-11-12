using RDCore.External.Interaction;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.ConsoleIO;
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
        private ConsoleKeyInfo? _typedAhead;

        public bool IsInteractive => true;

        public void Send(params ConsoleKeyInfo[] keys)
        {
            foreach (var key in keys)
            {
                _keys.Add(key);
            }
        }

        // a key typed that the read waiting for one has not seen yet: the read finds it as it is stopped, as the console's does (SystemConsoleKeys).
        public void TypeAhead(ConsoleKeyInfo key)
        {
            lock (_waiting)
            {
                _typedAhead = key;
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

            try
            {
                return _keys.Take(token);
            }
            catch (OperationCanceledException) when (TakeTypedAhead() is { } typed)
            {
                return typed;
            }
        }

        private ConsoleKeyInfo? TakeTypedAhead()
        {
            lock (_waiting)
            {
                var typed = _typedAhead;
                _typedAhead = null;
                return typed;
            }
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

    // the prompt, and the questions it asks: the shell has read its first line, so a question waits to be asked from now on. The shell's messages are written
    // above its line, and a slogan writes "MESSAGE".
    private static (WaitingKeys Keys, Console Output, ReplServerQuestions Questions, ConsolePromptEditor Editor, PromptAwareMessageWriter Messages) Prompt()
    {
        var keys = new WaitingKeys();
        var output = new Console();
        var inner = Substitute.For<IConsoleMessageWriter>();
        inner.WriteSlogan().Returns(_ =>
        {
            output.Write("MESSAGE\n");
            return inner;
        });

        var messages = new PromptAwareMessageWriter(inner);
        var questions = new ReplServerQuestions(keys, messages);
        var editor = new ConsolePromptEditor(keys, output, () => 80) { Interjections = questions };
        messages.Editor = editor;
        questions.AskWaiting(keys);
        return (keys, output, questions, editor, messages);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AQuestionThatComesWhileALineIsTyped_IsAskedAfterTheLineIsEntered_AndTheLineKeepsItsKeys()
    {
        var (keys, output, questions, editor, _) = Prompt();
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
        var (keys, output, questions, editor, _) = Prompt();
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
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AKeyTypedBeforeAQuestionIsAskedAtAPromptWhereNothingIsTyped_IsTheLines_AndTheQuestionWaitsForTheLine()
    {
        var (keys, output, questions, editor, _) = Prompt();
        var reading = editor.ReadLineAsync(Token);

        // "x" is typed as the question comes: the prompt that stops reading its key to ask it finds it.
        keys.TypeAhead(Char('x'));
        var read = new List<char>();
        var answer = questions.AskAsync((at, token) =>
        {
            output.Write("QUESTION\n");
            read.Add(at.ReadKey(token).KeyChar);
            return 1;
        }, Token);

        // the line asks for its next key once it has "x" - with it, the line is no longer one where nothing is typed.
        await keys.ReadingAsync(2).WaitAsync(Token);
        keys.Send(Key(ConsoleKey.Enter));
        Assert.AreEqual("x", await reading.WaitAsync(Token), "the key typed before the question was asked is the line's");
        Assert.IsFalse(answer.IsCompleted, "the question waits for the line");
        StringAssert.DoesNotMatch(output.Text, new System.Text.RegularExpressions.Regex("QUESTION"));

        keys.Send(Char('y'));
        questions.AskWaiting(keys);
        Assert.AreEqual(1, await answer.WaitAsync(Token));
        CollectionAssert.AreEqual(new[] { 'y' }, read, "the question reads the keys typed once it is asked");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AQuestionWithdrawnWhileItIsAsked_LeavesTheNextKeyToTheLine()
    {
        var (keys, _, questions, editor, _) = Prompt();
        var reading = editor.ReadLineAsync(Token);

        using var withdrawal = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = questions.AskAsync((at, token) =>
        {
            asking.SetResult();
            return at.ReadKey(token).KeyChar == 'y' ? 0 : 1;
        }, withdrawal.Token);

        // the server cancels its request ($/cancelRequest) while the question waits for its key.
        await asking.Task.WaitAsync(Token);
        await withdrawal.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => answer);

        // the prompt's first read stopped for the question, which read the second: the third is the line's again.
        await keys.ReadingAsync(3).WaitAsync(Token);
        keys.Send(Char('z'), Key(ConsoleKey.Enter));
        Assert.AreEqual("z", await reading.WaitAsync(Token));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AQuestionWithdrawnWhileItWaits_IsNotAsked()
    {
        var (keys, _, questions, editor, _) = Prompt();
        var reading = editor.ReadLineAsync(Token);
        keys.Send(Char('a'));
        await keys.ReadingAsync(2).WaitAsync(Token);

        using var withdrawal = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var asked = false;
        var answer = questions.AskAsync((_, _) =>
        {
            asked = true;
            return 0;
        }, withdrawal.Token);

        await withdrawal.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => answer);

        keys.Send(Key(ConsoleKey.Enter));
        Assert.AreEqual("a", await reading.WaitAsync(Token));
        questions.AskWaiting(keys);
        Assert.IsFalse(asked);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task TwoQuestionsThatWait_AreAskedInTheOrderTheyCame()
    {
        var (keys, _, questions, editor, _) = Prompt();
        var reading = editor.ReadLineAsync(Token);
        keys.Send(Char('a'));
        await keys.ReadingAsync(2).WaitAsync(Token);

        var asked = new List<(string Question, char Key)>();
        var first = questions.AskAsync((at, token) =>
        {
            asked.Add(("first", at.ReadKey(token).KeyChar));
            return 0;
        }, Token);
        var second = questions.AskAsync((at, token) =>
        {
            asked.Add(("second", at.ReadKey(token).KeyChar));
            return 1;
        }, Token);

        keys.Send(Key(ConsoleKey.Enter));
        Assert.AreEqual("a", await reading.WaitAsync(Token));

        keys.Send(Char('1'), Char('2'));
        questions.AskWaiting(keys);

        Assert.AreEqual(0, await first.WaitAsync(Token));
        Assert.AreEqual(1, await second.WaitAsync(Token));
        CollectionAssert.AreEqual(new[] { ("first", '1'), ("second", '2') }, asked);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AMessageThatComesWhileAQuestionIsAsked_IsWrittenAfterTheAnswer_AndItsWriterDoesNotWaitForIt()
    {
        var (keys, output, questions, editor, messages) = Prompt();
        var reading = editor.ReadLineAsync(Token);

        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = questions.AskAsync((at, token) =>
        {
            output.Write("QUESTION\n");
            asking.SetResult();
            return at.ReadKey(token).KeyChar == 'y' ? 0 : 1;
        }, Token);
        await asking.Task.WaitAsync(Token);

        // the shell logs something while the user has not answered yet.
        await Task.Run(() => messages.WriteSlogan(), Token).WaitAsync(Token);
        Assert.IsFalse(answer.IsCompleted);
        StringAssert.DoesNotMatch(output.Text, new System.Text.RegularExpressions.Regex("MESSAGE"), "not written into the question");

        keys.Send(Char('y'));
        Assert.AreEqual(0, await answer.WaitAsync(Token));
        var text = output.Text;
        Assert.IsGreaterThan(text.IndexOf("QUESTION", StringComparison.Ordinal), text.IndexOf("MESSAGE", StringComparison.Ordinal), "written once it is answered");

        keys.Send(Key(ConsoleKey.Enter));
        Assert.AreEqual(string.Empty, await reading.WaitAsync(Token));
    }

    // the server waits for the answer to answer initialize: nothing else reads the keys yet.
    [TestMethod]
    public async Task AQuestionBeforeInitializeIsAnswered_IsAskedAsItComes()
    {
        var keys = new WaitingKeys();
        keys.Send(Char('y'));
        var questions = new ReplServerQuestions(keys);

        Assert.AreEqual(0, await questions.AskAsync((at, token) => at.ReadKey(token).KeyChar == 'y' ? 0 : 1, CancellationToken.None));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AQuestionAfterInitializeIsAnswered_WaitsToBeAsked_AndReadsNoKeyUntilItIs()
    {
        var keys = new WaitingKeys();
        keys.Send(Char('y'));
        var questions = new ReplServerQuestions(keys);
        questions.Hold();

        var answer = questions.AskAsync((at, token) => at.ReadKey(token).KeyChar == 'y' ? 0 : 1, Token);

        Assert.IsFalse(answer.IsCompleted, "it waits");
        Assert.IsTrue(questions.Waiting.IsCompleted, "and is what waits to be asked");
        await Task.Run(() => questions.AskWaiting(keys), Token);
        Assert.AreEqual(0, await answer, "the key is the question's once it is asked");
    }

    // a question the server asks while the shell comes up - the indenter settings of Rubberduck, say - is not drawn among the splash and the memory the shell
    // writes as it starts, nor while the server's work is reported: it is asked once the shell has said READY, before its first prompt.
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AQuestionThatComesWhileTheShellComesUp_IsAskedOnceItSaysReady()
    {
        var keys = new WaitingKeys();
        var questions = new ReplServerQuestions(keys);
        questions.Hold();
        var said = new List<string>();
        var answer = questions.AskAsync((_, _) =>
        {
            lock (said)
            {
                said.Add("asked");
            }

            return 0;
        }, Token);

        var console = Substitute.For<IReplConsole>();
        console.When(each => each.WriteLine(Arg.Is<string>(line => line.Contains(RDCore.CLI.Resources.Repl_Ready)))).Do(_ =>
        {
            lock (said)
            {
                said.Add("READY");
            }
        });
        var editor = new ConsolePromptEditor(new EndingInput(() => { }), new Console(), () => 80);
        var program = new ReplProgram();
        var platform = Substitute.For<IReplPlatformClient>();
        var shell = new ReplShell(
            program, console, platform, new ReplDocument(program, platform), Substitute.For<IReplCommandDispatcher>(), NullLogger<ReplShell>.Instance,
            editor, questions: questions, keys: keys);

        Assert.IsFalse(answer.IsCompleted, "nothing is asked before the shell runs");
        await shell.RunAsync(Token);

        Assert.AreEqual(0, await answer);
        CollectionAssert.AreEqual(new[] { "READY", "asked" }, said);
    }

    [TestMethod]
    public async Task AQuestionTheShellNeverGotToAsk_IsAnsweredThatNobodyAnswers_WhenTheShellEnds()
    {
        var keys = new WaitingKeys();
        var questions = new ReplServerQuestions(keys);
        questions.AskWaiting(keys);
        var answer = questions.AskAsync((_, _) => 0, CancellationToken.None);

        questions.Close();

        await Assert.ThrowsExactlyAsync<ReplNobodyAnswersException>(() => answer);
        await Assert.ThrowsExactlyAsync<ReplNobodyAnswersException>(() => questions.AskAsync((_, _) => 0, CancellationToken.None), "and nothing more is asked");
    }

    // the input ends while a question waits for the next prompt, which is never drawn.
    private sealed class EndingInput(Action ending) : IConsoleKeys
    {
        public bool IsInteractive => false;

        public ConsoleKeyInfo ReadKey(CancellationToken token) => throw new InvalidOperationException("a line at a time");

        public string? ReadLine(CancellationToken token)
        {
            ending();
            return null;
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task TheShellThatEnds_AnswersThatNobodyAnswersTheQuestionThatWaits()
    {
        var keys = new WaitingKeys();
        var questions = new ReplServerQuestions(keys);
        Task<int?>? answer = null;
        var editor = new ConsolePromptEditor(new EndingInput(() => answer = questions.AskAsync((_, _) => 0, Token)), new Console(), () => 80);

        var program = new ReplProgram();
        var platform = Substitute.For<IReplPlatformClient>();
        var shell = new ReplShell(
            program, Substitute.For<IReplConsole>(), platform, new ReplDocument(program, platform), Substitute.For<IReplCommandDispatcher>(), NullLogger<ReplShell>.Instance,
            editor, questions: questions, keys: keys);

        await shell.RunAsync(Token);

        Assert.IsNotNull(answer, "asked once the shell read its first line, so it waited for the next prompt");
        await Assert.ThrowsExactlyAsync<ReplNobodyAnswersException>(() => answer);
    }
}
