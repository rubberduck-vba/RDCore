using Microsoft.Extensions.Options;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Parser;

/// <summary>
/// A module that may have statements outside its procedures (<see cref="SdkWorkspaceOptions.AllowsFileLevelStatements"/>: a script, or any module
/// when the workspace says so) has them as the code of its <see cref="ProgramEntryPoint.ScriptName"/> procedure, in the order they are written; what it
/// declares there is the module's. A module that may not have them has them as a syntax error, as MS-VBA does (<c>Invalid outside procedure</c>).
/// </summary>
[TestClass]
public sealed class FileLevelStatementsTests
{
    private static readonly Uri Script = new("file:///c:/ws/Backup.vbs");
    private static readonly Uri Module = new("file:///c:/ws/Module1.bas");

    private static ModuleParseResult Parse(Uri uri, bool setting, params string[] lines)
        => new ModuleParser(workspaceOptions: Options.Create(new SdkWorkspaceOptions { FileLevelStatements = setting }))
            .Parse(uri, string.Join("\r\n", lines) + "\r\n");

    private static MemberDeclarationNode? ScriptOf(ModuleParseResult parse)
        => parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().SingleOrDefault(member => member.Name == ProgramEntryPoint.ScriptName);

    private static string Errors(ModuleParseResult parse)
        => string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}"));

    [TestMethod]
    public void TheStatementsOfAScript_AreItsScriptProcedure_InTheOrderTheyAreWritten_AroundItsProcedures()
    {
        var parse = Parse(Script, setting: false, "x = 1", "Sub Two()", "x = 2", "End Sub", "Two", "x = 3");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        var script = ScriptOf(parse);
        Assert.IsNotNull(script);
        Assert.AreEqual(MemberKind.Procedure, script.MemberKind);
        Assert.AreEqual(AccessModifier.Public, script.AccessModifier);
        CollectionAssert.AreEqual(new[] { "AssignmentStatementNode", "CallStatementNode", "AssignmentStatementNode" },
            script.Children.Select(child => child.GetType().Name).ToArray());
        Assert.AreEqual(0, script.SourceLocation.Range.Start.Line, "from the first statement");
        Assert.AreEqual(5, script.SourceLocation.Range.End.Line, "to the last");

        var two = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == "Two");
        Assert.HasCount(1, two.Children.OfType<AssignmentStatementNode>(), "a procedure keeps its own code");
    }

    [TestMethod]
    public void WhatAScriptDeclaresOutsideItsProcedures_IsTheModules()
    {
        var parse = Parse(Script, setting: false, "Dim count As Long", "count = 2", "Const Greeting = \"hi\"", "Dim later");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        var declared = parse.SyntaxTree!.Children.OfType<VariableDeclarationNode>().Select(variable => variable.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "count", "later" }, declared);
        Assert.HasCount(1, ScriptOf(parse)!.Children, "the assignment");
    }

    [TestMethod]
    public void AScriptOfProceduresOnly_HasAScriptThatDoesNothing()
    {
        var parse = Parse(Script, setting: false, "Sub Main()", "End Sub");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        Assert.IsEmpty(ScriptOf(parse)!.Children);
    }

    [TestMethod]
    public void AnyModule_HasThem_WhenTheWorkspaceSaysSo()
    {
        var parse = Parse(Module, setting: true, "Debug.Print 1", "Sub Main()", "End Sub");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        Assert.HasCount(1, ScriptOf(parse)!.Children);
    }

    [TestMethod]
    public void AModuleThatMayNotHaveThem_HasThemAsASyntaxError_AndNoScript()
    {
        var parse = Parse(Module, setting: false, "Debug.Print 1", "Sub Main()", "End Sub");

        Assert.AreEqual(VBCompileErrorId.InvalidOutsideProcedure, parse.SyntaxErrors.Single().VBCompileErrorId, Errors(parse));
        Assert.IsNull(ScriptOf(parse));
    }

    // a document of a language that always has them (a .vbs, any module of a VBScript workspace) is a script whatever it writes; a module that the setting
    // alone allows them is one only when it writes one, and has no script procedure otherwise.
    [TestMethod]
    [DataRow("vba", "Module1.bas", true, false, DisplayName = "a VBA module of procedures only, the setting on")]
    [DataRow("vba", "Backup.vbs", false, true, DisplayName = "a script of procedures only, in a VBA workspace")]
    [DataRow("vbscript", "Module1.bas", false, true, DisplayName = "a module of procedures only, in a VBScript workspace")]
    public void AModuleOfProceduresOnly_IsAScript_OnlyWhereItsLanguageAlwaysHasThem(string language, string file, bool setting, bool isScript)
    {
        var parse = new ModuleParser(workspaceOptions: Options.Create(new SdkWorkspaceOptions { Language = language, FileLevelStatements = setting }))
            .Parse(new Uri($"file:///c:/ws/{file}"), "Sub Main()\r\nEnd Sub\r\n");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        CollectionAssert.AreEqual(isScript ? new[] { "Main", ProgramEntryPoint.ScriptName } : new[] { "Main" },
            parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Select(member => member.Name).ToArray());
    }

    // a class module is never a script: what it writes outside its procedures is MS-VBA's syntax error, the setting on or off.
    [TestMethod]
    [DataRow(false, DisplayName = "the setting off")]
    [DataRow(true, DisplayName = "the setting on")]
    public void AClassModule_IsNoScript_WhatItWritesOutsideItsProceduresAnError(bool setting)
    {
        var parse = Parse(new Uri("file:///c:/ws/Class1.cls"), setting,
            "VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Class1\"", "Debug.Print 1", "Public Sub Work()", "End Sub");

        Assert.AreEqual(VBCompileErrorId.InvalidOutsideProcedure, parse.SyntaxErrors.Single().VBCompileErrorId, Errors(parse));
        Assert.IsNull(ScriptOf(parse));
    }

    // a module's own description is the annotation it writes before its procedures, a script's and a module's of the setting as any module's.
    [TestMethod]
    [DataRow("Module1.bas", true, false, DisplayName = "a VBA module of procedures only, the setting on")]
    [DataRow("Module1.bas", true, true, DisplayName = "a VBA module with code, the setting on")]
    [DataRow("Backup.vbs", false, false, DisplayName = "a script of procedures only")]
    [DataRow("Backup.vbs", false, true, DisplayName = "a script with code")]
    public void AModulesOwnDescription_IsTheAnnotationItWritesBeforeItsProcedures(string file, bool setting, bool hasCode)
    {
        string[] code = hasCode ? ["Main"] : [];
        var parse = Parse(new Uri($"file:///c:/ws/{file}"), setting, ["'@ModuleDescription(\"Backs up.\")", "Sub Main()", "End Sub", .. code]);

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        Assert.AreEqual("Backs up.", parse.GetModuleDescription());
    }

    [TestMethod]
    [DataRow("If True Then", "x = 1", VBCompileErrorId.BlockIfWithoutEndIf, DisplayName = "a block If the script does not end")]
    [DataRow("x = 1", "Next", VBCompileErrorId.NextWithoutFor, DisplayName = "a Next of no For")]
    public void ABlockOfTheScriptThatDoesNotMatch_IsMsVbasError(string first, string second, VBCompileErrorId expected)
    {
        var parse = Parse(Script, setting: false, first, second);

        Assert.AreEqual(expected, parse.SyntaxErrors.Single().VBCompileErrorId, Errors(parse));
    }
}
