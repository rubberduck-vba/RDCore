using RDCore.SDK.Model.Source;
using System.Globalization;
using System.Resources;

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
    /// The member's body has a <c>RaiseEvent</c>, and the call is in another module: only the class module that declares an event raises it
    /// (MS-VBAL §5.4.2.20).
    /// </summary>
    RaisesAnEvent,

    /// <summary>
    /// A local or the result of the member has a value the inlining cannot set at a call site: a type it writes no default value of, or a <c>Type</c> with a
    /// field that may hold an object, which no statement lets go of.
    /// </summary>
    UnsupportedConstruct,

    /// <summary>
    /// A parameter the inlining does not bind at a call site: an array, a <c>Type</c> or a type that does not resolve; a <c>Variant</c> given an argument
    /// that may hold an object; an omitted <c>Optional</c> <c>Variant</c> read other than by <c>IsMissing</c>; a parameter the body writes and tests with
    /// <c>IsMissing</c>; a <c>ParamArray</c> the body writes, or given an element it cannot copy.
    /// </summary>
    UnsupportedParameter,

    /// <summary>
    /// The call is where the inlining does not write the body: on a line with another statement, in a loop's condition, an <c>ElseIf</c> or a <c>Case</c>
    /// clause, a function's result assigned to something other than a variable, a result that may hold an object read in a larger statement.
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
    /// The call is in another project than the member, which sees only what the member's project exposes, under the name it references it by.
    /// </summary>
    OtherProject,

    /// <summary>
    /// A name of the body refers to a <c>Private</c> declaration of the member's module, and the call is in another module, which does not see it
    /// (MS-VBAL §5.3.1.1).
    /// </summary>
    PrivateToItsModule,

    /// <summary>
    /// The call is in another module than the member, whose <c>Option Compare</c> differs: the body's string comparisons would compare otherwise
    /// (MS-VBAL §5.2.1.1).
    /// </summary>
    OptionCompare,

    /// <summary>
    /// The call is in another module than the member, whose <c>Option Base</c> differs, and the body declares an array without a lower bound or calls
    /// <c>Array</c>: its lower bound would differ (MS-VBAL §5.2.1.2).
    /// </summary>
    OptionBase,

    /// <summary>
    /// The call is in another module than the member, whose <c>#Const</c> directives differ, and the body has an <c>#If</c> block: another of its branches
    /// could be the one that runs (MS-VBAL §3.4.1).
    /// </summary>
    ConditionalConstants,

    /// <summary>
    /// A local of the member may hold an object when an error leaves the body. The callee's activation let go of it before the caller's handler ran, and so
    /// ran its <c>Class_Terminate</c> then; a local of the caller is let go of later.
    /// </summary>
    HeldThroughAnError,

    /// <summary>
    /// The call is under the caller's <c>On Error Resume Next</c>, and the body can raise an error. An error skipped the rest of the body, which inlined runs
    /// after the statement that raised (MS-VBAL §5.4.4).
    /// </summary>
    CallerResumesNext,

    /// <summary>
    /// A name of the body refers to something of the module or the project, and a parameter, a local or a declaration of the caller's module has that name,
    /// which no qualification gets past: written into the caller, it would refer to the caller's (MS-VBAL §5.6.10).
    /// </summary>
    Captured,

    /// <summary>
    /// Bound again in the edited module, a name the body or the call writes refers to a declaration other than the one it refers to in the member or at
    /// the call, or to one where it referred to none (MS-VBAL §5.6.10).
    /// </summary>
    Rebound,

    /// <summary>
    /// The call is in a larger statement, which evaluates something else that has an effect or reads what the body can write. The body written before the
    /// statement would run before it, where MS-VBAL leaves the order of an operator's operands open.
    /// </summary>
    EvaluationOrder,

    /// <summary>
    /// An error the body can raise is caught by a handler of the caller that resumes. Its <c>Resume</c> ran the call again, and inlined runs the body's
    /// statement that raised; its <c>Resume Next</c> went on after the call, and inlined goes on inside the body (MS-VBAL §5.4.4).
    /// </summary>
    CallerHandlerResumes,

    /// <summary>
    /// Several locals of the member may hold an object when the body ends, and the order MS-VBA lets go of a procedure's locals in, which decides the order
    /// their <c>Class_Terminate</c> run in, is not measured.
    /// </summary>
    ReleaseOrder,

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
internal sealed record class InlineRefusal(InlineRefusalKind Kind, string? Detail = null, SourceLocation? Location = null)
{
    private static readonly ResourceManager Messages =
        new("RDCore.LanguageServer.Services.Refactoring.InlineMember.InlineMemberMessages", typeof(InlineRefusal).Assembly);

    /// <summary>
    /// What the refusal says, in the language of the reader.
    /// </summary>
    /// <param name="locate">How a position is named to the reader; by default, the name of the module and the line and the column, from 1.</param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public string ToMessage(Func<Uri, SourcePosition, string>? locate = null, CultureInfo? culture = null)
        => Format($"InlineMember_{Kind}", culture, Detail,
            Location is { } location ? $"({(locate ?? DefaultLocation)(location.Uri, location.Range.Start)})" : string.Empty);

    /// <summary>
    /// A text of Inline Member's, in the language of the reader, with its arguments.
    /// </summary>
    /// <param name="key">The text's name.</param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    /// <param name="arguments">What the text names.</param>
    public static string Format(string key, CultureInfo? culture, params object?[] arguments)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString(key, culture) ?? key, arguments)
            .Replace("  ", " ", StringComparison.Ordinal).Replace(" .", ".", StringComparison.Ordinal);
    }

    private static string DefaultLocation(Uri module, SourcePosition position)
        => $"{module.Fragment.TrimStart('#')} {position.Line + 1}:{position.Character + 1}";
}
