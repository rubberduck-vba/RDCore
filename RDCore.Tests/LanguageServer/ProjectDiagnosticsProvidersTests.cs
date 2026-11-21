using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Diagnostics;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The language server asks each extension that provides <see cref="DiagnoseProject"/> what its project analyzers find of a project, and no other; one that
/// fails does not sink the others' findings. The core extension provides it.
/// </summary>
[TestClass]
public sealed class ProjectDiagnosticsProvidersTests
{
    private static readonly DiagnoseProjectPayload Payload = new(new Uri("file:///c:/ws/.rdproj"), new RDCoreProject { Name = RDCoreProject.DefaultName });

    private readonly IPlatformOrchestrationService _orchestration = Substitute.For<IPlatformOrchestrationService>();

    private static ExtensionInfo Manifest(string title, string capability) => new()
    {
        Name = $"{title}.exe",
        Title = title,
        Version = new Version(1, 0),
        Publisher = "publisher",
        PublisherWebUrl = "https://example.test",
        Description = "desc",
        FileHash = "hash",
        Capabilities = [new PlatformExtensionServerCapability(capability)],
        DiagnosticPrefix = "RDC",
        IsCoreExtension = true,
    };

    private static IRDCoreClientApp Extension(string title, string capability, params Diagnostic[] diagnostics)
    {
        var app = Substitute.For<IRDCoreClientApp>();
        app.ExtensionInfo.Returns(Manifest(title, capability));
        app.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        app.SendRequestAsync<DiagnoseProjectParams, DiagnoseProjectResult>(Arg.Any<DiagnoseProjectParams>(), Arg.Any<CancellationToken>())
            .Returns(new DiagnoseProjectResult { Diagnostics = diagnostics });
        return app;
    }

    [TestMethod]
    public void TheCoreExtension_ProvidesDiagnoseProject()
        => CollectionAssert.Contains(
            ProvidedCorePlatformCapabilities.Reflect(typeof(RDCore.Diagnostics.Program).Assembly).ToList(), nameof(DiagnoseProject));

    [TestMethod]
    public async Task OnlyAnExtensionThatProvidesIt_IsAsked_WithThePayload()
    {
        var provider = Extension("RDCore.Diagnostics", nameof(DiagnoseProject), new Diagnostic { Code = "RDC01024", Message = "default" });
        var other = Extension("Other", nameof(DiagnoseDocument));
        _orchestration.Extensions.Returns([provider, other]);

        var found = await new ProjectDiagnosticsProviders(_orchestration, NullLogger<ProjectDiagnosticsProviders>.Instance)
            .DiagnoseAsync(Payload, CancellationToken.None);

        Assert.AreEqual("RDC01024", found.Single().Code?.String);
        await provider.Received(1).SendRequestAsync<DiagnoseProjectParams, DiagnoseProjectResult>(
            Arg.Is<DiagnoseProjectParams>(request => PlatformJson.Deserialize<DiagnoseProjectPayload>(request.Json).Project.Name == "VBAProject"),
            Arg.Any<CancellationToken>());
        await other.DidNotReceiveWithAnyArgs().SendRequestAsync<DiagnoseProjectParams, DiagnoseProjectResult>(default!, default);
    }

    [TestMethod]
    public async Task AnExtensionThatFails_DoesNotSinkTheOthers()
    {
        var failing = Extension("Failing", nameof(DiagnoseProject));
        failing.SendRequestAsync<DiagnoseProjectParams, DiagnoseProjectResult>(Arg.Any<DiagnoseProjectParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no"));
        var answering = Extension("RDCore.Diagnostics", nameof(DiagnoseProject), new Diagnostic { Code = "RDC01024" });
        _orchestration.Extensions.Returns([failing, answering]);

        var found = await new ProjectDiagnosticsProviders(_orchestration, NullLogger<ProjectDiagnosticsProviders>.Instance)
            .DiagnoseAsync(Payload, CancellationToken.None);

        Assert.AreEqual("RDC01024", found.Single().Code?.String);
    }
}
