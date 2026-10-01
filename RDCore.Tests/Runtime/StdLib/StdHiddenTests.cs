using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// The members of the <c>VBA</c> library's <c>_HiddenModule</c> (<c>Array</c>, <c>Input</c>, <c>Input$</c>, <c>Width</c>,
/// <c>ObjPtr</c>), which resolve unqualified like any member of a standard module.
/// </summary>
[TestClass]
public sealed class StdHiddenTests
{
    private const string Root = "/ws";

    private static (string[] Output, RuntimeExecutionOutcome Outcome, IRuntimeSession Session) Run(
        MockFileSystem? fileSystem, ModuleDirectives directives, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (session, outcome) = RuntimeSourceHarness.Run(
            fileSystem, [], output, standardLibrary: true, arrange: null, directives, body);
        return ([.. output.Lines.Select(line => line.Trim())], outcome, session);
    }

    private static string[] Run(params string[] body)
    {
        var (output, outcome, _) = Run(null, ModuleDirectives.None, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Description);
        return output;
    }

    private static MockFileSystem WithFile(string content)
        => new(new Dictionary<string, MockFileData> { [$"{Root}/data.txt"] = new(content) });

    #region Array

    [TestMethod]
    public void Array_HoldsItsArguments_InOrder()
        => CollectionAssert.AreEqual(new[] { "a", "b", "c" },
            Run("Dim v As Variant", "v = Array(\"a\", \"b\", \"c\")", "Debug.Print v(0)", "Debug.Print v(1)", "Debug.Print v(2)"));

    [TestMethod]
    public void Array_HasAnElementForEachArgument_AndTheyNeedNotBeOfOneType()
        => CollectionAssert.AreEqual(new[] { "2", "7", "x" },
            Run("Dim v As Variant", "v = Array(7, \"x\")", "Debug.Print UBound(v) + 1", "Debug.Print v(0)", "Debug.Print v(1)"));

    [TestMethod]
    public void Array_WithNoArguments_IsZeroLength()
        => CollectionAssert.AreEqual(new[] { "0", "-1" }, Run("Debug.Print LBound(Array())", "Debug.Print UBound(Array())"));

    [TestMethod]
    public void TheArrayKeyword_StartsAtTheOptionBaseOfItsModule()
    {
        var (output, outcome, _) = Run(null, ModuleDirectives.None with { Base = 1 }, "Debug.Print LBound(Array(1, 2))", "Debug.Print UBound(Array(1, 2))");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "1", "2" }, output);
    }

    [TestMethod]
    public void TheArrayKeyword_WithNoArguments_IsZeroLengthFromTheOptionBase()
    {
        var (output, _, _) = Run(null, ModuleDirectives.None with { Base = 1 }, "Debug.Print LBound(Array())", "Debug.Print UBound(Array())");

        CollectionAssert.AreEqual(new[] { "1", "0" }, output);
    }

    [TestMethod]
    public void TheLibrarysMember_IsZeroBased_WhateverTheOptionBaseSays()
    {
        // `VBA.Array(...)`: the same name, qualified, is the member of the library's hidden module and not the keyword.
        var (output, outcome, _) = Run(null, ModuleDirectives.None with { Base = 1 }, "Debug.Print LBound(VBA.Array(1, 2))", "Debug.Print UBound(VBA.Array(1, 2))");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "0", "1" }, output);
    }

    [TestMethod]
    public void TheLibrarysMember_CalledDirectly_BuildsAZeroBasedArray()
    {
        var (_, _, session) = Run(null, ModuleDirectives.None);

        var result = new StdHidden(session).Array(new VBVariantValue(new VBLongValue(5)), new VBVariantValue(new VBLongValue(6)));

        var array = (VBResizableArrayValue)((VBVariantValue)result.Result!).TypedValue;
        Assert.AreEqual(0, array.Dimensions[0].LowerBound);
        Assert.AreEqual(1, array.Dimensions[0].UpperBound);
    }

    [TestMethod]
    public void TheArrayKeyword_ReferencesTheObjectsItHolds()
        // an object in an element is held by the array: one that was only ever in the array is released with it.
        => CollectionAssert.AreEqual(new[] { "a" }, Run("Dim v As Variant", "v = Array(\"a\")", "Debug.Print v(LBound(v))"));

    [TestMethod]
    public void Array_IsAnArrayOfVariants_WhoseElementsCanBeAssigned()
        => CollectionAssert.AreEqual(new[] { "changed", "b" },
            Run("Dim v As Variant", "v = Array(\"a\", \"b\")", "v(0) = \"changed\"", "Debug.Print v(0)", "Debug.Print v(1)"));

    #endregion

    #region Input and Input$

    [TestMethod]
    public void InputString_ReadsTheCharactersItIsAskedFor()
    {
        var (output, outcome, _) = Run(WithFile("Hello, world"), ModuleDirectives.None,
            $"Open \"{Root}/data.txt\" For Input As #1", "Debug.Print Input$(5, #1)", "Debug.Print Input$(2, 1)", "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Description);
        // Debug.Print trims here: the second read begins with the comma.
        CollectionAssert.AreEqual(new[] { "Hello", ","  }, output);
    }

    [TestMethod]
    public void Input_ReadsWhateverIsThere_SoAQuoteOrACommaIsACharacter()
    {
        var (output, outcome, _) = Run(WithFile("\"a,b\""), ModuleDirectives.None,
            $"Open \"{Root}/data.txt\" For Input As #1", "Debug.Print Input(5, #1)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "\"a,b\"" }, output);
    }

    [TestMethod]
    public void Input_PastTheEndOfTheFile_IsError62()
    {
        var (_, outcome, _) = Run(WithFile("abc"), ModuleDirectives.None,
            $"Open \"{Root}/data.txt\" For Input As #1", "Debug.Print Input$(4, #1)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InputPastEndOfFile, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Input_OnAFileNumberThatIsNotOpen_IsError52()
    {
        var (_, outcome, _) = Run(null, ModuleDirectives.None, "Debug.Print Input$(1, #7)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileNameOrNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Input_OnAFileOpenedForOutput_IsError54()
    {
        var (_, outcome, _) = Run(WithFile("abc"), ModuleDirectives.None,
            $"Open \"{Root}/data.txt\" For Output As #1", "Debug.Print Input$(1, #1)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileMode, outcome.ErrorInfo!.ErrorId);
    }

    #endregion

    #region Width

    // the statement (`Width #1, 40`) is its own syntax, and has its own tests; this is the member of the module that is its name.
    private static (StdHidden Hidden, IRuntimeSession Session) HiddenModuleWithAnOpenFile(string openStatement)
    {
        var (_, outcome, session) = Run(WithFile(""), ModuleDirectives.None, openStatement);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Description);
        return (new StdHidden(session), session);
    }

    [TestMethod]
    public void Width_SetsTheLineWidthOfTheFile()
    {
        var (hidden, session) = HiddenModuleWithAnOpenFile($"Open \"{Root}/data.txt\" For Output As #1");

        var result = hidden.Width(new VBIntegerValue(1), new VBIntegerValue(40));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        Assert.AreEqual(40, channel!.Output.MaxLineLength);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(256)]
    public void Width_OutsideZeroTo255_IsError5(int width)
    {
        var (hidden, _) = HiddenModuleWithAnOpenFile($"Open \"{Root}/data.txt\" For Output As #1");

        var result = hidden.Width(new VBIntegerValue(1), new VBIntegerValue((short)width));

        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Width_OnAFileNumberThatIsNotOpen_IsError52()
    {
        var (hidden, _) = HiddenModuleWithAnOpenFile($"Open \"{Root}/data.txt\" For Output As #1");

        Assert.AreEqual((int)VBRuntimeErrorId.BadFileNameOrNumber, hidden.Width(new VBIntegerValue(9), new VBIntegerValue(10)).ErrorInfo!.ErrorId);
    }

    #endregion

    #region Hidden members

    [TestMethod]
    public void TheHiddenModule_IsAModuleOfTheLibrary_AndItsMembersAreFlaggedHidden()
    {
        var (_, _, session) = Run(null, ModuleDirectives.None);

        // resolved the way source resolves a name: unqualified, from inside a module of the workspace.
        var array = session.Symbols.Resolver.ResolveValue("Array", ScopeKind.Local, RuntimeSourceHarness.ModuleUri).Symbol;
        Assert.IsNotNull(array);
        Assert.AreEqual(SymbolProperties.HiddenMemberFlag, array.GetProperty(SymbolProperties.MemberFlags) & SymbolProperties.HiddenMemberFlag);
        Assert.AreEqual("_HiddenModule", session.Symbols.Resolver.ResolveMember(
            session.Symbols.Resolver.ResolveQualifier("_HiddenModule", ScopeKind.Local, RuntimeSourceHarness.ModuleUri).Symbol!, "Array",
            RuntimeSourceHarness.ModuleUri).Symbol is { } member ? "_HiddenModule" : string.Empty);
    }

    [TestMethod]
    public void AnUnimplementedMember_SaysSoWhenItIsCalled()
    {
        var (_, outcome, _) = Run(null, ModuleDirectives.None, "Debug.Print StrPtr(\"abc\")");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        StringAssert.Contains(outcome.ErrorInfo!.Verbose, "StrPtr");
    }

    #endregion
}
