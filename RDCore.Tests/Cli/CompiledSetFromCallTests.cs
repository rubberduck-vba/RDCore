using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledModuleFieldTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Set</c> statement given what a call returns, compiled: a function of the workspace, a member of an object variable, or a member of a class's
/// default instance, that returns an object, and <c>New</c> of a class of the workspace. The call is made once: the object it returns is Set-coerced, stored
/// and counted as the interpreter does it, so a type mismatch is raised after the call, at the <c>Set</c>, and each object's <c>Terminate</c> runs when it
/// is let go of last. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledSetFromCallTests
{
    // an object that says when it is destroyed, made by its class's default instance; and another class.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Noisy", ModuleWorkspace.ClassModule("Noisy",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Public Name As String",
            "Public Function Create(ByVal Named As String) As Noisy",
            "    Dim Made As Noisy",
            "    Set Made = New Noisy",
            "    Made.Name = Named",
            "    Set Create = Made",
            "End Function",
            "Public Function Twin() As Noisy",
            "    Set Twin = Create(Name & \"'\")",
            "End Function",
            "Private Sub Class_Terminate()",
            "    If Name <> vbNullString Then Debug.Print \"bye \"; Name",
            "End Sub")),
        ("Other", ModuleWorkspace.ClassModule("Other", "Option Explicit", "Public Name As String")),
    ];

    // each pass Sets the variable to an object a function of the workspace returns, and lets go of the one before, last: its Terminate runs there. Compiled
    // code makes the call; the Set that destroys an object is the interpreter's, with what the call returned known: the recoveries are passes 2 and 3.
    [TestMethod]
    public async Task ASetOfWhatAFunctionReturns_IsCompiled_AndTerminatesTheObjectItLetsGoOf()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Count(3)",
            "    Debug.Print \"back\"",
            "End Sub",
            "Private Function Make(ByVal N As Long) As Noisy",
            "    Set Make = Noisy.Create(\"n\" & N)",
            "End Function",
            "Private Function Count(ByVal N As Long) As Long",
            "    Dim I As Long, Item As Noisy",
            "    For I = 1 To N",
            "        Set Item = Make(I)",
            "        Count = Count + I",
            "    Next",
            "End Function"), [("Count", 2)], classes: Classes);

    // the factory of a class's default instance, and a member of an object variable that returns an object. The factory is given a String, which a member
    // call of compiled code is not: its Set is a support call. The recovery is the Set of Copy in pass 2, which destroys the twin of pass 1.
    [TestMethod]
    public async Task ASetOfWhatAMemberReturns_OfADefaultInstance_OrOfAnObjectVariable_IsCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Names(2)",
            "End Sub",
            "Private Function Names(ByVal N As Long) As Long",
            "    Dim I As Long, Item As Noisy, Copy As Noisy",
            "    For I = 1 To N",
            "        Set Item = Noisy.Create(\"c\" & I)",
            "        Set Copy = Item.Twin",
            "        Names = Names + I",
            "    Next",
            "End Function"), [("Names", 1)], classes: Classes);

    // the function result variable Set to what a call returns, and to a new object.
    [TestMethod]
    public async Task TheFunctionResult_IsSetToWhatACallReturns_OrToANewObject()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Pick(True).Name; Pick(False).Name = vbNullString",
            "End Sub",
            "Private Function Pick(ByVal Made As Boolean) As Noisy",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        If Made Then Set Pick = Noisy.Create(\"p\" & I) Else Set Pick = New Noisy",
            "    Next",
            "End Function"), [("Pick", 0)], classes: Classes);

    // New of a class of the workspace, in a loop: each object made is let go of by the next pass.
    [TestMethod]
    public async Task ASetOfANewObject_IsCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Fresh(3)",
            "End Sub",
            "Private Function Fresh(ByVal N As Long) As Long",
            "    Dim I As Long, Item As Other",
            "    For I = 1 To N",
            "        Set Item = New Other",
            "        Fresh = Fresh + I",
            "    Next",
            "End Function"), [("Fresh", 0)], classes: Classes);

    // what the call returns is not of the variable's class: the call is made once, and error 13 is raised at the Set, after it - to the handler, which
    // resumes after the Set, and to the caller, with nothing to catch it. Each error given to the handler is the interpreter's: two; each read of
    // Err.Number is compiled code's.
    [TestMethod]
    public async Task WhatACallReturnsOfAnotherClass_IsATypeMismatch_AfterTheCallIsMadeOnce()
        => await AssertAlikeAsync(Program(
            "Public Calls As Long",
            "Public Sub Main()",
            "    Debug.Print Handled(2); Calls",
            "    Debug.Print Unhandled(); Calls",
            "End Sub",
            "Private Function Wrong() As Object",
            "    Calls = Calls + 1",
            "    Set Wrong = New Other",
            "End Function",
            "Private Function Handled(ByVal N As Long) As Long",
            "    Dim I As Long, Item As Noisy",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        Set Item = Wrong()",
            "        Handled = Handled + Err.Number",
            "    Next",
            "End Function",
            "Private Function Unhandled() As Long",
            "    Dim I As Long, Item As Noisy",
            "    For I = 1 To 2",
            "        Set Item = Wrong()",
            "        Unhandled = Unhandled + I",
            "    Next",
            "End Function"), [("Handled", 2)], classes: Classes);
}
