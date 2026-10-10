using RDCore.External.Protocol;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.External.Hosting;

// Every handler makes its call on a thread of its own: a call can wait for an event to be handled, and the handlers of the event make requests that have to be
// dispatched meanwhile - which OmniSharp does not do while a handler holds the thread it dispatches requests on, however they are scheduled.

/// <summary>Handles <see cref="ExternalProtocol.AutomationStatus"/>.</summary>
internal sealed class AutomationStatusHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationStatusParams, AutomationStatusResult>
{
    protected override Task<AutomationStatusResult> HandleAsync(AutomationStatusParams request, CancellationToken token) => Task.Run(() => service.Status(request));
}

/// <summary>Handles <see cref="ExternalProtocol.AutomationCreate"/>.</summary>
internal sealed class AutomationCreateHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationCreateParams, AutomationObjectResult>
{
    protected override Task<AutomationObjectResult> HandleAsync(AutomationCreateParams request, CancellationToken token) => Task.Run(() => service.Create(request));
}

/// <summary>Handles <see cref="ExternalProtocol.AutomationInvoke"/>.</summary>
internal sealed class AutomationInvokeHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationInvokeParams, AutomationInvokeResult>
{
    protected override Task<AutomationInvokeResult> HandleAsync(AutomationInvokeParams request, CancellationToken token) => Task.Run(() => service.Invoke(request));
}

/// <summary>Handles <see cref="ExternalProtocol.AutomationClassName"/>.</summary>
internal sealed class AutomationClassNameHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationClassNameParams, AutomationClassNameResult>
{
    protected override Task<AutomationClassNameResult> HandleAsync(AutomationClassNameParams request, CancellationToken token) => Task.Run(() => service.ClassName(request));
}

/// <summary>Handles <see cref="ExternalProtocol.AutomationMoveNext"/>.</summary>
internal sealed class AutomationMoveNextHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationMoveNextParams, AutomationMoveNextResult>
{
    protected override Task<AutomationMoveNextResult> HandleAsync(AutomationMoveNextParams request, CancellationToken token) => Task.Run(() => service.MoveNext(request));
}

/// <summary>Handles <see cref="ExternalProtocol.AutomationReset"/>.</summary>
internal sealed class AutomationResetHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationResetParams, ExternalDoneResult>
{
    protected override Task<ExternalDoneResult> HandleAsync(AutomationResetParams request, CancellationToken token) => Task.Run(() => service.Reset(request));
}

/// <summary>Handles <see cref="ExternalProtocol.AutomationAdvise"/>.</summary>
internal sealed class AutomationAdviseHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationAdviseParams, ExternalDoneResult>
{
    protected override Task<ExternalDoneResult> HandleAsync(AutomationAdviseParams request, CancellationToken token) => Task.Run(() => service.Advise(request));
}

/// <summary>Handles <see cref="ExternalProtocol.AutomationUnadvise"/>.</summary>
internal sealed class AutomationUnadviseHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationUnadviseParams, ExternalDoneResult>
{
    protected override Task<ExternalDoneResult> HandleAsync(AutomationUnadviseParams request, CancellationToken token) => Task.Run(() => service.Unadvise(request));
}

/// <summary>Handles <see cref="ExternalProtocol.AutomationRelease"/>.</summary>
internal sealed class AutomationReleaseHandler(ExternalAutomationService service) : RDCoreRequestHandler<AutomationReleaseParams, ExternalDoneResult>
{
    protected override Task<ExternalDoneResult> HandleAsync(AutomationReleaseParams request, CancellationToken token) => Task.Run(() => service.Release(request));
}
