using RDCore.CLI.App.Commands;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc run</c>'s command line, split between rdc and the program: rdc's options, then the path, then the program's command line; a <c>--</c> ends rdc's part.
/// </summary>
[TestClass]
public sealed class RunCommandLineTests
{
    private static void AssertSplit(string[] args, string[] own, string[] program)
    {
        var split = RunCommandLine.Split(args);

        CollectionAssert.AreEqual(own, split.Own.ToArray(), "rdc's");
        CollectionAssert.AreEqual(program, split.Program.ToArray(), "the program's");
    }

    [TestMethod]
    public void PathThenArguments_TheArgumentsAreTheProgramsCommandLine()
        => AssertSplit(["demo", "--seed", "42"], ["demo"], ["--seed", "42"]);

    [TestMethod]
    public void PathThenSeparatorThenArguments_TheSeparatorIsSkipped()
        => AssertSplit(["demo", "--", "--seed", "42"], ["demo"], ["--seed", "42"]);

    [TestMethod]
    public void PathThenTwoSeparators_TheSecondIsTheProgramsArgument()
        => AssertSplit(["demo", "--", "--", "x"], ["demo"], ["--", "x"]);

    [TestMethod]
    public void SeparatorAlone_NeitherHasAnything()
        => AssertSplit(["--"], [], []);

    [TestMethod]
    public void SeparatorWithNoPath_WhatFollowsItIsTheProgramsCommandLine()
        => AssertSplit(["--unattended", "--", "--seed", "42"], ["--unattended"], ["--seed", "42"]);

    [TestMethod]
    public void NoPathAndNoSeparator_EverythingIsRdcs()
        => AssertSplit(["--seed", "42"], ["--seed", "42"], []);

    [TestMethod]
    public void NoArguments_NeitherHasAnything()
        => AssertSplit([], [], []);

    [TestMethod]
    public void OptionsBeforeThePath_AreRdcs_AndAnOptionsValueIsNotThePath()
        => AssertSplit(["--unattended", "-c", "Release", "demo", "x"], ["--unattended", "-c", "Release", "demo"], ["x"]);

    [TestMethod]
    public void LongOptionsValue_IsNotThePath()
        => AssertSplit(["--configuration", "Release", "demo", "x"], ["--configuration", "Release", "demo"], ["x"]);

    [TestMethod]
    public void RdcsOwnSwitchAfterThePath_IsTheProgramsArgument()
        => AssertSplit(["demo", "--unattended"], ["demo"], ["--unattended"]);
}
