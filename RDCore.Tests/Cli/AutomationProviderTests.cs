using RDCore.Runtime.Execution.External.Automation;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using System.Reflection;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program runs against the objects of a library it references: created, called, read, written and let go of through the pipeline every call outside the
/// workspace goes through. The servers here are plain objects called by name, which is all a late-bound call is, so what is under test is the provider - how
/// calls are made, how values cross, how objects keep their identity and their lifetime, and how a server's failure becomes a run-time error - on any platform.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
public sealed class AutomationProviderTests
{
    private static readonly LibraryDescription Lab = new()
    {
        Name = "Lab",
        Classes =
        [
            new ClassDescription
            {
                Name = "Beaker",
                ProgId = "Lab.Beaker",
                IsCreatable = true,
                Members =
                [
                    new MemberDescription { Name = "Volume", Kind = MemberKind.PropertyGet, Type = "Long" },
                    new MemberDescription { Name = "Volume", Kind = MemberKind.PropertyLet, Parameters = [new ParameterDescription { Name = "Value", Type = "Long", IsByVal = true }] },
                    new MemberDescription
                    {
                        Name = "Fill", Kind = MemberKind.Method,
                        Parameters = [new ParameterDescription { Name = "Amount", Type = "Double", IsByVal = true }, new ParameterDescription { Name = "Spill", Type = "Long" }],
                    },
                    new MemberDescription
                    {
                        Name = "Pour", Kind = MemberKind.Method, Type = "Variant",
                        Parameters = [new ParameterDescription { Name = "Into", Type = "Variant", IsOptional = true }],
                    },
                    new MemberDescription { Name = "Self", Kind = MemberKind.PropertyGet, Type = "Beaker" },
                    new MemberDescription { Name = "Clone", Kind = MemberKind.Method, Type = "Beaker" },
                    new MemberDescription
                    {
                        Name = "Mix", Kind = MemberKind.Method, Type = "String",
                        Parameters = [new ParameterDescription { Name = "Parts", Type = "Variant", IsParamArray = true }],
                    },
                    new MemberDescription { Name = "Contents", Kind = MemberKind.PropertyGet, Type = "Variant" },
                    new MemberDescription { Name = "Boom", Kind = MemberKind.Method },
                    new MemberDescription { Name = "Measure", Kind = MemberKind.Method, Type = "Object" },
                    new MemberDescription { Name = "When", Kind = MemberKind.PropertyGet, Type = "Date" },
                    new MemberDescription { Name = "Price", Kind = MemberKind.PropertyGet, Type = "Currency" },
                    new MemberDescription { Name = "Tag", Kind = MemberKind.PropertyGet, Type = "Variant" },
                    new MemberDescription { Name = "Tag", Kind = MemberKind.PropertyLet, Parameters = [new ParameterDescription { Name = "Value", Type = "Variant", IsByVal = true }] },
                ],
            },
            new ClassDescription
            {
                Name = "Gauge",
                Members = [new MemberDescription { Name = "Reading", Kind = MemberKind.PropertyGet, Type = "Double" }],
            },
        ],
    };

    // the servers' side of it: plain objects, called by the name of the member the way a late-bound call is.
    private sealed class Beaker
    {
        public int Volume { get; set; }

        public object? Tag { get; set; }

        public DateTime When => new(2026, 10, 9, 12, 0, 0, DateTimeKind.Unspecified);

        public decimal Price => 12.5m;

        public Beaker Self => this;

        public Beaker Clone() => new() { Volume = Volume };

        public void Fill(double amount, ref int spill) => spill = (int)Math.Ceiling(amount);

        public string Pour(object? into) => into is Omitted ? "nowhere" : $"into {into}";

        public string Mix(params object[] parts) => string.Join("+", parts);

        public object Contents => new object[] { "a", 2, true };

        public void Boom() => throw new AutomationException(unchecked((int)0x800A03EC), "The beaker is cracked.");

        public object Measure() => new Gauge();
    }

    private sealed class Gauge
    {
        public double Reading => 1.5;
    }

    // what a parameter that the caller left out is, to a server that can tell: a plain object has no such thing, so the fake makes it one.
    private sealed class Omitted
    {
        public static readonly Omitted Value = new();
    }

    private sealed class FakeServer : IAutomationServer
    {
        public List<object> Created { get; } = [];

        public List<object> Released { get; } = [];

        public bool IsAvailable => true;

        public object CreateObject(string progId)
        {
            var created = progId == "Lab.Beaker"
                ? new Beaker()
                : throw new AutomationException(unchecked((int)0x80040154), $"'{progId}' is not registered.");
            Created.Add(created);
            return created;
        }

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference)
        {
            var modifier = new ParameterModifier(Math.Max(arguments.Length, 1));
            for (var index = 0; index < byReference.Length; index++)
            {
                modifier[index] = byReference[index];
                if (arguments[index] is Missing)
                {
                    arguments[index] = Omitted.Value;
                }
            }

            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | invocation switch
            {
                AutomationInvocation.Get => BindingFlags.GetProperty | BindingFlags.InvokeMethod,
                AutomationInvocation.Let or AutomationInvocation.Set => BindingFlags.SetProperty,
                _ => BindingFlags.InvokeMethod,
            };

            try
            {
                return target.GetType().InvokeMember(member, flags, null, target, arguments, [modifier], CultureInfo.InvariantCulture, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is AutomationException failure)
            {
                throw failure;
            }
        }

        public string? ClassNameOf(object target) => target switch
        {
            Gauge => "Lab._Gauge",
            Beaker => "Lab.Beaker",
            _ => null,
        };

        public void Release(object handle) => Released.Add(handle);
    }

    private static async Task<(string[] Output, FakeServer Server)> Run(bool allowAutomation, params string[] lines)
    {
        var server = new FakeServer();
        var output = await RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n",
            new WorkspaceLibraries(["Lab"], new InMemoryLibrarySource(Lab), server, allowAutomation));
        return (output, server);
    }

    private static async Task<string[]> Run(params string[] lines) => (await Run(true, lines)).Output;

    [TestMethod]
    public async Task AnObjectOfALibrary_IsCreated_AndItsPropertiesAreAssignedAndRead()
        => CollectionAssert.AreEqual(new[] { "0", "5" }, await Run(
            "Dim b As New Lab.Beaker",
            "Debug.Print b.Volume",
            "b.Volume = 5",
            "Debug.Print b.Volume"));

    [TestMethod]
    public async Task AnObjectCreatedWithNew_IsTheServersObject()
    {
        var (output, server) = await Run(true, "Dim b As Lab.Beaker", "Set b = New Lab.Beaker", "b.Volume = 3", "Debug.Print b.Volume");

        CollectionAssert.AreEqual(new[] { "3" }, output);
        Assert.HasCount(1, server.Created);
        Assert.AreEqual(3, ((Beaker)server.Created[0]).Volume, "the call reached the object the server made");
    }

    [TestMethod]
    public async Task AnArgumentPassedByReference_IsTheVariableTheServerWritesTo()
        => CollectionAssert.AreEqual(new[] { "3" }, await Run(
            "Dim b As New Lab.Beaker",
            "Dim spill As Long",
            "b.Fill 2.5, spill",
            "Debug.Print spill"));

    [TestMethod]
    public async Task AnOptionalArgumentThatIsLeftOut_IsLeftOutForTheServer_AndOneThatIsGiven_IsGiven()
        => CollectionAssert.AreEqual(new[] { "nowhere", "into 7" }, await Run(
            "Dim b As New Lab.Beaker",
            "Debug.Print b.Pour()",
            "Debug.Print b.Pour(7)"));

    [TestMethod]
    public async Task TheArgumentsOfAParamArray_AreTheRestOfTheCall()
        => CollectionAssert.AreEqual(new[] { "x+2+True" }, await Run(
            "Dim b As New Lab.Beaker",
            "Debug.Print b.Mix(\"x\", 2, True)"));

    [TestMethod]
    public async Task TheSameObjectOfAServer_IsTheSameObject_AndAnotherIsAnother()
        => CollectionAssert.AreEqual(new[] { "True", "False" }, await Run(
            "Dim b As New Lab.Beaker",
            "Dim c As Lab.Beaker",
            "Set c = b.Self",
            "Debug.Print c Is b",
            "Set c = b.Clone",
            "Debug.Print c Is b"));

    [TestMethod]
    public async Task AnObjectDeclaredAsAnObject_IsOfTheClassTheServerSaysItIs()
        => CollectionAssert.AreEqual(new[] { "1.5" }, await Run(
            "Dim b As New Lab.Beaker",
            "Dim g As Object",
            "Set g = b.Measure",
            "Debug.Print g.Reading"));

    [TestMethod]
    public async Task AnArrayTheServerReturns_IsAnArrayOfVariants_WithTheBoundsItHas()
        => CollectionAssert.AreEqual(new[] { "0", "2", "a", "2", "True" }, await Run(
            "Dim b As New Lab.Beaker",
            "Dim v As Variant",
            "v = b.Contents",
            "Debug.Print LBound(v)",
            "Debug.Print UBound(v)",
            "Debug.Print v(0)",
            "Debug.Print v(1)",
            "Debug.Print v(2)"));

    [TestMethod]
    public async Task ADateAndACurrencyTheServerReturns_AreValuesOfTheirDeclaredTypes()
        => CollectionAssert.AreEqual(new[] { "2026-10-09 12:00:00", "25" }, await Run(
            "Dim b As New Lab.Beaker",
            "Debug.Print b.When",
            "Debug.Print b.Price * 2"));

    [TestMethod]
    public async Task AValueOfAnyTypeIsGivenToAVariantMember_AndReadBackAsTheTypeItWas()
        => CollectionAssert.AreEqual(new[] { "8", "s!", "True" }, await Run(
            "Dim b As New Lab.Beaker",
            "b.Tag = 7",
            "Debug.Print b.Tag + 1",
            "b.Tag = \"s\"",
            "Debug.Print b.Tag & \"!\"",
            "b.Tag = True",
            "Debug.Print b.Tag"));

    [TestMethod]
    public async Task AFailureOfTheServer_IsARunTimeErrorWithItsNumberAndItsMessage_TheProgramCanHandle()
        => CollectionAssert.AreEqual(new[] { "1004", "The beaker is cracked." }, await Run(
            "Dim b As New Lab.Beaker",
            "On Error Resume Next",
            "b.Boom",
            "Debug.Print Err.Number",
            "Debug.Print Err.Description"));

    [TestMethod]
    public async Task AMemberCalledOnNoObject_IsAnObjectVariableNotSet()
        => CollectionAssert.AreEqual(new[] { "91" }, await Run(
            "Dim b As Lab.Beaker",
            "On Error Resume Next",
            "Debug.Print b.Volume",
            "Debug.Print Err.Number"));

    [TestMethod]
    public async Task WhenTheLastReferenceIsLetGo_TheServerIsToo()
    {
        var (output, server) = await Run(true,
            "Dim b As New Lab.Beaker",
            "Dim c As Lab.Beaker",
            "Set c = b",
            "Set b = Nothing",
            "Debug.Print \"held\"",
            "Set c = Nothing",
            "Debug.Print \"gone\"");

        CollectionAssert.AreEqual(new[] { "held", "gone" }, output);
        Assert.HasCount(1, server.Released);
        Assert.AreSame(server.Created[0], server.Released[0]);
    }

    [TestMethod]
    public async Task WhenTheEnvironmentRefusesAutomation_NoObjectIsMade_AndTheRefusalIsPermissionDenied()
    {
        var (output, server) = await Run(false,
            "On Error Resume Next",
            "Dim b As New Lab.Beaker",
            "b.Volume = 1",
            "Debug.Print Err.Number");

        Assert.IsEmpty(server.Created);
        Assert.AreEqual("70", output.Last());
    }

    [TestMethod]
    public async Task AClassTheServerHasNotGot_IsAnObjectTheComponentCannotCreate()
    {
        var unregistered = new LibraryDescription
        {
            Name = "Lab",
            Classes = [new ClassDescription { Name = "Missing", ProgId = "Lab.Missing", IsCreatable = true }],
        };

        var output = await RunAsync([], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\nDim m As New Lab.Missing\r\nm.ToString\r\nDebug.Print Err.Number\r\nEnd Sub\r\n",
            new WorkspaceLibraries(["Lab"], new InMemoryLibrarySource(unregistered), new FakeServer()));

        Assert.AreEqual("429", output.Last());
    }

    [TestMethod]
    public async Task OnAPlatformWithNoAutomationServers_AProgramThatNeedsOneIsToldSo()
    {
        var output = await RunAsync([], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\nDim b As New Lab.Beaker\r\nb.Volume = 1\r\nDebug.Print Err.Number\r\nEnd Sub\r\n",
            new WorkspaceLibraries(["Lab"], new InMemoryLibrarySource(Lab), new UnavailableAutomationServer()));

        CollectionAssert.AreEqual(new[] { "429" }, output);
    }
}
