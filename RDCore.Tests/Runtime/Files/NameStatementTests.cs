using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Errors;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// The <c>Name … As …</c> statement, which renames a file or a directory and moves it when the new name is somewhere else
/// (<strong>RD-VBAL §5.4.5.13</strong>; not in MS-VBAL).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.4.5.13 Name")]
public sealed class NameStatementTests
{
    private const string Root = "/ws";

    private static (MockFileSystem Files, RuntimeExecutionOutcome Outcome) Run(params string[] body)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$"{Root}/old.txt"] = new("hello"),
            [$"{Root}/taken.txt"] = new("taken"),
            [$"{Root}/dir/inside.txt"] = new("inside"),
        });
        fileSystem.AddDirectory($"{Root}/elsewhere");

        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem, [], body);
        return (fileSystem, outcome);
    }

    private static void AssertFailed(RuntimeExecutionOutcome outcome, VBRuntimeErrorId expected)
    {
        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)expected, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void AFile_IsRenamed_AndKeepsItsContent()
    {
        var (files, outcome) = Run($"Name \"{Root}/old.txt\" As \"{Root}/new.txt\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsFalse(files.File.Exists($"{Root}/old.txt"));
        Assert.AreEqual("hello", files.File.ReadAllText($"{Root}/new.txt"));
    }

    [TestMethod]
    public void AFile_IsMoved_WhenTheNewNameIsInAnotherDirectory()
    {
        var (files, outcome) = Run($"Name \"{Root}/old.txt\" As \"{Root}/elsewhere/old.txt\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsFalse(files.File.Exists($"{Root}/old.txt"));
        Assert.IsTrue(files.File.Exists($"{Root}/elsewhere/old.txt"));
    }

    [TestMethod]
    public void ADirectory_IsRenamed_WithWhatIsInIt()
    {
        var (files, outcome) = Run($"Name \"{Root}/dir\" As \"{Root}/renamed\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsFalse(files.Directory.Exists($"{Root}/dir"));
        Assert.AreEqual("inside", files.File.ReadAllText($"{Root}/renamed/inside.txt"));
    }

    [TestMethod]
    public void TheOperands_AreStringExpressions()
    {
        var (files, outcome) = Run($"Name \"{Root}/\" & \"old\" & \".txt\" As \"{Root}/n\" & \"1\" & \".txt\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(files.File.Exists($"{Root}/n1.txt"));
    }

    [TestMethod]
    public void TheKeywords_AreCaseInsensitive()
    {
        var (files, outcome) = Run($"NAME \"{Root}/old.txt\" AS \"{Root}/new.txt\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(files.File.Exists($"{Root}/new.txt"));
    }

    [TestMethod]
    public void ANameThatIsNotThere_IsError53()
    {
        var (_, outcome) = Run($"Name \"{Root}/missing.txt\" As \"{Root}/new.txt\"");

        AssertFailed(outcome, VBRuntimeErrorId.FileNotFound);
    }

    [TestMethod]
    public void ADirectoryThatIsNotThere_IsError76()
    {
        var (_, outcome) = Run($"Name \"{Root}/nowhere/old.txt\" As \"{Root}/new.txt\"");

        AssertFailed(outcome, VBRuntimeErrorId.PathNotFound);
    }

    [TestMethod]
    public void ANewNameThatExists_IsError58_AndNothingIsChanged()
    {
        var (files, outcome) = Run($"Name \"{Root}/old.txt\" As \"{Root}/taken.txt\"");

        AssertFailed(outcome, VBRuntimeErrorId.FileAlreadyExists);
        Assert.AreEqual("hello", files.File.ReadAllText($"{Root}/old.txt"));
        Assert.AreEqual("taken", files.File.ReadAllText($"{Root}/taken.txt"));
    }

    [TestMethod]
    public void ANewNameInADirectoryThatIsNotThere_IsError76_BecauseNameCreatesNothing()
    {
        var (files, outcome) = Run($"Name \"{Root}/old.txt\" As \"{Root}/nowhere/old.txt\"");

        AssertFailed(outcome, VBRuntimeErrorId.PathNotFound);
        Assert.IsTrue(files.File.Exists($"{Root}/old.txt"));
        Assert.IsFalse(files.Directory.Exists($"{Root}/nowhere"));
    }

    [TestMethod]
    public void AnOpenFile_IsError55()
    {
        var (files, outcome) = Run(
            $"Open \"{Root}/old.txt\" For Input As #1",
            $"Name \"{Root}/old.txt\" As \"{Root}/new.txt\"");

        AssertFailed(outcome, VBRuntimeErrorId.FileAlreadyOpen);
        Assert.IsTrue(files.File.Exists($"{Root}/old.txt"));
    }

    [TestMethod]
    public void AFile_IsRenamedOnceItIsClosed()
    {
        var (files, outcome) = Run(
            $"Open \"{Root}/old.txt\" For Input As #1",
            "Close #1",
            $"Name \"{Root}/old.txt\" As \"{Root}/new.txt\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(files.File.Exists($"{Root}/new.txt"));
    }

    [TestMethod]
    public void ADirectoryWithAnOpenFileInIt_IsError55()
    {
        var (files, outcome) = Run(
            $"Open \"{Root}/dir/inside.txt\" For Input As #1",
            $"Name \"{Root}/dir\" As \"{Root}/renamed\"");

        AssertFailed(outcome, VBRuntimeErrorId.FileAlreadyOpen);
        Assert.IsTrue(files.Directory.Exists($"{Root}/dir"));
    }

    [TestMethod]
    [DataRow("*.txt", "new.txt", DisplayName = "a * in the old name")]
    [DataRow("old.t?t", "new.txt", DisplayName = "a ? in the old name")]
    [DataRow("old.txt", "*.bak", DisplayName = "a * in the new name")]
    public void AWildcard_IsNotAPattern_AndIsError52(string oldName, string newName)
    {
        var (files, outcome) = Run($"Name \"{Root}/{oldName}\" As \"{Root}/{newName}\"");

        AssertFailed(outcome, VBRuntimeErrorId.BadFileNameOrNumber);
        Assert.IsTrue(files.File.Exists($"{Root}/old.txt"));
    }
}
