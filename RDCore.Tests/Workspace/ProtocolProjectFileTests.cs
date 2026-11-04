using RDCore.SDK.Workspace;
using System.Text.Json;

namespace RDCore.Tests.Workspace;

/// <summary>
/// The project file model is in <c>RDCore.SDK.Protocol</c>, which a client of the platform references alone: an IDE's add-in reads a <c>.rdproj</c>
/// with it, and with nothing of the platform.
/// </summary>
[TestClass]
public sealed class ProtocolProjectFileTests
{
    [TestMethod]
    public void TheProjectFileModel_IsInAnAssemblyThatReferencesTheBaseLibraryAlone()
    {
        var assembly = typeof(ProjectFile).Assembly;

        Assert.AreEqual("RDCore.SDK.Protocol", assembly.GetName().Name);
        var others = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name != "System" && !name.StartsWith("System.", StringComparison.Ordinal) && name != "netstandard" && name != "mscorlib")
            .ToArray();
        Assert.IsEmpty(others, string.Join(", ", others));
    }

    [TestMethod]
    public void AProjectFile_ReadsWithTheSerializerAlone_InEitherFormOfItsFiles()
    {
        const string json = """
            {
              "Version": "0.1.0",
              "ProjectInfo": {
                "Name": "Battleship",
                "Modules": [ "src/Game.bas", { "RelativeUri": "Sheet1.doccls", "Super": "ExcelWorksheet" }, { "RelativeUri": "src/Ship.cls" } ],
                "OtherFiles": [ "README.md" ]
              }
            }
            """;

        var project = JsonSerializer.Deserialize<ProjectFile>(json)!;

        Assert.AreEqual("0.1.0", project.Version);
        Assert.AreEqual("Battleship", project.ProjectInfo.Name);
        CollectionAssert.AreEqual(new[] { "src/Game.bas", "Sheet1.doccls", "src/Ship.cls" }, project.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
        CollectionAssert.AreEqual(new DocClassType?[] { null, DocClassType.ExcelWorksheet, null }, project.ProjectInfo.Modules.Select(module => module.Super).ToArray());
        Assert.AreEqual("README.md", project.ProjectInfo.OtherFiles.Single().RelativeUri);
    }
}
