using System.Runtime.CompilerServices;
using System.Text;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdRegExpClass"/>
/// <remarks>
/// The pattern and the options are state of the object, kept with its own instance. The pattern is translated when it is first matched with the options as they
/// are then (<see cref="VBScriptPattern"/>), and again only once one of them has changed. What is wrong with a pattern is the error of the member that matches
/// it, numbered as MS-VBA numbers it (<c>5017</c> to <c>5021</c>): MS-VBA has no description of its own for those, and says
/// "Application-defined or object-defined error".
/// <para>
/// 🚧 Matching is not bounded in time: a pattern that backtracks without end does here what it does in MS-VBA.
/// </para>
/// </remarks>
internal sealed class StdRegExp : IStdRegExpClass, IStdLibReceiverBound
{
    private sealed class Options
    {
        public string Pattern { get; set; } = string.Empty;

        public bool Global { get; set; }

        public bool IgnoreCase { get; set; }

        public bool Multiline { get; set; }

        // what the pattern was translated to, with the options it was translated with; null until it is matched, and after a change.
        public VBScriptPattern? Translated { get; set; }
    }

    private readonly IRuntimeSession session;
    private readonly StdMatchCollection matches;
    private readonly ConditionalWeakTable<IObjectInstance, Options> _options = [];

    /// <param name="session">The session the objects are objects of.</param>
    /// <param name="matches">Makes the collection of matches that <see cref="Execute"/> returns.</param>
    public StdRegExp(IRuntimeSession session, StdMatchCollection matches)
    {
        this.session = session;
        this.matches = matches;
    }

    /// <inheritdoc/>
    public VBRuntimeObjectId? Receiver { get; set; }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> Pattern()
        => Read(options => new VBStringValue(options.Pattern));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Pattern(VBStringValue value)
        => Write(options => options.Pattern = value.Value ?? string.Empty);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> Global()
        => Read(options => new VBBooleanValue(options.Global));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Global(VBBooleanValue value)
        => Write(options => options.Global = IsTrue(value));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IgnoreCase()
        => Read(options => new VBBooleanValue(options.IgnoreCase));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult IgnoreCase(VBBooleanValue value)
        => Write(options => options.IgnoreCase = IsTrue(value));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> Multiline()
        => Read(options => new VBBooleanValue(options.Multiline));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Multiline(VBBooleanValue value)
        => Write(options => options.Multiline = IsTrue(value));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> Execute(VBStringValue sourceString)
    {
        if (!TryTranslate(out var options, out var pattern, out var failure))
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.Error(failure);
        }

        var text = sourceString.Value ?? string.Empty;
        var found = pattern.Regex.Match(pattern.Subject(text));
        var all = new List<MatchData>();
        while (found.Success)
        {
            all.Add(MatchData.Of(found, text, pattern.GroupCount));
            if (!options.Global)
            {
                break;
            }

            found = found.NextMatch();
        }

        return matches.Create(all) is { } collection
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(collection)
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> Test(VBStringValue sourceString)
        => TryTranslate(out _, out var pattern, out var failure)
            ? RuntimeSemanticsEvaluationResult<VBBooleanValue>.Success(new VBBooleanValue(pattern.Regex.IsMatch(pattern.Subject(sourceString.Value ?? string.Empty))))
            : RuntimeSemanticsEvaluationResult<VBBooleanValue>.Error(failure);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> Replace(VBStringValue sourceString, VBVariantValue replaceVar)
    {
        if (!TryTranslate(out var options, out var pattern, out var failure))
        {
            return RuntimeSemanticsEvaluationResult<VBStringValue>.Error(failure);
        }

        if (ReplacementOf(replaceVar) is not { } replacement)
        {
            return RuntimeSemanticsEvaluationResult<VBStringValue>.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.TypeMismatch, default, "A match cannot be replaced with Null."));
        }

        var text = sourceString.Value ?? string.Empty;
        var replaced = new StringBuilder();
        var copied = 0;
        var found = pattern.Regex.Match(pattern.Subject(text));
        while (found.Success)
        {
            replaced.Append(text, copied, found.Index - copied);
            Expand(replaced, replacement, MatchData.Of(found, text, pattern.GroupCount), text);
            copied = found.Index + found.Length;
            if (!options.Global)
            {
                break;
            }

            found = found.NextMatch();
        }

        replaced.Append(text, copied, text.Length - copied);
        return RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(replaced.ToString()));
    }

    // what a match is replaced with, as text: a Boolean as the number it is (VBScript's, not VBA's, way of writing one), Empty as nothing; Null as none.
    private string? ReplacementOf(VBVariantValue replaceVar)
    {
        var value = ObjectDefaultMember.ValueOf(session, replaceVar) is { IsSuccess: true, Result: { } read } ? read : replaceVar;
        return value switch
        {
            VBNullValue => null,
            VBEmptyValue => string.Empty,
            VBBooleanValue boolean => IsTrue(boolean) ? "-1" : "0",
            _ => ValueConversions.ToText(value, session.Environment.Culture) is { IsSuccess: true, Value: VBStringValue text } ? text.Value ?? string.Empty : null,
        };
    }

    // $1 to $99, $&, $`, $' and $$; anything else is written as it is.
    private static void Expand(StringBuilder replaced, string replacement, MatchData match, string text)
    {
        for (var index = 0; index < replacement.Length; index++)
        {
            var character = replacement[index];
            if (character != '$' || index + 1 >= replacement.Length)
            {
                replaced.Append(character);
                continue;
            }

            var next = replacement[index + 1];
            switch (next)
            {
                case '$':
                    replaced.Append('$');
                    index++;
                    continue;
                case '&':
                    replaced.Append(match.Value);
                    index++;
                    continue;
                case '`':
                    replaced.Append(text, 0, match.FirstIndex);
                    index++;
                    continue;
                case '\'':
                    var after = match.FirstIndex + match.Value.Length;
                    replaced.Append(text, after, text.Length - after);
                    index++;
                    continue;
            }

            // $10 is group 10 when there is one, and group 1 followed by 0 when there is not.
            if (char.IsAsciiDigit(next))
            {
                var groups = match.Groups.Length;
                if (index + 2 < replacement.Length && char.IsAsciiDigit(replacement[index + 2])
                    && (next - '0') * 10 + (replacement[index + 2] - '0') is var both && both >= 1 && both <= groups)
                {
                    replaced.Append(match.Groups[both - 1]);
                    index += 2;
                    continue;
                }

                if (next - '0' is var single && single >= 1 && single <= groups)
                {
                    replaced.Append(match.Groups[single - 1]);
                    index++;
                    continue;
                }
            }

            replaced.Append(character);
        }
    }

    private bool TryTranslate(out Options options, out VBScriptPattern pattern, out IVBRaisableError failure)
    {
        pattern = null!;
        failure = null!;
        if (!TryGetOptions(out options))
        {
            failure = VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, default, "The RegExp object has no state.");
            return false;
        }

        if (options.Translated is { } translated)
        {
            pattern = translated;
            return true;
        }

        if (!VBScriptPattern.TryTranslate(options.Pattern, options.IgnoreCase, options.Multiline, out var made, out var error, out var reason))
        {
            failure = VBApplicationErrorInfo.Raised(error, default, $"The pattern '{options.Pattern}' is not a regular expression: {reason}.");
            return false;
        }

        options.Translated = pattern = made!;
        return true;
    }

    private RuntimeSemanticsEvaluationResult<T> Read<T>(Func<Options, T> read) where T : VBTypedValue
        => TryGetOptions(out var options)
            ? RuntimeSemanticsEvaluationResult<T>.Success(read(options))
            : RuntimeSemanticsEvaluationResult<T>.InternalError();

    // a change of the pattern or of an option is one the next match is translated with.
    private RuntimeSemanticsEvaluationResult Write(Action<Options> write)
    {
        if (!TryGetOptions(out var options))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        write(options);
        options.Translated = null;
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private bool TryGetOptions(out Options options)
    {
        options = null!;
        if (Receiver is not { } receiver || !session.Symbols.TryGetInstance(receiver, out var instance))
        {
            return false;
        }

        options = _options.GetValue(instance, _ => new Options());
        return true;
    }

    private static bool IsTrue(VBBooleanValue boolean) => boolean is { Value.StoredValue: not 0 };
}
