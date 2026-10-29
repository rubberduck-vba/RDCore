using RDCore.External.Automation;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using System.Reflection;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What the code of a worksheet's module becomes when it moves out of its host (RDC01005): a class that holds the worksheet in a <c>WithEvents</c> variable of
/// <c>Excel.Worksheet</c>, from an application the program created, handles the worksheet's events as the module's <c>Worksheet_Change</c> did. A stand-in for
/// Excel raises them, so this runs where no Excel is.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.9 WithEvents")]
public sealed class AutomationSheetEventTests
{
    private static readonly LibraryDescription StandInExcel = new()
    {
        Name = "Excel",
        Classes =
        [
            new ClassDescription
            {
                Name = "Application",
                ProgId = "Excel.Application",
                IsCreatable = true,
                Members = [new MemberDescription { Name = "ActiveSheet", Kind = MemberKind.PropertyGet, Type = "Worksheet" }],
            },
            new ClassDescription
            {
                Name = "Worksheet",
                Members =
                [
                    new MemberDescription { Name = "Name", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription { Name = "Edit", Kind = MemberKind.Method, Parameters = [new ParameterDescription { Name = "Address", Type = "String", IsByVal = true }] },
                ],
                Events = [new EventDescription { Name = "Change", Parameters = [new ParameterDescription { Name = "Target", Type = "Range", IsByVal = true }] }],
            },
            new ClassDescription
            {
                Name = "Range",
                Members = [new MemberDescription { Name = "Address", Kind = MemberKind.PropertyGet, Type = "String" }],
            },
        ],
    };

    private sealed class Range(string address)
    {
        public string Address { get; } = address;
    }

    private sealed class Worksheet
    {
        public Action<object?[]>? Changed { get; set; }

        public string Name => "GameSheet";

        // a cell edited, as a user would edit it: the sheet says it changed, while the call that edits it is made.
        public void Edit(string address) => Changed?.Invoke([new Range(address)]);
    }

    private sealed class Application
    {
        public Worksheet ActiveSheet { get; } = new();
    }

    private sealed class StandInServer : IAutomationServer
    {
        private readonly Dictionary<object, IAutomationEventSink> _sinks = new(ReferenceEqualityComparer.Instance);

        public bool IsAvailable => true;

        public object CreateObject(string progId)
        {
            var application = new Application();
            application.ActiveSheet.Changed = arguments =>
            {
                if (_sinks.TryGetValue(application.ActiveSheet, out var sink))
                {
                    sink.OnEvent(new AutomationEvent("Change", arguments, isSynchronous: true, _ => { }));
                }
            };

            return application;
        }

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
                | (invocation == AutomationInvocation.Get ? BindingFlags.GetProperty : BindingFlags.InvokeMethod);
            try
            {
                return target.GetType().InvokeMember(member, flags, null, target, arguments, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }

        public string? ClassNameOf(object target) => $"Excel.{target.GetType().Name}";

        public bool MoveNext(object enumerator, out object? current)
        {
            current = null;
            return false;
        }

        public void Reset(object enumerator)
        {
        }

        public void Advise(object source, IAutomationEventSink sink) => _sinks[source] = sink;

        public void Unadvise(object source) => _ = _sinks.Remove(source);

        public void Release(object handle)
        {
        }
    }

    // the code of the worksheet's module, moved into a class: the handler is named for the variable, not for the document.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("GameSheetView", ClassModule(
            "GameSheetView",
            "Private WithEvents AutomationSheet As Excel.Worksheet",
            "Public Sub Attach(ByVal sheet As Excel.Worksheet)",
            "    Set AutomationSheet = sheet",
            "End Sub",
            "Private Sub AutomationSheet_Change(ByVal Target As Excel.Range)",
            "    Debug.Print AutomationSheet.Name & \" changed at \" & Target.Address",
            "End Sub")),
    ];

    [TestMethod]
    public async Task ASheetHeldInAWithEventsVariable_FromAnApplicationTheProgramCreated_HasItsChangesHandled()
    {
        var output = await RunAsync(Classes, string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            "    Dim app As Excel.Application",
            "    Set app = New Excel.Application",
            "    Dim view As GameSheetView",
            "    Set view = New GameSheetView",
            "    view.Attach app.ActiveSheet",
            "    app.ActiveSheet.Edit \"$A$1\"",
            "End Sub",
            ""), new WorkspaceLibraries(["Excel"], new InMemoryLibrarySource(StandInExcel), new StandInServer()));

        CollectionAssert.AreEqual(new[] { "GameSheet changed at $A$1" }, output);
    }
}
