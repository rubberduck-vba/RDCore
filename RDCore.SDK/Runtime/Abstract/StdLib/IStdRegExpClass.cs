using RDCore.SDK.Model;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>RD-VBAL §6.2 VBScript Regular Expressions</strong>: the <c>RegExp</c> class of <em>Microsoft VBScript Regular Expressions 5.5</em>, a regular
/// expression and the options it is matched with.
/// </summary>
/// <remarks>
/// <para>
/// MS-VBAL has no section for the library. RD-VBA implements it itself, as a class of the standard library: <c>New RegExp</c> needs no reference, and
/// <c>CreateObject("VBScript.RegExp")</c> creates one on any platform, before the machine's servers are asked (<see cref="StdLibClassAttribute.ProgId"/>).
/// </para>
/// <para>
/// A pattern is the syntax of VBScript 5.5 (JScript's, which ECMAScript 3 describes), as MS-VBA matches it: <c>\d</c>, <c>\w</c>, <c>\s</c> and <c>\b</c>
/// are ASCII; <c>.</c> is any character but a line feed; a pattern is checked when it is first matched, not when it is assigned, and a pattern that is not
/// one is a run-time error of its own (<c>5017</c> to <c>5021</c>). <strong>RD-VBAL §6.2</strong> says what each construct is.
/// </para>
/// </remarks>
[StdLibClass(ProgId = "VBScript.RegExp")]
public interface IStdRegExpClass
{
    #region Public Properties
    /// <summary>
    /// Gets the regular expression: empty for a new object, which matches anywhere.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBStringValue> Pattern();

    /// <summary>
    /// Sets the regular expression. Any text is one: what is wrong with it is reported when it is matched.
    /// </summary>
    /// <param name="value">The regular expression.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult Pattern(VBStringValue value);

    /// <summary>
    /// Gets whether every match of the pattern is found and replaced (<c>True</c>), or only the first (<c>False</c>, for a new object).
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBBooleanValue> Global();

    /// <summary>
    /// Sets whether every match of the pattern is found and replaced, or only the first.
    /// </summary>
    /// <param name="value">Whether every match is.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult Global(VBBooleanValue value);

    /// <summary>
    /// Gets whether a letter matches either of its cases (<c>True</c>), or only the one it is written in (<c>False</c>, for a new object).
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IgnoreCase();

    /// <summary>
    /// Sets whether a letter matches either of its cases.
    /// </summary>
    /// <param name="value">Whether it does.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult IgnoreCase(VBBooleanValue value);

    /// <summary>
    /// Gets whether <c>^</c> and <c>$</c> match at the start and the end of each line (<c>True</c>), or only of the whole text (<c>False</c>, for a new
    /// object). A line ends at a carriage return or a line feed, each of them: a <c>vbCrLf</c> has an empty line between its two characters.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBBooleanValue> Multiline();

    /// <summary>
    /// Sets whether <c>^</c> and <c>$</c> match at the start and the end of each line, or only of the whole text.
    /// </summary>
    /// <param name="value">Whether they match at each line.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult Multiline(VBBooleanValue value);
    #endregion

    #region Public Methods
    /// <summary>
    /// Matches the pattern against a text: the first match, or every match when <see cref="Global()"/> is set.
    /// </summary>
    /// <remarks>
    /// The matches are found when this is called: a change of the pattern or of the options afterwards is not theirs. An empty match is a match, at a
    /// position of its own (<c>x*</c> matches <c>"axb"</c> four times).
    /// </remarks>
    /// <param name="sourceString">The text.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is a <c>MatchCollection</c> (<see cref="IStdMatchCollectionClass"/>), empty when nothing matches.</returns>
    [StdLibMember(ReturnType = typeof(IStdMatchCollectionClass))]
    RuntimeSemanticsEvaluationResult<VBObjectValue> Execute(VBStringValue sourceString);

    /// <summary>
    /// Replaces the first match of the pattern in a text, or every match when <see cref="Global()"/> is set.
    /// </summary>
    /// <remarks>
    /// In the replacement, <c>$1</c> to <c>$99</c> are the text of a group (<c>$10</c> is group 10 when the pattern has ten groups, and <c>$1</c> followed
    /// by <c>0</c> otherwise), <c>$&amp;</c> the match, <c>$`</c> and <c>$'</c> the text before and after it, and <c>$$</c> a dollar sign. Anything else -
    /// a group the pattern does not have, a <c>$</c> that starts nothing - is written as it is.
    /// </remarks>
    /// <param name="sourceString">The text.</param>
    /// <param name="replaceVar">
    /// What a match is replaced with: text, or a value written as text - a <c>Boolean</c> as <c>-1</c> or <c>0</c>, <c>Empty</c> as nothing.<br/>
    /// 💥<see cref="Model.Errors.VBRuntimeErrorId.TypeMismatch"/> for <c>Null</c>.
    /// </param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> Replace(VBStringValue sourceString, VBVariantValue replaceVar);

    /// <summary>
    /// Whether the pattern matches anywhere in a text.
    /// </summary>
    /// <param name="sourceString">The text.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> Test(VBStringValue sourceString);
    #endregion
}

/// <summary>
/// <strong>RD-VBAL §6.2 VBScript Regular Expressions</strong>: the <c>MatchCollection</c> class, the matches <c>RegExp.Execute</c> found.
/// </summary>
/// <remarks>
/// Its members are numbered from <c>0</c>. <see cref="Item"/> is the default member of the class, so <c>m(0)</c> is <c>m.Item(0)</c>, and a <c>For Each</c>
/// over it enumerates the matches in the order they are in the text. Only <c>RegExp.Execute</c> makes one.
/// </remarks>
[StdLibClass(IsCreatable = false)]
public interface IStdMatchCollectionClass
{
    /// <summary>
    /// Gets how many matches there are.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBLongValue> Count();

    /// <summary>
    /// Gets a match by its position.
    /// </summary>
    /// <param name="index">From <c>0</c> to <c>Count - 1</c>.<br/>
    /// 💥<see cref="Model.Errors.VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> for any other.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is a <c>Match</c> (<see cref="IStdMatchClass"/>).</returns>
    /// <remarks>
    /// It is declared <c>Object</c>, as the library's own type library declares it (<c>IDispatch</c>): <c>m(0).SubMatches(Null)</c> is a late-bound call
    /// whatever <c>m</c> is declared as, a type mismatch where an early-bound one is error 94 (MS-VBA, recorded in Excel).
    /// </remarks>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet, UserMemId = WellKnownDispIds.Value)]
    RuntimeSemanticsEvaluationResult<VBObjectValue> Item(VBLongValue index);

    /// <summary>
    /// Returns the object that enumerates the matches: what a <c>For Each</c> over them drives.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is an object of the <c>IEnumVARIANT</c> class (<see cref="IStdEnumVariantClass"/>).</returns>
    [StdLibMember("_NewEnum", ReturnType = typeof(IStdEnumVariantClass), IsHidden = true, UserMemId = WellKnownDispIds.NewEnum)]
    RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum();
}

/// <summary>
/// <strong>RD-VBAL §6.2 VBScript Regular Expressions</strong>: the <c>Match</c> class, one match of a pattern in a text.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is the default member of the class, so a match is the text it matched where a value is wanted. Its properties are read-only: an
/// assignment to one is error <c>450</c>. Only a <c>MatchCollection</c> makes one.
/// </remarks>
[StdLibClass(IsCreatable = false)]
public interface IStdMatchClass
{
    /// <summary>
    /// Gets the text that was matched.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet, UserMemId = WellKnownDispIds.Value)]
    RuntimeSemanticsEvaluationResult<VBStringValue> Value();

    /// <summary>
    /// Gets where the match starts in the text, counted from <c>0</c>.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBLongValue> FirstIndex();

    /// <summary>
    /// Gets how many characters the match is.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBLongValue> Length();

    /// <summary>
    /// Gets what each group of the pattern matched.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is a <c>SubMatches</c> (<see cref="IStdSubMatchesClass"/>).</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet, ReturnType = typeof(IStdSubMatchesClass))]
    RuntimeSemanticsEvaluationResult<VBObjectValue> SubMatches();
}

/// <summary>
/// <strong>RD-VBAL §6.2 VBScript Regular Expressions</strong>: the <c>SubMatches</c> class, what each group of a pattern matched in one match.
/// </summary>
/// <remarks>
/// There is one member for each group of the pattern, in the order of their opening parentheses, numbered from <c>0</c>: the text the group matched, which
/// can be empty, or <c>Empty</c> for a group that took no part in the match (<c>(a)|(b)</c> matching <c>"b"</c>). A group inside a repetition is what it
/// matched last. <see cref="Item"/> is the default member of the class. Only a <c>Match</c> makes one.
/// </remarks>
[StdLibClass(IsCreatable = false)]
public interface IStdSubMatchesClass
{
    /// <summary>
    /// Gets how many groups the pattern has.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBLongValue> Count();

    /// <summary>
    /// Gets what a group matched, by its position.
    /// </summary>
    /// <param name="index">From <c>0</c> to <c>Count - 1</c>.<br/>
    /// 💥<see cref="Model.Errors.VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> for any other.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is a <c>String</c>, or <c>Empty</c> for a group that took no part in the match.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet, UserMemId = WellKnownDispIds.Value)]
    RuntimeSemanticsEvaluationResult<VBVariantValue> Item(VBLongValue index);

    /// <summary>
    /// Returns the object that enumerates what the groups matched: what a <c>For Each</c> over them drives.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is an object of the <c>IEnumVARIANT</c> class (<see cref="IStdEnumVariantClass"/>).</returns>
    [StdLibMember("_NewEnum", ReturnType = typeof(IStdEnumVariantClass), IsHidden = true, UserMemId = WellKnownDispIds.NewEnum)]
    RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum();
}
