using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

/// <summary>
/// What a module's load failed with is said once: a module an editor sends to load again at every edit, failing as it did, is not reported again.
/// </summary>
[TestClass]
public sealed class ModuleLoadFailuresTests
{
    [TestMethod]
    public void AModuleThatFailsAsItFailedTheLastTime_IsNotReportedAgain()
    {
        var failures = new ModuleLoadFailures();
        string[] errors = ["Invalid use of Me keyword"];

        Assert.AreEqual(ModuleLoadChange.Failed, failures.Record("GameSheet", errors));
        Assert.AreEqual(ModuleLoadChange.Unchanged, failures.Record("GameSheet", errors));
        Assert.AreEqual(ModuleLoadChange.Unchanged, failures.Record("gamesheet", [.. errors]), "a module is known by its name, whatever its case");
    }

    [TestMethod]
    public void AModuleThatFailsOtherwise_IsReportedAgain_AndOneThatLoadsOnceItFailed_IsReportedToLoadAgain()
    {
        var failures = new ModuleLoadFailures();

        Assert.AreEqual(ModuleLoadChange.Failed, failures.Record("GameSheet", ["Invalid use of Me keyword"]));
        Assert.AreEqual(ModuleLoadChange.Failed, failures.Record("GameSheet", ["Variable not defined"]));
        Assert.AreEqual(ModuleLoadChange.Recovered, failures.Record("GameSheet", []));
        Assert.AreEqual(ModuleLoadChange.Unchanged, failures.Record("GameSheet", []));
        Assert.AreEqual(ModuleLoadChange.Failed, failures.Record("GameSheet", ["Variable not defined"]), "a failure after it loaded is news");
    }

    [TestMethod]
    public void EachModule_IsKnownOnItsOwn()
    {
        var failures = new ModuleLoadFailures();

        Assert.AreEqual(ModuleLoadChange.Failed, failures.Record("GameSheet", ["Invalid use of Me keyword"]));
        Assert.AreEqual(ModuleLoadChange.Failed, failures.Record("TitleSheet", ["Invalid use of Me keyword"]));
        Assert.AreEqual(ModuleLoadChange.Unchanged, failures.Record("Program", []), "a module that never failed has nothing to recover from");
    }
}
