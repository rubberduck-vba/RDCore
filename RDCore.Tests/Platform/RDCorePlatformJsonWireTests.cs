using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Serialization;
using RDCore.SDK.Workspace;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmniSharpSerializer = OmniSharp.Extensions.JsonRpc.ISerializer;

namespace RDCore.Tests.Platform;

/// <summary>
/// A client that has only <c>RDCore.SDK.Protocol</c> writes and reads every message of it with <see cref="RDCorePlatformJson.Options"/>; the platform's own processes
/// write and read them with OmniSharp's Newtonsoft serializers. The JSON must be the same both ways, member for member.
/// </summary>
[TestClass]
public sealed class RDCorePlatformJsonWireTests
{
    private static readonly Uri Module = new("file:///c:/workspace/Module1.bas");

    private static readonly SourceRange Range = new(1, 2, 3, 4);

    /// <summary>
    /// One message of each Protocol type, with every member off its default, and what the platform puts on the wire for it: its SDK wrapper where the wrapper
    /// writes it differently from Newtonsoft's defaults (<see cref="SystemTextJsonBridge{T}"/>), the message itself otherwise.
    /// </summary>
    private static IEnumerable<(object Message, object OnTheWire)> Messages()
    {
        static (object, object) Same(object message) => (message, message);

        yield return Same(new SessionStatusParams { WaitMilliseconds = 250 });
        yield return Same(new SessionStatusResult
        {
            IsComposed = true,
            ProjectName = "VBAProject",
            ModuleCount = 3,
            ReferenceCount = 2,
            Memory = new SessionMemoryStatus { ReservedBytes = 1, AvailableBytes = 2, AllocatedBytes = 3, FreeBytes = 4, CommittedBytes = 5, LargestFreeBlock = 6 },
        });
        yield return Same(new SessionExecuteParams { Source = "Debug.Print 1", ModuleName = "Module1", EntryPoint = "Main", Debug = true, Immediate = true, SessionModule = true });
        yield return Same(new ExecuteResult
        {
            Outcome = ExecutionOutcome.Suspended,
            Output = ["1"],
            StreamedLines = 7,
            Diagnostics = ["RDC00001"],
            ErrorNumber = 13,
            ErrorMessage = "Type mismatch",
            ErrorCode = "VBR00013",
            ErrorTitle = "Type mismatch",
            ErrorSource = "VBAProject",
            ErrorLine = 3,
            ErrorCharacter = 4,
            ErrorLineNumber = 100,
            ConditionError = "x",
            StackTrace = [new ExecuteStackFrame("Main", 1, 2, [5, 6])],
        });
        yield return Same(new SessionDiscardParams { ModuleName = "Module1", EndProgram = true });
        yield return Same(new DiscardResult { Discarded = 2 });
        yield return Same(new SessionPeekParams { Address = 16 });
        yield return Same(new PeekResult { Address = 16, IsAllocated = true, Value = 255 });
        yield return Same(new SessionPokeParams { Address = 16, Value = 7 });
        yield return Same(new PokeResult { Address = 16, IsWritten = true });
        yield return Same(new PokeResult { Address = 16, Error = "the program is running" });

        yield return Same(new SessionMessageBoxParams { Prompt = "Hello", Title = "Title", Style = 4, Output = ["before"] });
        yield return Same(new MessageBoxResult { Handled = true, Result = 6 });
        yield return Same(new SessionInputBoxParams { Prompt = "Name?", Title = "Title", Default = "x", XPos = 10, YPos = 20, Output = ["before"] });
        yield return Same(new InputBoxResult { Handled = true, Cancelled = true, Value = "y" });

        yield return Same(new SessionDebugResumeParams { Step = StepKind.Over });
        yield return Same(new SessionDebugResumeParams { Step = null });
        yield return Same(new SessionDebugPauseParams());
        yield return Same(new SessionDebugGotoParams { Line = 4, Label = "Retry" });
        yield return Same(new SessionDebugEditStatusParams());
        yield return Same(new SessionDebugTerminateParams { Wipe = true });
        yield return Same(new SessionDebugBreakpointsParams { ModuleName = "Module1", Breakpoints = [new SessionBreakpointLine { Line = 2, IsEnabled = false, Condition = "i > 3" }] });
        yield return Same(new SessionDebugStackParams());
        yield return Same(new SessionDebugVariablesParams { FrameId = 1, Scope = DebugVariableScope.Module, Reference = 9 });
        yield return Same(new SessionDebugEvaluateParams { FrameId = 1, Expression = "i + 1" });
        yield return Same(new SessionDebugExecuteParams { FrameId = 1, Statement = "i = 2" });

        yield return Same(new DebugAck { Acted = true });
        yield return Same(new DebugGotoResult { Moved = true, Reason = "moved", Line = 4, Character = 0 });
        yield return Same(new DebugGotoResult { Moved = false, Reason = null });
        yield return Same(new DebugEditStatusResult
        {
            Suspended = true,
            Edits = ImmutableArray.Create(
                new DebugEdit { Name = "Module1.Main", Kind = DebugEditKind.Restart, Reason = "signature", ModuleUri = Module, Range = Range },
                new DebugEdit { Name = "Module1.Other", ModuleUri = null, Range = null }),
        });
        yield return Same(new DebugBreakpointsResult { Breakpoints = [new DebugBreakpoint(2, true), new DebugBreakpoint(3, false, "Expected: expression")], Judged = true });
        yield return Same(new DebugStackResult { Frames = [new DebugStackFrame(1, "Main", "Module1", 2, 3, [4], "ErrHandler"), new DebugStackFrame(2, "Other", "Module1", 5, 6)] });
        yield return Same(new DebugVariablesResult { Variables = [new DebugVariable("i", "1", "Integer"), new DebugVariable("o", "Object", "Class1", 3)], NotListed = 2 });
        yield return Same(new DebugEvaluateResult { Success = true, Value = "2", Type = "Integer", Reference = 1, Error = "none", Output = ["x"], EndedTheProgram = true });

        yield return Same(new PlatformInitializeParams
        {
            ExpectedComponent = CoreServerComponent.LanguageServer,
            Expected = new CorePlatformClientCapabilities
            {
                Parsing = new ParserCapabilities { ParseFullDocument = new ParseFullDocument(true) },
                EnvironmentHost = new EnvironmentHostCapabilities { DefineSymbols = new DefineSymbols(true) },
                LanguageServer = new LanguageServerCapabilities
                {
                    SessionExecute = new SessionExecute(true), SessionInteraction = new SessionInteraction(true), Unattended = new Unattended(true),
                },
            },
        });
        yield return Same(new PlatformInitializeParams { ExpectedComponent = CoreServerComponent.LanguageServer, Expected = new CorePlatformClientCapabilities { LanguageServer = new() } });
        yield return Same(new PlatformInitializeResult { Component = CoreServerComponent.LanguageServer, Provided = ["SessionExecute", "SessionStatus"] });

        // declared by an extension's assembly and read from its manifest, in no capability group: written as they are.
        yield return Same(new DiagnoseDocument(true));
        yield return Same(new CliCommand(true));

        // a client's opt-out of the dialogs, as the handshake carries it.
        yield return Same(new Unattended(true));

        yield return Same(new SourceLocation(Module, Range));

        yield return (new RubberduckIndenterSettingsParams(), new RubberduckIndenterSettingsRequest());

        var read = new RubberduckIndenterSettingsResult
        {
            Source = RubberduckIndenterSettingsSource.SmartIndenterRegistry,
            Values = new Dictionary<string, JsonElement>
            {
                ["IndentProc"] = JsonSerializer.SerializeToElement("Y"),
                ["AlignDimCol"] = JsonSerializer.SerializeToElement(15),
            },
            TabWidth = 4,
        };
        yield return (read, new RubberduckIndenterSettingsReply(read));

        var apply = new RubberduckIndenterSettingsApplyParams { Source = RubberduckIndenterSettingsSource.RubberduckConfig, Settings = new() { ["Formatting.IndentSpaces"] = "2" } };
        yield return (apply, new RubberduckIndenterSettingsApplyRequest(apply));
        yield return Same(new RubberduckIndenterSettingsApplyResult { Applied = 1, Refused = ["Formatting.Nope"] });
    }

    /// <summary>
    /// The wire of each message above, in the same order, as a client that has only the Protocol package must write it: what a rename of a member, or a change of the
    /// order of an enumeration, would change, and the two serializers compared with each other would not see.
    /// </summary>
    private static readonly (string Type, string Json)[] Goldens =
    [
        ("SessionStatusParams", """{"waitMilliseconds":250}"""),
        ("SessionStatusResult", """{"isComposed":true,"projectName":"VBAProject","moduleCount":3,"referenceCount":2,"memory":{"reservedBytes":1,"availableBytes":2,"allocatedBytes":3,"freeBytes":4,"committedBytes":5,"largestFreeBlock":6}}"""),
        ("SessionExecuteParams", """{"source":"Debug.Print 1","moduleName":"Module1","entryPoint":"Main","debug":true,"immediate":true,"sessionModule":true}"""),
        ("ExecuteResult", """{"outcome":7,"output":["1"],"streamedLines":7,"diagnostics":["RDC00001"],"errorNumber":13,"errorMessage":"Type mismatch","errorCode":"VBR00013","errorTitle":"Type mismatch","errorSource":"VBAProject","errorLine":3,"errorCharacter":4,"errorLineNumber":100,"conditionError":"x","stackTrace":[{"procedure":"Main","line":1,"character":2,"returnLines":[5,6]}]}"""),
        ("SessionDiscardParams", """{"moduleName":"Module1","endProgram":true}"""),
        ("DiscardResult", """{"discarded":2}"""),
        ("SessionPeekParams", """{"address":16}"""),
        ("PeekResult", """{"address":16,"isAllocated":true,"value":255}"""),
        ("SessionPokeParams", """{"address":16,"value":7}"""),
        ("PokeResult", """{"address":16,"isWritten":true,"error":null}"""),
        ("PokeResult", """{"address":16,"isWritten":false,"error":"the program is running"}"""),
        ("SessionMessageBoxParams", """{"prompt":"Hello","title":"Title","style":4,"output":["before"]}"""),
        ("MessageBoxResult", """{"handled":true,"result":6,"declined":false,"failure":null}"""),
        ("SessionInputBoxParams", """{"prompt":"Name?","title":"Title","default":"x","xPos":10,"yPos":20,"output":["before"]}"""),
        ("InputBoxResult", """{"handled":true,"cancelled":true,"value":"y","declined":false}"""),
        ("SessionDebugResumeParams", """{"step":1}"""),
        ("SessionDebugResumeParams", """{"step":null}"""),
        ("SessionDebugPauseParams", """{}"""),
        ("SessionDebugGotoParams", """{"line":4,"label":"Retry"}"""),
        ("SessionDebugEditStatusParams", """{}"""),
        ("SessionDebugTerminateParams", """{"wipe":true}"""),
        ("SessionDebugBreakpointsParams", """{"moduleName":"Module1","breakpoints":[{"line":2,"isEnabled":false,"condition":"i > 3"}]}"""),
        ("SessionDebugStackParams", """{}"""),
        ("SessionDebugVariablesParams", """{"frameId":1,"scope":1,"reference":9}"""),
        ("SessionDebugEvaluateParams", """{"frameId":1,"expression":"i + 1"}"""),
        ("SessionDebugExecuteParams", """{"frameId":1,"statement":"i = 2"}"""),
        ("DebugAck", """{"acted":true,"stopping":false}"""),
        ("DebugGotoResult", """{"moved":true,"reason":"moved","line":4,"character":0}"""),
        ("DebugGotoResult", """{"moved":false,"reason":null,"line":-1,"character":-1}"""),
        ("DebugEditStatusResult", """{"suspended":true,"edits":[{"name":"Module1.Main","kind":1,"reason":"signature","moduleUri":"file:///c:/workspace/Module1.bas","range":{"start":{"line":1,"character":2},"end":{"line":3,"character":4},"isEmpty":false}},{"name":"Module1.Other","kind":0,"reason":"","moduleUri":null,"range":null}]}"""),
        ("DebugBreakpointsResult", """{"breakpoints":[{"line":2,"verified":true,"conditionError":null},{"line":3,"verified":false,"conditionError":"Expected: expression"}],"judged":true}"""),
        ("DebugStackResult", """{"frames":[{"id":1,"procedure":"Main","module":"Module1","line":2,"character":3,"handler":"ErrHandler","returnLines":[4]},{"id":2,"procedure":"Other","module":"Module1","line":5,"character":6,"handler":null,"returnLines":[]}]}"""),
        ("DebugVariablesResult", """{"variables":[{"name":"i","value":"1","type":"Integer","reference":0},{"name":"o","value":"Object","type":"Class1","reference":3}],"notListed":2}"""),
        ("DebugEvaluateResult", """{"success":true,"value":"2","type":"Integer","reference":1,"error":"none","output":["x"],"endedTheProgram":true}"""),
        ("PlatformInitializeParams", """{"expectedComponent":1,"expected":{"parsing":{"parseFullDocument":{"isSupported":true},"parseTokens":{"isSupported":false}},"environmentHost":{"defineSymbols":{"isSupported":true},"sessionStatus":{"isSupported":false},"sessionExecute":{"isSupported":false},"sessionDiscard":{"isSupported":false},"projectReload":{"isSupported":false},"programDebugging":{"isSupported":false},"semanticAnalysis":{"isSupported":false},"sessionMemoryAccess":{"isSupported":false}},"languageServer":{"sessionStatus":{"isSupported":false},"sessionExecute":{"isSupported":true},"programDebugging":{"isSupported":false},"sessionDiscard":{"isSupported":false},"sessionAnalyze":{"isSupported":false},"sessionCompletion":{"isSupported":false},"sessionSignatureHelp":{"isSupported":false},"sessionSemanticTokens":{"isSupported":false},"sessionMemoryAccess":{"isSupported":false},"sessionInteraction":{"isSupported":true},"unattended":{"isSupported":true},"rubberduckIndenterSettings":{"isSupported":false}}}}"""),
        ("PlatformInitializeParams", """{"expectedComponent":1,"expected":{"languageServer":{"sessionStatus":{"isSupported":false},"sessionExecute":{"isSupported":false},"programDebugging":{"isSupported":false},"sessionDiscard":{"isSupported":false},"sessionAnalyze":{"isSupported":false},"sessionCompletion":{"isSupported":false},"sessionSignatureHelp":{"isSupported":false},"sessionSemanticTokens":{"isSupported":false},"sessionMemoryAccess":{"isSupported":false},"sessionInteraction":{"isSupported":false},"unattended":{"isSupported":false},"rubberduckIndenterSettings":{"isSupported":false}}}}"""),
        ("PlatformInitializeResult", """{"component":1,"provided":["SessionExecute","SessionStatus"]}"""),
        ("DiagnoseDocument", """{"isSupported":true}"""),
        ("CliCommand", """{"isSupported":true}"""),
        ("Unattended", """{"isSupported":true}"""),
        ("SourceLocation", """{"uri":"file:///c:/workspace/Module1.bas","range":{"start":{"line":1,"character":2},"end":{"line":3,"character":4},"isEmpty":false}}"""),
        ("RubberduckIndenterSettingsParams", """{}"""),
        ("RubberduckIndenterSettingsResult", """{"source":"SmartIndenterRegistry","values":{"IndentProc":"Y","AlignDimCol":15},"tabWidth":4}"""),
        ("RubberduckIndenterSettingsApplyParams", """{"source":"RubberduckConfig","settings":{"Formatting.IndentSpaces":"2"}}"""),
        ("RubberduckIndenterSettingsApplyResult", """{"applied":1,"refused":["Formatting.Nope"]}"""),
    ];

    private static IEnumerable<OmniSharpSerializer> PlatformSerializers() => [new LspSerializer(), new PullDiagnosticsLspSerializer()];

    [TestMethod]
    public void EveryMessage_IsWrittenAsItsGoldenJsonSays()
    {
        var messages = Messages().ToArray();
        Assert.HasCount(messages.Length, Goldens, "a golden for every message above");

        foreach (var ((message, _), (type, golden)) in messages.Zip(Goldens))
        {
            Assert.AreEqual(type, message.GetType().Name, "the goldens are in the order of the messages");
            AssertSameJson(golden, JsonSerializer.Serialize(message, message.GetType(), RDCorePlatformJson.Options), type);
        }
    }

    private static JsonNode? Parse(string json) => JsonNode.Parse(json);

    private static void AssertSameJson(string expected, string actual, string what)
        => Assert.IsTrue(JsonNode.DeepEquals(Parse(expected), Parse(actual)), $"{what}\nexpected: {expected}\nactual:   {actual}");

    [TestMethod]
    public void EveryMessage_IsWrittenTheSame_ByTheProtocolOptionsAndByThePlatform()
    {
        foreach (var serializer in PlatformSerializers())
        {
            foreach (var (message, onTheWire) in Messages())
            {
                var protocol = JsonSerializer.Serialize(message, message.GetType(), RDCorePlatformJson.Options);
                var platform = serializer.SerializeObject(onTheWire);

                AssertSameJson(platform, protocol, $"{message.GetType().Name}, {serializer.GetType().Name}");
            }
        }
    }

    [TestMethod]
    public void WhatThePlatformWrites_TheProtocolOptionsRead_AsTheSameMessage()
    {
        foreach (var serializer in PlatformSerializers())
        {
            foreach (var (message, onTheWire) in Messages())
            {
                var platform = serializer.SerializeObject(onTheWire);
                var read = JsonSerializer.Deserialize(platform, message.GetType(), RDCorePlatformJson.Options);

                AssertSameJson(platform, JsonSerializer.Serialize(read, message.GetType(), RDCorePlatformJson.Options), $"{message.GetType().Name}, {serializer.GetType().Name}");
            }
        }
    }

    [TestMethod]
    public void WhatTheProtocolOptionsWrite_ThePlatformReads_AsTheSameMessage()
    {
        foreach (var serializer in PlatformSerializers())
        {
            foreach (var (message, onTheWire) in Messages())
            {
                var protocol = JsonSerializer.Serialize(message, message.GetType(), RDCorePlatformJson.Options);
                var read = serializer.DeserializeObject(protocol, onTheWire.GetType());

                AssertSameJson(protocol, serializer.SerializeObject(read), $"{message.GetType().Name}, {serializer.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// The name on the wire of each method a client sends or answers, as a client that has only the Protocol package must send or answer it: what a change of a
    /// value of <see cref="RDCorePlatformProtocol"/> would change, and a client and a language server built from the same source would not see.
    /// </summary>
    private static readonly (string Constant, string Method)[] MethodGoldens =
    [
        ("PlatformInitialize", "rdcore/platform/initialize"),
        ("SessionStatus", "rdcore/session/status"),
        ("SessionExecute", "rdcore/session/execute"),
        ("SessionDiscard", "rdcore/session/discard"),
        ("SessionMessageBox", "rdcore/session/interaction/msgBox"),
        ("SessionInputBox", "rdcore/session/interaction/inputBox"),
        ("SessionDebugResume", "rdcore/session/debug/resume"),
        ("SessionDebugPause", "rdcore/session/debug/pause"),
        ("SessionDebugGoto", "rdcore/session/debug/goto"),
        ("SessionDebugEditStatus", "rdcore/session/debug/editStatus"),
        ("SessionDebugTerminate", "rdcore/session/debug/terminate"),
        ("SessionDebugBreakpoints", "rdcore/session/debug/breakpoints"),
        ("SessionDebugStack", "rdcore/session/debug/stack"),
        ("SessionDebugVariables", "rdcore/session/debug/variables"),
        ("SessionDebugEvaluate", "rdcore/session/debug/evaluate"),
        ("SessionDebugExecute", "rdcore/session/debug/execute"),
        ("SessionAnalyze", "rdcore/session/analyze"),
        ("SessionCompletion", "rdcore/session/completion"),
        ("RubberduckIndenterSettings", "rdcore/formatting/indenterSettings"),
        ("RubberduckIndenterSettingsApply", "rdcore/formatting/indenterSettings/apply"),
        ("SessionSignatureHelp", "rdcore/session/signatureHelp"),
        ("SessionSemanticTokens", "rdcore/session/semanticTokens"),
        ("SessionCommands", "rdcore/session/commands"),
        ("SessionPeek", "rdcore/session/memory/peek"),
        ("SessionPoke", "rdcore/session/memory/poke"),
    ];

    [TestMethod]
    public void EveryMethodOfTheClient_IsNamedAsItsGoldenSays()
    {
        var methods = typeof(RDCorePlatformProtocol).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!))
            .ToArray();

        CollectionAssert.AreEqual(MethodGoldens, methods, "\n" + string.Join("\n", methods));
    }

    [TestMethod]
    public void EveryTypeOfTheProtocol_IsInAMessageAbove()
    {
        var reached = new HashSet<Type>();
        foreach (var (message, _) in Messages())
        {
            Reach(message, reached);
        }

        // the client logic sends and answers messages, and is none; the pre-launch preparations (the language server's, the debug adapter's) are local to the
        // client, and the manifest is a file of the platform root.
        Type[] notMessages =
        [
            typeof(RDCorePlatformClient),
            typeof(RDCoreLanguageServerLaunch), typeof(RDCoreLanguageServerLaunchOptions),
            typeof(RDCoreDebugAdapterLaunch), typeof(RDCoreDebugAdapterLaunchOptions),
            typeof(PlatformManifest),
        ];
        // the project file model is a workspace's .rdproj, which a client reads from disk: it is no message either.
        var projectFileModel = typeof(ProjectFile).Namespace;

        var unreached = typeof(RDCorePlatformJson).Assembly.GetExportedTypes()
            .Where(type => !type.IsAbstract && !type.IsEnum && !type.IsInterface && !typeof(Exception).IsAssignableFrom(type) && !notMessages.Contains(type) && !reached.Contains(type))
            .Where(type => type.Namespace != projectFileModel)
            .Select(type => type.FullName)
            .ToList();

        Assert.IsEmpty(unreached, "Protocol types no message above has: " + string.Join(", ", unreached));
    }

    // every type a message is made of, through its public properties and the items of its collections.
    private static void Reach(object? value, HashSet<Type> reached)
    {
        if (value is null or string or Uri or JsonElement || value.GetType().IsPrimitive || value.GetType().IsEnum)
        {
            return;
        }

        if (value is IEnumerable items)
        {
            foreach (var item in items)
            {
                Reach(item, reached);
            }

            return;
        }

        reached.Add(value.GetType());
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
        {
            Reach(property.GetValue(value), reached);
        }
    }
}
