using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// An object written to a file, or printed: a <c>Variant</c> that holds one is no record of <c>Put</c>, and <c>Print #</c>, <c>Write #</c> and
/// <c>Debug.Print</c> write what it stands for - its default member's value - as MS-VBA does (recorded in Excel, oracle\s28 U02-U10, B11-B13, D01-D04).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.11 Put")]
[TestCategory("MS-VBAL 5.4.5.8 Print Statement")]
[TestCategory("MS-VBAL 5.4.5.9 Write Statement")]
public sealed class ObjectToFileTests
{
    private const string Root = "/ws";
    private const string Path = $"{Root}/p.bin";

    // P x prints x; what the body prints is the printed values, each followed by |.
    private static (string Printed, MockFileSystem Files) Run(string body)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), currentDirectory: Root);
        fileSystem.Directory.CreateDirectory(Root);
        var output = new RuntimeOutputBuffer();
        var lines = body.Split(" ;; ").Select(line => line.StartsWith("P ") ? "Debug.Print " + line[2..] : line).Prepend("On Error Resume Next");
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem, [], output, standardLibrary: true, [.. lines]);
        Assert.AreNotEqual(RDCore.Runtime.Execution.RuntimeExecutionOutcomeKind.Error, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return (string.Concat(output.Lines.Select(line => line.Trim() + "|")), fileSystem);
    }

    [TestMethod]
    [DataRow("Set v = New Collection", DisplayName = "U02 an object")]
    [DataRow("Set v = Nothing", DisplayName = "U06 Nothing")]
    public void Put_OfAVariantThatHoldsAnObject_IsATypeMismatch_OnceItsDescriptorIsWritten(string set)
    {
        var (printed, files) = Run($"Dim v As Variant ;; {set} ;; Open \"{Path}\" For Binary As #1 ;; Err.Clear ;; Put #1, , v ;; P Err.Number ;; Close #1");

        Assert.AreEqual("13|", printed);
        CollectionAssert.AreEqual(new byte[] { 9, 0 }, files.File.ReadAllBytes(Path), "vbObject, the descriptor of the Variant (B11, B12)");
    }

    // U05: a Random record is not written at all.
    [TestMethod]
    public void Put_OfAVariantThatHoldsAnObject_ToARandomFile_WritesNothing()
    {
        var (printed, files) = Run($"Dim v As Variant ;; Set v = New Collection ;; Open \"{Path}\" For Random As #1 Len = 16 ;; Err.Clear ;; Put #1, 1, v ;; P Err.Number ;; Close #1");

        Assert.AreEqual("13|", printed);
        Assert.IsEmpty(files.File.ReadAllBytes(Path));
    }

    // U10, D04: a Collection's default member takes an argument, which writing its value does not give it; D03: Nothing has no value.
    [TestMethod]
    [DataRow("Set v = New Collection", "450|450|", DisplayName = "an object whose default member takes an argument")]
    [DataRow("Set v = Nothing", "91|91|", DisplayName = "Nothing")]
    public void WriteAndPrint_OfAnObjectWithNoValue_IsTheErrorOfReadingIt(string set, string printed)
        => Assert.AreEqual(printed, Run($"Dim v As Variant ;; {set} ;; Open \"{Path}\" For Output As #1 ;; Err.Clear ;; Write #1, v ;; P Err.Number ;; Err.Clear ;; Print #1, v ;; P Err.Number ;; Close #1").Printed);

    // D01.
    [TestMethod]
    public void DebugPrint_OfACollection_IsWrongNumberOfArguments()
        => Assert.AreEqual("450|", Run("Dim c As Object ;; Set c = New Collection ;; Err.Clear ;; Debug.Print c ;; P Err.Number").Printed);

    // D02: what an object stands for is written as text, quoted, by Write #, and printed as the value it is by Print #.
    [TestMethod]
    public void WriteAndPrint_OfAnObjectWithAValue_WriteItsValue()
    {
        var (_, files) = Run($"Err.Clear ;; Open \"{Path}\" For Output As #1 ;; Write #1, Err ;; Print #1, Err ;; Close #1");

        Assert.AreEqual("\"0\"\r\n 0 \r\n", files.File.ReadAllText(Path));
    }
}
