using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.External;
using RDCore.External.Automation;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class EnvironmentSessionProviderTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");
    private static readonly Symbol GlobalScope = GlobalSymbols.UnresolvedSymbol;

    private static EnvironmentSessionProvider NewProvider(MockFileSystem? fileSystem = null, SupportedLanguage? language = null)
        => new(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, SourceLanguage: language),
            fileSystem ?? new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);

    [TestMethod]
    public void Session_BeforeCompose_Throws()
    {
        var sut = NewProvider();

        Assert.IsFalse(sut.IsComposed);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = sut.Session);
    }

    [TestMethod]
    public void Compose_MakesModuleAndConstantSymbolsResolvable()
    {
        var project = new RDCoreProject
        {
            Modules = [new RDCoreModule { RelativeUri = "src/MyModule.bas" }],
            PrecompilerConstants = { ["RDDEBUG"] = "1" },
        };
        var sut = NewProvider();

        var session = sut.Compose(project, WorkspaceRoot);

        Assert.IsTrue(sut.IsComposed);
        Assert.AreSame(session, sut.Session);

        Assert.IsTrue(session.Symbols.TryResolveValue("MyModule", GlobalScope, out var module));
        Assert.IsInstanceOfType<VBStandardModuleSymbol>(module);

        // a #Const resolves in its own binding context and in no other (MS-VBAL §3.4.1).
        Assert.IsTrue(session.Symbols.TryResolveConditionalConstant("RDDEBUG", GlobalScope, out var rdDebug));
        Assert.AreEqual((short)1, ((VBIntegerValue)((PrecompilerConstantSymbol)rdDebug!).Value).Value);

        Assert.IsTrue(session.Symbols.TryResolveConditionalConstant("Win64", GlobalScope, out var win64));
        Assert.AreEqual((short)1, ((VBIntegerValue)((PrecompilerConstantSymbol)win64!).Value).Value);
    }

    [TestMethod]
    public void Compose_ResolvesModuleName_FromVBNameAttribute_NotFileName()
    {
        // an absolute root the MockFileSystem accepts on both Windows and the Linux CI runner.
        var root = Path.Combine(Path.GetTempPath(), "rdcore-esp-vbname");
        var project = new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = "src/oldName.bas" }] };
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "src", "oldName.bas")] = new("Attribute VB_Name = \"Renamed\"\r\nOption Explicit\r\n"),
        });

        var session = NewProvider(fs).Compose(project, new Uri(root));

        Assert.IsTrue(session.Symbols.TryResolveValue("Renamed", GlobalScope, out var module), "resolves under VB_Name");
        Assert.IsInstanceOfType<VBStandardModuleSymbol>(module);
        Assert.IsFalse(session.Symbols.TryResolveValue("oldName", GlobalScope, out _), "not under the file name");
    }

    [TestMethod]
    public void Compose_FallsBackToFileName_WhenSourceHasNoVBNameAttribute()
    {
        var root = Path.Combine(Path.GetTempPath(), "rdcore-esp-fallback");
        var project = new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = "src/Helpers.bas" }] };
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "src", "Helpers.bas")] = new("Option Explicit\r\nPublic X As Long\r\n"),
        });

        var session = NewProvider(fs).Compose(project, new Uri(root));

        Assert.IsTrue(session.Symbols.TryResolveValue("Helpers", GlobalScope, out _));
    }

    [TestMethod]
    public void Compose_Twice_ReplacesTheSession()
    {
        var sut = NewProvider();

        var first = sut.Compose(new RDCoreProject(), WorkspaceRoot);
        var second = sut.Compose(new RDCoreProject(), WorkspaceRoot);

        Assert.AreNotSame(first, second);
        Assert.AreSame(second, sut.Session);
    }

    // the standard library is every project's own: first of the session's references, whether or not the project lists it (as a project file written before
    // does, with the path of Office's VBE7.DLL).
    [TestMethod]
    [DataRow(false, DisplayName = "a project that lists no standard library")]
    [DataRow(true, DisplayName = "a project written before, that lists it first")]
    public void Compose_ExposesTheStandardLibrary_ThenTheReferences_InTheOrderTheProjectDeclaresThem(bool listsTheStandardLibrary)
    {
        var excel = new RDCoreReference { Name = "Excel", Major = 1, Minor = 9 };
        var project = new RDCoreProject
        {
            References = listsTheStandardLibrary
                ? [new RDCoreReference { Name = "VBA", Guid = RDCoreReference.StandardLibraryGuid, AbsolutePath = @"C:\Program Files\Microsoft Office\VBE7.DLL", Major = 4, Minor = 2 }, excel]
                : [excel],
        };

        var session = NewProvider().Compose(project, WorkspaceRoot);

        Assert.HasCount(2, session.References);
        Assert.AreEqual("VBA", session.References[0].Name);
        Assert.AreEqual(0, session.References[0].Priority);
        Assert.AreEqual("Excel", session.References[1].Name);
        Assert.AreEqual(1, session.References[1].Priority);
    }

    // a VB6 project's Visual Basic runtime is in the session's references in its place: after the standard library, before the ones the project lists, and once
    // when the project lists it as well.
    [TestMethod]
    [DataRow(false, DisplayName = "a project that lists only Scripting")]
    [DataRow(true, DisplayName = "a project that also lists VBRUN")]
    public void Compose_ExposesTheLanguagesImplicitReferences_BetweenTheStandardLibraryAndTheReferences(bool listsTheRuntime)
    {
        var scripting = new RDCoreReference { Name = "Scripting", Major = 1, Minor = 0 };
        var project = new RDCoreProject { References = listsTheRuntime ? [new RDCoreReference { Name = "vbrun", Major = 6, Minor = 0 }, scripting] : [scripting] };

        var session = NewProvider(language: SupportedLanguages.VB6).Compose(project, WorkspaceRoot);

        CollectionAssert.AreEqual(new[] { "VBA 0", "VBRUN 1", "VB 2", "Scripting 3" }, session.References.Select(reference => $"{reference.Name} {reference.Priority}").ToArray());
    }

    // a project with an RDForms form references RDForms without its .rdproj listing it: after the language's own, before the ones the project lists.
    [TestMethod]
    [DataRow("vb6", new[] { "VBA 0", "VBRUN 1", "VB 2", "RDForms 3", "Scripting 4" }, DisplayName = "a VB6 project")]
    [DataRow("vba", new[] { "VBA 0", "RDForms 1", "Scripting 2" }, DisplayName = "an RD-VBA project")]
    public void Compose_ExposesRDForms_ForAProjectWithAnRDFormsForm(string language, string[] references)
    {
        var project = new RDCoreProject
        {
            References = [new RDCoreReference { Name = "Scripting", Major = 1, Minor = 0 }],
            Modules = [new RDCoreModule { RelativeUri = "Module1.bas" }, new RDCoreModule { RelativeUri = "Forms/Form2.rdform" }],
        };

        var session = NewProvider(language: language == "vb6" ? SupportedLanguages.VB6 : SupportedLanguages.RDVBA).Compose(project, WorkspaceRoot);

        CollectionAssert.AreEqual(references, session.References.Select(reference => $"{reference.Name} {reference.Priority}").ToArray());
    }

    [TestMethod]
    public void Compose_DefaultProject_ExposesTheVBStandardLibraryReference()
    {
        var session = NewProvider().Compose(new RDCoreProject(), WorkspaceRoot);

        Assert.HasCount(1, session.References);
        Assert.AreEqual("VBA", session.References[0].Name);
        Assert.AreEqual(0, session.References[0].Priority);
    }

    // a cap on a hang, not a budget: the wait is on the evaluation's own signal that it began.
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(5);

    // stores a module whose evaluation waits for release, and answers the token it is given once it has begun.
    private static CancellationToken EvaluationBegun(EnvironmentSessionProvider provider, ManualResetEventSlim release)
    {
        var module = new Uri("file:///c:/ws/Mod1.bas#Mod1");
        using var begun = new ManualResetEventSlim();
        CancellationToken given = default;
        provider.Image.Semantics.Store(new ModuleSemanticModel(module, [], [new ProcedureSemanticModel(new SemanticId(new Uri("file:///c:/ws/Mod1.bas#Mod1.Run")), [])]), token =>
        {
            given = token;
            begun.Set();
            release.Wait();
            return ImmutableDictionary<SemanticId, RuntimeFacts>.Empty;
        });

        Assert.IsTrue(begun.Wait(HangGuard));
        return given;
    }

    [TestMethod]
    public void ASessionLetGoOf_StopsTheEvaluationOfItsCode()
    {
        var provider = NewProvider();
        provider.Compose(new RDCoreProject(), WorkspaceRoot);
        using var release = new ManualResetEventSlim();
        var token = EvaluationBegun(provider, release);

        provider.Dispose();

        Assert.IsTrue(token.IsCancellationRequested, "what a session that is over asked of its code is not found out");
        release.Set();
    }

    [TestMethod]
    public void ASessionComposedAgain_StopsTheEvaluationOfTheCodeOfTheOneItReplaces()
    {
        using var provider = NewProvider();
        provider.Compose(new RDCoreProject(), WorkspaceRoot);
        using var release = new ManualResetEventSlim();
        var token = EvaluationBegun(provider, release);

        provider.Compose(new RDCoreProject(), WorkspaceRoot);

        Assert.IsTrue(token.IsCancellationRequested, "the code of the session that was replaced is not there any more");
        release.Set();
    }

    [TestMethod]
    [DataRow(ProjectOutputType.Application, 1, DisplayName = "an RDForms application")]
    [DataRow(ProjectOutputType.ConsoleApplication, 0, DisplayName = "a console application")]
    public void WhatHostsTheObjectsOfALibraryAndTheForms_IsStartedWithTheSession_UnlessTheProgramIsAConsoleApplication(ProjectOutputType output, int prepared)
    {
        var automation = Substitute.For<IAutomationServer>();
        using var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new MockFileSystem(), NullLogger<EnvironmentSessionProvider>.Instance,
            new DirectoryLibrarySource(new FileSystem(), RDCore.Tests.Runtime.Libraries.ShippedLibrariesTests.Symbols),
            ExternalWorld.None with { Automation = automation });

        provider.Compose(
            new RDCoreProject
            {
                Name = "NavalConsole",
                References = [new RDCoreReference { Name = "Scripting" }],
                Build = new ProjectBuild { OutputType = output },
            },
            WorkspaceRoot);

        automation.Received(prepared).Prepare();
    }
}
