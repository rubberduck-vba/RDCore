using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A member of the standard library is bound once to the code behind it (IR lowering S1.9): every call of it is marshalled from what was found the first
/// time, whatever its arguments - given, left out, passed on as Missing, collected into a ParamArray - and whatever the call before it was given.
/// </summary>
[TestClass]
public sealed class StdLibCallTests
{
    private static string Run(string procedures, string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + string.Join("\r\n", procedures.Split(" ;; "))
            + "\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", RunAsync([], program).GetAwaiter().GetResult());
    }

    // Split's Limit, left out, is the -1 its declaration states; given, it is what it was given - one call after the other, in a loop.
    [TestMethod]
    public void AnOptionalArgument_LeftOutThenGiven_IsEachTimeWhatTheCallSays()
        => Assert.AreEqual("3/2/3/2", Run("", "Dim i As Long ;; For i = 1 To 2 ;; Debug.Print UBound(Split(\"a,b,c,d\", \",\")) ;; Debug.Print UBound(Split(\"a,b,c,d\", \",\", 3)) ;; Next"));

    // an Optional Variant parameter that was itself omitted passes on Missing, which the library takes as left out.
    [TestMethod]
    public void AMissingArgumentPassedOn_IsLeftOut()
        => Assert.AreEqual("bcd/bc", Run("Private Sub F(Optional L As Variant) ;; Debug.Print Mid$(\"abcd\", 2, L) ;; End Sub", "F ;; F 2"));

    [TestMethod]
    public void AParamArray_CollectsEachCallsOwnArguments()
        => Assert.AreEqual("b/c/a", Run("", "Debug.Print Choose(2, \"a\", \"b\", \"c\") ;; Debug.Print Choose(3, \"a\", \"b\", \"c\") ;; Debug.Print Choose(1, \"a\")"));

    [TestMethod]
    public void AMemberOfAClassOfTheLibrary_IsCalledOnTheObjectItIsCalledOn()
        => Assert.AreEqual("2/1", Run("", "Dim a As New Collection, b As New Collection ;; a.Add 1 ;; a.Add 2 ;; b.Add 3 ;; Debug.Print a.Count ;; Debug.Print b.Count"));
}
