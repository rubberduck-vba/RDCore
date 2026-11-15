using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Workspace;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>rdcore.changeProject</c>: the change of the project a diagnostic's fix says, made of the project of the workspace as it is.
/// </summary>
[TestClass]
public sealed class ChangeProjectCommandTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-changeproject-ws");
    private static readonly string Sheet1 = DocumentUri.FromFileSystemPath(Path.Combine(Root, "Sheet1.doccls")).ToString();

    private readonly IWorkspaceReloadService _reload = Substitute.For<IWorkspaceReloadService>();
    private readonly IServerCommandContext _context = Substitute.For<IServerCommandContext>();

    // the project the change is made of, and what the change made of it.
    private ProjectFile _project = new(Root, new RDCoreProject
    {
        References = [new RDCoreReference { Name = "VBA" }],
        Modules = [new RDCoreModule { RelativeUri = "Sheet1.doccls", Super = DocClassType.ExcelWorksheet }],
    });

    private ProjectFile? _changed;

    public ChangeProjectCommandTests()
        => _reload.ChangeAsync(Arg.Any<Func<ProjectFile, ProjectFile>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _changed = call.Arg<Func<ProjectFile, ProjectFile>>()(_project);
            return Task.FromResult<WorkspaceReloadResult?>(WorkspaceReloadResult.Unchanged);
        });

    private ChangeProjectCommand Sut() => new(_reload, new InMemoryLibrarySource(new LibraryDescription
    {
        Name = "Excel",
        Origin = new LibraryOrigin { Guid = "{00020813-0000-0000-C000-000000000046}", Version = "1.9" },
    }), Substitute.For<ILanguageServerFacade>(), NullLogger<ChangeProjectCommand>.Instance);

    private static JArray Arguments(params ProjectChange[] changes)
        => new(Sheet1, JObject.FromObject(new ProjectFix { Title = "Fix it", Changes = changes }));

    [TestMethod]
    public async Task TheProject_IsChangedAsTheFixSays_AndWritten()
    {
        await Sut().ExecuteAsync(Arguments(ProjectChange.AddReference("Excel")), _context, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "VBA", "Excel" }, _changed!.ProjectInfo.References.Select(reference => reference.Name).ToArray());
        Assert.AreEqual(9, _changed.ProjectInfo.References[1].Minor);
    }

    [TestMethod]
    public async Task AChangeThatCannotBeMadeOfTheProjectAsItIsNow_IsRefused_AndChangesNothing()
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            var refused = await Assert.ThrowsAsync<ServerCommandRefusedException>(
                () => Sut().ExecuteAsync(Arguments(ProjectChange.AddReference("Excel"), ProjectChange.AddReference("Word")), _context, CancellationToken.None));

            Assert.AreEqual("The project cannot reference 'Word': the platform has no description of that library.", refused.Message);
            Assert.AreSame(_project, _changed, "a change that cannot be made leaves the project as it is: nothing is written");
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [TestMethod]
    public async Task AModuleTheProjectDoesNotListNow_IsNamedByItsPath_InTheRefusal()
    {
        _project = _project with { ProjectInfo = _project.ProjectInfo with { Modules = [] } };
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            var refused = await Assert.ThrowsAsync<ServerCommandRefusedException>(
                () => Sut().ExecuteAsync(Arguments(ProjectChange.RemoveModule()), _context, CancellationToken.None));

            StringAssert.Contains(refused.Message, "'Sheet1.doccls'");
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
