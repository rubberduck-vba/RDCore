using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.ConsoleIO;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The messages that come while a question is asked at the console are written once it is answered (<see cref="PromptAwareMessageWriter.WhileAsking"/>).
/// </summary>
[TestClass]
public sealed class PromptAwareMessageWriterTests
{
    [TestMethod]
    public void AHeldMessageThatFailsToBeWritten_DoesNotTakeTheAnswersPlace_NorKeepTheMessagesAfterItHeld()
    {
        // a slogan fails to be written, as a console that is gone fails; a legal notice is written.
        var written = new List<string>();
        var inner = Substitute.For<IConsoleMessageWriter>();
        inner.WriteSlogan().Returns(_ => throw new IOException("the console is gone"));
        inner.WriteLegalNotice().Returns(_ =>
        {
            written.Add("NOTICE");
            return inner;
        });
        var messages = new PromptAwareMessageWriter(inner);

        var answer = messages.WhileAsking(() =>
        {
            messages.WriteSlogan();
            messages.WriteLegalNotice();
            return 42;
        });

        Assert.AreEqual(42, answer, "the user's answer is the question's");
        CollectionAssert.AreEqual(new[] { "NOTICE" }, written, "the message after the one that failed is written");

        messages.WriteLegalNotice();
        CollectionAssert.AreEqual(new[] { "NOTICE", "NOTICE" }, written, "a message that comes once the question is answered is written as it comes");
    }

    [TestMethod]
    public void AHeldMessageThatFailsToBeWritten_DoesNotTakeThePlaceOfWhatTheQuestionThrew()
    {
        var inner = Substitute.For<IConsoleMessageWriter>();
        inner.WriteSlogan().Returns(_ => throw new IOException("the console is gone"));
        var messages = new PromptAwareMessageWriter(inner);

        Assert.ThrowsExactly<OperationCanceledException>(() => messages.WhileAsking<int>(() =>
        {
            messages.WriteSlogan();
            throw new OperationCanceledException();
        }));
    }
}
