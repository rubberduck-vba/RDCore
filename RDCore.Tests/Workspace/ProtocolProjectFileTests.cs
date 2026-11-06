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

    // a project file's version is the version of its format, major.minor; what follows them is not compared. The first builds stamped 1.0.0, the next 0.1.x.
    [TestMethod]
    [DataRow(null, ProjectFileSchemaMatch.Unstamped)]
    [DataRow("", ProjectFileSchemaMatch.Unstamped)]
    [DataRow("1.0", ProjectFileSchemaMatch.Same)]
    [DataRow("1.0.0", ProjectFileSchemaMatch.Same, DisplayName = "1.0.0, as the first builds stamped it")]
    [DataRow("1.0.0-rc3", ProjectFileSchemaMatch.Same)]
    [DataRow("0.1.3", ProjectFileSchemaMatch.Older, DisplayName = "0.1.3, as the next builds stamped it")]
    [DataRow("0.87.2-rc3", ProjectFileSchemaMatch.Older)]
    [DataRow("1.2", ProjectFileSchemaMatch.NewerMinor)]
    [DataRow("1.2.0+abc", ProjectFileSchemaMatch.NewerMinor)]
    [DataRow("2.0", ProjectFileSchemaMatch.NewerMajor)]
    [DataRow("2", ProjectFileSchemaMatch.NewerMajor, DisplayName = "a lone number is a major")]
    [DataRow("v1.0", ProjectFileSchemaMatch.Unparseable)]
    [DataRow("1.x", ProjectFileSchemaMatch.Unparseable)]
    [DataRow("99999999999.0", ProjectFileSchemaMatch.Unparseable, DisplayName = "a major no int holds")]
    public void AVersion_IsComparedWithThisPlatformsFormat_ByItsMajorAndMinor(string? version, ProjectFileSchemaMatch expected)
        => Assert.AreEqual(expected, ProjectFile.SchemaOf(version));

    // what a file says its version is is read whatever it is: it is never why a project file can't be read.
    [TestMethod]
    [DataRow("\"1.1\"", "1.1")]
    [DataRow("2", "2")]
    [DataRow("1.5", "1.5", DisplayName = "the number 1.5")]
    [DataRow("true", "true")]
    [DataRow("{ \"Major\": 2 }", "{ \"Major\": 2 }")]
    [DataRow("null", "")]
    public async Task AVersion_IsReadWhateverItIs(string written, string read)
        => Assert.AreEqual(read, (await ReadAsync($$"""{ "Version": {{written}}, "ProjectInfo": { "Name": "P" } }""")).Version);

    // a file a newer platform wrote opens in an older one, and keeps what the newer one added when the older one writes it: the members this model does not know,
    // at every level, are written back as they were read, and so is the newer format's stamp.
    [TestMethod]
    public async Task AFileOfANewerMinor_KeepsWhatThisPlatformDoesNotKnow_AndItsStamp_WhenItIsWritten()
    {
        const string json = """
            {
              "Version": "1.1",
              "Theme": { "Accent": "teal" },
              "ProjectInfo": {
                "Name": "Newer",
                "Kind": "Library",
                "References": [ { "Name": "Scripting", "Pinned": true } ],
                "Modules": [ "src/Plain.bas", { "RelativeUri": "src/Tagged.bas", "Tags": [ "core" ] }, { "RelativeUri": "Sheet1.doccls", "Super": "ExcelWorksheet", "Sheet": 1 } ],
                "OtherFiles": [ { "RelativeUri": "README.md", "Preview": true } ]
              }
            }
            """;

        var project = await ReadAsync(json);
        var written = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(project.WithSchemaVersion()))!;

        Assert.AreEqual("1.1", written["Version"]!.GetValue<string>(), "the newer format's stamp is kept, since what it added is");
        Assert.AreEqual("""{"Accent":"teal"}""", written["Theme"]!.ToJsonString());
        var info = written["ProjectInfo"]!;
        Assert.AreEqual("Library", info["Kind"]!.GetValue<string>());
        Assert.IsTrue(info["References"]![0]!["Pinned"]!.GetValue<bool>());
        Assert.AreEqual("""["src/Plain.bas",{"RelativeUri":"src/Tagged.bas","Tags":["core"]},{"RelativeUri":"Sheet1.doccls","Super":"ExcelWorksheet","Sheet":1}]""",
            info["Modules"]!.ToJsonString());
        Assert.AreEqual("""[{"RelativeUri":"README.md","Preview":true}]""", info["OtherFiles"]!.ToJsonString());
    }

    // a file in an older format, or no format, is in this one once it is written: only a newer one keeps its stamp.
    [TestMethod]
    [DataRow("1.0.0", "1.0")]
    [DataRow("0.1.3", "1.0")]
    [DataRow("v1", "1.0")]
    [DataRow("", "1.0")]
    [DataRow("1.4", "1.4")]
    [DataRow("3.0", "3.0")]
    public void AWrittenFile_IsStampedWithThisPlatformsFormat_UnlessItIsInANewerOne(string version, string stamped)
        => Assert.AreEqual(stamped, new ProjectFile().WithVersion(version).WithSchemaVersion().Version);

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
