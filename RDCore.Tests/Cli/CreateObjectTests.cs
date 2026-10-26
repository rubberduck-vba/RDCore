using RDCore.External.Automation;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using System.Reflection;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>CreateObject</c> and <c>GetObject</c> get an object of a server by the name of its class, whether or not the project references a library that declares
/// it: an object no referenced library declares is late-bound, and its members are reached by name, through what the object answers to. The server here is a
/// fake that holds plain objects, so what is under test is the runtime's side of it - the request, the late-bound members, the errors - on any platform.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.8.1.4 CreateObject")]
[TestCategory("MS-VBAL 6.1.2.8.1.9 GetObject")]
public sealed class CreateObjectTests
{
    // a library that declares the class, for a project that references it.
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
                    new MemberDescription { Name = "Blank", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription { Name = "Stamp", Kind = MemberKind.PropertyGet, Type = "Date" },
                ],
            },
        ],
    };

    // the server's side of it: a plain object, called by the name of the member.
    private sealed class Shelf
    {
        public string Label { get; set; } = "empty";

        // what a server returns where it says nothing - a null BSTR, as Microsoft Forms' Text of an empty TextBox is - for a String and a Date.
        public string? Blank => null;

        public object? Stamp => null;

        // the member the server calls its default one (DISPID_VALUE).
        public object? Value { get; set; } = 42;

        public Shelf? Next { get; set; }

        public Shelf Self => this;

        public List<object?> Items { get; } = ["a", 2, true];

        public string Describe(object? first, object? second) => $"{first}/{second}";

        public void Bump(ref object? amount) => amount = (int)amount! + 1;
    }

    private sealed class FakeServer : IAutomationServer
    {
        public List<object> Created { get; } = [];

        public List<object> Released { get; } = [];

        public List<ExternalObjectRequest> Requests { get; } = [];

        // the object that is running, for GetObject(, "Lab.Shelf").
        public Shelf Running { get; } = new() { Label = "running" };

        public bool IsAvailable => true;

        public object CreateObject(string progId)
        {
            var created = string.Equals(progId, "Lab.Shelf", StringComparison.OrdinalIgnoreCase)
                ? new Shelf()
                : throw new AutomationException(unchecked((int)0x80040154), $"'{progId}' is not registered.");
            Created.Add(created);
            return created;
        }

        public object GetObject(ExternalObjectRequest request)
        {
            Requests.Add(request);
            return request switch
            {
                { Source: ExternalObjectSource.Create, ServerName: "faraway" } => throw new AutomationException(unchecked((int)0x800706BA), "The RPC server is unavailable."),
                { Source: ExternalObjectSource.Create } => CreateObject(request.ProgId!),
                { Source: ExternalObjectSource.Running, ProgId: "Lab.Shelf" } => Running,
                { Source: ExternalObjectSource.Running } => throw new AutomationException(unchecked((int)0x800401E3), "Operation unavailable."),
                { PathName: "missing.shelf" } => throw new AutomationException(unchecked((int)0x800401EA), "Moniker cannot open file."),
                _ => new Shelf { Label = $"{request.PathName}|{request.ProgId}" },
            };
        }

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        {
            var shelf = (Shelf)target;
            if (member == "[DISPID=-4]")
            {
                return shelf.Items.GetEnumerator();
            }

            var name = member == "[DISPID=0]" ? nameof(Shelf.Value) : member;
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | invocation switch
            {
                AutomationInvocation.Get => BindingFlags.GetProperty | BindingFlags.InvokeMethod,
                AutomationInvocation.Let or AutomationInvocation.Set => BindingFlags.SetProperty,
                _ => BindingFlags.InvokeMethod,
            };

            try
            {
                return typeof(Shelf).InvokeMember(name, flags, null, shelf, arguments, CultureInfo.InvariantCulture);
            }
            catch (MissingMemberException)
            {
                throw new AutomationException(unchecked((int)0x80020006), "Unknown name.");
            }
        }

        public string? ClassNameOf(object target) => target is Shelf ? "Lab.Shelf" : null;

        public void Release(object handle) => Released.Add(handle);

        public bool MoveNext(object enumerator, out object? current)
        {
            var moved = ((System.Collections.IEnumerator)enumerator).MoveNext();
            current = moved ? ((System.Collections.IEnumerator)enumerator).Current : null;
            return moved;
        }

        public void Reset(object enumerator) => ((System.Collections.IEnumerator)enumerator).Reset();

        public void Advise(object source, IAutomationEventSink sink)
        {
        }

        public void Unadvise(object source)
        {
        }
    }

    private static async Task<(string[] Output, FakeServer Server)> Run(bool allowAutomation, string[] references, params string[] lines)
    {
        var server = new FakeServer();
        var output = await RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n",
            new WorkspaceLibraries(references, new InMemoryLibrarySource(Lab), server, allowAutomation));
        return (output, server);
    }

    // a project that references nothing: every object is late-bound.
    private static async Task<string[]> Run(params string[] lines) => (await Run(true, [], lines)).Output;

    [TestMethod]
    public async Task AnObjectOfAClassNoLibraryDeclares_IsCreated_AndItsMembersAreReachedByName()
        => CollectionAssert.AreEqual(new[] { "empty", "full", "x/2" }, await Run(
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print s.Label",
            "s.Label = \"full\"",
            "Debug.Print s.Label",
            "Debug.Print s.Describe(\"x\", 2)"));

    // VarType of an object is that of its default member's value, which is a Long here (MS-VBAL 5.5.1.2.13).
    [TestMethod]
    public async Task ALateBoundObject_IsAnObjectOfTheClassTheServerSaysItIs()
        => CollectionAssert.AreEqual(new[] { "Shelf", "3", "True" }, await Run(
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print TypeName(s)",
            "Debug.Print VarType(s)",
            "Debug.Print IsObject(s)"));

    [TestMethod]
    public async Task AnObjectAssignedToAMemberOfALateBoundObject_IsSetAssigned_AndIsTheSameObject()
        => CollectionAssert.AreEqual(new[] { "True", "True" }, await Run(
            "Dim s As Object",
            "Dim t As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Set t = CreateObject(\"Lab.Shelf\")",
            "Set s.Next = t",
            "Debug.Print s.Next Is t",
            "Debug.Print s.Self Is s"));

    [TestMethod]
    public async Task TheDefaultMemberOfALateBoundObject_IsWhatTheObjectIsAsAValue()
        => CollectionAssert.AreEqual(new[] { "42", "43" }, await Run(
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print s",
            "Dim v As Variant",
            "v = CreateObject(\"Lab.Shelf\")",
            "Debug.Print v + 1"));

    [TestMethod]
    public async Task ForEach_OverALateBoundObject_IsDrivenByTheEnumeratorItGives()
        => CollectionAssert.AreEqual(new[] { "a", "2", "True" }, await Run(
            "Dim s As Object",
            "Dim item As Variant",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "For Each item In s",
            "Debug.Print item",
            "Next"));

    [TestMethod]
    public async Task CallByName_ReachesAMemberOfALateBoundObject()
        => CollectionAssert.AreEqual(new[] { "empty", "z", "p/q" }, await Run(
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print CallByName(s, \"Label\", VbGet)",
            "CallByName s, \"Label\", VbLet, \"z\"",
            "Debug.Print s.Label",
            "Debug.Print CallByName(s, \"Describe\", VbMethod, \"p\", \"q\")"));

    [TestMethod]
    public async Task AMemberALateBoundObjectHasNot_IsObjectDoesntSupportThisPropertyOrMethod()
        => CollectionAssert.AreEqual(new[] { "438" }, await Run(
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "On Error Resume Next",
            "s.Nope",
            "Debug.Print Err.Number"));

    // 🚧 a late-bound member's arguments are passed by value (LateBinding): MS-VBA passes a variable by reference, and what the server writes is the variable's.
    [TestMethod]
    [Ignore("late-bound arguments are passed by value; MS-VBA passes a variable by reference")]
    public async Task AVariablePassedToALateBoundMember_IsTheVariableTheServerWritesTo()
        => CollectionAssert.AreEqual(new[] { "2" }, await Run(
            "Dim s As Object",
            "Dim n As Variant",
            "n = 1",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "s.Bump n",
            "Debug.Print n"));

    [TestMethod]
    public async Task WhenTheLastReferenceToALateBoundObjectIsLetGo_TheServerIsToo()
    {
        var (output, server) = await Run(true, [],
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Set s = Nothing",
            "Debug.Print \"gone\"");

        CollectionAssert.AreEqual(new[] { "gone" }, output);
        Assert.HasCount(1, server.Released);
        Assert.AreSame(server.Created[0], server.Released[0]);
    }

    // what a member declared As String returns where the server said nothing is "" - not the text of the number 0 - and a Date's is day 0.
    [TestMethod]
    public async Task NothingReturnedForAStringOrADate_IsTheirDefault()
        => CollectionAssert.AreEqual(new[] { "[]", "0", "True" }, (await Run(true, ["Lab"],
            "Dim s As Lab.Shelf",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print \"[\" & s.Blank & \"]\"",
            "Debug.Print Len(s.Blank)",
            "Debug.Print s.Stamp = 0")).Output);

    [TestMethod]
    public async Task AnObjectOfAClassAReferencedLibraryDeclares_IsAnObjectOfThatClass()
        => CollectionAssert.AreEqual(new[] { "Shelf", "empty" }, (await Run(true, ["Lab"],
            "Dim s As Lab.Shelf",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print TypeName(s)",
            "Debug.Print s.Label")).Output);

    [TestMethod]
    public async Task WhenTheEnvironmentRefusesAutomation_NoObjectIsMade_AndTheRefusalIsPermissionDenied()
    {
        var (output, server) = await Run(false, [],
            "On Error Resume Next",
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\")",
            "Debug.Print Err.Number");

        Assert.IsEmpty(server.Requests);
        CollectionAssert.AreEqual(new[] { "70" }, output);
    }

    [TestMethod]
    public async Task AClassNoServerHas_IsAnObjectTheComponentCannotCreate()
        => CollectionAssert.AreEqual(new[] { "429" }, await Run(
            "On Error Resume Next",
            "Dim s As Object",
            "Set s = CreateObject(\"No.Such\")",
            "Debug.Print Err.Number"));

    // MS-VBA reports a machine that is not there by its HRESULT, not as error 462 (oracle\s25 O03).
    [TestMethod]
    public async Task AnObjectOnAMachineThatIsNotThere_IsTheServersHResult()
    {
        var (output, server) = await Run(true, [],
            "On Error Resume Next",
            "Dim s As Object",
            "Set s = CreateObject(\"Lab.Shelf\", \"faraway\")",
            "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "-2147023174" }, output);
        Assert.AreEqual(new ExternalObjectRequest(ExternalObjectSource.Create, "Lab.Shelf", ServerName: "faraway"), server.Requests.Single());
    }

    [TestMethod]
    public async Task GetObject_WithAClassAndNoPath_IsTheObjectThatIsRunning()
        => CollectionAssert.AreEqual(new[] { "running" }, await Run(
            "Dim s As Object",
            "Set s = GetObject(, \"Lab.Shelf\")",
            "Debug.Print s.Label"));

    [TestMethod]
    public async Task GetObject_WithAClassThatIsNotRunning_IsAnObjectTheComponentCannotCreate()
        => CollectionAssert.AreEqual(new[] { "429" }, await Run(
            "On Error Resume Next",
            "Dim s As Object",
            "Set s = GetObject(, \"Lab.Idle\")",
            "Debug.Print Err.Number"));

    [TestMethod]
    public async Task GetObject_WithAPath_IsTheObjectOfThatFile_OpenedByTheClassGivenIfAny()
        => CollectionAssert.AreEqual(new[] { "a.shelf|", "b.shelf|Lab.Shelf" }, await Run(
            "Dim s As Object",
            "Set s = GetObject(\"a.shelf\")",
            "Debug.Print s.Label",
            "Set s = GetObject(\"b.shelf\", \"Lab.Shelf\")",
            "Debug.Print s.Label"));

    [TestMethod]
    public async Task GetObject_WithAFileThatCannotBeOpened_IsFileOrClassNameNotFound()
        => CollectionAssert.AreEqual(new[] { "432" }, await Run(
            "On Error Resume Next",
            "Dim s As Object",
            "Set s = GetObject(\"missing.shelf\")",
            "Debug.Print Err.Number"));

    // an empty path is a new object of the class (oracle\s25).
    [TestMethod]
    public async Task GetObject_WithAnEmptyPathAndAClass_IsANewObject()
    {
        var (output, server) = await Run(true, [],
            "Dim s As Object",
            "Set s = GetObject(\"\", \"Lab.Shelf\")",
            "Debug.Print s.Label");

        CollectionAssert.AreEqual(new[] { "empty" }, output);
        Assert.AreEqual(ExternalObjectSource.Create, server.Requests.Single().Source);
    }

    [TestMethod]
    public async Task GetObject_WithNeitherAPathNorAClass_IsATypeMismatch()
        => CollectionAssert.AreEqual(new[] { "13" }, await Run(
            "On Error Resume Next",
            "Dim s As Object",
            "Set s = GetObject()",
            "Debug.Print Err.Number"));

    [TestMethod]
    public async Task GetObject_WithAnEmptyPathAndNoClass_IsAnInvalidProcedureCall()
        => CollectionAssert.AreEqual(new[] { "5" }, await Run(
            "On Error Resume Next",
            "Dim s As Object",
            "Set s = GetObject(\"\")",
            "Debug.Print Err.Number"));
}
