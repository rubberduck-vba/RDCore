using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Workspace;
using System.Text.Json;

namespace RDCore.Tests.Workspace;

/// <summary>
/// What kind a module of a project is: the project's word for a document module (<c>Super</c>), and the module's own text otherwise - the same for the
/// environment host and the language server.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.2.3.3 RDCoreModule")]
public sealed class ModuleKindsTests
{
    private const string ClassModule = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"C1\"\r\n";

    // what legacy Rubberduck exported of a worksheet's module: the text the code pane shows, with no header and no attribute.
    private const string LegacyDocumentExport = "'@Folder(\"Battleship.View\")\r\nOption Explicit\r\nPrivate Sub Worksheet_Activate()\r\n    Me.Range(\"A1\").Select\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow(DocClassType.ExcelWorksheet)]
    [DataRow(DocClassType.ExcelWorkbook)]
    [DataRow(DocClassType.WordDocument)]
    [DataRow(DocClassType.AccessForm)]
    [DataRow(DocClassType.Unknown)]
    public void AModuleTheProjectGivesASuper_IsADocumentModule_WhateverItsTextSays(DocClassType super)
    {
        Assert.AreEqual(ModuleType.DocumentModule, ModuleKinds.Of(new RDCoreModule { RelativeUri = "Sheet1.doccls", Super = super }, LegacyDocumentExport));
        Assert.AreEqual(ModuleType.DocumentModule, ModuleKinds.Of(new RDCoreModule { RelativeUri = "Sheet1.cls", Super = super }, ClassModule));
    }

    [TestMethod]
    public void AModuleWithNoSuper_IsTheKindItsTextSays_AndNoNameOrHandlerMakesItADocumentModule()
    {
        Assert.AreEqual(ModuleType.ClassModule, ModuleKinds.Of(new RDCoreModule { RelativeUri = "C1.cls" }, ClassModule));
        Assert.AreEqual(ModuleType.StdModule, ModuleKinds.Of(new RDCoreModule { RelativeUri = "ThisWorkbook.doccls" }, LegacyDocumentExport));
        Assert.AreEqual(ModuleType.StdModule, ModuleKinds.Of((RDCoreModule?)null, LegacyDocumentExport));
    }

    [TestMethod]
    public void ADocumentModule_IsAClassModule()
    {
        Assert.IsTrue(ModuleType.DocumentModule.IsClass());
        Assert.IsTrue(ModuleType.ClassModule.IsClass());
        Assert.IsFalse(ModuleType.StdModule.IsClass());
    }

    [TestMethod]
    public void ADocumentModule_IsNotCreatable_IsExtensible_AndHasNoDefaultInstance_WhateverItsTextSays()
    {
        var declared = new ModuleDirectives(Explicit: true, PredeclaredId: true, Creatable: true, Extensible: false);

        var directives = ModuleKinds.DirectivesOf(ModuleType.DocumentModule, declared);

        Assert.AreEqual(new ModuleDirectives(Explicit: true, PredeclaredId: false, Creatable: false, Extensible: true), directives);
        Assert.AreEqual(declared, ModuleKinds.DirectivesOf(ModuleType.ClassModule, declared), "a class module's attributes are its text's");
    }

    [TestMethod]
    public void EachKnownHostDocument_IsAnObjectOfAClassOfItsLibrary_AndAnUnknownOneOfNone()
    {
        Assert.AreEqual(new DocumentHostClass("Excel", "Worksheet"), ModuleKinds.HostClassOf(DocClassType.ExcelWorksheet));
        Assert.AreEqual(new DocumentHostClass("Excel", "Workbook"), ModuleKinds.HostClassOf(DocClassType.ExcelWorkbook));
        Assert.AreEqual(new DocumentHostClass("Word", "Document"), ModuleKinds.HostClassOf(DocClassType.WordDocument));
        Assert.AreEqual(new DocumentHostClass("Access", "Form"), ModuleKinds.HostClassOf(DocClassType.AccessForm));
        Assert.AreEqual(new DocumentHostClass("Access", "Report"), ModuleKinds.HostClassOf(DocClassType.AccessReport));
        Assert.IsNull(ModuleKinds.HostClassOf(DocClassType.Unknown));
    }

    // ---- the project file ----

    private static DocClassType? SuperRead(string super)
        => JsonSerializer.Deserialize<RDCoreModule>($$"""{ "RelativeUri": "Sheet1.doccls", "Super": {{super}} }""")!.Super;

    [TestMethod]
    [DataRow("2", DocClassType.ExcelWorksheet)]
    [DataRow("\"ExcelWorksheet\"", DocClassType.ExcelWorksheet)]
    [DataRow("\"excelworkbook\"", DocClassType.ExcelWorkbook)]
    [DataRow("5", DocClassType.WordDocument)]
    public void Super_IsReadByItsNameOrByItsNumber(string written, DocClassType expected)
        => Assert.AreEqual(expected, SuperRead(written));

    [TestMethod]
    [DataRow("\"PowerPointSlide\"")]
    [DataRow("\"2\"")]
    [DataRow("\"\"")]
    [DataRow("42")]
    public void ASuperRDVBADoesNotKnow_IsReadAsUnknown_AndTheProjectStillLoads(string written)
        => Assert.AreEqual(DocClassType.Unknown, SuperRead(written));

    [TestMethod]
    public void Super_IsWrittenByItsName_AndNoneAsNull()
    {
        StringAssert.Contains(JsonSerializer.Serialize(new RDCoreModule { RelativeUri = "Sheet1.doccls", Super = DocClassType.ExcelWorksheet }), "\"Super\":\"ExcelWorksheet\"");
        StringAssert.Contains(JsonSerializer.Serialize(new RDCoreModule { RelativeUri = "Module1.bas" }), "\"Super\":null");
        Assert.IsNull(SuperRead("null"));
    }
}
