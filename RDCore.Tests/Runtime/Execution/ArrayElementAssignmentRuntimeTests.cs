using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Assignment to an element of an array, <c>A(i) = value</c> and <c>Set A(i) = object</c> (<strong>MS-VBAL §5.4.3.8</strong>,
/// <strong>§5.4.3.9</strong>): the value is coerced to the element type of the array, and a subscript outside its bounds is error 9.
/// </summary>
/// <remarks>
/// The array is a module-level variable named <c>A</c>, the state the code under test starts from, as it is for
/// <see cref="ArrayBoundRuntimeTests"/>.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class ArrayElementAssignmentRuntimeTests
{
    private static IReadOnlyList<string> RunOn(VBType elementType, (int Lower, int Upper)[] bounds, params string[] body)
    {
        var variable = new VBModuleFieldVariableMemberSymbol(
            TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, "A", ScopeKind.Module,
            new VBFixedSizeArrayType(elementType), SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
        var array = new VBFixedSizeArrayValue(bounds, elementType);

        var output = new RuntimeOutputBuffer();
        RuntimeSourceHarness.Run(
            fileSystem: null, [variable], output, standardLibrary: true,
            session => session.Symbols.Resolver.GetValue(variable).SetValue(
                session.Symbols.Resolver, new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))),
            body);
        return output.Lines;
    }

    private static string[] Trimmed(IReadOnlyList<string> output) => [.. output.Select(line => line.Trim())];

    [TestMethod]
    public void AResizableArrayDeclaredAsLong_IsAnArrayOfLong_OnceItIsReDimmed()
    {
        // `Dim A() As Long`: the uninitialized array every such variable starts as knows nothing of its element type, which
        // is the declaration's to say - an element nothing was assigned to is a Long's default, not an Empty Variant's.
        var variable = new VBModuleFieldVariableMemberSymbol(
            TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, "A", ScopeKind.Module,
            new VBResizableArrayType(VBLongType.TypeInfo), SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

        var output = new RuntimeOutputBuffer();
        RuntimeSourceHarness.Run(fileSystem: null, [variable], output, standardLibrary: true, "ReDim A(1 To 2)", "A(1) = 3", "Debug.Print A(1)", "Debug.Print A(2)");

        // an Empty element prints as nothing at all.
        CollectionAssert.AreEqual(new[] { "3", "0" }, Trimmed(output.Lines), string.Join(" / ", output.Lines));
    }

    [TestMethod]
    public void AssigningAnElement_DoesNotCopyTheArray()
    {
        // a regression guard: an element is written in place, so what it costs must not depend on how big the array is.
        // Copying the cells of an array of a hundred thousand is 800 KB per assignment; the budget catches that many times over.
        const int Cells = 100_000;
        const int Iterations = 200;
        string[] loop(int count) =>
        [
            "Dim i As Long",
            $"For i = 1 To {count}",
            "A(i) = i",
            "Next",
        ];

        long Allocated(int count)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            RunOn(VBLongType.TypeInfo, [(1, Cells)], loop(count));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Allocated(1);
        var perAssignment = (Allocated(Iterations + 1) - Allocated(1)) / (2d * Iterations);

        Assert.IsLessThan(16 * 1024d, perAssignment, $"each assignment allocated {perAssignment:N0} bytes - the array is being copied");
    }

    [TestMethod]
    public void AnElement_IsAssigned_AndReadBack()
    {
        var output = RunOn(VBLongType.TypeInfo, [(1, 3)], "A(2) = 42", "Debug.Print A(2)", "Debug.Print A(1)");

        CollectionAssert.AreEqual(new[] { "42", "0" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void EachElement_HasItsOwnCell()
    {
        var output = RunOn(VBLongType.TypeInfo, [(0, 2)], "A(0) = 1", "A(1) = 2", "A(2) = 3", "Debug.Print A(0) + A(1) * 10 + A(2) * 100");

        CollectionAssert.AreEqual(new[] { "321" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void AnElementOfAMultiDimensionalArray_IsSelectedByAllItsSubscripts()
    {
        var output = RunOn(VBLongType.TypeInfo, [(1, 2), (1, 2)], "A(1, 2) = 12", "A(2, 1) = 21", "Debug.Print A(1, 2)", "Debug.Print A(2, 1)", "Debug.Print A(1, 1)");

        CollectionAssert.AreEqual(new[] { "12", "21", "0" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void TheValue_IsLetCoercedToTheElementType()
    {
        // a String that reads as a number is a Long once it is an element of an array of Long.
        var output = RunOn(VBLongType.TypeInfo, [(1, 1)], "A(1) = \"7\"", "Debug.Print A(1) + 1");

        CollectionAssert.AreEqual(new[] { "8" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void AnElementOfAnArrayOfStrings_TakesTheTextOfANumber()
    {
        var output = RunOn(VBStringType.TypeInfo, [(1, 1)], "A(1) = 5", "Debug.Print A(1) & \"!\"");

        CollectionAssert.AreEqual(new[] { "5!" }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void ASubscriptOutsideTheBounds_IsError9()
    {
        var output = RunOn(
            VBLongType.TypeInfo, [(1, 3)],
            "On Error Resume Next", "A(4) = 1", "Debug.Print Err.Number", "Err.Clear", "A(0) = 1", "Debug.Print Err.Number");

        var expected = ((int)VBRuntimeErrorId.SubscriptOutOfRange).ToString();
        CollectionAssert.AreEqual(new[] { expected, expected }, Trimmed(output), string.Join(" / ", output));
    }

    [TestMethod]
    public void AnElement_CanBeAssignedFromAnotherElement()
    {
        var output = RunOn(VBLongType.TypeInfo, [(1, 2)], "A(1) = 9", "A(2) = A(1) + 1", "Debug.Print A(2)");

        CollectionAssert.AreEqual(new[] { "10" }, Trimmed(output), string.Join(" / ", output));
    }
}
