using RDCore.Runtime.Execution.External.Automation;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// Two libraries that each declare a class of one name - a host application's library and the library of its editor both declare an <c>Application</c> - are
/// told apart in the environment host as they are in the language server: a bare name means what the order of the references makes it mean, and a name
/// qualified by a library means that library's, whichever the order.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
public sealed class LibraryNamesInTheHostTests
{
    private static LibraryDescription Hosting(string name) => new()
    {
        Name = name,
        Classes = [new ClassDescription { Name = "Application", ProgId = $"{name}.Application", IsCreatable = true, Members = [new MemberDescription { Name = "Ping", Kind = MemberKind.Method }] }],
    };

    // an application that does nothing but say which one it was asked for.
    private sealed class Recording : IAutomationServer
    {
        public List<string> Created { get; } = [];

        public bool IsAvailable => true;

        public object CreateObject(string progId)
        {
            Created.Add(progId);
            return new object();
        }

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture) => null;

        public string? ClassNameOf(object target) => null;

        public bool MoveNext(object enumerator, out object? current)
        {
            current = null;
            return false;
        }

        public void Reset(object enumerator)
        {
        }

        public void Release(object handle)
        {
        }
    }

    private static async Task<List<string>> CreatedBy(string[] references, params string[] lines)
    {
        var server = new Recording();
        _ = await RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n",
            new WorkspaceLibraries(references, new InMemoryLibrarySource(Hosting("North"), Hosting("South")), server));
        return server.Created;
    }

    [TestMethod]
    public async Task ABareName_IsTheClassOfTheLibraryReferencedLast()
    {
        CollectionAssert.AreEqual(new[] { "South.Application" }, await CreatedBy(["North", "South"], "Dim a As New Application", "a.Ping"));
        CollectionAssert.AreEqual(new[] { "North.Application" }, await CreatedBy(["South", "North"], "Dim a As New Application", "a.Ping"));
    }

    [TestMethod]
    public async Task AQualifiedName_IsTheClassOfItsLibrary_WhateverTheOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "North.Application", "South.Application" },
            await CreatedBy(["North", "South"], "Dim a As New North.Application", "a.Ping", "Dim b As New South.Application", "b.Ping"));
        CollectionAssert.AreEqual(
            new[] { "North.Application", "South.Application" },
            await CreatedBy(["South", "North"], "Dim a As New North.Application", "a.Ping", "Dim b As New South.Application", "b.Ping"));
    }

    [TestMethod]
    public async Task AClassQualifiedByItsLibrary_IsCreatedByNew_OfTheSameLibrary()
        => CollectionAssert.AreEqual(
            new[] { "North.Application" },
            await CreatedBy(["North", "South"], "Dim a As North.Application", "Set a = New North.Application", "a.Ping"));
}
