using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The symbols of events: an <c>Event</c> declaration (<strong>MS-VBAL §5.2.4.3</strong>), a <c>WithEvents</c> variable
/// (<strong>§5.2.3.1.2</strong>) and the procedures that handle an event of it (<strong>§5.3.1.8</strong>), built from
/// real source through <c>WorkspaceSymbolResolver</c>.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.4.3 Event Declaration")]
public sealed class EventSymbolTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Class(string name, string body)
        => (ModuleUri(name), ModuleType.ClassModule, new ModuleParser().Parse(
            new Uri($"file:///c:/ws/{name}.cls"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    private static VBClassModuleSymbol Compose(string name, params (Uri, ModuleType, ModuleParseResult)[] modules)
        => Assert.IsInstanceOfType<VBClassModuleSymbol>(
            WorkspaceSymbolResolver.Compose(Root, modules, new IntrinsicSymbolResolver())
                .ResolveType(name, ScopeKind.Global, ModuleUri(name)).Symbol);

    private const string SourceBody =
        "Public Event Changed(ByVal Value As Long)\r\n" +
        "Event Closed()\r\n" +
        "Public Sub Touch()\r\nRaiseEvent Changed(1)\r\nEnd Sub\r\n";

    private const string SinkBody =
        "Private WithEvents Src As Source\r\n" +
        "Private Plain As Source\r\n" +
        "Private Sub Src_Changed(ByVal Value As Long)\r\nEnd Sub\r\n" +
        "Private Sub Plain_Changed(ByVal Value As Long)\r\nEnd Sub\r\n";

    [TestMethod]
    public void AnEventDeclaration_IsAnEventOfItsClass_WithItsParameters()
    {
        var source = Compose("Source", Class("Source", SourceBody));

        CollectionAssert.AreEquivalent(new[] { "Changed", "Closed" }, source.Events.Select(declared => declared.Name).ToArray());
        var changed = source.FindEvent("changed");
        Assert.IsNotNull(changed, "an event is found without regard to case");
        Assert.AreEqual("Value", changed.Parameters.Single().Name);
        Assert.IsEmpty(source.FindEvent("Closed")!.Parameters);
        Assert.IsNull(source.FindEvent("Opened"));
    }

    [TestMethod]
    public void AnEventDeclaredWithoutAnAccessModifier_IsPublic()
    {
        // MS-VBAL §5.2.4.3: it has the same meaning as if Public was written - nothing here makes it Private.
        var closed = Compose("Source", Class("Source", SourceBody)).FindEvent("Closed")!;

        Assert.AreNotEqual(AccessModifier.Private, closed.AccessModifier);
    }

    [TestMethod]
    public void AWithEventsVariable_IsFlagged_AndAPlainVariableIsNot()
    {
        var sink = Compose("Sink", Class("Source", SourceBody), Class("Sink", SinkBody));

        Assert.AreEqual("Src", sink.WithEventsVariables.Single().Name);
        var plain = sink.Members.Single(member => member.Name == "Plain");
        Assert.IsFalse(plain.GetProperty(SymbolProperties.WithEvents));
    }

    [TestMethod]
    public void TheProcedureNamedForTheVariableAndTheEvent_HandlesIt()
    {
        var source = Compose("Source", Class("Source", SourceBody));
        var sink = Compose("Sink", Class("Source", SourceBody), Class("Sink", SinkBody));
        var variable = sink.WithEventsVariables.Single();

        var handler = sink.FindEventHandler(variable, source.FindEvent("Changed")!);

        Assert.AreEqual("Src_Changed", handler?.Name);
    }

    [TestMethod]
    public void AnEventWithNoProcedureNamedForIt_IsNotHandled()
    {
        var source = Compose("Source", Class("Source", SourceBody));
        var sink = Compose("Sink", Class("Source", SourceBody), Class("Sink", SinkBody));

        Assert.IsNull(sink.FindEventHandler(sink.WithEventsVariables.Single(), source.FindEvent("Closed")!));
    }
}
