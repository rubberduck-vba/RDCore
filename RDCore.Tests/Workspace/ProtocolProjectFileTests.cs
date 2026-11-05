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

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-protocol-read");

    private static Task<ProjectFile> ReadAsync(string json, bool byteOrderMark = false)
        => ProjectFile.ReadAsync(new MemoryStream([.. byteOrderMark ? new byte[] { 0xEF, 0xBB, 0xBF } : [], .. System.Text.Encoding.UTF8.GetBytes(json)]), Root);

    [TestMethod]
    [DataRow("""{ "ProjectInfo": null }""", new string[0], DisplayName = "a null project")]
    [DataRow("""{ "ProjectInfo": { "Name": null, "Modules": null, "OtherFiles": null, "Folders": null, "References": null, "PrecompilerConstants": null } }""", new string[0], DisplayName = "null lists")]
    [DataRow("""{ "ProjectInfo": { "Modules": [ null, "Module1.bas" ], "OtherFiles": [ null ], "Folders": [ null ], "References": [ null ] } }""", new[] { "Module1.bas" }, DisplayName = "null items")]
    [DataRow("""{ }""", new string[0], DisplayName = "nothing at all")]
    public async Task WhatAFileLeavesOut_ReadsAsEmpty_NeverNull(string json, string[] modules)
    {
        var project = await ReadAsync(json);

        Assert.AreEqual(Root, project.Uri);
        Assert.AreEqual(string.Empty, project.Version);
        Assert.IsNotNull(project.Configuration);
        var info = project.ProjectInfo;
        Assert.AreEqual(string.Empty, info.Name);
        CollectionAssert.AreEqual(modules, info.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.IsEmpty(info.OtherFiles);
        Assert.IsEmpty(info.Folders);
        Assert.IsEmpty(info.References);
        Assert.IsNotNull(info.PrecompilerConstants);
    }

    [TestMethod]
    public async Task TheStandardLibrary_IsNoReferenceOfAProjectAsItIsRead_AndAMarkedFileReadsTheSame()
    {
        const string json = """
            { "ProjectInfo": { "References": [ { "Name": "VBA", "Guid": "000204ef-0000-0000-c000-000000000046" }, { "Name": "Scripting" } ] } }
            """;

        foreach (var marked in new[] { false, true })
        {
            var project = await ReadAsync(json, marked);

            Assert.AreEqual("Scripting", project.ProjectInfo.References.Single().Name);
        }
    }

    [TestMethod]
    public async Task AFileThatIsNoJsonObject_ThrowsTheJsonException()
    {
        await Assert.ThrowsAsync<JsonException>(() => ReadAsync("null"));
        await Assert.ThrowsAsync<JsonException>(() => ReadAsync("{ \"ProjectInfo\": { ]"));
    }

    [TestMethod]
    [DataRow(null, "Attribute VB_Name = \"Module1\"\r\n", ProjectModuleKind.Standard)]
    [DataRow(null, "VERSION 1.0 CLASS\r\nBEGIN\r\nEND\r\nAttribute VB_Name = \"Class1\"\r\n", ProjectModuleKind.Class)]
    [DataRow(DocClassType.ExcelWorksheet, "Attribute VB_Name = \"Sheet1\"\r\n", ProjectModuleKind.Document)]
    [DataRow(DocClassType.Unknown, "VERSION 1.0 CLASS\r\n", ProjectModuleKind.Document)]
    [DataRow(null, null, ProjectModuleKind.Standard)]
    public void AModulesKind_IsTheProjectsWordForItsDocument_ElseWhatItsTextSays(DocClassType? super, string? source, ProjectModuleKind expected)
    {
        Assert.AreEqual(expected, ProjectModuleKinds.Of(super, source));
        Assert.AreEqual(expected, ProjectModuleKinds.Of(new RDCoreModule { RelativeUri = "M.cls", Super = super }, source));
    }

    [TestMethod]
    [DataRow("utf-8", true)]
    [DataRow("utf-8", false)]
    [DataRow("utf-16", true)]
    [DataRow("utf-16BE", true)]
    public void AModulesText_IsReadAsThePlatformReadsAFile_AndAClassModuleBehindAMarkIsOne(string encodingName, bool byteOrderMark)
    {
        const string text = "VERSION 1.0 CLASS\r\nBEGIN\r\nEND\r\nAttribute VB_Name = \"Modèle\"\r\n";
        var encoding = System.Text.Encoding.GetEncoding(encodingName);
        byte[] bytes = [.. byteOrderMark ? encoding.GetPreamble() : [], .. encoding.GetBytes(text)];
        var path = Path.Combine(Path.GetTempPath(), $"rdcore-module-text-{Guid.NewGuid():N}.cls");
        try
        {
            File.WriteAllBytes(path, bytes);

            var read = ProjectModuleKinds.ReadText(bytes);

            Assert.AreEqual(File.ReadAllText(path), read);
            Assert.AreEqual(text, read);
            Assert.AreEqual(ProjectModuleKind.Class, ProjectModuleKinds.Of((DocClassType?)null, read));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
