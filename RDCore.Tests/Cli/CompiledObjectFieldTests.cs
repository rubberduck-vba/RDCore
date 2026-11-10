using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledModuleFieldTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A field or <c>Static</c> variable of an object type, compiled: held in the cell the interpreter resolves it to, whose reference compiled code reads, and
/// which a <c>Set</c> compiled code runs stores in and counts as the interpreter does, so that each object's <c>Terminate</c> runs at the instruction that
/// lets go of it last, which the interpreter runs alone. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledObjectFieldTests
{
    // an object that says when it is destroyed.
    private static readonly (string Name, string Source) Noisy = ("Noisy", ModuleWorkspace.ClassModule("Noisy", "Option Explicit",
        "Public Name As String",
        "Private Sub Class_Terminate()",
        "    Debug.Print \"bye \"; Name",
        "End Sub"));

    // what one module's compiled code Sets a field of another to, the other's compiled code reads, and the object lives for as long as the field or a
    // variable holds it.
    [TestMethod]
    public async Task AnObjectFieldSetInOneModule_IsReadInAnother()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As Noisy, B As Noisy",
            "    Set A = New Noisy",
            "    A.Name = \"a\"",
            "    Set B = New Noisy",
            "    B.Name = \"b\"",
            "    Keep A, 3",
            "    Debug.Print Picked().Name",
            "    Keep B, 2",
            "    Debug.Print Picked().Name",
            "    Set A = Nothing",
            "    Debug.Print \"a let go of\"",
            "    Release",
            "    Debug.Print \"end\"",
            "End Sub",
            "Private Function Picked() As Noisy",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Set Picked = Current",
            "    Next",
            "End Function"), [("Keep", 0), ("Picked", 0), ("Release", 0)], modules:
            [("Registry", ModuleWorkspace.StandardModule("Registry", ["Option Explicit", "Public Current As Noisy",
                "Public Sub Keep(ByVal Item As Noisy, ByVal N As Long)",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Set Current = Item",
                "    Next",
                "End Sub",
                "Public Sub Release()",
                "    Set Current = Nothing",
                "End Sub"]))], classes: [Noisy]);

    // a field of the object Set by its own member: what it held is let go of there, and Terminate runs between what the member prints before the Set and
    // after it when the field held the object last (a Set the interpreter runs); compiled code prints.
    [TestMethod]
    public async Task AnObjectFieldOfAClass_IsSet_AndLetsGoOfWhatItHeldThere()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim H As New Holder, N As Noisy",
            "    Debug.Print H.HoldsNothing",
            "    Set N = New Noisy",
            "    N.Name = \"first\"",
            "    H.Hold N",
            "    Set N = New Noisy",
            "    N.Name = \"second\"",
            "    Debug.Print H.Held.Name; H.HoldsNothing",
            "    H.Hold N",
            "    Set N = Nothing",
            "    Debug.Print H.Held.Name",
            "    H.Hold Nothing",
            "    Debug.Print \"end\"",
            "End Sub"), [("Hold", 2), ("Held", 0), ("HoldsNothing", 0)], classes: [Noisy, ("Holder", ModuleWorkspace.ClassModule("Holder", "Option Explicit",
                "Private mItem As Noisy",
                "Public Sub Hold(ByVal Value As Noisy)",
                "    Debug.Print \"holding\"",
                "    Set mItem = Value",
                "    Debug.Print \"held\"",
                "End Sub",
                "Public Function Held() As Noisy",
                "    Set Held = mItem",
                "End Function",
                "Public Function HoldsNothing() As Boolean",
                "    HoldsNothing = mItem Is Nothing",
                "End Function"))]);

    // a member of the object a field holds is called by compiled code, on that object: the one the field holds when the call is made, whichever code Set
    // it, compiled or not.
    [TestMethod]
    public async Task AMemberOfTheObjectAFieldHolds_IsCalledOnIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Counter, B As New Counter",
            "    Set Current = A",
            "    Debug.Print Counted(3);",
            "    Set Current = B",
            "    Debug.Print Counted(2);",
            "    Debug.Print Switched(A, 1); A.Total; B.Total",
            "End Sub",
            "Private Function Switched(ByVal Given As Counter, ByVal N As Long) As Long",
            "    Set Current = Given",
            "    Switched = Counted(N)",
            "End Function",
            "Private Function Counted(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Current.Add I",
            "    Next",
            "    Counted = Current.Sum()",
            "End Function"), [("Counted", 0), ("Switched", 0), ("Add", 0), ("Sum", 0)], modules: [("Registry", ModuleWorkspace.StandardModule("Registry", ["Option Explicit",
                "Public Current As Counter"]))], classes: [("Counter", ModuleWorkspace.ClassModule("Counter", "Option Explicit",
                "Public Total As Long",
                "Public Sub Add(ByVal N As Long)",
                "    Total = Total + N",
                "End Sub",
                "Public Function Sum() As Long",
                "    Sum = Total",
                "End Function"))]);

    // a Static of an object type holds what it was Set to from call to call.
    [TestMethod]
    public async Task AnObjectStatic_IsKeptFromCallToCall()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As Noisy, B As Noisy",
            "    Set A = New Noisy",
            "    A.Name = \"a\"",
            "    Set B = New Noisy",
            "    B.Name = \"b\"",
            "    Debug.Print Swapped(A) Is Nothing",
            "    Debug.Print Swapped(B).Name",
            "    Set A = Nothing",
            "    Debug.Print Swapped(Nothing).Name",
            "    Debug.Print \"end\"",
            "End Sub",
            "Private Function Swapped(ByVal Item As Noisy) As Noisy",
            "    Static Last As Noisy",
            "    Set Swapped = Last",
            "    Set Last = Item",
            "End Function"), [("Swapped", 0)], classes: [Noisy]);

    // a WithEvents field is Set by the interpreter, which detaches its handlers from what it held and attaches them to what it is given (MS-VBAL §5.4.3.9):
    // the handler runs for an event raised while the field holds the object, and not before nor after. The Set is a support call, no recovery.
    [TestMethod]
    public async Task AWithEventsField_IsSetByTheInterpreter_WhichAttachesItsHandlers()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As New Source, L As New Listener",
            "    S.Fire 1",
            "    L.Watch S",
            "    S.Fire 2",
            "    L.Watch Nothing",
            "    S.Fire 3",
            "    Debug.Print \"end\"; L.Watched",
            "End Sub"), [("Watch", 0)], classes:
            [
                ("Source", ModuleWorkspace.ClassModule("Source", "Option Explicit",
                    "Public Event Ping(ByVal N As Long)",
                    "Public Sub Fire(ByVal N As Long)",
                    "    RaiseEvent Ping(N)",
                    "End Sub")),
                ("Listener", ModuleWorkspace.ClassModule("Listener", "Option Explicit",
                    "Private WithEvents mSource As Source",
                    "Public Watched As Long",
                    "Public Sub Watch(ByVal S As Source)",
                    "    Set mSource = S",
                    "    Watched = Watched + 1",
                    "End Sub",
                    "Private Sub mSource_Ping(ByVal N As Long)",
                    "    Debug.Print \"ping\"; N",
                    "End Sub")),
            ]);

    // a RaiseEvent in a loop compiled code runs is a support call: the handler runs at each pass, and what it writes into the argument passed to it by
    // reference is read back into the variable of the activation it was given (MS-VBAL §5.4.2.20). No recovery.
    [TestMethod]
    public async Task ARaiseEvent_IsASupportCall_WhoseHandlerWritesBackAnArgumentPassedByReference()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As New Source, L As New Listener",
            "    L.Watch S",
            "    Debug.Print \"fired\"; S.Fired(4)",
            "End Sub"), [("Fired", 0)], classes:
            [
                ("Source", ModuleWorkspace.ClassModule("Source", "Option Explicit",
                    "Public Event Ask(ByVal N As Long, Cancel As Boolean)",
                    "Public Function Fired(ByVal Count As Long) As Long",
                    "    Dim K As Long",
                    "    Dim Cancel As Boolean",
                    "    For K = 1 To Count",
                    "        Cancel = False",
                    "        RaiseEvent Ask(K, Cancel)",
                    "        If Cancel Then Fired = Fired + K",
                    "    Next",
                    "End Function")),
                ("Listener", ModuleWorkspace.ClassModule("Listener", "Option Explicit",
                    "Private WithEvents mSource As Source",
                    "Public Sub Watch(ByVal S As Source)",
                    "    Set mSource = S",
                    "End Sub",
                    "Private Sub mSource_Ask(ByVal N As Long, Cancel As Boolean)",
                    "    Debug.Print \"ask\"; N",
                    "    Cancel = N Mod 2 = 0",
                    "End Sub")),
            ]);
}
