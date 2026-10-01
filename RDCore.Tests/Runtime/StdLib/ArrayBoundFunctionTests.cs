using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <c>LBound</c> and <c>UBound</c>: the special forms (<strong>MS-VBAL §3.3.5.2</strong>) that read the bounds of
/// one dimension of an array. MS-VBAL reserves the names and gives them no semantics, so these pin the behavior
/// MS-VBA documents: the dimension counts from 1 and defaults to 1, and an array that has no such dimension —
/// including a dynamic array not yet sized — is error 9.
/// </summary>
/// <remarks>
/// The array is the test's input: a module-level variable holding one, which is the state the functions read,
/// and which a procedure-local array cannot yet be (see the ignored tests at the end).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 3.3.5.2 Special forms")]
public sealed class ArrayBoundFunctionTests
{
    // a fixed-size array of Long with the given (lower, upper) bounds, held by a module-level variable named A.
    private static IReadOnlyList<string> RunOn((int Lower, int Upper)[] bounds, params string[] body)
    {
        var variable = new VBModuleFieldVariableMemberSymbol(
            TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, "A", ScopeKind.Module,
            new VBFixedSizeArrayType(VBLongType.TypeInfo), SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
        var array = new VBFixedSizeArrayValue(bounds, VBLongType.TypeInfo);

        var output = new RuntimeOutputBuffer();
        RuntimeSourceHarness.Run(
            fileSystem: null, [variable], output, standardLibrary: true,
            session => session.Symbols.Resolver.GetValue(variable).SetValue(
                session.Symbols.Resolver, new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))),
            body);
        return output.Lines;
    }

    private static IReadOnlyList<string> Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, body);
        return output.Lines;
    }

    private static string[] Trimmed(IReadOnlyList<string> output) => [.. output.Select(line => line.Trim())];

    [TestMethod]
    public void AnArrayWithExplicitBounds_ReportsThem()
    {
        var output = RunOn([(1, 5)], "Debug.Print LBound(A)", "Debug.Print UBound(A)");

        CollectionAssert.AreEqual(new[] { "1", "5" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void AnArrayThatStartsAtZero_ReportsIt()
    {
        var output = RunOn([(0, 3)], "Debug.Print LBound(A)", "Debug.Print UBound(A)");

        CollectionAssert.AreEqual(new[] { "0", "3" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void EachDimensionOfAMultiDimensionalArray_HasItsOwnBounds()
    {
        var output = RunOn(
            [(1, 2), (0, 9)],
            "Debug.Print LBound(A, 1)",
            "Debug.Print UBound(A, 1)",
            "Debug.Print LBound(A, 2)",
            "Debug.Print UBound(A, 2)");

        CollectionAssert.AreEqual(new[] { "1", "2", "0", "9" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void TheLBoundDocumentationTable_IncludingANegativeLowerBound()
    {
        // learn.microsoft.com LBound function, Remarks: an array A with a lower bound of 1, 0 and -3 in its three
        // dimensions, and an upper bound of 100, 3 and 4 (the UBound page's table).
        var output = RunOn(
            [(1, 100), (0, 3), (-3, 4)],
            "Debug.Print LBound(A, 1)",
            "Debug.Print LBound(A, 2)",
            "Debug.Print LBound(A, 3)",
            "Debug.Print UBound(A, 1)",
            "Debug.Print UBound(A, 2)",
            "Debug.Print UBound(A, 3)");

        CollectionAssert.AreEqual(new[] { "1", "0", "-3", "100", "3", "4" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void TheDocumentationExample_MyArray()
    {
        // the examples of both pages: Dim MyArray(1 To 10, 5 To 15, 10 To 20).
        var output = RunOn(
            [(1, 10), (5, 15), (10, 20)],
            "Debug.Print LBound(A, 1)",
            "Debug.Print LBound(A, 3)",
            "Debug.Print UBound(A, 1)",
            "Debug.Print UBound(A, 3)");

        CollectionAssert.AreEqual(new[] { "1", "10", "10", "20" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    [Ignore("Split is declared but not implemented yet, so there is no array-returning expression to ask. The pages " +
        "call the argument the \"name of the array variable\", but UBound(Split(...)) is how the keyword is most used. " +
        "Un-ignore when Split is written.")]
    public void TheArgument_MayBeAnExpressionThatYieldsAnArray()
    {
        var output = Run("Debug.Print UBound(Split(\"a b c\"))");

        CollectionAssert.AreEqual(new[] { "2" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void TheDimensionDefaultsToTheFirst()
    {
        var output = RunOn([(1, 2), (0, 9)], "Debug.Print UBound(A)", "Debug.Print UBound(A, 1)");

        CollectionAssert.AreEqual(new[] { "2", "2" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void TheBoundsOfAnArray_DriveALoopOverIt()
    {
        var output = RunOn(
            [(1, 3)],
            "Dim i As Long",
            "Dim total As Long",
            "For i = LBound(A) To UBound(A)",
            "total = total + i",
            "Next",
            "Debug.Print total");

        CollectionAssert.AreEqual(new[] { "6" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    [DataRow("UBound(A, 0)", DisplayName = "dimension 0")]
    [DataRow("UBound(A, 2)", DisplayName = "a dimension the array does not have")]
    [DataRow("LBound(A, -1)", DisplayName = "a negative dimension")]
    public void ADimensionTheArrayDoesNotHave_IsSubscriptOutOfRange(string call)
    {
        var output = RunOn([(1, 5)], "On Error Resume Next", $"Debug.Print {call}", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { ((int)VBRuntimeErrorId.SubscriptOutOfRange).ToString() }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void ANonArrayArgument_IsATypeMismatch()
    {
        var output = Run("On Error Resume Next", "Debug.Print UBound(5)", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { ((int)VBRuntimeErrorId.TypeMismatch).ToString() }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void ADynamicArrayNotYetSized_IsSubscriptOutOfRange()
    {
        var variable = new VBModuleFieldVariableMemberSymbol(
            TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, "A", ScopeKind.Module,
            new VBResizableArrayType(VBLongType.TypeInfo), SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

        var output = new RuntimeOutputBuffer();
        RuntimeSourceHarness.Run(
            fileSystem: null, [variable], output, standardLibrary: true,
            "On Error Resume Next", "Debug.Print UBound(A)", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { ((int)VBRuntimeErrorId.SubscriptOutOfRange).ToString() }, Trimmed(output.Lines), string.Join(" / ", output.Lines));
    }

    // the forms a program is written in. A procedure-local array keeps only the name "Array" on its way to the
    // environment host - not that it is an array, nor fixed-size or resizable, nor its element type - so the
    // local reaches the function as an unknown value, and `a(1)` on it is an internal error before any bound is
    // asked for. Un-ignore with the descriptor that carries the array's declared type (and a fixed-size array's
    // bounds) to the host.
    private const string LocalArrayGap =
        "A procedure-local array loses its declared type on the way to the host (SymbolDescriptorProjector projects " +
        "every array type as the bare name \"Array\"), so the local is an unknown value: `Debug.Print a(1)` is already " +
        "an internal error. Un-ignore when a local array's type, element type and fixed-size bounds reach the host.";

    [TestMethod]
    [Ignore(LocalArrayGap)]
    public void ALocalArrayDeclaredWithBounds_ReportsThem()
    {
        var output = Run("Dim a(1 To 5) As Long", "Debug.Print LBound(a)", "Debug.Print UBound(a)");

        CollectionAssert.AreEqual(new[] { "1", "5" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    [Ignore(LocalArrayGap)]
    public void ALocalArrayDeclaredByItsUpperBound_StartsAtZero()
    {
        var output = Run("Dim a(3) As Long", "Debug.Print LBound(a)", "Debug.Print UBound(a)");

        CollectionAssert.AreEqual(new[] { "0", "3" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    [Ignore(LocalArrayGap)]
    public void ALocalDynamicArray_ReportsTheBoundsItWasSizedTo()
    {
        var output = Run("Dim a() As Long", "ReDim a(2 To 7)", "Debug.Print LBound(a)", "Debug.Print UBound(a)");

        CollectionAssert.AreEqual(new[] { "2", "7" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    [Ignore(LocalArrayGap)]
    public void ALocalDynamicArrayNotYetSized_IsSubscriptOutOfRange()
    {
        var output = Run("On Error Resume Next", "Dim a() As Long", "Debug.Print UBound(a)", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { ((int)VBRuntimeErrorId.SubscriptOutOfRange).ToString() }, Trimmed(output), string.Join(" / ", output));
    }
}
