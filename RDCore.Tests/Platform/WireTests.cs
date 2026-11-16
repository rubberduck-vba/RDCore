using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Platform;

/// <summary>
/// What a round trip of the symbols of a module that is not read back says of itself: which end's buffer was overwritten.
/// </summary>
[TestClass]
public sealed class WireTests
{
    private static readonly DefineSymbolsParams Symbols = new() { ModuleName = "Module1" };

    [TestMethod]
    public void SymbolsWrittenAsJson_SayTheReadersBufferWasOverwritten()
    {
        var failure = Wire.FailureOf(Symbols, LspSerializer.Instance.SerializeObject(Symbols));

        StringAssert.Contains(failure, "module Module1");
        StringAssert.Contains(failure, "are JSON, as a new serialization writes them");
        StringAssert.Contains(failure, "the reader's buffer");
    }

    [TestMethod]
    public void SymbolsWrittenAsSomethingElse_SayTheWritersBufferWasOverwritten_AndHoldWhatWasWrittenBesideANewSerialization()
    {
        var written = LspSerializer.Instance.SerializeObject(Symbols).Replace("\"[]\"", "\"[{\\\"Name\\\":\"", StringComparison.Ordinal);

        var failure = Wire.FailureOf(Symbols, written);

        StringAssert.Contains(failure, "are not JSON");
        StringAssert.Contains(failure, "the writer's buffer");
        StringAssert.Contains(failure, "Written: [{\"Name\":");
        StringAssert.Contains(failure, "Written again: []");
    }
}
