using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledModuleFieldTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A compiled <c>Set</c> that lets go of the last reference to an object: compiled code runs it when destroying the object runs no code (MS-VBAL §5.3.1.10),
/// and the session counts and destroys the object as it does for the interpreter. The object's class is one of the workspace's, with no
/// <c>Class_Terminate</c>, and none of its variables or <c>Static</c> locals can hold an object. Any other such <c>Set</c> is the interpreter's: one of an
/// object whose class has a <c>Class_Terminate</c>, a field or a <c>Static</c> local that holds an object (a <c>Collection</c> included), a <c>Variant</c>
/// field, a <c>WithEvents</c> variable, or of a <c>Collection</c>, which lets go of its members. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledSetDestroyTests
{
    // an object whose destruction runs no code: its variables are numbers, a String and an array of numbers.
    private static readonly (string Name, string Source) Plain = ("Plain", ModuleWorkspace.ClassModule("Plain", "Option Explicit",
        "Public Size As Long",
        "Public Label As String",
        "Private mSizes(1 To 3) As Long"));

    // an object that says when it is destroyed.
    private static readonly (string Name, string Source) Counted = ("Counted", ModuleWorkspace.ClassModule("Counted", "Option Explicit",
        "Public Size As Long",
        "Private Sub Class_Terminate()",
        "    Debug.Print \"bye\"; Size",
        "End Sub"));

    private static readonly (string Name, string Source) Noisy = ("Noisy", ModuleWorkspace.ClassModule("Noisy", "Option Explicit",
        "Private Sub Class_Terminate()",
        "    Debug.Print \"bye\"",
        "End Sub"));

    // the program, with a factory function, as a module's: each call returns a new object of the class.
    private static string Factory(string className, params string[] body)
        => Program([.. body,
            $"Private Function Make(ByVal N As Long) As {className}",
            $"    Dim Made As {className}",
            $"    Set Made = New {className}",
            "    Made.Size = N",
            "    Set Make = Made",
            "End Function"]);

    // each Set of A lets go of the only reference to the object it held. Compiled code destroys an object whose destruction runs no code; one whose
    // Class_Terminate runs as it goes is destroyed by the interpreter, between what is printed before it and after it.
    [TestMethod]
    [DataRow("Plain", 0, DisplayName = "no Class_Terminate")]
    [DataRow("Counted", 6, DisplayName = "a Class_Terminate")]
    public async Task ASetThatDestroysAnObject_IsCompiledCodes_WhenItsDestructionRunsNoCode(string className, int recoveries)
        => await AssertAlikeAsync(Factory(className,
            "Public Sub Main()",
            "    Debug.Print Work()",
            "    Debug.Print \"back\"",
            "End Sub",
            "Private Function Work() As Long",
            $"    Dim A As {className}, B As {className}, I As Long",
            "    Set A = Make(0)",
            "    For I = 1 To 5",
            "        Set B = Make(I)",
            "        Set A = B",
            "        Work = Work + A.Size",
            "    Next",
            "    Set B = Nothing",
            "    Set A = Nothing",
            "End Function"), [("Work", recoveries)], classes: [Plain, Counted]);

    // the function result variable lets go of what it held as a variable does.
    [TestMethod]
    [DataRow("Plain", 0, DisplayName = "no Class_Terminate")]
    [DataRow("Counted", 1, DisplayName = "a Class_Terminate")]
    public async Task ASetOfTheFunctionResultThatDestroysAnObject_IsCompiledCodes_WhenItsDestructionRunsNoCode(string className, int recoveries)
        => await AssertAlikeAsync(Factory(className,
            "Public Sub Main()",
            "    Debug.Print Pick().Size",
            "    Debug.Print \"back\"",
            "End Sub",
            $"Private Function Pick() As {className}",
            $"    Dim A As {className}, B As {className}",
            "    Set A = Make(1)",
            "    Set B = Make(2)",
            "    Set Pick = A",
            "    Set A = Nothing",
            "    Set Pick = B",
            "End Function"), [("Pick", recoveries)], classes: [Plain, Counted]);

    // what an object's field holds is let go of with it: an object, a Collection that holds one, or a Variant that does. Its class has no Class_Terminate.
    [TestMethod]
    [DataRow("Private Item As Noisy", "Set Item = Given", DisplayName = "an object field")]
    [DataRow("Private Item As Object", "Set Item = Given", DisplayName = "an Object field")]
    [DataRow("Private Item As Variant", "Set Item = Given", DisplayName = "a Variant field")]
    [DataRow("Private Item As Variant", "Item = Array(1, Given)", DisplayName = "a Variant field holding an array")]
    [DataRow("Private Item As Collection", "Set Item = New Collection: Item.Add Given", DisplayName = "a Collection field")]
    public async Task ASetThatDestroysAnObjectWhoseFieldHoldsAnObject_IsTheInterpreters(string field, string fill)
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Work",
            "    Debug.Print \"back\"",
            "End Sub",
            "Private Sub Work()",
            "    Dim H As Holder, Inside As Noisy",
            "    Set H = New Holder",
            "    Set Inside = New Noisy",
            "    H.Fill Inside",
            "    Set Inside = Nothing",
            "    Debug.Print \"before\"",
            "    Set H = Nothing",
            "    Debug.Print \"after\"",
            "End Sub"), [("Work", 1)], classes: [Noisy, ("Holder", ModuleWorkspace.ClassModule("Holder", "Option Explicit",
                field,
                "Public Sub Fill(ByVal Given As Noisy)",
                $"    {fill}",
                "End Sub"))]);

    // a Static local of a class's procedure is the object's, and what it holds is let go of with it.
    [TestMethod]
    public async Task ASetThatDestroysAnObjectWhoseStaticLocalHoldsAnObject_IsTheInterpreters()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Work",
            "    Debug.Print \"back\"",
            "End Sub",
            "Private Sub Work()",
            "    Dim K As Keeper, Inside As Noisy",
            "    Set K = New Keeper",
            "    Set Inside = New Noisy",
            "    K.Keep Inside",
            "    Set Inside = Nothing",
            "    Debug.Print \"before\"",
            "    Set K = Nothing",
            "    Debug.Print \"after\"",
            "End Sub"), [("Work", 1)], classes: [Noisy, ("Keeper", ModuleWorkspace.ClassModule("Keeper", "Option Explicit",
                "Public Sub Keep(ByVal Given As Noisy)",
                "    Static Kept As Noisy",
                "    Set Kept = Given",
                "End Sub"))]);

    // an object that handles another's events stops handling them when it is destroyed: the second event finds no handler.
    [TestMethod]
    public async Task ASetThatDestroysAWithEventsSubscriber_IsTheInterpreters()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Work",
            "    Debug.Print \"back\"",
            "End Sub",
            "Private Sub Work()",
            "    Dim T As Ticker, L As Listener",
            "    Set T = New Ticker",
            "    Set L = New Listener",
            "    L.Attach T",
            "    T.Fire",
            "    Set L = Nothing",
            "    T.Fire",
            "    Debug.Print \"end\"",
            "End Sub"), [("Work", 1)], classes: [
                ("Ticker", ModuleWorkspace.ClassModule("Ticker", "Option Explicit",
                    "Public Event Ticked()",
                    "Public Sub Fire()",
                    "    RaiseEvent Ticked",
                    "End Sub")),
                ("Listener", ModuleWorkspace.ClassModule("Listener", "Option Explicit",
                    "Private WithEvents mSource As Ticker",
                    "Public Sub Attach(ByVal Source As Ticker)",
                    "    Set mSource = Source",
                    "End Sub",
                    "Private Sub mSource_Ticked()",
                    "    Debug.Print \"ticked\"",
                    "End Sub"))]);

    // a Collection lets go of its members as it goes.
    [TestMethod]
    public async Task ASetThatDestroysACollection_IsTheInterpreters()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Work",
            "    Debug.Print \"back\"",
            "End Sub",
            "Private Sub Work()",
            "    Dim C As Collection, Inside As Noisy",
            "    Set C = New Collection",
            "    Set Inside = New Noisy",
            "    C.Add Inside",
            "    Set Inside = Nothing",
            "    Debug.Print \"before\"",
            "    Set C = Nothing",
            "    Debug.Print \"after\"",
            "End Sub"), [("Work", 1)], classes: [Noisy]);

    // a Set that lets go of a reference something else holds destroys nothing: the object is the Collection's, whole, after it.
    [TestMethod]
    public async Task ASetOfAnObjectSomethingElseHolds_DestroysNothing()
        => await AssertAlikeAsync(Factory("Plain",
            "Public Sub Main()",
            "    Dim Kept As New Collection",
            "    Work Kept",
            "    Debug.Print Kept(1).Size; Kept.Count",
            "End Sub",
            "Private Sub Work(ByVal Kept As Collection)",
            "    Dim G As Plain",
            "    Set G = Make(7)",
            "    Kept.Add G",
            "    Set G = Make(8)",
            "    Set G = Nothing",
            "End Sub"), [("Work", 0)], classes: [Plain]);
}
