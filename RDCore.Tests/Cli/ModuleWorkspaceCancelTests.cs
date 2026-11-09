namespace RDCore.Tests.Cli;

/// <summary>
/// A test that cancels its program where the session says the program is fails at once, saying so, when the program ends before it is ever there: it does not
/// wait for a time that would pass anyway.
/// </summary>
[TestClass]
public sealed class ModuleWorkspaceCancelTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    [TestMethod]
    public async Task ACancelThatIsNeverReached_FailsAsTheProgramEnds_SayingSo()
    {
        var never = new TaskCompletionSource();
        var inspected = false;

        var failed = await Assert.ThrowsExactlyAsync<AssertFailedException>(() => ModuleWorkspace.InspectAsync(NoClasses, "Public Sub Main()\r\nEnd Sub\r\n",
            (_, _, _) =>
            {
                inspected = true;
                return Task.CompletedTask;
            }, cancelWhen: _ => never.Task));

        StringAssert.Contains(failed.Message, "the program ended (Completed) before it was where the test cancels it");
        Assert.IsFalse(inspected, "a test whose program was never where it cancels it inspects nothing");
    }
}
