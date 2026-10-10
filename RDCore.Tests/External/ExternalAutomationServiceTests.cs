using RDCore.External.Automation;
using RDCore.External.Hosting;
using RDCore.External.Protocol;
using System.Globalization;
using System.Reflection;

namespace RDCore.Tests.External;

/// <summary>
/// The external host answers its environment host's requests with the servers of its platform: objects cross as handles, a failure as what the server reported, and
/// an event goes to the environment host while the thread that raised it waits - making, meanwhile, the calls its handlers make.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class ExternalAutomationServiceTests
{
    private sealed class Valve
    {
        public Action<AutomationEvent>? Raise { get; set; }

        public Valve? Twin { get; set; }

        public int Thread => Environment.CurrentManagedThreadId;

        public void Fill(ref int amount) => amount *= 2;

        // raises an event inside the call, as a server does for what can be cancelled, and says whether it was.
        public string Tap(int amount)
        {
            var arguments = new object?[] { amount, false };
            Raise?.Invoke(new AutomationEvent("Spilled", arguments, isSynchronous: true, _ => { }));
            return $"{amount} cancelled={arguments[1]}";
        }

        public void Break() => throw new AutomationException(unchecked((int)0x800A03EC), "The valve is broken.", "Rig");
    }

    private sealed class FakePlatform : IAutomationServer
    {
        public List<object> Released { get; } = [];

        public bool IsAvailable => true;

        public object CreateObject(string progId) => progId == "Rig.Valve"
            ? new Valve()
            : throw new AutomationException(unchecked((int)0x800401F3), "Invalid class string.");

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | invocation switch
            {
                AutomationInvocation.Get => BindingFlags.GetProperty,
                AutomationInvocation.Let => BindingFlags.SetProperty,
                _ => BindingFlags.InvokeMethod,
            };
            try
            {
                return target.GetType().InvokeMember(member, flags, null, target, arguments, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is AutomationException failure)
            {
                throw failure;
            }
        }

        public string? ClassNameOf(object target) => target is Valve ? "Rig.Valve" : null;

        public bool MoveNext(object enumerator, out object? current)
        {
            current = null;
            return false;
        }

        public void Reset(object enumerator)
        {
        }

        public void Advise(object source, IAutomationEventSink sink) => ((Valve)source).Raise = sink.OnEvent;

        public void Unadvise(object source) => ((Valve)source).Raise = null;

        public void Release(object handle) => Released.Add(handle);
    }

    private static long Create(ExternalAutomationService service)
        => service.Create(new AutomationCreateParams { ProgId = "Rig.Valve" }).Handle;

    private static AutomationInvokeResult Invoke(ExternalAutomationService service, long target, string member, params ExternalValue[] arguments)
        => service.Invoke(new AutomationInvokeParams
        {
            Target = target,
            Member = member,
            Invocation = AutomationInvocation.Method,
            Arguments = arguments,
            ByReference = new bool[arguments.Length],
            Culture = string.Empty,
        });

    private static ExternalValue Wire(object? value) => ExternalValues.ToWire(value, _ => 0);

    [TestMethod]
    public void AnObjectTheServerHandsOverTwice_IsOneHandle()
    {
        var service = new ExternalAutomationService(new FakePlatform());
        var valve = Create(service);

        // the twin of the valve's twin is the valve itself.
        var twin = Create(service);
        _ = service.Invoke(new AutomationInvokeParams
        {
            Target = valve, Member = "Twin", Invocation = AutomationInvocation.Let, Arguments = [new ExternalValue { Kind = ExternalValueKind.Object, Handle = twin }],
            ByReference = [false], Culture = string.Empty,
        });
        var first = service.Invoke(new AutomationInvokeParams { Target = valve, Member = "Twin", Invocation = AutomationInvocation.Get, Culture = string.Empty });
        var second = service.Invoke(new AutomationInvokeParams { Target = valve, Member = "Twin", Invocation = AutomationInvocation.Get, Culture = string.Empty });

        Assert.AreEqual(ExternalValueKind.Object, first.Returned.Kind);
        Assert.AreEqual(twin, first.Returned.Handle);
        Assert.AreEqual(first.Returned.Handle, second.Returned.Handle);
    }

    [TestMethod]
    public void AnArgumentPassedByReference_IsWhatTheServerWroteToIt_AndNoOtherIsSaid()
    {
        var service = new ExternalAutomationService(new FakePlatform());
        var valve = Create(service);

        var filled = service.Invoke(new AutomationInvokeParams
        {
            Target = valve, Member = "Fill", Invocation = AutomationInvocation.Method, Arguments = [Wire(21)], ByReference = [true], Culture = string.Empty,
        });

        Assert.AreEqual("42", filled.Arguments.Single().Text);
    }

    [TestMethod]
    public void WhatTheServerReported_IsTheFailure_WithItsCodeItsMessageAndItsSource()
    {
        var service = new ExternalAutomationService(new FakePlatform());

        var broken = Invoke(service, Create(service), "Break");
        var unknown = service.Create(new AutomationCreateParams { ProgId = "Rig.Nothing" });

        Assert.AreEqual(unchecked((int)0x800A03EC), broken.Failure!.HResult);
        Assert.AreEqual("The valve is broken.", broken.Failure.Message);
        Assert.AreEqual("Rig", broken.Failure.Source);
        Assert.AreEqual(unchecked((int)0x800401F3), unknown.Failure!.HResult);
    }

    [TestMethod]
    public void AnObjectThatWasLetGoOf_IsLetGoOfByTheServer_AndIsNoLongerThere()
    {
        var platform = new FakePlatform();
        var service = new ExternalAutomationService(platform);
        var valve = Create(service);

        _ = service.Release(new AutomationReleaseParams { Handle = valve });
        var after = Invoke(service, valve, "Break");

        Assert.HasCount(1, platform.Released);
        Assert.AreEqual(unchecked((int)0x80010108), after.Failure!.HResult);
    }

    [TestMethod]
    public void AnEventThatACallRaises_IsAnsweredByTheEnvironmentHost_AndTheCallsItsHandlersMake_AreMadeOnTheThreadThatRaisedIt()
    {
        var service = new ExternalAutomationService(new FakePlatform());
        var valve = Create(service);
        _ = service.Advise(new AutomationAdviseParams { Source = valve });

        int? madeOn = null;
        AutomationEventParams? told = null;
        service.RaiseOnClient = raised => Task.Run(() =>
        {
            // a handler that asks the server something, from wherever the environment host runs it.
            told = raised;
            var asked = service.Invoke(new AutomationInvokeParams
            {
                Target = valve, Member = "Thread", Invocation = AutomationInvocation.Get, Culture = string.Empty, HandlingEvent = raised.Event,
            });
            madeOn = int.Parse(asked.Returned.Text!, CultureInfo.InvariantCulture);
            return new AutomationEventResult { Arguments = [raised.Arguments[0], Wire(true)] };
        });

        var tapped = Invoke(service, valve, "Tap", Wire(5));

        Assert.AreEqual("5 cancelled=True", tapped.Returned.Text);
        Assert.AreEqual("Spilled", told!.Name);
        Assert.IsTrue(told.IsSynchronous);
        Assert.AreEqual(Environment.CurrentManagedThreadId, madeOn);
    }

    [TestMethod]
    public void AnEventNobodyListensTo_IsNotRaised_AndTheServerFindsItsArgumentsAsItPassedThem()
    {
        var service = new ExternalAutomationService(new FakePlatform());
        var valve = Create(service);
        _ = service.Advise(new AutomationAdviseParams { Source = valve });
        _ = service.Unadvise(new AutomationUnadviseParams { Source = valve });
        service.RaiseOnClient = _ => throw new AssertFailedException("an event of an object nobody listens to was raised");

        Assert.AreEqual("6 cancelled=False", Invoke(service, valve, "Tap", Wire(6)).Returned.Text);
    }
}
