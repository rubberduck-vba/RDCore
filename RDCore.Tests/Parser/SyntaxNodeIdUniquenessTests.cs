using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.Tests.Parser;

[TestClass]
public sealed class SyntaxNodeIdUniquenessTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    [TestMethod]
    // adversarial review, PRs #208-224: "SyntaxNodeId is no longer unique. Pervasive collisions at
    // head, zero before the AST delta." Root cause: GetCurrentNodeId() derived a node's id from
    // CurrentBuilder.ChildCount, but CaptureIsolatedExpression/CaptureIsolated push and pop their own
    // temporary scope per call without ever touching the OUTER builder's _children in between - so any
    // statement capturing more than one isolated expression in sequence handed every capture the exact
    // same base id. Confirmed empirically before fixing: both SimpleNameExpressionNodes in the simplest
    // possible `x = y` shared the identical id. Fixed by minting ids from a monotonic per-scope counter
    // (NodeBuilder.AllocateChildId) instead of ChildCount.
    public void SimplestAssignment_BothOperandsGetDistinctIds()
        => AssertAllIdsAreDistinct("Sub S()\r\nx = y\r\nEnd Sub");

    [TestMethod]
    public void StatementRichModule_EveryNodeGetsADistinctId()
    {
        const string source = """
            Option Explicit
            Public Field1 As Long
            Public Const K = 1

            Sub Alpha(ByVal a As Long, Optional ByVal b As Long)
                Dim total As Long
                total = a + b
                Name "old.txt" As "new.txt"
                Print #1, "x"; "y", "z"
                Open "file.txt" For Input Access Read As #1
                If total > 0 Then
                    total = total - 1
                ElseIf total < 0 Then
                    total = total + 1
                Else
                    total = 0
                End If
                For i = 1 To 10
                    total = total + i
                Next i
                Select Case total
                    Case 1
                        total = 1
                    Case 2 To 5
                        total = 2
                    Case Else
                        total = 0
                End Select
                With Nothing
                    x = .Foo
                End With
            End Sub
            """;

        AssertAllIdsAreDistinct(source);
    }

    private static void AssertAllIdsAreDistinct(string source)
    {
        var result = new ModuleParser().Parse(Uri, source);
        Assert.IsNotNull(result.SyntaxTree, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0]!.Description);

        var ids = new List<SyntaxNodeId>();
        void Walk(SyntaxNode node)
        {
            ids.Add(node.Identity);
            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }
        Walk(result.SyntaxTree!);

        var duplicates = ids.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        Assert.IsEmpty(duplicates, $"duplicate SyntaxNodeId(s): {string.Join(", ", duplicates)}");
    }
}
