using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Globalization;
using System.Text;

namespace RDCore.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.4.5.10</strong> the <c>Input #</c> statement's input list — the reading half of
/// <see cref="WriteOutputEvaluator"/>.
/// </summary>
/// <remarks>
/// Every spelling <c>Write #</c> produces is read back here, which is the point of the pair: a record written
/// by one is the record read by the other. What makes the statement unusual is that <em>how much it reads
/// depends on what it is reading into</em> — a <c>String</c> variable takes everything up to the next
/// separator, a <c>Date</c> variable insists on a <c>#</c>-delimited field, and a quoted field read into
/// something that is neither <c>String</c> nor <c>Variant</c> yields that type's default value rather than an
/// error. So the declared type of each variable is resolved before its field is read, not after.
/// </remarks>
/// <param name="Assignments">Resolves each variable and Let-assigns the value read into it.</param>
public sealed record class InputListEvaluator(LetAssignmentEvaluator Assignments)
{
    private const char Quote = '"';
    private const char Delimiter = '#';
    private const char Separator = ',';

    /// <summary>The <c>Write #</c> spelling of <c>True</c>, which is the only text that reads as it.</summary>
    private const string True = "TRUE";
    private const string False = "FALSE";
    private const string Null = "NULL";
    private const string Error = "ERROR ";

    // "yyyy-mm-dd hh:mm:ss", and the two halves on their own - the three shapes Write # emits (§5.4.5.9).
    private static readonly string[] DateFormats =
        ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "HH:mm:ss"];

    /// <summary>
    /// VBA's date origin, serial <c>0</c> — what a field carrying only a time of day is a time on.
    /// </summary>
    private static readonly DateTime DateOrigin = new(1899, 12, 30);

    // the characters "which are valid in a VBA number", per the specification's own list.
    private const string NumericPunctuation = ".eE+-";

    /// <summary>
    /// How a field was delimited in the file, which decides what the characters in it mean.
    /// </summary>
    private enum FieldKind
    {
        /// <summary>Up to the next separator: a number, or text that was written unquoted.</summary>
        Bare,

        /// <summary>Between two <c>"</c>: a string, whatever it contains.</summary>
        Quoted,

        /// <summary>Between two <c>#</c>: <c>#TRUE#</c>, <c>#NULL#</c>, <c>#ERROR n#</c>, or a date.</summary>
        Delimited,
    }

    private readonly record struct Field(FieldKind Kind, string Text);

    /// <summary>
    /// Reads one field per variable in <paramref name="variables"/> and Let-assigns each.
    /// </summary>
    /// <param name="session">The session the variables are resolved against.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, for the location of an error it raises.</param>
    /// <param name="variables">The input list, in source order.</param>
    /// <param name="input">The channel the fields are read from.</param>
    public RuntimeExecutionOutcome Execute(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement,
        IReadOnlyList<ExpressionNode> variables, IFileChannelInput input)
    {
        // "Each <input-variable> defined in <input-list> is processed in the order specified" - and the last
        // assignment to the same underlying variable is the one that sticks, which falls out of doing them in
        // order rather than needing a rule of its own.
        foreach (var variable in variables)
        {
            if (!Assignments.TryResolveTarget(session, context, variable, out var symbol))
            {
                return RuntimeExecutionOutcome.InternalError;
            }

            var declaredType = symbol is ITypedSymbol { ResolvedType: var resolved } ? resolved : VBVariantType.TypeInfo;

            // "If one of the operations described in this section causes more characters to be read from the
            // file but file-pointer-position is already pointing at the last character in the file, then an
            // 'Input past end of file' error is raised."
            if (!TryReadField(input, declaredType, out var field))
            {
                return Failed(VBRuntimeErrorId.InputPastEndOfFile, statement, $"reading into {symbol!.Name}");
            }

            if (!TryValueOf(field, declaredType, out var value))
            {
                // the two places the specification names error 6 rather than a coercion failure: a numeric
                // field read into a Boolean, and a Date variable whose field is not #-delimited.
                return Failed(VBRuntimeErrorId.Overflow, statement,
                    $"{field.Text} is not a {declaredType.Name} field");
            }

            var outcome = Assignments.Assign(session, context, statement, symbol!, variable, variable, value!);
            if (outcome.Kind is not RuntimeExecutionOutcomeKind.Next)
            {
                return outcome;
            }
        }

        return RuntimeExecutionOutcome.Next;
    }

    // "Characters are read using the file number value until a non-whitespace character is encountered. These
    // whitespace characters are discarded" - a record separator counts as whitespace here, so a list spanning
    // several records reads as one sequence of fields.
    private static bool TryReadField(IFileChannelInput input, VBType declaredType, out Field field)
    {
        field = default;
        while (input.Peek() is var peeked && peeked >= 0 && char.IsWhiteSpace((char)peeked))
        {
            input.Read();
        }

        if (input.IsEndOfFile)
        {
            return false;
        }

        field = input.Peek() switch
        {
            Quote => new(FieldKind.Quoted, ReadUntil(input, Quote)),
            // a String variable takes "all characters read from the file until a ',' is encountered", so a
            // field it reads is never #-delimited - `Input #1, s` on `#TRUE#` assigns the text "#TRUE#".
            Delimiter when !IsTextual(declaredType) => new(FieldKind.Delimited, ReadUntil(input, Delimiter)),
            _ => new(FieldKind.Bare, ReadBare(input)),
        };

        return true;
    }

    // consumes the opening delimiter, the characters up to the closing one, and the closing one - then the
    // separator after it, which belongs to this field rather than to the next.
    private static string ReadUntil(IFileChannelInput input, char delimiter)
    {
        input.Read();
        var text = new StringBuilder();
        while (input.Read() is var next && next >= 0 && next != delimiter)
        {
            text.Append((char)next);
        }

        SkipSeparator(input);
        return text.ToString();
    }

    // "the sequence of characters is a concatenation of all characters read from the file until a ',' is
    // encountered" - and until the end of the record too, because that is where Write # put the end of the
    // last field of one, and a record has to read back as the fields it was written from.
    private static string ReadBare(IFileChannelInput input)
    {
        var text = new StringBuilder();
        while (input.Peek() is var peeked && peeked >= 0 && peeked != Separator && peeked is not ('\r' or '\n'))
        {
            text.Append((char)input.Read());
        }

        SkipSeparator(input);
        return text.ToString().Trim();
    }

    private static void SkipSeparator(IFileChannelInput input)
    {
        if (input.Peek() == Separator)
        {
            input.Read();
        }
    }

    // the declared types a field is assigned to as text rather than being read for a value. Fixed-length
    // String is one of them, VBFixedStringType being a VBStringType.
    private static bool IsTextual(VBType declaredType) => declaredType is VBStringType;

    private static bool TryValueOf(Field field, VBType declaredType, out VBTypedValue? value)
    {
        value = null;
        if (IsTextual(declaredType))
        {
            // a String variable is assigned the characters, whatever they look like.
            value = new VBStringValue(field.Text);
            return true;
        }

        if (declaredType.Equals(VBBooleanType.TypeInfo))
        {
            // "it is assigned the value false, unless the sequence of characters read are '#TRUE#'. If the
            // sequence of characters is numeric an 'Overflow' error is generated."
            if (field.Kind is FieldKind.Bare && IsNumeric(field.Text))
            {
                return false;
            }

            value = field.Kind is FieldKind.Delimited && field.Text == True
                ? VBBooleanValue.True
                : VBBooleanValue.False;
            return true;
        }

        if (declaredType.Equals(VBDateType.TypeInfo))
        {
            // "If the first character at file-pointer-position is not '#', then error 6 ('Overflow') is
            // generated" - so a Date variable takes a delimited field and nothing else.
            if (field.Kind is not FieldKind.Delimited || !TryParseDate(field.Text, out var date))
            {
                return false;
            }

            value = date;
            return true;
        }

        value = field.Kind switch
        {
            // "If the sequence of characters is surrounded by DQUOTEs and the declared type of
            // <input-variable> is not String or Variant, then <input-variable> is set to its default value."
            FieldKind.Quoted when !declaredType.Equals(VBVariantType.TypeInfo) => declaredType.DefaultValue,
            FieldKind.Quoted => new VBStringValue(field.Text),
            FieldKind.Delimited => DelimitedValue(field.Text),
            // "If the sequence of characters are all numbers or characters which are valid in a VBA number
            // then the characters are concatenated together into a string and Let-coerced into the declared
            // type" - which is what assigning the String below does, for a numeric field and for any other.
            _ when field.Text.Length == 0 => declaredType.DefaultValue,
            _ => new VBStringValue(field.Text),
        };

        return true;
    }

    // the delimited spellings Write # emits, read back as the values they were written from.
    private static VBTypedValue DelimitedValue(string text) => text switch
    {
        True => VBBooleanValue.True,
        False => VBBooleanValue.False,
        // "If the sequence of characters read from the file are '#NULL#' then the Null value is Let-coerced
        // into <input-variable>."
        Null => VBNullValue.Null,
        // "If the sequence of characters read from the file are '#ERROR ' followed by a number followed by a
        // '#' then the error number value is Let-coerced into <input-variable>."
        _ when text.StartsWith(Error, StringComparison.Ordinal)
            && int.TryParse(text[Error.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            => new VBErrorValue(number),
        _ when TryParseDate(text, out var date) => date!,
        // a delimited field that spells none of those is text; the coercion the assignment applies decides
        // whether the variable it is going into can take it.
        _ => new VBStringValue(text),
    };

    private static bool TryParseDate(string text, out VBDateValue? value)
    {
        var parsed = DateTime.TryParseExact(
            text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment);

        // a field carrying only a time of day parses onto today's date, and VBA counts one from serial 0.
        if (parsed && moment.Date == DateTime.Today && text.Length == "HH:mm:ss".Length)
        {
            moment = DateOrigin + moment.TimeOfDay;
        }

        value = parsed ? new VBDateValue(moment.ToOADate()) : null;
        return parsed;
    }

    private static bool IsNumeric(string text)
        => text.Length > 0 && text.All(character => char.IsAsciiDigit(character) || NumericPunctuation.Contains(character));

    private static RuntimeExecutionOutcome Failed(VBRuntimeErrorId error, StatementNode statement, string detail)
        => RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(error, statement.SourceLocation, detail));
}
