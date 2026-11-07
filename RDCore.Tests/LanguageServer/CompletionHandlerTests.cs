using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Completion;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What the server says it serves of <strong>LSP 3.17</strong> §Completion Request.
/// </summary>
[TestClass]
public sealed class CompletionHandlerTests
{
    private static CompletionRegistrationOptions OptionsOf(CompletionHandler handler)
        => ((IRegistration<CompletionRegistrationOptions, CompletionCapability>)handler).GetRegistrationOptions(new CompletionCapability(), new ClientCapabilities());

    [TestMethod]
    public void ADot_AsksForTheMembers()
        => CollectionAssert.Contains(OptionsOf(new CompletionHandler(Substitute.For<ICompletionService>())).TriggerCharacters!.ToArray(), ".");

    // what follows a `!` is the name of a dictionary access (MS-VBAL §5.6.14), which nothing declares: there is no list to ask for.
    [TestMethod]
    public void ABang_AsksForNothing()
        => CollectionAssert.DoesNotContain(OptionsOf(new CompletionHandler(Substitute.For<ICompletionService>())).TriggerCharacters!.ToArray(), "!");
}
