using RDCore.External.Forms;

namespace RDCore.External.Protocol;

/// <summary>Request for <see cref="ExternalProtocol.FormCreate"/>: <see cref="IFormHost.Create"/>.</summary>
public sealed record class FormCreateParams : AutomationRequest
{
    /// <summary>The name of the form, as its module is named.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The designer's state of the form, as Microsoft Forms loads it; empty for a form that has none.</summary>
    public byte[] State { get; init; } = [];

    /// <summary>The form's caption, as its designer block says; <see langword="null"/> leaves the form's own.</summary>
    public string? Caption { get; init; }

    /// <summary>Where the form starts up.</summary>
    public FormStartUpPosition StartUpPosition { get; init; } = FormStartUpPosition.CenterOwner;

    /// <summary>
    /// <see cref="FormDefinition.ClientLeft"/>.
    /// </summary>
    public double? ClientLeft { get; init; }

    /// <summary>
    /// <see cref="FormDefinition.ClientTop"/>.
    /// </summary>
    public double? ClientTop { get; init; }

    /// <summary>
    /// <see cref="FormDefinition.ClientWidth"/>.
    /// </summary>
    public double? ClientWidth { get; init; }

    /// <summary>
    /// <see cref="FormDefinition.ClientHeight"/>.
    /// </summary>
    public double? ClientHeight { get; init; }

    /// <summary>
    /// The designer that wrote the form.
    /// </summary>
    public SDK.Workspace.Forms.FormDesignerKind Kind { get; init; } = SDK.Workspace.Forms.FormDesignerKind.MSForms;

    /// <summary>
    /// The text of the form's designer block, for a form whose controls it declares; <see langword="null"/> when there is none.
    /// </summary>
    public string? DesignerText { get; init; }

    /// <summary>The request for <paramref name="form"/>.</summary>
    /// <param name="form">The form.</param>
    /// <param name="handlingEvent">The event whose handler asks for it, if one does.</param>
    public static FormCreateParams Of(FormDefinition form, long handlingEvent) => new()
    {
        Name = form.Name,
        State = form.State.ToArray(),
        Caption = form.Caption,
        StartUpPosition = form.StartUpPosition,
        ClientLeft = form.ClientLeft,
        ClientTop = form.ClientTop,
        ClientWidth = form.ClientWidth,
        ClientHeight = form.ClientHeight,
        Kind = form.Kind,
        DesignerText = form.DesignerText,
        HandlingEvent = handlingEvent,
    };

    /// <summary>The form this request is for.</summary>
    public FormDefinition ToDefinition() => new(Name, State)
    {
        Caption = Caption,
        StartUpPosition = StartUpPosition,
        ClientLeft = ClientLeft,
        ClientTop = ClientTop,
        ClientWidth = ClientWidth,
        ClientHeight = ClientHeight,
        Kind = Kind,
        DesignerText = DesignerText,
    };
}

/// <summary>Request for <see cref="ExternalProtocol.FormShow"/>: <see cref="IFormHost.Show"/>.</summary>
public sealed record class FormShowParams : AutomationRequest
{
    /// <summary>The handle of the form.</summary>
    public long Target { get; init; }

    /// <summary>Whether it is shown modal, and answered once it is hidden.</summary>
    public bool Modal { get; init; }

    /// <summary>
    /// The handle of the form that owns it, or <see langword="null"/> when it has none.
    /// </summary>
    public long? Owner { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.FormHold"/>.</summary>
public sealed record class FormHoldParams : AutomationRequest
{
    /// <summary>Whether a program runs.</summary>
    public bool Running { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.FormDoEvents"/>.</summary>
public sealed record class FormDoEventsParams : AutomationRequest
{
}

/// <summary>Request for <see cref="ExternalProtocol.FormHide"/> and <see cref="ExternalProtocol.FormIsShown"/>.</summary>
public sealed record class FormParams : AutomationRequest
{
    /// <summary>The handle of the form.</summary>
    public long Target { get; init; }
}

/// <summary>Response for <see cref="ExternalProtocol.FormIsShown"/>.</summary>
public sealed record class FormIsShownResult : ExternalResult
{
    /// <summary>Whether the form is shown.</summary>
    public bool IsShown { get; init; }
}

/// <summary>Request for <see cref="ExternalProtocol.FormCaption"/>: <see cref="IFormHost.GetCaption"/>, or <see cref="IFormHost.SetCaption"/> when it gives one.</summary>
public sealed record class FormCaptionParams : AutomationRequest
{
    /// <summary>The handle of the form.</summary>
    public long Target { get; init; }

    /// <summary>The caption to set; <see langword="null"/> asks what it is.</summary>
    public string? Caption { get; init; }
}

/// <summary>Response for <see cref="ExternalProtocol.FormCaption"/>.</summary>
public sealed record class FormCaptionResult : ExternalResult
{
    /// <summary>The form's caption, once the request is made.</summary>
    public string Caption { get; init; } = string.Empty;
}

/// <summary>
/// Request for <see cref="ExternalProtocol.FormBounds"/>: <see cref="IFormHost.GetBounds"/>, or <see cref="IFormHost.SetBounds"/> when it gives them.
/// </summary>
public sealed record class FormBoundsParams : AutomationRequest
{
    /// <summary>
    /// The handle of the form.
    /// </summary>
    public long Target { get; init; }

    /// <summary>
    /// Where the window goes and its size, in points; <see langword="null"/> asks where it is.
    /// </summary>
    public FormBounds? Bounds { get; init; }
}

/// <summary>
/// Response for <see cref="ExternalProtocol.FormBounds"/>.
/// </summary>
public sealed record class FormBoundsResult : ExternalResult
{
    /// <summary>
    /// Where the form's window is and its size, in points, once the request is made.
    /// </summary>
    public FormBounds Bounds { get; init; }
}
