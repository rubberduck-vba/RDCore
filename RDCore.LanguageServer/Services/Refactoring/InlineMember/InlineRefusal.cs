using RDCore.SDK.Model.Source;

namespace RDCore.LanguageServer.Services.Refactoring.InlineMember;

/// <summary>
/// Why a member, or one call of it, is not inlined.
/// </summary>
internal enum InlineRefusalKind
{
    /// <summary>
    /// The name is not a procedure the workspace declares with a body: a library's member, a variable, a <c>Declare</c> (MS-VBAL §5.2.3.5).
    /// </summary>
    NotAProcedure,

    /// <summary>
    /// The member is a <c>Property Let</c> or <c>Property Set</c>, whose call is an assignment.
    /// </summary>
    PropertyAssignment,

    /// <summary>
    /// The runtime or the host calls the member: an event handler, the implementation of an interface member, a class's lifecycle handler, an entry point
    /// (MS-VBAL §5.3.1.8-10).
    /// </summary>
    CalledByTheRuntime,

    /// <summary>
    /// The member is <c>Static</c>, or declares a <c>Static</c> local, whose value outlives each call (MS-VBAL §5.3.1.2, §5.4.3.1).
    /// </summary>
    Static,

    /// <summary>
    /// The member reaches itself through the procedures it calls.
    /// </summary>
    Recursive,

    /// <summary>
    /// The member's body has a <c>GoSub</c> or a <c>Return</c> (MS-VBAL §5.4.2.14, §5.4.2.15).
    /// </summary>
    GoSub,

    /// <summary>
    /// The member's body has an <c>On Error</c> or a <c>Resume</c> statement, which would apply to the caller once inlined (MS-VBAL §5.4.4).
    /// </summary>
    ErrorHandling,

    /// <summary>
    /// The member's body has a <c>RaiseEvent</c> (MS-VBAL §5.4.2.20), which raises the event of the object it runs for.
    /// </summary>
    RaisesAnEvent,

    /// <summary>
    /// The member's body has a construct the inlining does not write at a call site: a label or a line number, an exit before its last statement.
    /// </summary>
    UnsupportedConstruct,

    /// <summary>
    /// A parameter the inlining does not bind at a call site: an <c>Optional</c> one, or a <c>ParamArray</c>.
    /// </summary>
    UnsupportedParameter,

    /// <summary>
    /// The call is not a call statement of a procedure.
    /// </summary>
    UnsupportedSite,

    /// <summary>
    /// The call's arguments do not map to the member's parameters (MS-VBAL §5.3.1.11): the code does not compile.
    /// </summary>
    ArgumentsDoNotMap,

    /// <summary>
    /// A <c>ByRef</c> argument is an array element, a field of a <c>Type</c>, or another expression that is fixed when the call is made, which no variable
    /// at the call site stands for (MS-VBAL §5.3.1.11).
    /// </summary>
    ByRefElement,

    /// <summary>
    /// The call is in another module than the member: the names of its body would have to be qualified there.
    /// </summary>
    OtherModule,

    /// <summary>
    /// The edited module, checked before the edit is offered, is not what the call did: it does not parse, a name binds to something else, it has a new
    /// compile error.
    /// </summary>
    NotVerified,
}

/// <summary>
/// Why a member, or one call of it, is not inlined, and where.
/// </summary>
/// <param name="Kind">Why.</param>
/// <param name="Detail">What it is about: the statement, the parameter, the name; <see langword="null"/> when there is nothing to name.</param>
/// <param name="Location">Where, in the module of the member or of the call; <see langword="null"/> for the member as a whole.</param>
internal sealed record class InlineRefusal(InlineRefusalKind Kind, string? Detail = null, SourceLocation? Location = null);
