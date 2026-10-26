using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An Enum of the workspace named in an <c>As</c> clause means <c>Long</c> (<strong>MS-VBAL §5.2.3.4</strong>): a variable, a field, a parameter or a result
/// declared as one holds a <c>Long</c>, which is 0 until it is assigned and which what is assigned to it is let-coerced to, wherever the Enum is declared and
/// however its name is qualified. Run through <see cref="ModuleWorkspace"/>, so the declared type the host makes is the one the code runs against.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.4 Enum Declarations")]
public sealed class WorkspaceEnumTypeTests
{
    private const string Colour = "Private Enum Colour\r\ncRed = 1\r\ncBlue = 2\r\nEnd Enum";

    private static string Program(string declarations, params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("", "Dim c As Colour\r\nDebug.Print c\r\nc = 2\r\nDebug.Print c", DisplayName = "A local")]
    [DataRow("Private c As Colour", "Debug.Print c\r\nc = 2\r\nDebug.Print c", DisplayName = "A variable of the module")]
    [DataRow("Private Type TRecord\r\nc As Colour\r\nEnd Type", "Dim t As TRecord\r\nDebug.Print t.c\r\nt.c = 2\r\nDebug.Print t.c", DisplayName = "A field")]
    [DataRow("Private Sub Show(ByVal c As Colour)\r\nDebug.Print c\r\nEnd Sub", "Show 0\r\nShow 2", DisplayName = "A ByVal parameter")]
    [DataRow("Private Sub Paint(ByRef c As Colour)\r\nDebug.Print c\r\nc = 2\r\nEnd Sub", "Dim v As Colour\r\nPaint v\r\nDebug.Print v", DisplayName = "A ByRef parameter")]
    [DataRow("Private Function Pick(ByVal n As Long) As Colour\r\nIf n > 0 Then Pick = n\r\nEnd Function", "Debug.Print Pick(0)\r\nDebug.Print Pick(2)", DisplayName = "A function's result")]
    [DataRow("Private m As Colour\r\nPrivate Property Get Pr() As Colour\r\nPr = m\r\nEnd Property\r\nPrivate Property Let Pr(ByVal v As Colour)\r\nm = v\r\nEnd Property",
        "Debug.Print Pr\r\nPr = 2\r\nDebug.Print Pr", DisplayName = "A property of the module, assigned by its bare name")]
    [DataRow("", "Dim c As Program.Colour\r\nDebug.Print c\r\nc = 2\r\nDebug.Print c", DisplayName = "Qualified by its own module")]
    [DataRow("", "Dim a(1 To 2) As Colour\r\nDebug.Print a(2)\r\na(2) = 2\r\nDebug.Print a(2)", DisplayName = "An element of an array")]
    public async Task AVariableOfTheModulesOwnEnum_HoldsALong_ZeroUntilAssigned(string declarations, string body)
    {
        // without the type the host reads with the Enum, assigning a variable of it raised error 13, and reading one that was never assigned raised 51.
        CollectionAssert.AreEqual(new[] { "0", "2" }, await RunAsync([], Program($"{Colour}\r\n{declarations}", body)));
    }

    [TestMethod]
    public async Task AVariableOfAnotherModulesEnum_BareOrQualified_HoldsALong()
    {
        // another module's Enum travels to the host with that module's name (Mod2.Colour); its bare name is the same Enum, so the two variables share a type.
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Enum Colour", "cRed = 1", "End Enum"));

        CollectionAssert.AreEqual(new[] { "2", "3" }, await RunAsync([], [mod2], Program(
            "Private Sub Paint(ByRef c As Mod2.Colour)\r\nc = c + 1\r\nEnd Sub",
            "Dim q As Mod2.Colour", "Dim b As Colour", "q = 2", "b = q", "Paint b", "Debug.Print q", "Debug.Print b")));
    }

    [TestMethod]
    public async Task AParameterOfAnEnumOfAModuleDefinedLater_IsALong_ItsDefaultIncluded()
    {
        // the host defines the modules one at a time: Picker names Holder's Enum before Holder is defined, and its parameter was of a type nothing knew - the
        // omitted argument's default raised 13 (MVVM's BindingManager, whose Mode is an IPropertyBinding.BindingMode).
        var picker = ("Picker", ClassModule("Picker", "Option Explicit",
            "Public Sub Pick(Optional ByVal Mode As BindingMode = BindingMode.OneWay)", "Debug.Print Mode", "End Sub"));
        var holder = ("Holder", ClassModule("Holder", "Option Explicit", "Public Enum BindingMode", "TwoWay", "OneWay", "End Enum"));

        CollectionAssert.AreEqual(new[] { "1", "0" }, await RunAsync([picker, holder], Program("",
            "Dim p As New Picker", "p.Pick", "p.Pick TwoWay")));
    }

    [TestMethod]
    public async Task AValueAssignedToIt_IsLetCoercedToLong()
    {
        // MS-VBAL §5.2.3.4: the Enum's name in an As clause means Long - 1.7 rounds to 2, 2.5 to the even 2, and a String holding a number is that number.
        CollectionAssert.AreEqual(new[] { "2", "2", "3" }, await RunAsync([], Program(Colour,
            "Dim c As Colour", "c = 1.7", "Debug.Print c", "c = 2.5", "Debug.Print c", "c = \"3\"", "Debug.Print c")));
    }

    [TestMethod]
    public async Task AVariableOfIt_IsALong_WhichIsNoObject()
    {
        // without its type the variable was of a type nothing knows, and the Set loaded and did nothing.
        var errors = await LoadErrorsAsync([], Program(Colour, "Dim c As Colour", "Set c = Nothing"));

        StringAssert.Contains(errors.Single(), "Set-coercion is statically invalid");
    }

    [TestMethod]
    public async Task AMemberOfAVariableOfIt_Loads_AndStopsAsNotYetImplemented()
    {
        // MS-VBAL §5.2.3.4 makes the variable a Long, which has no members. The type the host reads with the Enum carries the Enum's members, so the access
        // is not refused when the module loads, and it stops when it runs. Left as it is, for Mat.
        await InspectAsync([], Program(Colour, "Dim c As Colour", "Debug.Print c.cRed"), (_, result, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.NotImplemented, result.Outcome, result.ErrorMessage);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task AValueOutsideALong_AssignedToIt_IsAnOverflow()
        => await InspectAsync([], Program(Colour, "Dim c As Colour", "c = 2147483648#", "Debug.Print c"), (_, result, _) =>
        {
            Assert.AreEqual(6, result.ErrorNumber, result.ErrorMessage);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task WhatTheSweepStates_OfAValueAssignedToIt_IsOnlyTheOverflowOfOneNoLongHolds()
    {
        // a run-time error the sweep states (RuntimeFacts.Errors) is reported before the program runs. Without its type, each assignment was
        // stated as error 13, k = 7 included.
        var model = await ModelAsync([], Program(Colour, "Dim k As Colour", "k = 7", "k = 3000000000#"));

        var facts = model.Procedures.Single().Runtime;
        Assert.IsNotNull(facts, "the module was loaded, so its code was evaluated");
        var error = facts.Errors.Single();
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, error.ErrorId);
        Assert.AreEqual(9, error.Location.Range.Start.Line, "k = 3000000000# is the assignment that raises it");
    }
}
