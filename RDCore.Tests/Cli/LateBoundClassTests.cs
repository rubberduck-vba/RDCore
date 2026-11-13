using RDCore.External.Automation;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using System.Reflection;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An object a member declared <c>Object</c> or <c>Variant</c> returns is late-bound, as in MS-VBA, whether or not the project references a library that declares
/// its class: its server is asked what its class is only when something needs it, which is <c>TypeName</c>, and <c>TypeOf</c> and a Set to a variable of a class
/// when the server cannot tell whether the object is one. The server here is a fake that counts how often it is asked.
/// </summary>
[TestClass]
public sealed class LateBoundClassTests
{
    private static readonly LibraryDescription Lab = new()
    {
        Name = "Lab",
        Classes =
        [
            new ClassDescription
            {
                Name = "Shelf",
                ProgId = "Lab.Shelf",
                IsCreatable = true,
                Members =
                [
                    new MemberDescription { Name = "Label", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription { Name = "Label", Kind = MemberKind.PropertyLet, Parameters = [new ParameterDescription { Name = "Value", Type = "String", IsByVal = true }] },
                    new MemberDescription
                    {
                        Name = "Child", Kind = MemberKind.Method, Type = "Object",
                        Parameters = [new ParameterDescription { Name = "Index", Type = "Long", IsByVal = true }],
                    },
                ],
            },
            new ClassDescription { Name = "Crate", ProgId = "Lab.Crate", Members = [] },
        ],
    };

    private sealed class Shelf
    {
        public string Label { get; set; } = "empty";

        // the member the server calls its default one (DISPID_VALUE).
        public object? Value { get; set; } = 42;

        // an object a member with arguments returns, as Excel's Cells(1, 1) returns a Range.
        public Shelf Child(int index) => new() { Label = $"child {index}" };
    }

    private sealed class FakeServer(bool tells) : IAutomationServer
    {
        private int _asked;

        /// <summary>How many times the server was asked what an object's class is.</summary>
        public int Asked => Volatile.Read(ref _asked);

        public bool IsAvailable => true;

        public object CreateObject(string progId) => new Shelf();

        public object GetObject(ExternalObjectRequest request) => new Shelf();

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        {
            var name = member == "[DISPID=0]" ? nameof(Shelf.Value) : member;
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | invocation switch
            {
                AutomationInvocation.Get => BindingFlags.GetProperty | BindingFlags.InvokeMethod,
                AutomationInvocation.Let or AutomationInvocation.Set => BindingFlags.SetProperty,
                _ => BindingFlags.InvokeMethod,
            };

            return typeof(Shelf).InvokeMember(name, flags, null, target, arguments, CultureInfo.InvariantCulture);
        }

        public string? ClassNameOf(object target)
        {
            _ = Interlocked.Increment(ref _asked);
            return target is Shelf ? "Lab.Shelf" : null;
        }

        // what the object answers to (QueryInterface), when the server tells; null leaves it to the class the object is.
        public bool? IsInstanceOf(object target, string library, string className) => tells ? target is Shelf && className == "Shelf" : null;

        public void Release(object handle)
        {
        }

        public bool MoveNext(object enumerator, out object? current)
        {
            current = null;
            return false;
        }

        public void Reset(object enumerator)
        {
        }

        public void Advise(object source, IAutomationEventSink sink)
        {
        }

        public void Unadvise(object source)
        {
        }
    }

    private static async Task<(string[] Output, int Asked)> Run(bool referenced, bool tells, params string[] lines)
    {
        var server = new FakeServer(tells);
        var output = await RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n",
            new WorkspaceLibraries(referenced ? ["Lab"] : [], new InMemoryLibrarySource(Lab), server, true));
        return (output, server.Asked);
    }

    // calls, the objects they return, the default member as a value, Is: none of it needs the class.
    [TestMethod]
    [DataRow(true, DisplayName = "the project references the library")]
    [DataRow(false, DisplayName = "the project references nothing")]
    public async Task AnObjectNothingNeedsTheClassOf_IsNeverAskedForIt(bool referenced)
    {
        var (output, asked) = await Run(referenced, tells: false,
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print s.Label",
            "Debug.Print s.Child(1).Label",
            "Dim c As Object",
            "Set c = s.Child(2)",
            "Debug.Print c.Label",
            "Debug.Print c Is c",
            "Debug.Print s",
            "Dim v As Variant",
            "v = s.Child(3)",
            "Debug.Print v + 1",
            "Debug.Print VarType(s)",
            "Debug.Print IsObject(c)");

        CollectionAssert.AreEqual(new[] { "empty", "child 1", "child 2", "True", "42", "43", "3", "True" }, output);
        Assert.AreEqual(0, asked);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the project references the library")]
    [DataRow(false, DisplayName = "the project references nothing")]
    public async Task TypeName_AsksTheServer_EachTime(bool referenced)
    {
        var (output, asked) = await Run(referenced, tells: false,
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print TypeName(s)",
            "Debug.Print TypeName(s)",
            "Debug.Print TypeName(s.Child(1))");

        CollectionAssert.AreEqual(new[] { "Shelf", "Shelf", "Shelf" }, output);
        Assert.AreEqual(3, asked);
    }

    [TestMethod]
    [DataRow(true, 0, DisplayName = "the server tells what the object is")]
    [DataRow(false, 2, DisplayName = "the server cannot tell: the class is asked")]
    public async Task TypeOf_IsTheObjectsClass(bool tells, int expectedAsked)
    {
        var (output, asked) = await Run(referenced: true, tells,
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print TypeOf s Is Lab.Shelf",
            "Debug.Print TypeOf s Is Lab.Crate");

        CollectionAssert.AreEqual(new[] { "True", "False" }, output);
        Assert.AreEqual(expectedAsked, asked);
    }

    // a variable of the class holds the object, and its members are those of the class.
    [TestMethod]
    [DataRow(true, 0, DisplayName = "the server tells what the object is")]
    [DataRow(false, 1, DisplayName = "the server cannot tell: the class is asked")]
    public async Task ASetToAVariableOfTheClass_HoldsTheObject(bool tells, int expectedAsked)
    {
        var (output, asked) = await Run(referenced: true, tells,
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Dim t As Lab.Shelf",
            "Set t = s.Child(2)",
            "Debug.Print t.Label",
            "t.Label = \"relabelled\"",
            "Debug.Print t.Label",
            "Debug.Print t.Child(4).Label");

        CollectionAssert.AreEqual(new[] { "child 2", "relabelled", "child 4" }, output);
        Assert.AreEqual(expectedAsked, asked);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the server tells what the object is")]
    [DataRow(false, DisplayName = "the server cannot tell: the class is asked")]
    public async Task ASetToAVariableOfAnotherClass_IsATypeMismatch(bool tells)
    {
        var (output, _) = await Run(referenced: true, tells,
            "On Error Resume Next",
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Dim c As Lab.Crate",
            "Set c = s",
            "Debug.Print Err.Number",
            "Debug.Print c Is Nothing");

        CollectionAssert.AreEqual(new[] { "13", "True" }, output);
    }
}
