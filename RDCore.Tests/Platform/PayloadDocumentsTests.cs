using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;
using System.Text.Json;

namespace RDCore.Tests.Platform;

/// <summary>
/// A <see cref="PlatformJson"/> payload writes the document of a location or a node's identity where it changes, and not in every node, and is read back to
/// the same model: every location and identity has its document again (RD-VBAL §2.0.2).
/// </summary>
[TestClass]
public sealed class PayloadDocumentsTests
{
    private static readonly Uri Uri = new(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

    private static string Program()
        => "Attribute VB_Name = \"Program\"\r\n" + string.Concat(Enumerable.Range(1, 20).Select(i =>
            $"Public Function F{i}(ByVal a As Long) As Long\r\n    Dim b As Long\r\n    b = a + {i}\r\n    F{i} = b\r\nEnd Function\r\n"));

    // the model as written with every document: two models are the same when their full forms are.
    private static string FullForm<T>(T model) => JsonSerializer.Serialize(model, SyntaxNodeJson.Options);

    [TestMethod]
    public void AParseResult_IsReadBackTheSame()
    {
        var parsed = new ModuleParser().Parse(Uri, Program());

        var back = PlatformJson.Deserialize<ModuleParseResult>(PlatformJson.Serialize(parsed));

        Assert.AreEqual(FullForm(parsed), FullForm(back));
        Assert.AreEqual(parsed.SyntaxTree!.Descendants().Last().SourceLocation.Uri.OriginalString, back.SyntaxTree!.Descendants().Last().SourceLocation.Uri.OriginalString);
    }

    [TestMethod]
    public void APayloadOfTwoModules_IsReadBackTheSame()
    {
        // two modules, so that the document changes along the payload, and back.
        var payload = ModuleWorkspace.SemanticsAsync([], Program(), modules: [("Other", ModuleWorkspace.StandardModule("Other", "Public Function G() As Long", "    G = F1(1)", "End Function"))])
            .GetAwaiter().GetResult();

        var back = PlatformJson.Deserialize<SemanticsPayload>(PlatformJson.Serialize(payload));

        Assert.IsGreaterThan(1, payload.Modules.Length);
        Assert.AreEqual(FullForm(payload), FullForm(back));
    }

    [TestMethod]
    public void TheDocument_IsWrittenOncePerForm_InAPayload_AndInEveryNode_Outside()
    {
        var parsed = new ModuleParser().Parse(Uri, Program());
        var path = Uri.OriginalString;
        static int Count(string text, string sought) => (text.Length - text.Replace(sought, string.Empty).Length) / sought.Length;

        var payload = PlatformJson.Serialize(parsed);
        var outside = FullForm(parsed);

        // a location writes the path as its Uri has it, once in the payload; outside it, every location does.
        var written = JsonEncodedText.Encode(path).ToString();
        Assert.AreEqual(1, Count(payload, written));
        Assert.IsGreaterThan(100, Count(outside, written));
    }
}
