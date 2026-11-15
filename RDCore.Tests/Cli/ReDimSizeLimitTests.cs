using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §5.4.3.3</strong> RD-VBA holds each element of an array in a cell of its own, and a <c>ReDim</c> of more than
/// <see cref="VBArrayValue.MaximumLength"/> elements is run-time error 7, <em>Out of memory</em>. MS-VBA (64-bit) allocates
/// <c>ReDim b(2147483647) As Byte</c> and a billion <c>Long</c>s (observed in Excel), where RD-VBA crashed, hung, or made an empty
/// array that every subscript was out of range of. A <c>ReDim</c> that MS-VBA can't allocate lets go of what the variable held before it fails: a declared
/// array is erased and a Variant is Empty; a <c>ReDim Preserve</c> keeps it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
public sealed class ReDimSizeLimitTests
{
    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([], string.Join("\r\n",
            [
                "Attribute VB_Name = \"Program\"",
                "Option Explicit",
                "Public Sub Main()",
                "    On Error Resume Next",
                .. body,
                "End Sub",
            ]) + "\r\n");

    // MS-VBA can't allocate 65535³ Integers and lets go of the array, as it does of 2147483648 Longs. It does
    // allocate the Bytes and the billion Longs, and the others are a few dozen megabytes. Every Long subscript of Variants is a deliberate
    // divergence: MS-VBA accepts the bounds and holds no elements, as its count wraps, and RD-VBA raises error 7 and erases the array there, the
    // same as for an array MS-VBA can't allocate.
    [TestMethod]
    [DataRow("Byte", "2147483647", DisplayName = "2,147,483,648 Bytes")]
    [DataRow("Byte", "2147483646", DisplayName = "2,147,483,647 Bytes")]
    [DataRow("Long", "1000000000", DisplayName = "a billion Longs")]
    [DataRow("Long", "16777216", DisplayName = "one more than the most")]
    [DataRow("Variant", "-2147483648 To 2147483647", DisplayName = "every Long subscript, deliberately not MS-VBA's")]
    [DataRow("Integer", "4096, 4096", DisplayName = "two dimensions")]
    [DataRow("Integer", "65535, 65535, 65535", DisplayName = "three dimensions")]
    public async Task AReDimOfMoreElementsThanRDVBAHolds_IsOutOfMemory_AndTheArrayIsErased(string type, string bounds)
        => CollectionAssert.AreEqual(new[] { "7", "9", $"{type}()" },
            await RunAsync("Dim b() As " + type, "Dim n As Long", "ReDim b(2)", "b(2) = 3", $"ReDim b({bounds})", "Debug.Print Err.Number", "Err.Clear",
                "n = UBound(b)", "Debug.Print Err.Number", "Debug.Print TypeName(b)"));

    [TestMethod]
    public async Task AReDimPreserveOfMoreElementsThanRDVBAHolds_IsOutOfMemory_AndTheArrayIsKept()
        => CollectionAssert.AreEqual(new[] { "7", "2", "3" },
            await RunAsync("Dim b() As Byte", "ReDim b(2)", "b(2) = 3", "ReDim Preserve b(2147483647)", "Debug.Print Err.Number", "Err.Clear",
                "Debug.Print UBound(b)", "Debug.Print b(2)"));

    [TestMethod]
    public async Task AReDimOfAVariantToMoreElementsThanRDVBAHolds_IsOutOfMemory_AndTheVariantIsEmpty()
        => CollectionAssert.AreEqual(new[] { "7", "Empty" },
            await RunAsync("Dim v As Variant", "ReDim v(1) As Integer", "ReDim v(2147483647)", "Debug.Print Err.Number", "Err.Clear",
                "Debug.Print TypeName(v)"));

    [TestMethod]
    [DataRow("Dim b(100000000) As Byte", DisplayName = "a local (C12)")]
    [DataRow("Dim b(1 To 4097, 1 To 4097) As Integer", DisplayName = "two dimensions")]
    [DataRow("Static b(16777216) As Byte", DisplayName = "a Static local")]
    [DataRow("Dim b(16777216) As Byte", DisplayName = "one more than the most, from the Option Base 0")]
    [DataRow("Dim b(4096, 4095) As Byte", DisplayName = "two dimensions from the Option Base 0")]
    [DataRow("Const N As Long = 100000000\r\n    Dim b(N) As Byte", DisplayName = "bounds that name a constant")]
    [DataRow("Const N As Long = 16777216\r\n    Dim b(N) As Byte", DisplayName = "a bound that names a constant, from the Option Base 0")]
    [DataRow("Const N As Long = 4096\r\n    Dim b(N, 1 To 4097) As Byte", DisplayName = "a bound that names a constant beside a literal")]
    public async Task AFixedSizeArrayOfMoreElementsThanRDVBAHolds_IsOutOfMemory_WhenItIsCompiled(string declaration)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    {declaration}\r\nEnd Sub\r\n");

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        Assert.StartsWith("Out of memory", errors[0]);
    }

    [TestMethod]
    [DataRow("Private b(100000000) As Byte", DisplayName = "of the module")]
    [DataRow("Private Type TBig\r\n    b(100000000) As Byte\r\nEnd Type", DisplayName = "a field of a record")]
    public async Task AFixedSizeArrayOfAModuleOfMoreElementsThanRDVBAHolds_IsOutOfMemory_WhenItIsCompiled(string declaration)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declaration}\r\nPublic Sub Main()\r\nEnd Sub\r\n");

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        Assert.StartsWith("Out of memory", errors[0]);
    }

    [TestMethod]
    public async Task AFixedSizeArrayOfTheMostElementsRDVBAHolds_Compiles()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim b(1 To 16777216) As Byte\r\nEnd Sub\r\n");

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    [DataRow("Dim b(16777216) As Byte", DisplayName = "literal bounds")]
    [DataRow("Dim b(4096, 4096) As Byte", DisplayName = "two dimensions")]
    [DataRow("Const N As Long = 16777216\r\n    Dim b(N) As Byte", DisplayName = "a bound that names a constant")]
    public async Task AFixedSizeArrayOfTheMostElementsRDVBAHolds_FromTheOptionBase1_Compiles(string declaration)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nOption Base 1\r\nPublic Sub Main()\r\n    {declaration}\r\nEnd Sub\r\n");

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    [DataRow("    b(16777216) As Byte", DisplayName = "literal bounds")]
    [DataRow("    b(N) As Byte", DisplayName = "a bound that names a constant")]
    public async Task AFieldOfARecord_IsBoundedFrom0_WhateverTheOptionBase(string field)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([],
            $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nOption Base 1\r\nPrivate Const N As Long = 16777216\r\nPrivate Type TBig\r\n{field}\r\nEnd Type\r\nPublic Sub Main()\r\nEnd Sub\r\n");

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        Assert.StartsWith("Out of memory", errors[0]);
    }

    [TestMethod]
    public void TheLengthOfBounds_IsCountedPastWhatAnInt32Holds()
    {
        Assert.AreEqual(VBArrayValue.MaximumLength + 1, VBArrayValue.LengthOf([(0, int.MaxValue)]));
        Assert.AreEqual(VBArrayValue.MaximumLength + 1, VBArrayValue.LengthOf([(int.MinValue, int.MaxValue), (int.MinValue, int.MaxValue), (int.MinValue, int.MaxValue)]));
        Assert.AreEqual(12L, VBArrayValue.LengthOf([(1, 3), (0, 3)]));
        Assert.AreEqual(0L, VBArrayValue.LengthOf([(0, int.MaxValue), (5, 4)]));
    }
}
