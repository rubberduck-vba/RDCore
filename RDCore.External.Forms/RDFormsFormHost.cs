using RDCore.External.Automation;
using RDCore.Forms.Import.VB6;
using RDCore.Forms.Model;
using RDCore.Forms.Xaml;
using RDCore.SDK.Workspace.Forms;
using Model = RDCore.Forms.Model;

namespace RDCore.External.Forms.RDForms;

/// <summary>
/// Hosts RDForms forms and Visual Basic 6.0 forms in RDForms windows: each form's model is read from its RDForms document, or from its designer's text and its
/// <c>.frx</c>, and is an object of an <see cref="RDFormsAutomationServer"/>.
/// </summary>
/// <remarks>
/// The window raises the events of the form and of its controls through the server's sinks. A user's close does not close it: the form's sink is asked to
/// (<see cref="FormWindowEvents.Close"/>), and the program unloads the form, or does not.
/// </remarks>
/// <param name="server">The server the forms are objects of.</param>
public sealed class RDFormsFormHost(RDFormsAutomationServer server) : IFormHost
{
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int Failed = unchecked((int)0x80004005);
    private const int MemberNotFound = unchecked((int)0x80020003);

    // whether this host's program runs, which holds the windows' messages.
    private readonly object _holdGate = new();
    private bool _holding;

    // the program's App.KeepSystemTheme, which each window is given as it is made. Written on the UI thread once it is started.
    private volatile bool _keepSystemTheme;

    /// <summary>
    /// The server the forms are objects of.
    /// </summary>
    public RDFormsAutomationServer Server { get; } = server;

    /// <summary>
    /// Whether a form is one of this host's.
    /// </summary>
    /// <param name="form">The handle of the form.</param>
    public bool Owns(object form) => Server.Owns(form);

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public bool HostsVisualBasic6Forms => true;

    // before the UI thread is started there is no window to give it to, and it is not started for this: the first form's window is given it as it is made.
    // Once it is, the value and every window's change together on that thread, so that a form made meanwhile is never given the one before.
    /// <inheritdoc/>
    public bool KeepSystemTheme
    {
        get => _keepSystemTheme;
        set
        {
            if (!RDFormsUI.IsStarted)
            {
                _keepSystemTheme = value;
                return;
            }

            RDFormsUI.Run(() =>
            {
                _keepSystemTheme = value;
                foreach (var hosted in Server.Forms)
                {
                    hosted.Window.KeepSystemTheme = value;
                }
            });
        }
    }

    /// <inheritdoc/>
    public object Create(FormDefinition form)
    {
        if (form.Kind is not (FormDesignerKind.VisualBasic6 or FormDesignerKind.RDForms))
        {
            throw new AutomationException(ClassNotRegistered, $"{form.Name}: RDForms hosts RDForms forms and Visual Basic 6.0 forms only.");
        }

        var root = RootOf(form);
        if (form.Unattended)
        {
            RDFormsUI.StartUnattended();
        }
        else
        {
            RDFormsUI.Start(Server.Platform);
        }

        return RDFormsUI.Run(() =>
        {
            // a VB6 form's code reads a control's position and size in its container's scale (twips in a Frame), and the form's own in twips; an RDForms
            // form's code reads them in pixels, as its document holds them.
            var visualBasic = form.Kind == FormDesignerKind.VisualBasic6;
            var hosted = new RDFormsForm(new Model.FormDefinition(root), visualBasic ? CodeUnit.VB6 : CodeUnit.Pixel, Server.Program)
            {
                IsVisualBasic = visualBasic,
            };
            // in the program's theme before anything shows it.
            hosted.Window.KeepSystemTheme = _keepSystemTheme;
            Server.Forms.Add(hosted);
            return hosted.Handle;
        });
    }

    // the form's model: the one its designer's text declares (an RDForms form's document, or a VB6 form's designer block, read in place), or, with no
    // text, an empty form with the caption its designer gives it.
    private static ComponentInstance RootOf(FormDefinition form)
    {
        if (form.DesignerText is not { } text)
        {
            return new ComponentInstance(ComponentClasses.Form, form.Name).Set(Properties.Caption, form.Caption ?? form.Name);
        }

        Model.FormDefinition? model;
        if (form.Kind == FormDesignerKind.RDForms)
        {
            var read = FormXaml.Read(text);
            model = read.Form ?? throw new AutomationException(Failed,
                $"{form.Name}: its document cannot be read. {string.Join(" ", read.Diagnostics.Where(d => d.Severity == FormXamlSeverity.Error))}");
        }
        else
        {
            var import = VB6FormReader.Read(text, form.State.IsEmpty ? null : form.State.ToArray());
            model = import.Form ?? throw new AutomationException(Failed,
                $"{form.Name}: its designer cannot be read. {string.Join(" ", import.Diagnostics.Where(d => d.Severity == VB6ImportSeverity.Error))}");
        }

        if (!ReferenceEquals(model.Root.Class, ComponentClasses.Form))
        {
            throw new AutomationException(ClassNotRegistered, $"{form.Name}: RDForms cannot host a {model.Root.Class.TypeName} yet.");
        }

        return model.Root;
    }

    /// <inheritdoc/>
    public void Show(object form, bool modal, object? owner = null)
    {
        var hosted = FormOf(form);
        var owning = owner is null ? null : FormOf(owner);
        RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            if (modal)
            {
                hosted.ShowModal(Server.Forms.Where(other => !ReferenceEquals(other, hosted)), owning);
            }
            else
            {
                hosted.Show(owning);
            }
        });
    }

    /// <inheritdoc/>
    public void Hide(object form)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            hosted.Hide();
        });
    }

    /// <inheritdoc/>
    public bool IsShown(object form)
    {
        var hosted = FormOf(form);
        return RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            return hosted.Window.IsVisible;
        });
    }

    /// <inheritdoc/>
    public string GetCaption(object form)
    {
        var hosted = FormOf(form);
        return RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            return hosted.Model.Root.Get(Properties.Caption);
        });
    }

    /// <inheritdoc/>
    public void SetCaption(object form, string caption)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            hosted.Model.Root.Set(Properties.Caption, caption);
        });
    }

    // a Visual Basic 6.0 form's position and size are members of its own object model, which its server serves: only a UserForm's are its form host's,
    // and RDForms hosts no UserForm.
    /// <inheritdoc/>
    public FormBounds GetBounds(object form)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() => RDFormsAutomationServer.Alive(hosted.Handle));
        throw new AutomationException(MemberNotFound, "RDForms hosts Visual Basic 6.0 forms only: a form's Left, Top, Width and Height are its own members.");
    }

    /// <inheritdoc/>
    public void SetBounds(object form, FormBounds bounds)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() => RDFormsAutomationServer.Alive(hosted.Handle));
        throw new AutomationException(MemberNotFound, "RDForms hosts Visual Basic 6.0 forms only: a form's Left, Top, Width and Height are its own members.");
    }

    /// <inheritdoc/>
    public void Leave(object form)
    {
        var hosted = FormOf(form);
        RDFormsUI.Run(() =>
        {
            RDFormsAutomationServer.Alive(hosted.Handle);
            hosted.Hide();
        });
    }

    /// <inheritdoc/>
    public void Destroy(object form)
    {
        if (form is RDFormsObject { IsForm: true } handle)
        {
            Server.Destroy(handle.Form);
        }
    }

    /// <inheritdoc/>
    public void Hold(bool running)
    {
        lock (_holdGate)
        {
            if (running != _holding)
            {
                _holding = running;
                RDFormsUI.Hold(running);
            }
        }
    }

    /// <inheritdoc/>
    public void DoEvents()
    {
        if (Server.Forms.Count > 0)
        {
            RDFormsUI.Run(RDFormsUI.Pump);
        }
    }

    private static RDFormsForm FormOf(object form)
        => form is RDFormsObject { IsForm: true } handle ? handle.Form : throw new AutomationException(unchecked((int)0x80020005), "The object is not a form of RDForms.");
}
