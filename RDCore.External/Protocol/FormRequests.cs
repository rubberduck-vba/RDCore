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

    /// <summary>Where a manual form's window is, from the left of the desktop, in points.</summary>
    public double Left { get; init; }

    /// <summary>Where a manual form's window is, from the top of the desktop, in points.</summary>
    public double Top { get; init; }

    /// <summary>The request for <paramref name="form"/>.</summary>
    /// <param name="form">The form.</param>
    /// <param name="handlingEvent">The event whose handler asks for it, if one does.</param>
    public static FormCreateParams Of(FormDefinition form, long handlingEvent) => new()
    {
        Name = form.Name,
        State = form.State.ToArray(),
        Caption = form.Caption,
        StartUpPosition = form.StartUpPosition,
        Left = form.Left,
        Top = form.Top,
        HandlingEvent = handlingEvent,
    };

    /// <summary>The form this request is for.</summary>
    public FormDefinition ToDefinition() => new(Name, State) { Caption = Caption, StartUpPosition = StartUpPosition, Left = Left, Top = Top };
}

/// <summary>Request for <see cref="ExternalProtocol.FormShow"/>: <see cref="IFormHost.Show"/>.</summary>
public sealed record class FormShowParams : AutomationRequest
{
    /// <summary>The handle of the form.</summary>
    public long Target { get; init; }

    /// <summary>Whether it is shown modal, and answered once it is hidden.</summary>
    public bool Modal { get; init; }
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
