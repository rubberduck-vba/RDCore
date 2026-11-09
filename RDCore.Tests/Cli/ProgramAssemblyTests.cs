using NSubstitute;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace RDCore.Tests.Cli;

/// <summary>
/// The procedures of a program compiled into an assembly a build saves: the assembly references nothing of the runtime a loaded assembly cannot call, and a
/// session bound to it runs its code, and runs the program as the interpreter does.
/// </summary>
[TestClass]
public sealed class ProgramAssemblyTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // two procedures that call each other, declared before the one that calls them first: the build compiles them in another order than the run calls them.
    private static readonly string[] Calls =
    [
        "Private Function Ping(ByVal N As Long) As Long",
        "    If N <= 0 Then",
        "        Ping = 0",
        "    Else",
        "        Ping = 1 + Pong(N - 1)",
        "    End If",
        "End Function",
        "Private Function Pong(ByVal N As Long) As Long",
        "    If N <= 0 Then",
        "        Pong = 0",
        "    Else",
        "        Pong = 2 + Ping(N - 1)",
        "    End If",
        "End Function",
        "Private Function Fact(ByVal N As Long) As Long",
        "    If N <= 1 Then",
        "        Fact = 1",
        "    Else",
        "        Fact = N * Fact(N - 1)",
        "    End If",
        "End Function",
        "Public Sub Main()",
        "    Debug.Print Pong(10)",
        "    Debug.Print Fact(10)",
        "    Debug.Print Total()",
        "End Sub",
    ];

    private static string[] TotalOf(int count) =>
    [
        "Private Function Total() As Double",
        "    Dim I As Long",
        "    Dim S As Double",
        $"    For I = 1 To {count}",
        "        S = S + I",
        "    Next",
        "    Total = S",
        "End Function",
    ];

    private static readonly string[] Compiled = ["Ping", "Pong", "Fact", "Total"];

    // every procedure of the Program module compiled into an assembly, as rdc build compiles a workspace.
    private static async Task<byte[]> BuildAsync(string program)
    {
        byte[] built = [];
        await ModuleWorkspace.WithHostAsync([], program, async provider =>
        {
            await provider.Image.Semantics.WhenAllEvaluatedAsync();
            var session = provider.Session;
            var pipeline = RuntimeExecutionPipeline.Create(session, provider.Image, Substitute.For<IVerboseMessageBuilder>());
            Assert.IsTrue(session.Symbols.TryGetModule("Program", out var module));

            var builder = new ProgramAssemblyBuilder(new AssemblyName("Built"));
            foreach (var procedure in session.Symbols.MembersOf(module.Uri).OfType<VBTypeMemberSymbol>())
            {
                _ = builder.Add(provider.Image, session, pipeline.Executor, procedure);
            }

            using var stream = new MemoryStream();
            builder.Save(stream);
            built = stream.ToArray();
        });

        return built;
    }

    private sealed record class BoundRun(ExecuteResult Interpreted, ExecuteResult Bound, IReadOnlyDictionary<string, CompilationResult> Compiled, int BoundCount,
        Assembly Assembly);

    // the program run by the interpreter, then again, compiled, in the same session bound to the assembly: what each procedure compiled to, and how many
    // ran the assembly's code.
    private static async Task<BoundRun> RunBoundAsync(string program, byte[] built)
    {
        BoundRun? run = null;
        var context = new AssemblyLoadContext("built program", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(built));
            await ModuleWorkspace.InspectAsync([], program, async (provider, interpreted, again) =>
            {
                await provider.Image.Semantics.WhenAllEvaluatedAsync();
                Assert.IsTrue(ProgramAssembly.Bind(provider.Image, assembly), "the assembly is of this build of the runtime");
                using var compiled = CompiledExecution.Running(VBExecutionEngine.Compiled);
                var bound = await again();
                run = new BoundRun(interpreted, bound, CompiledExecutionTests.ByName(provider), provider.Image.Compiler.Bound, assembly);
            }, engine: VBExecutionEngine.Interpreter);
        }
        finally
        {
            context.Unload();
        }

        return run!;
    }

    private static string Observed(ExecuteResult result)
        => $"{result.Outcome} | {string.Join(" ; ", result.Output.Select(line => line.Trim()))} | {result.ErrorNumber} @{result.ErrorLine}";

    [TestMethod]
    public async Task ASessionBoundToTheAssembly_RunsTheCodeOfEachProcedure_AsTheInterpreterRunsTheProgram()
    {
        CompiledExecutionTests.RequirePayloads();
        var program = Program([.. Calls, .. TotalOf(1000)]);
        var built = await BuildAsync(program);

        var run = await RunBoundAsync(program, built);

        Assert.AreEqual(Observed(run.Interpreted), Observed(run.Bound));
        CollectionAssert.AreEqual(new[] { "15", "3628800", "500500" }, run.Bound.Output.Select(line => line.Trim()).ToArray());
        Assert.AreEqual(0, run.Bound.ErrorNumber);
        foreach (var name in Compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.AreSame(run.Assembly, result.Body?.Entry?.Module.Assembly, $"{name} runs the code it was built to");
        }

        Assert.IsGreaterThanOrEqualTo(Compiled.Length, run.BoundCount);
    }

    [TestMethod]
    public async Task AProcedureThatIsNoLongerTheOneBuilt_IsCompiledAtItsFirstCall_AndTheOthersRunTheirBuiltCode()
    {
        CompiledExecutionTests.RequirePayloads();
        var built = await BuildAsync(Program([.. Calls, .. TotalOf(1000)]));
        var changed = Program([.. Calls, .. TotalOf(10)]);

        var run = await RunBoundAsync(changed, built);

        Assert.AreEqual(Observed(run.Interpreted), Observed(run.Bound));
        Assert.IsTrue(run.Compiled.TryGetValue("Total", out var total));
        Assert.IsNotNull(total.Body, "the changed procedure is compiled all the same");
        Assert.AreNotSame(run.Assembly, total.Body.Entry?.Module.Assembly, "the changed procedure is compiled at its first call");
        foreach (var name in Compiled.Except(["Total"]))
        {
            Assert.AreSame(run.Assembly, run.Compiled[name].Body?.Entry?.Module.Assembly, $"{name} runs the code it was built to");
        }
    }

    [TestMethod]
    public async Task TheAssembly_ReferencesOnlyPublicMembersOfTheRuntime()
    {
        CompiledExecutionTests.RequirePayloads();
        var built = await BuildAsync(Program([.. Calls, .. TotalOf(1000)]));
        var runtime = typeof(ProgramAssemblyBuilder).Assembly;

        using var pe = new PEReader(new MemoryStream(built));
        var reader = pe.GetMetadataReader();
        var referenced = 0;
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var parent = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (parent.ResolutionScope.Kind != HandleKind.AssemblyReference
                || reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)parent.ResolutionScope).Name) != runtime.GetName().Name)
            {
                continue;
            }

            var typeName = $"{reader.GetString(parent.Namespace)}.{reader.GetString(parent.Name)}";
            var type = runtime.GetType(typeName);
            Assert.IsNotNull(type, typeName);
            Assert.IsTrue(type.IsPublic, $"{typeName} is public");
            var name = reader.GetString(member.Name);
            var members = type.GetMember(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            Assert.IsNotEmpty(members, $"{typeName}.{name}");
            Assert.IsTrue(members.All(IsPublic), $"{typeName}.{name} is public");
            referenced++;
        }

        Assert.IsGreaterThan(0, referenced, "the code calls the runtime");
    }

    [TestMethod]
    public void AnAssemblyOfAnotherBuildOfTheRuntime_IsNotBound()
    {
        // an assembly that says nothing of the runtime that compiled it is no assembly a build saved.
        Assert.IsFalse(ProgramAssembly.Bind(new ProgramImage(), typeof(ProgramAssemblyTests).Assembly));
    }

    private static bool IsPublic(MemberInfo member) => member switch
    {
        MethodBase method => method.IsPublic,
        FieldInfo field => field.IsPublic,
        _ => false,
    };
}
