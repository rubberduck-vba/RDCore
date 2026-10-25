namespace RDCore.SDK.Platform.Commands;

/// <summary>
/// What an argument of a verb is, which decides what can be written for it.
/// </summary>
public enum VerbArgumentKind
{
    /// <summary>One of the words of <see cref="VerbArgument.Values"/>, which are the verb's own: <c>BREAK CLEAR</c>.</summary>
    Keyword,

    /// <summary>One of <see cref="VerbArgument.Values"/>, which are names of things the verb acts on: <c>THEME dark</c>.</summary>
    Choice,

    /// <summary>The number of a line of the program: <c>BREAK 40</c>.</summary>
    LineNumber,

    /// <summary>An expression of the program's language, to the end of the line: the condition of <c>BREAK 40 I &gt; 5</c>.</summary>
    Expression,

    /// <summary>The path of a file, to the end of the line: <c>LOAD c:\dev\fizzbuzz</c>.</summary>
    Path,

    /// <summary>Any text, to the end of the line.</summary>
    Text,
}

/// <summary>
/// A value an argument of a verb can be (<see cref="VerbArgumentKind.Keyword"/>, <see cref="VerbArgumentKind.Choice"/>).
/// </summary>
public sealed record class VerbArgumentValue
{
    /// <summary>The value, as it is written.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>What the value does, in a sentence; <see langword="null"/> when its name says it.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// One argument of a verb: what it is named, and what it is.
/// </summary>
public sealed record class VerbArgument
{
    /// <summary>The name of the argument, as the verb's help names it: <c>line</c>, <c>condition</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the argument is.</summary>
    public VerbArgumentKind Kind { get; init; }

    /// <summary>The values the argument can be, for a <see cref="VerbArgumentKind.Keyword"/> or a <see cref="VerbArgumentKind.Choice"/>.</summary>
    public IReadOnlyList<VerbArgumentValue> Values { get; init; } = [];

    /// <summary>
    /// Whether the argument takes the rest of the line: an expression, a path, and text do - a space is in them, and does not end them.
    /// </summary>
    public bool TakesTheRest => Kind is VerbArgumentKind.Expression or VerbArgumentKind.Path or VerbArgumentKind.Text;
}

/// <summary>
/// A verb of a platform application, and the forms its arguments take: what a client or a server needs to know of it to complete it as it is typed, and to
/// describe it - without running it.
/// </summary>
/// <remarks>
/// <para>
/// A verb has one form or several: <c>BREAK</c> alone, <c>BREAK CLEAR</c>, <c>BREAK TOGGLE &lt;line&gt;</c>, <c>BREAK &lt;line&gt; [condition]</c>. A form is its
/// arguments in the order they are written; a form whose arguments are fewer than what is typed is not the one that is being written, and an argument that takes the
/// rest of the line is the last of its form.
/// </para>
/// <para>
/// The descriptor is data: it travels where the verb does not - with a completion request (<see cref="Protocol.SessionCompletionParams.Command"/>) - and is all
/// that is needed to complete its arguments (<see cref="VerbCompletion"/>).
/// </para>
/// </remarks>
public sealed record class VerbDescriptor
{
    /// <summary>The verb, as it is written.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The other spellings of the verb.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>What the verb does, in a sentence.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>The forms the arguments of the verb take, each its arguments in the order they are written; none for a verb that takes no arguments.</summary>
    public IReadOnlyList<IReadOnlyList<VerbArgument>> Forms { get; init; } = [];
}
