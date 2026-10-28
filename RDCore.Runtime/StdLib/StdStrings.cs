using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.Runtime.StdLib.Formatting;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdStringsModule"/>
/// <remarks>
/// <para>
/// 👉 The session is what these members read of the world: the regional settings of its environment, which a number or a
/// date is written in when a <c>Variant</c> argument is read as a <c>String</c> (as Let-coercion writes it,
/// <strong>MS-VBAL §5.5.1.2.4</strong>) and which <c>LCase</c>, <c>UCase</c> and <c>StrConv</c> case by; the environment's
/// ANSI code page, which is what the implementation-defined character codes of <c>Asc</c>, <c>Chr</c> and <c>String</c> are,
/// and what <c>StrConv</c> converts to and from; and the comparison an explicit <c>Compare</c> names.
/// </para>
/// <para>
/// 👉 What a member is given has been through the call site first. A typed parameter's argument was Let-coerced to its type,
/// so a Null <c>String</c> or <c>Long</c> was error 94 before any member was reached; a typed optional left out arrives as its
/// declared default (<c>StdLibDefault</c>), or as <c>null</c> when it declares none (<c>StrConv</c>'s <c>LocaleID</c>, which its
/// member reads as left out), and a <c>Compare</c> left out as the calling module's <c>Option Compare</c>
/// (<c>StdLibOptionCompare</c>). An omitted <c>Variant</c> optional, or a caller's Missing passed on to one, arrives as
/// <c>null</c>, and nothing else is read as omitted: an explicit Empty is Empty. A member reads a <c>Variant</c> argument the
/// way a parameter of the type it needs would be Let-coerced, and always in one order: the arguments' coercions in the order
/// they are declared, then a Null operand, which makes the result Null, then the values' own checks in the order they are
/// declared. A character member reads a <c>String</c>'s characters, so a trailing byte is not one of them, as <see cref="Len"/>
/// does not count it; a byte member (<c>AscB</c>, <c>ChrB</c>, <c>LeftB</c>, <c>RightB</c>, <c>MidB</c>, <c>InStrB</c>) reads every
/// byte.
/// Each <c>$</c> function is its <c>Variant</c> twin's result Let-coerced to <c>String</c>, so where the twin returns Null it
/// raises error 94.
/// </para>
/// <para>
/// 👉 An object given for a <c>Variant</c> parameter is read as its Let-coercion reads it (<strong>MS-VBAL §5.5.1.2.13</strong>):
/// as its "simple data value", what its default member returns when it is called with no arguments. <c>Nothing</c> is error 91,
/// as the call site makes it for a typed parameter; an object with no default member that can be called so is error 438; what
/// the default member raises is raised.
/// </para>
/// </remarks>
/// <param name="session">The session whose environment (regional settings, ANSI code page) and comparison rules these members read.</param>
public sealed class StdStrings(IRuntimeSession session) : IStdStringsModule
{
    // "spaces" are U+0020, and nothing else: a tab, a no-break space or an ideographic space is not one.
    private const char SpaceCharacter = ' ';

    // the longest String .NET can hold; a longer one is out of string space.
    private const int LongestString = 0x3FFFFFDF;

    // 👉 MS-VBAL 6.1.2.11.1.6 bounds ChrW at "-32,767", which is read as the bottom of an Integer: AscW of U+8000 is -32768,
    // and ChrW(AscW(c)) is c for every code unit.
    private const int LowestWideCode = -32768;

    /// <summary>
    /// The error a member returns for what it has no code for yet: <c>Len</c> and <c>LenB</c> of a record with a field they cannot measure.
    /// </summary>
    private static RuntimeSemanticsEvaluationResult<TValue> NotImplemented<TValue>(string member)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default,
            $"'{member}' is declared but not implemented yet."));

    // a member's errors say which member, and what about its arguments: "Mid: Start is at least 1, not 0."
    private static VBRuntimeErrorInfo Failure(VBRuntimeErrorId error, string member, string rule)
        => VBRuntimeErrorInfo.For(error, default, $"{member}: {rule}.");

    private static RuntimeSemanticsEvaluationResult<TValue> Fail<TValue>(VBRuntimeErrorId error, string member, string rule)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(Failure(error, member, rule));

    private static RuntimeSemanticsEvaluationResult<TValue> Fail<TValue>(IVBRaisableError error)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(error);

    private static RuntimeSemanticsEvaluationResult<TValue> Succeed<TValue>(TValue value)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Success(value);

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> InVariant(VBTypedValue value) => Succeed(new VBVariantValue(value));

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> InVariant(string text) => InVariant(new VBStringValue(text));

    // a character member reads a String through this and nothing else, so a trailing byte is dropped the way Len drops it;
    // a byte member never comes through here.
    private static string Text(VBStringValue value) => value.Value;

    // a typed optional that declares a default is filled from its declaration before any call reaches a member - by the call site
    // from source, by the dispatcher for a direct call that leaves it out or passes Missing - so null here is a C# caller's mistake.
    // Members never restate a default. One that declares none (StrConv's LocaleID) arrives as null, and is not read through here.
    private static TValue Given<TValue>(TValue? argument, [CallerArgumentExpression(nameof(argument))] string name = "")
        where TValue : VBTypedValue
        => argument ?? throw new ArgumentNullException(name, "an omitted optional is filled from its declaration before the call reaches its implementation");

    // a Variant argument's String, as a String parameter's Let-coercion would give it (MS-VBAL 5.5.1.2.4), in the regional
    // settings of the session's environment; null for Null, which each member answers for itself. An object is its simple data
    // value (TryReadValue). A value with no String form (an Error, Missing, an array that is not a Byte array, a record) is a type
    // mismatch.
    private bool TryReadString(VBTypedValue argument, string member, string parameter, out VBStringValue? text, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        text = null;
        if (!TryReadValue(argument, member, parameter, out var value, out error))
        {
            return false;
        }

        if (value is VBNullValue)
        {
            return true;
        }

        var converted = ValueConversions.ToText(value, session.Environment.Culture, session.Environment.Regional);
        if (converted.IsSuccess)
        {
            text = (VBStringValue)converted.Value!;
            return true;
        }

        error = Failure(converted.Error ?? VBRuntimeErrorId.TypeMismatch, member, $"{parameter} cannot be a value of type {value.TypeInfo.Name}");
        return false;
    }

    // a Variant argument's Long, as a Long parameter's Let-coercion would give it (MS-VBAL 5.5.1.2.1): Empty is 0, True -1, a
    // number or a date rounds to even (6 outside a Long), a numeric String is read as one, an object is its simple data value;
    // Null is 94, anything else 13.
    private bool TryReadLong(VBTypedValue argument, string member, string parameter, out int number, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        number = default;
        if (!TryReadValue(argument, member, parameter, out var value, out error))
        {
            return false;
        }

        var converted = ValueConversions.ToNumeric(value, VBLongType.TypeInfo);
        if (converted.IsSuccess)
        {
            number = (int)((VBNumericTypedValue)converted.Value!).AsDouble;
            error = null;
            return true;
        }

        error = converted.Error switch
        {
            VBRuntimeErrorId.InvalidUseOfNull => Failure(VBRuntimeErrorId.InvalidUseOfNull, member, $"{parameter} is Null"),
            VBRuntimeErrorId.Overflow => Failure(VBRuntimeErrorId.Overflow, member, $"{parameter} is outside the range of a Long"),
            _ => Failure(VBRuntimeErrorId.TypeMismatch, member, $"{parameter} cannot be a value of type {value.TypeInfo.Name}"),
        };
        return false;
    }

    // how many default members an argument is read through before it is taken for a chain that never ends in a value.
    private const int MostDefaultMembers = 64;

    // a Variant argument's value, out of any Variant: an object is its "simple data value" (MS-VBAL 5.5.1.2.13), what its default
    // member returns when it is called with no arguments, read again while that is an object. Nothing is error 91; what has no value
    // is the error ObjectDefaultMember.NoValue says (438, 450, or 13 for an object a default member returned); what the default member
    // raises is its error; and a chain of default members that does not end in a value is out of stack space, as it is to Let-coercion.
    private bool TryReadValue(VBTypedValue argument, string member, string parameter, out VBTypedValue value, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        value = Unwrapped(argument);
        for (var depth = 0; value is VBObjectValue reference; depth++)
        {
            if (reference.IsNothing())
            {
                error = Failure(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, member, $"{parameter} is Nothing");
                return false;
            }

            if (depth == MostDefaultMembers)
            {
                error = Failure(VBRuntimeErrorId.OutOfStackSpace, member, $"{parameter}'s default members never return a value that is not an object");
                return false;
            }

            if (ObjectDefaultMember.Call(session, reference) is not { } called)
            {
                var noValue = ObjectDefaultMember.NoValue(session, reference, returnedByADefaultMember: depth > 0);
                error = Failure((VBRuntimeErrorId)noValue.ErrorId, member, $"{parameter}: {noValue.Verbose}");
                return false;
            }

            if (called.ErrorInfo is { } raised)
            {
                error = raised;
                return false;
            }

            value = Unwrapped(called.Result!);
        }

        error = null;
        return true;
    }

    // an explicit Compare is vbBinaryCompare, vbTextCompare, vbDatabaseCompare where the member takes it, or the identifier of a locale, whose text comparison
    // it is (StringComparisonRules.TryForLocale); an omitted one has already been filled in as one of the first two, Option Compare Database resolved, so this
    // never sees a caller's omission. Split, InStrRev and Replace take vbDatabaseCompare, as the module's Option Compare Database compares; InStr, StrComp
    // and Filter refuse it, as MS-VBA's do.
    // MS-VBA's vbDatabaseCompare, which MS-VBAL's VbCompareMethod (§6.1.1.5) does not name.
    private const VBCompareMethod DatabaseCompare = (VBCompareMethod)2;

    private bool TryRulesFor(VBCompareMethod compare, string member, out StringComparisonRules rules, [NotNullWhen(false)] out IVBRaisableError? error,
        bool takesDatabaseCompare = false)
    {
        error = null;
        switch (compare)
        {
            case VBCompareMethod.VBBinaryCompare:
                rules = session.StringComparisonIn(OptionCompare.Binary);
                return true;
            case VBCompareMethod.VBTextCompare:
                rules = session.StringComparisonIn(OptionCompare.Text);
                return true;
            case DatabaseCompare when takesDatabaseCompare:
                rules = session.StringComparisonIn(OptionCompare.Database);
                return true;
            case not DatabaseCompare when StringComparisonRules.TryForLocale((int)compare, session.Environment.Culture, out rules):
                return true;
            default:
                rules = default;
                error = Failure(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member,
                    $"Compare is vbBinaryCompare (0), vbTextCompare (1) or the identifier of a locale, not {(int)compare}");
                return false;
        }
    }

    // every $ function: "functionally identical to the Left function, with the exception that the return type of the function is
    // String rather than Variant" - the Variant member's result, Let-coerced to String, where Null is 94 (MS-VBAL 5.5.1.2.10).
    private static RuntimeSemanticsEvaluationResult<VBStringValue> Twin(string member, RuntimeSemanticsEvaluationResult<VBVariantValue> variant)
        => variant switch
        {
            { IsSuccess: true, Result.TypedValue: VBStringValue text } => Succeed(text),
            { IsSuccess: true, Result.TypedValue: VBNullValue } => Fail<VBStringValue>(VBRuntimeErrorId.InvalidUseOfNull, member, "a String cannot hold Null"),
            { ErrorInfo: { } error } => Fail<VBStringValue>(error),
            // a Variant member returned neither a String nor Null: a bug, not a program's error.
            _ => RuntimeSemanticsEvaluationResult<VBStringValue>.InternalError(),
        };

    // the character a code names in the environment's ANSI code page: one byte up to 255, a lead and a trail byte above it. A
    // single-byte code page names one with every byte. A double-byte code page names one only where it reads the bytes as exactly
    // one character it defines: not a lone lead byte, not two single-byte characters, and not the character its decoder puts in
    // place of bytes it does not define.
    private bool TryCharacterOf(int code, [NotNullWhen(true)] out string? character)
    {
        if (code < 128)
        {
            character = ((char)code).ToString();
            return true;
        }

        byte[] bytes = code <= 0xFF ? [(byte)code] : [(byte)(code >> 8), (byte)code];
        var encoding = session.Environment.AnsiEncoding;
        if (encoding.IsSingleByte)
        {
            character = encoding.GetString(bytes);
            return true;
        }

        var strict = (System.Text.Encoding)encoding.Clone();
        strict.DecoderFallback = System.Text.DecoderFallback.ExceptionFallback;
        try
        {
            var decoded = strict.GetString(bytes);
            character = decoded.Length == 1 ? decoded : null;
        }
        catch (System.Text.DecoderFallbackException)
        {
            character = null;
        }

        return character is not null;
    }

    // a String longer than .NET can hold is out of string space, and so is one the allocation fails for.
    private static bool TryRepeat(char character, int count, string member, [NotNullWhen(true)] out string? text, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        if (count <= LongestString)
        {
            try
            {
                text = new string(character, count);
                error = null;
                return true;
            }
            catch (OutOfMemoryException)
            {
            }
        }

        text = null;
        error = Failure(VBRuntimeErrorId.OutOfStringSpace, member, $"{count} characters are more than a String can hold");
        return false;
    }

    /// <summary>
    /// The number of characters in a string, or the number of bytes a value of another type occupies
    /// (<strong>MS-VBAL §6.1.2.11.1.22</strong>).
    /// </summary>
    /// <remarks>
    /// "With user-defined types, <c>Len</c> returns the size as it will be written to the file". 👉 RD-VBA reports what MS-VBA
    /// does instead: the members' memory widths with no padding between them, a variable-length <c>String</c>, an object or a
    /// resizable array member counting as the pointer it is, a <c>Variant</c> as 24 bytes in a 64-bit environment, and a
    /// fixed-length <c>String</c> as its characters (<see cref="VBUserDefinedTypeLayout.UnpaddedSize"/>). <see cref="LenB"/>
    /// is the in-memory size, padding included.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Len(VBVariantValue expression)
        => Measure(expression, inMemory: false);

    /// <summary>
    /// The number of <em>bytes</em> a value occupies (<strong>MS-VBAL §6.1.2.11.1.22</strong>).
    /// </summary>
    /// <remarks>
    /// "<c>LenB</c> will return the same value as <c>Len</c>, except for strings or UDTs": a string's is
    /// "the number of bytes used to represent that string", and a UDT's "in-memory size, including any
    /// implementation-specific padding between elements" is what <see cref="VBUserDefinedTypeLayout"/> lays out.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> LenB(VBVariantValue expression)
        => Measure(expression, inMemory: true);

    // one measurement with one dial, because the specification defines LenB as Len "except for strings or
    // UDTs" - stating one function and its two exceptions rather than two functions.
    private RuntimeSemanticsEvaluationResult<VBVariantValue> Measure(VBVariantValue expression, bool inMemory)
    {
        // an object is measured as its value, what its default member returns - not as the reference it is.
        if (!TryReadValue(expression, inMemory ? nameof(LenB) : nameof(Len), "Expression", out var value, out var error))
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(error);
        }

        // "If Expression contains the data value Null, Null is returned" - the one case that is not a number.
        if (value is VBNullValue)
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(VBNullValue.Null));
        }

        return RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(new VBLongValue(Length(value, inMemory))));
    }

    private int Length(VBTypedValue value, bool inMemory) => value switch
    {
        // a fixed-length String is its declared length, whatever it currently holds.
        VBFixedStringValue fixedString => inMemory ? 2 * fixedString.Length : fixedString.Length,
        // "Returns a Long containing the number of characters in a string", and LenB "returns the number of
        // bytes used to represent that string" - every byte of its data, a trailing one included.
        VBStringValue text => inMemory ? text.ByteLength : text.Length,
        // the two UDT sizes, laid out for the environment's pointer width: LenB's is the padded in-memory one, Len's the
        // same widths with no padding.
        VBUserDefinedTypeValue udt => RecordLength(VBUserDefinedTypeLayout.Of(udt, session.Environment.Is64Bit ? 8 : 4), inMemory),
        // "the number of bytes required to store a variable on the current platform", which for every scalar
        // is the size the value itself reports.
        VBEmptyValue => 0,
        _ => value.Size,
    };

    private static int RecordLength(VBUserDefinedTypeLayout layout, bool inMemory) => inMemory ? layout.Size : layout.UnpaddedSize;

    // a Variant's own TypeInfo mirrors what it wraps while the instance stays a VBVariantValue, so measuring
    // one has to see the wrapped value - "if the variable name is a Variant, Len/LenB treats it the same as a
    // String and always returns the number of characters it contains".
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "Returns an Integer data value representing the 7-bit ASCII code point of the first character of StringValue. If the
    /// character does not correspond to an ASCII character the result is implementation defined": its code in the environment's
    /// ANSI code page, a character the code page lacks being what its encoder maps it to, and a double-byte code read as one
    /// number - "code point value greater than 32,767 are returned as negative Integer data values".
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> Asc(VBStringValue @string)
    {
        if (!TryFirstCharacter(@string, nameof(Asc), out var first, out var error))
        {
            return Fail<VBIntegerValue>(error);
        }

        if (first < 128)
        {
            return Succeed(new VBIntegerValue((short)first));
        }

        Span<byte> code = stackalloc byte[8];
        var length = session.Environment.AnsiEncoding.GetBytes(Text(@string).AsSpan(0, 1), code);
        return Succeed(new VBIntegerValue(length == 1 ? code[0] : unchecked((short)((code[0] << 8) | code[1]))));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "The first eight bits (the first byte) of the implementation dependent character encoding of the string", which is
    /// UTF-16LE: the low byte of the first character, or the byte of a String that is a single byte.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> AscB(VBStringValue @string)
    {
        var bytes = @string.GetBytes();
        return bytes.Length == 0
            ? Fail<VBIntegerValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(AscB), "String is a zero-length string")
            : Succeed(new VBIntegerValue(bytes[0]));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "The 16-bit Unicode code point of the first character": its first code unit, so the high half of a surrogate pair.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> AscW(VBStringValue @string)
        => TryFirstCharacter(@string, nameof(AscW), out var first, out var error)
            ? Succeed(new VBIntegerValue(unchecked((short)first)))
            : Fail<VBIntegerValue>(error);

    // "If the argument is the null string ("") Error Number 5"; a String that is a single byte has no first character either.
    private static bool TryFirstCharacter(VBStringValue value, string member, out char first, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        var text = Text(value);
        if (text.Length == 0)
        {
            first = default;
            error = Failure(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member,
                value.TrailingByte is null ? "String is a zero-length string" : "String is a single byte and no character");
            return false;
        }

        first = text[0];
        error = null;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 0 to 127 is "interpreted as a 7-bit ASCII code point", and 128 to 255 is "implementation defined": the character of the
    /// environment's ANSI code page. Anything else is error 5 "unless the implementation supports a character set with a larger
    /// code point range", which a double-byte code page is: there a code is -32,768 to 65,535, a negative one read as the code plus
    /// 65,536, and a code above 255 is a lead and a trail byte. The result is "a single character", so there a code from 128 on is
    /// error 5 unless the code page reads its byte, or its two bytes, as one character it defines: not a lone lead byte, not two
    /// single-byte characters, and not the character its decoder puts in place of bytes it does not define.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Chr(VBLongValue charCode)
    {
        var code = charCode.Value;
        if (session.Environment.AnsiEncoding.IsSingleByte)
        {
            if (code is < 0 or > 0xFF)
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(Chr), $"CharCode is 0 to 255 in a single-byte code page, not {code}");
            }
        }
        else if (code is < short.MinValue or > ushort.MaxValue)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(Chr), $"CharCode is -32768 to 65535 in a double-byte code page, not {code}");
        }

        return TryCharacterOf(code < 0 ? code + 0x10000 : code, out var character)
            ? InVariant(character)
            : Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(Chr), $"CharCode {code} names no character in the code page");
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> ChrString(VBLongValue charCode) => Twin("Chr$", Chr(charCode));

    /// <inheritdoc/>
    /// <remarks>
    /// "A String data value consisting of a single byte character whose code point value is the data value of the argument": a
    /// String of that one byte, whose <c>Len</c> is 0 and <c>LenB</c> 1. "If the argument is not in the range 0 to 255, Error
    /// Number 6 ("Overflow") is raised."
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> ChrB(VBLongValue charCode)
    {
        var code = charCode.Value;
        return code is < 0 or > 0xFF
            ? Fail<VBVariantValue>(VBRuntimeErrorId.Overflow, nameof(ChrB), $"CharCode is 0 to 255, not {code}")
            : InVariant(VBStringValue.FromBytes([(byte)code]));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> ChrBString(VBLongValue charCode) => Twin("ChrB$", ChrB(charCode));

    /// <inheritdoc/>
    /// <remarks>
    /// The character of a 16-bit code point, a negative argument "treated as if it was the value: CharCode + 65,536"; a lone
    /// surrogate is a character like any other. 👉 The range is -32,768 to 65,535 where the specification says "-32,767": see
    /// <see cref="LowestWideCode"/>.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> ChrW(VBLongValue charCode)
    {
        var code = charCode.Value;
        return code is < LowestWideCode or > ushort.MaxValue
            ? Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(ChrW), $"CharCode is -32768 to 65535, not {code}")
            : InVariant(((char)(code < 0 ? code + 0x10000 : code)).ToString());
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> ChrWString(VBLongValue charCode) => Twin("ChrW$", ChrW(charCode));

    /// <inheritdoc/>
    /// <remarks>
    /// "A zero-based array containing subset of a string array based on a specified filter criteria", zero-based whatever the
    /// calling module's <c>Option Base</c>: the elements that contain Match "as a substring", or with Include False the ones that do
    /// not, in their order, "only enough elements to contain the number of matched items", and an array with no elements when
    /// none is kept. "An error occurs if SourceArray is the data value Null or is not a one-dimensional array": error 13, as the
    /// declared <c>SourceArray()</c> would raise.
    /// <para>
    /// 👉 An element contains Match when <c>InStr</c> would find it there, so a zero-length element contains nothing and a
    /// zero-length Match is in every other element. Each element is read as a <c>String</c> parameter would Let-coerce it, all of
    /// them before any is kept, so a Null element is error 94 whether it would be kept or not; an element never assigned in a
    /// <c>Variant()</c> is Empty, a zero-length string. What is kept is that <c>String</c>, unchanged: the filter selects
    /// elements, and transforms none. A SourceArray with no dimensions yet - a dynamic array never <c>ReDim</c>'d - is filtered to a
    /// <c>String()</c> with none either, whose <c>UBound</c> is error 9 (recorded in Excel, oracle\s41 M03).
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Filter(VBVariantValue sourceArray, VBStringValue match, VBBooleanValue? include = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare)
    {
        if (!TryReadArray(sourceArray, nameof(Filter), out var array, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        var keep = (bool)Given(include).Value;
        if (!TryRulesFor(compare, nameof(Filter), out var rules, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (array.Rank == 0)
        {
            return InVariant(new VBResizableArrayValue([], VBStringType.TypeInfo));
        }

        if (!TryReadElements(array, nameof(Filter), out var elements, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        var pattern = Text(match);
        return InArray([.. elements.Where(element => Contains(Text(element)) == keep)]);

        bool Contains(string element) => CharacterPosition(element, pattern, 1, rules) > 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// "A String containing an expression formatted according to instructions contained in a format expression", in the regional
    /// settings of the session's environment, its date symbols in the session's calendar ("if the calendar is Hijri, all date format
    /// symbols [...] apply to the Hijri calendar"). Without a Format, or with a zero-length one, the expression is the String a
    /// Let-coercion writes ("similar to the Str function, although it is internationally aware", with no leading space), and Null
    /// is Null. A named format is read in any case; any other is user-defined, of up to four sections, "truncated to 257 characters"
    /// (<see cref="FormatExpression"/>). A Null is Null unless the format has a section for it: the fourth of a number format, the second
    /// of a string format.
    /// </para>
    /// <para>
    /// 👉 Expression is read as itself, "any valid expression": Null, Empty, a Boolean, a number, a <c>Date</c> or a <c>String</c>, and a
    /// <c>Byte()</c> as its <c>String</c>; Nothing is error 91, and anything else error 13, as a <c>String</c> parameter would raise. Format
    /// is read as a <c>String</c> parameter would be, so a Null one is error 94. FirstDayOfWeek and FirstWeekOfYear are values of their
    /// enumerations, else error 5, checked after a Null Expression has made the result Null. A date outside the range of dates is error
    /// 6, and one before the first day of the Hijri calendar error 5, as they are to the <c>DateTime</c> module.
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Format(VBVariantValue expression, VBVariantValue? format = default, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1)
    {
        if (!TryReadFormattable(expression, nameof(Format), out var value, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        var pattern = string.Empty;
        if (format is not null)
        {
            if (!TryReadString(format, nameof(Format), "Format", out var text, out error))
            {
                return Fail<VBVariantValue>(error);
            }

            if (text is null)
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidUseOfNull, nameof(Format), "Format is Null");
            }

            pattern = Text(text);
        }

        if (value is VBNullValue && !FormatExpression.WritesNull(pattern))
        {
            return InVariant(VBNullValue.Null);
        }

        if (!TryFormatSettings(nameof(Format), firstDayOfWeek, firstWeekOfYear, out var settings, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (pattern.Length == 0)
        {
            return TryReadString(value, nameof(Format), "Expression", out var unformatted, out error) ? InVariant(unformatted!) : Fail<VBVariantValue>(error);
        }

        var output = new TextBuilder(stackalloc char[FormattedCapacity]);
        try
        {
            var failure = FormatExpression.Write(ref output, value, pattern, settings, out var isNull);
            return failure is not null ? Fail<VBVariantValue>(Failure(failure.Error, nameof(Format), failure.Rule))
                : isNull ? InVariant(VBNullValue.Null)
                : InVariant(output.ToString());
        }
        catch (OutOfMemoryException)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.OutOfStringSpace, nameof(Format), "the result is more than a String can hold");
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatString(VBVariantValue expression, VBVariantValue? format = default, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1)
        => Twin("Format$", Format(expression, format, firstDayOfWeek, firstWeekOfYear));

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The amount with the environment's currency symbol, where its regional settings put it, to the places of its currency
    /// (<see cref="NumberFormatInfo.CurrencyDecimalDigits"/>) unless NumDigitsAfterDecimal says otherwise, its digits grouped by its
    /// currency separator, and a negative amount in its negative currency pattern. The named format <c>Currency</c> is this with
    /// every argument left out.
    /// </para>
    /// <para>
    /// 👉 Read as <see cref="FormatNumber"/> reads its arguments. UseParensForNegativeNumbers <c>vbTrue</c> puts a negative amount in
    /// parentheses, the symbol where the positive pattern has it, and <c>vbFalse</c> gives one the regional settings put in
    /// parentheses a minus sign before the amount and its symbol instead.
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatCurrency(VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default, VBTriState includeLeadingDigit = VBTriState.VBUseDefault, VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault, VBTriState groupDigits = VBTriState.VBUseDefault)
        => Styled(nameof(FormatCurrency), NumberStyle.Currency, expression, numDigitsAfterDecimal, includeLeadingDigit, useParensForNegativeNumbers, groupDigits);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>Format</c>'s named date formats, in the regional settings of the session's environment and the session's calendar:
    /// <c>vbGeneralDate</c> is <c>General Date</c> (the short date, the long time, or both, as a Let-coercion to <c>String</c> writes a
    /// date in the Gregorian calendar), <c>vbLongDate</c> <c>Long Date</c>, <c>vbShortDate</c> <c>Short Date</c>, <c>vbLongTime</c>
    /// <c>Long Time</c>, and <c>vbShortTime</c> "the 24-hour format (hh:mm)".
    /// </para>
    /// <para>
    /// 👉 Expression is Let-coerced as a <c>Date</c> parameter would be: Null is error 94, Nothing 91, and a value that is no date error 13;
    /// a date outside the range of dates is error 6. A NamedFormat other than 0 to 4 is error 5.
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatDateTime(VBVariantValue expression, VBDateTimeFormat namedFormat = VBDateTimeFormat.VBGeneralDate)
    {
        if (!TryReadValue(expression, nameof(FormatDateTime), "Expression", out var value, out var error))
        {
            return Fail<VBStringValue>(error);
        }

        var converted = ValueConversions.ToDate(value, session.Environment.Regional);
        if (converted.Value is not VBDateValue date)
        {
            return converted.Error is VBRuntimeErrorId.InvalidUseOfNull
                ? Fail<VBStringValue>(VBRuntimeErrorId.InvalidUseOfNull, nameof(FormatDateTime), "Expression is Null")
                : Fail<VBStringValue>(converted.Error ?? VBRuntimeErrorId.TypeMismatch, nameof(FormatDateTime), $"Expression cannot be a value of type {value.TypeInfo.Name} that is no date");
        }

        var symbols = namedFormat switch
        {
            VBDateTimeFormat.VBGeneralDate => "c",
            VBDateTimeFormat.VBLongDate => "dddddd",
            VBDateTimeFormat.VBShortDate => "ddddd",
            VBDateTimeFormat.VBLongTime => "ttttt",
            VBDateTimeFormat.VBShortTime => "hh:nn",
            _ => null,
        };

        if (symbols is null)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(FormatDateTime), $"NamedFormat is vbGeneralDate (0) to vbShortTime (4), not {(int)namedFormat}");
        }

        TryFormatSettings(nameof(FormatDateTime), VBDayOfWeek.VBSunday, VBFirstWeekOfYear.VBFirstJan1, out var settings, out _);
        var output = new TextBuilder(stackalloc char[FormattedCapacity]);
        try
        {
            return Written(nameof(FormatDateTime), FormatExpression.WriteDate(ref output, date, symbols, settings), output.Written);
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The number rounded half away from zero to NumDigitsAfterDecimal places, written with the environment's decimal symbol; a
    /// fraction with or without its leading zero, digits grouped or not by the environment's digit groups, and a negative number in
    /// parentheses or with the minus sign of its negative number pattern, each as it is told, and as the regional settings say for
    /// <c>vbUseDefault</c>. A number that rounds to zero is written with no sign.
    /// </para>
    /// <para>
    /// 👉 What the regional settings say, where .NET's culture says nothing or two platforms disagree, is decided here: the places
    /// are 2 (<see cref="NumberFormatting.DefaultFractionDigits"/>), a fraction has its leading zero, digits are grouped where the
    /// culture has groups, and a negative number is in parentheses where its negative number pattern is <c>(n)</c>.
    /// </para>
    /// <para>
    /// 👉 Expression is Let-coerced as a <c>Double</c> parameter would be, except that a number is read as the value it is, so a
    /// Currency, a Decimal or a LongLong keeps every digit: Empty is 0, a Boolean -1 or 0, a <c>Date</c> its serial number, a numeric
    /// <c>String</c> read in the regional settings; Null is error 94, Nothing 91, and anything else error 13. NumDigitsAfterDecimal
    /// left out is the declared -1, the regional settings' places, and one below -1 is error 5. A tristate other than <c>vbTrue</c> (-1),
    /// <c>vbFalse</c> (0) or <c>vbUseDefault</c> (-2) is error 5: the tables' 1 and 2 are read as the enumeration's -1 and -2
    /// (<strong>MS-VBAL §6.1.1.15</strong>).
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatNumber(VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default, VBTriState includeLeadingDigit = VBTriState.VBUseDefault, VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault, VBTriState groupDigits = VBTriState.VBUseDefault)
        => Styled(nameof(FormatNumber), NumberStyle.Number, expression, numDigitsAfterDecimal, includeLeadingDigit, useParensForNegativeNumbers, groupDigits);

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="FormatNumber"/> of the number "multiplied by 100 with a trailing % character", the sign or the parentheses
    /// around the two: <c>FormatPercent(-0.5)</c> is "-50.00%", and with parentheses "(50.00%)".
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatPercent(VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default, VBTriState includeLeadingDigit = VBTriState.VBUseDefault, VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault, VBTriState groupDigits = VBTriState.VBUseDefault)
        => Styled(nameof(FormatPercent), NumberStyle.Percent, expression, numDigitsAfterDecimal, includeLeadingDigit, useParensForNegativeNumbers, groupDigits);

    // what a format is written into first: most results fit, and one that does not grows into a rented buffer.
    private const int FormattedCapacity = 128;

    // FormatNumber, FormatCurrency and FormatPercent: Expression's coercion, then the values' checks in the order they are declared.
    private RuntimeSemanticsEvaluationResult<VBStringValue> Styled(string member, NumberStyle style, VBVariantValue expression, VBLongValue? numDigitsAfterDecimal, VBTriState includeLeadingDigit, VBTriState useParensForNegativeNumbers, VBTriState groupDigits)
    {
        if (!TryReadNumber(expression, member, out var number, out var error))
        {
            return Fail<VBStringValue>(error);
        }

        var places = Given(numDigitsAfterDecimal).Value;
        if (places < -1)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"NumDigitsAfterDecimal is -1 (the regional settings') or more, not {places}");
        }

        if (places >= LongestString)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.OutOfStringSpace, member, $"{places} places are more than a String can hold");
        }

        if (!TryTriState(includeLeadingDigit, member, "IncludeLeadingDigit", out var leadingDigit, out error)
            || !TryTriState(useParensForNegativeNumbers, member, "UseParensForNegativeNumbers", out var parentheses, out error)
            || !TryTriState(groupDigits, member, "GroupDigits", out var grouped, out error))
        {
            return Fail<VBStringValue>(error);
        }

        TryFormatSettings(member, VBDayOfWeek.VBSunday, VBFirstWeekOfYear.VBFirstJan1, out var settings, out _);
        if (!number.IsFinite)
        {
            return TryReadString(number.ToTypedValue(), member, "Expression", out var text, out error) ? Succeed(text!) : Fail<VBStringValue>(error);
        }

        var output = new TextBuilder(stackalloc char[FormattedCapacity]);
        try
        {
            NumberFormatting.WriteStyled(ref output, number, style, places, leadingDigit, parentheses, grouped, settings);
            return Succeed(new VBStringValue(output.ToString()));
        }
        catch (OutOfMemoryException)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.OutOfStringSpace, member, "the result is more than a String can hold");
        }
        finally
        {
            output.Dispose();
        }
    }

    private static RuntimeSemanticsEvaluationResult<VBStringValue> Written(string member, FormatFailure? failure, ReadOnlySpan<char> written)
        => failure is null
            ? Succeed(new VBStringValue(written.ToString()))
            : Fail<VBStringValue>(Failure(failure.Error, member, failure.Rule));

    // Format's Expression, read as itself: a value of a type a format writes, and a Byte() as its String, as a String parameter would
    // read it. Nothing is 91, and anything else - an object, an Error, Missing, another array, a record - has no String form, 13.
    private bool TryReadFormattable(VBVariantValue expression, string member, out VBTypedValue value, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        if (!TryReadValue(expression, member, "Expression", out value, out error))
        {
            return false;
        }

        if (value is VBNullValue or VBEmptyValue or VBBooleanValue or VBNumericTypedValue or VBDateValue or VBStringValue)
        {
            return true;
        }

        if (!TryReadString(value, member, "Expression", out var text, out error))
        {
            return false;
        }

        value = text!;
        return true;
    }

    // the number FormatNumber's family formats: a number, a Date, a Boolean and Empty as the number they are, and a String as a Double
    // parameter's Let-coercion reads it, in the regional settings; Null is 94, Nothing 91, anything else 13.
    private bool TryReadNumber(VBVariantValue expression, string member, out NumberValue number, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        number = default;
        if (!TryReadValue(expression, member, "Expression", out var value, out error))
        {
            return false;
        }

        if (value is VBNumericTypedValue or VBDateValue or VBBooleanValue or VBEmptyValue)
        {
            number = FormatExpression.NumberOf(value);
            return true;
        }

        var converted = ValueConversions.ToNumeric(value, VBDoubleType.TypeInfo, session.Environment.Regional);
        if (converted.Value is VBDoubleValue read)
        {
            number = NumberValue.Floating(read.Value, VBDoubleType.SignificantIntegerDigits);
            return true;
        }

        error = converted.Error switch
        {
            VBRuntimeErrorId.InvalidUseOfNull => Failure(VBRuntimeErrorId.InvalidUseOfNull, member, "Expression is Null"),
            VBRuntimeErrorId.Overflow => Failure(VBRuntimeErrorId.Overflow, member, "Expression is outside the range of a Double"),
            _ => Failure(VBRuntimeErrorId.TypeMismatch, member, $"Expression cannot be a value of type {value.TypeInfo.Name} that is no number"),
        };
        return false;
    }

    // vbTrue, vbFalse, or vbUseDefault for what the regional settings say (null); anything else is 5.
    private static bool TryTriState(VBTriState tristate, string member, string parameter, out bool? setting, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        (setting, error) = tristate switch
        {
            VBTriState.VBTrue => (true, null),
            VBTriState.VBFalse => (false, null),
            VBTriState.VBUseDefault => ((bool?)null, (IVBRaisableError?)null),
            _ => (null, Failure(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"{parameter} is vbTrue (-1), vbFalse (0) or vbUseDefault (-2), not {(int)tristate}")),
        };
        return error is null;
    }

    // what a format is written in: the session's culture and calendar, the regional settings a String is read by, and the week's
    // rules - vbUseSystem being the culture's own. A first day of the week or a first week of the year outside its enumeration is 5.
    private bool TryFormatSettings(string member, VBDayOfWeek firstDayOfWeek, VBFirstWeekOfYear firstWeekOfYear, out FormatSettings settings, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        var culture = session.Environment.Culture;
        DayOfWeek? weekStart = firstDayOfWeek switch
        {
            VBDayOfWeek.VBUseSystemDayOfWeek => culture.DateTimeFormat.FirstDayOfWeek,
            >= VBDayOfWeek.VBSunday and <= VBDayOfWeek.VBSaturday => (DayOfWeek)((int)firstDayOfWeek - 1),
            _ => null,
        };

        CalendarWeekRule? rule = firstWeekOfYear switch
        {
            VBFirstWeekOfYear.VBUseSystem => culture.DateTimeFormat.CalendarWeekRule,
            VBFirstWeekOfYear.VBFirstJan1 => CalendarWeekRule.FirstDay,
            VBFirstWeekOfYear.VBFirstFourDays => CalendarWeekRule.FirstFourDayWeek,
            VBFirstWeekOfYear.VBFirstFullWeek => CalendarWeekRule.FirstFullWeek,
            _ => null,
        };

        settings = default;
        error = weekStart is null
            ? Failure(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"FirstDayOfWeek is vbUseSystemDayOfWeek (0) or vbSunday (1) to vbSaturday (7), not {(int)firstDayOfWeek}")
            : rule is null
                ? Failure(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"FirstWeekOfYear is vbUseSystem (0) to vbFirstFullWeek (3), not {(int)firstWeekOfYear}")
                : null;
        if (error is not null)
        {
            return false;
        }

        settings = new FormatSettings(culture, CalendarFormats.Of(culture, session.Clock.Calendar), session.Environment.Regional, weekStart!.Value, rule!.Value);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Without <paramref name="arg3"/>, "Arg1 is used as the string to be searched, and Arg2 is used as the pattern (and the start
    /// position is 1)"; with it, Arg1 is the start, Arg2 the String to search and Arg3 the pattern, as the page's table has it where its
    /// prose says "Arg1 is used as a string". 👉 <paramref name="arg3"/> is left out when it arrives as <c>null</c> (omitted, or a
    /// caller's Missing passed on) and given otherwise, so an explicit Empty is a zero-length pattern. A start left out before a given
    /// <paramref name="arg3"/> is 1, "if omitted, search begins at the first character position"; a String to search or a pattern left
    /// out is error 449, as any required argument left out is.
    /// </para>
    /// <para>
    /// The start is Let-coerced as a <c>Long</c> parameter would be ("if start contains the data value Null, an error occurs") and the
    /// Strings as <c>String</c> parameters, in that order; a Null String makes the result Null before the start is checked. Then a
    /// zero-length String to search is 0, a zero-length pattern is the start, and a pattern not found is 0. 👉 The table's "Arg1 &gt;
    /// Arg3" row is read as a start past the end of the String to search, or more than one past it for a zero-length pattern, so
    /// <c>InStr(4, "abc", "")</c> is 4; a start below 1 is error 5, as it is to the <c>Mid</c> statement (<strong>MS-VBAL §5.4.3.5</strong>).
    /// </para>
    /// <para>
    /// A text comparison searches the letters of both, folded to one case and with <c>æ</c>, <c>œ</c>, <c>ß</c> and <c>þ</c> expanded, as MS-VBA does
    /// (<see cref="StringComparisonRules.TryFind"/>): a match may be part of an expanded character, and is found at the character it starts in. 👉 An explicit
    /// Compare is <c>vbBinaryCompare</c>, <c>vbTextCompare</c>, or the identifier of a locale, which is that locale's text comparison
    /// (<see cref="StringComparisonRules.TryForLocale"/>), as MS-VBA takes it and the table mentions; anything else, <c>vbDatabaseCompare</c> included, is
    /// error 5.
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> InStr(VBVariantValue? arg1 = default, VBVariantValue? arg2 = default, VBVariantValue? arg3 = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare)
        => Search(nameof(InStr), arg1, arg2, arg3, compare, inBytes: false);

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="InStr"/> in bytes: "instead of returning the character position of the first occurrence of one string within
    /// another, InStrB returns the byte position", so the start and the result are 1-based positions in the String's bytes, a trailing
    /// one included, and a match may begin inside a character. It compares the bytes whatever Compare is: Compare is let-coerced to a <c>Long</c> as every
    /// argument is (Null is error 94, a String that is no number error 13), and its value says nothing - <c>vbTextCompare</c>, a locale, <c>Option Compare
    /// Text</c> and a value no other member takes all search the bytes, as MS-VBA's <c>InStrB</c> does. MS-VBAL's table says nothing of a Compare that
    /// <c>InStrB</c> does not read.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> InStrB(VBVariantValue? arg1 = default, VBVariantValue? arg2 = default, VBVariantValue? arg3 = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare)
        => Search(nameof(InStrB), arg1, arg2, arg3, compare, inBytes: true);

    // InStr and InStrB: the shape of the call, then the coercions in the order the parameters are declared, then a Null String, then
    // the start's check and Compare's.
    private RuntimeSemanticsEvaluationResult<VBVariantValue> Search(string member, VBVariantValue? arg1, VBVariantValue? arg2, VBVariantValue? arg3, VBCompareMethod compare, bool inBytes)
    {
        var start = arg3 is null ? null : arg1;
        var searched = arg3 is null ? arg1 : arg2;
        var sought = arg3 ?? arg2;
        if (searched is null || sought is null)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.ArgumentNotOptional, member, "the String to search and the pattern are required");
        }

        var from = 1;
        IVBRaisableError? error;
        if (start is not null && !TryReadLong(start, member, "Start", out from, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (!TryReadString(searched, member, "the String to search", out var text, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (!TryReadString(sought, member, "the pattern", out var pattern, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (text is null || pattern is null)
        {
            return InVariant(VBNullValue.Null);
        }

        if (from < 1)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"Start is at least 1, not {from}");
        }

        // InStrB compares bytes whatever Compare is, as MS-VBA's does: a text comparison, a locale's or a value that is neither, an Option Compare Text too.
        if (inBytes)
        {
            return InVariant(new VBLongValue(BytePosition(text, pattern, from)));
        }

        if (!TryRulesFor(compare, member, out var rules, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        return InVariant(new VBLongValue(CharacterPosition(Text(text), Text(pattern), from, rules)));
    }

    // the 1-based position of the first run of characters from the start that is equal to the pattern, or 0. A zero-length String has
    // none, and a zero-length pattern is found at the start, up to one past the end.
    private static int CharacterPosition(string text, string pattern, int from, StringComparisonRules rules)
        => text.Length > 0 && from <= text.Length + 1 && rules.TryFind(text.AsSpan(from - 1), pattern, out var index, out _) ? from + index : 0;

    // the byte position of the first run of bytes from the start that is the pattern's, or 0.
    private static int BytePosition(VBStringValue text, VBStringValue pattern, int from)
    {
        var bytes = text.GetBytes();
        var sought = pattern.GetBytes();
        if (bytes.Length == 0 || from > bytes.Length + 1)
        {
            return 0;
        }

        if (sought.Length == 0)
        {
            return from;
        }

        var index = bytes.AsSpan(from - 1).IndexOf(sought);
        return index < 0 ? 0 : from + index;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The position of the last run of StringCheck that ends by Start and is equal to StringMatch, or 0; runs may overlap, so
    /// <c>InStrRev("aaa", "aa")</c> is 2. A zero-length StringCheck is 0, a zero-length StringMatch is Start, and "Start &gt;
    /// Len(StringMatch)" is read as a Start past the end of StringCheck, which is 0. The table's Null rows cannot be reached: both
    /// Strings are declared <c>As String</c>, so a Null one is error 94 at the call site.
    /// </para>
    /// <para>
    /// 👉 Start left out is the declared -1, the end of StringCheck, which the table calls "the data value 1"; a Start of 0 or below -1
    /// is error 5, as a Start below 1 is to the <c>Mid</c> statement (<strong>MS-VBAL §5.4.3.5</strong>). Compare left out is the
    /// calling module's <c>Option Compare</c>, as its runtime semantics say and its table does not, and an explicit one is
    /// <c>vbBinaryCompare</c>, <c>vbTextCompare</c>, <c>vbDatabaseCompare</c> (2, which compares as <c>Option Compare Database</c> does) or the identifier
    /// of a locale (<see cref="StringComparisonRules.TryForLocale"/>), else error 5, as MS-VBA's.
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBLongValue> InStrRev(VBStringValue stringCheck, VBStringValue stringMatch, VBLongValue? start = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare)
    {
        var from = Given(start).Value;
        if (from is 0 or < -1)
        {
            return Fail<VBLongValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(InStrRev), $"Start is -1 or at least 1, not {from}");
        }

        if (!TryRulesFor(compare, nameof(InStrRev), out var rules, out var error, takesDatabaseCompare: true))
        {
            return Fail<VBLongValue>(error);
        }

        var text = Text(stringCheck);
        var pattern = Text(stringMatch);
        var end = from == -1 ? text.Length : from;
        int position;
        if (text.Length == 0 || end > text.Length)
        {
            position = 0;
        }
        else if (pattern.Length == 0)
        {
            position = end;
        }
        else
        {
            position = rules.TryFindLast(text.AsSpan(0, end), pattern, out var index, out _) ? index + 1 : 0;
        }

        return Succeed(new VBLongValue(position));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "A string created by joining a number of substrings contained in an array", in index order from its lower bound, whatever
    /// that is. The Delimiter, "if omitted, the space character", and "if Delimiter is a zero-length string (""), all items in the
    /// list are concatenated with no delimiters". An array with no elements joins to a zero-length string, and so does one with no
    /// dimensions yet - a dynamic array never <c>ReDim</c>'d (recorded in Excel, oracle\s41 M01, M02).
    /// <para>
    /// 👉 The Delimiter and each element are read as a <c>String</c> parameter would Let-coerce them: Empty is a zero-length string,
    /// a number is written in the environment's regional settings, Null is error 94, and an Error, an object or an array that is not
    /// a <c>Byte()</c> is error 13; so is a SourceArray that is not a one-dimensional array, as <see cref="Filter"/>'s is. An element
    /// never assigned in a <c>Variant()</c> is Empty. The elements are read as characters, so an element's trailing byte is dropped.
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> Join(VBVariantValue sourceArray, VBVariantValue? delimiter = default)
    {
        if (!TryReadArray(sourceArray, nameof(Join), out var array, out var error))
        {
            return Fail<VBStringValue>(error);
        }

        if (!TryReadDelimiter(delimiter, nameof(Join), out var separator, out error))
        {
            return Fail<VBStringValue>(error);
        }

        if (array.Rank == 0)
        {
            return Succeed(new VBStringValue(string.Empty));
        }

        if (!TryReadElements(array, nameof(Join), out var elements, out error))
        {
            return Fail<VBStringValue>(error);
        }

        return Succeed(new VBStringValue(string.Join(separator, elements.Select(Text))));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "Only uppercase letters are converted to lowercase; all lowercase letters and non-letter characters remain unchanged", by
    /// the casing of the environment's regional settings (a Turkish <c>I</c> becomes a dotless <c>i</c>), one character for one.
    /// "If String contains the data value Null, Null is returned."
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> LCase(VBVariantValue @string) => Cased(nameof(LCase), @string, upper: false);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> LCaseString(VBVariantValue @string) => Twin("LCase$", LCase(@string));

    // LCase and UCase: TextInfo maps each character to one character, so "ß" stays "ß" and the length never changes.
    private RuntimeSemanticsEvaluationResult<VBVariantValue> Cased(string member, VBVariantValue @string, bool upper)
    {
        if (!TryReadString(@string, member, "String", out var text, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (text is null)
        {
            return InVariant(VBNullValue.Null);
        }

        var casing = session.Environment.Culture.TextInfo;
        return InVariant(upper ? casing.ToUpper(Text(text)) : casing.ToLower(Text(text)));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The first Length characters, "the entire string" when Length is at least its length, a zero-length string for 0; "if string
    /// contains Null, Null is returned", whatever Length is. A character is a UTF-16 code unit, so a surrogate pair can be split.
    /// 👉 A negative Length is error 5, as a negative length is to the <c>Mid</c> statement (<strong>MS-VBAL §5.4.3.5</strong>).
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Left(VBVariantValue @string, VBLongValue length) => Take(nameof(Left), @string, length, fromEnd: false, inBytes: false);

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="Left"/> in bytes: "instead of specifying the number of characters to return, length specifies the number of
    /// bytes", so an odd Length gives a String that ends in a byte.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> LeftB(VBVariantValue @string, VBLongValue length) => Take(nameof(LeftB), @string, length, fromEnd: false, inBytes: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> LeftString(VBVariantValue @string, VBLongValue length) => Twin("Left$", Left(@string, length));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> LeftBString(VBVariantValue @string, VBLongValue length) => Twin("LeftB$", LeftB(@string, length));

    // Left, Right and their byte forms: the String's coercion, then its Null, then Length's check.
    private RuntimeSemanticsEvaluationResult<VBVariantValue> Take(string member, VBVariantValue @string, VBLongValue length, bool fromEnd, bool inBytes)
    {
        if (!TryReadString(@string, member, "String", out var text, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (text is null)
        {
            return InVariant(VBNullValue.Null);
        }

        var count = length.Value;
        if (count < 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"Length cannot be negative, not {count}");
        }

        if (inBytes)
        {
            var bytes = text.GetBytes();
            var taken = Math.Min(count, bytes.Length);
            return InVariant(VBStringValue.FromBytes(fromEnd ? bytes.AsSpan(bytes.Length - taken) : bytes.AsSpan(0, taken)));
        }

        var characters = Text(text);
        var kept = Math.Min(count, characters.Length);
        return InVariant(fromEnd ? characters[^kept..] : characters[..kept]);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "A copy of a specified string without leading spaces": spaces are U+0020, and nothing else is removed. "If String contains
    /// the data value Null, Null is returned."
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> LTrim(VBVariantValue @string) => Trimmed(nameof(LTrim), @string, leading: true, trailing: false);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> LTrimString(VBVariantValue @string) => Twin("LTrim$", LTrim(@string));

    /// <inheritdoc/>
    /// <remarks>
    /// "A copy of a specified string without [...] trailing spaces": spaces are U+0020, and nothing else is removed. "If String
    /// contains the data value Null, Null is returned."
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> RTrim(VBVariantValue @string) => Trimmed(nameof(RTrim), @string, leading: false, trailing: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> RTrimString(VBVariantValue @string) => Twin("RTrim$", RTrim(@string));

    /// <inheritdoc/>
    /// <remarks>
    /// "A copy of a specified string without [...] both leading and trailing spaces": spaces are U+0020, so a tab, a line break, a
    /// no-break space or an ideographic space stays. "If String contains the data value Null, Null is returned."
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Trim(VBVariantValue @string) => Trimmed(nameof(Trim), @string, leading: true, trailing: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> TrimString(VBVariantValue @string) => Twin("Trim$", Trim(@string));

    private RuntimeSemanticsEvaluationResult<VBVariantValue> Trimmed(string member, VBVariantValue @string, bool leading, bool trailing)
    {
        if (!TryReadString(@string, member, "String", out var text, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (text is null)
        {
            return InVariant(VBNullValue.Null);
        }

        var characters = Text(text);
        return InVariant(leading && trailing
            ? characters.Trim(SpaceCharacter)
            : leading ? characters.TrimStart(SpaceCharacter) : characters.TrimEnd(SpaceCharacter));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Length characters from Start: "if omitted or if there are fewer than Length characters in the text (including the character
    /// at start), all characters from the start position to the end of the string are returned", and "if Start is greater than the
    /// number of characters in String, Mid returns a zero-length string". "If String contains the data value Null, Null is
    /// returned." Length is a <c>Variant</c>, Let-coerced here as a <c>Long</c> parameter would be, after the String and before
    /// its Null. 👉 A Start below 1 and a negative Length are error 5, past the end or not, as they are to the <c>Mid</c>
    /// statement (<strong>MS-VBAL §5.4.3.5</strong>).
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Mid(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default) => Middle(nameof(Mid), @string, start, length, inBytes: false);

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="Mid"/> in bytes: "the arguments specify numbers of bytes", so a slice can begin or end inside a character.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> MidB(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default) => Middle(nameof(MidB), @string, start, length, inBytes: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> MidString(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default) => Twin("Mid$", Mid(@string, start, length));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> MidBString(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default) => Twin("MidB$", MidB(@string, start, length));

    private RuntimeSemanticsEvaluationResult<VBVariantValue> Middle(string member, VBVariantValue @string, VBLongValue start, VBVariantValue? length, bool inBytes)
    {
        // the coercions in the order the parameters are declared: String, then Length (the call site coerced Start, a Long).
        if (!TryReadString(@string, member, "String", out var text, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        int? count = null;
        if (length is not null)
        {
            if (!TryReadLong(length, member, "Length", out var given, out error))
            {
                return Fail<VBVariantValue>(error);
            }

            count = given;
        }

        if (text is null)
        {
            return InVariant(VBNullValue.Null);
        }

        var from = start.Value;
        if (from < 1)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"Start is at least 1, not {from}");
        }

        if (count < 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"Length cannot be negative, not {count}");
        }

        if (inBytes)
        {
            var bytes = text.GetBytes();
            var restOfBytes = from > bytes.Length ? ReadOnlySpan<byte>.Empty : bytes.AsSpan(from - 1);
            return InVariant(VBStringValue.FromBytes(restOfBytes[..Math.Min(count ?? restOfBytes.Length, restOfBytes.Length)]));
        }

        var characters = Text(text).AsSpan();
        var rest = from > characters.Length ? ReadOnlySpan<char>.Empty : characters[(from - 1)..];
        return InVariant(rest[..Math.Min(count ?? rest.Length, rest.Length)].ToString());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "A String indicating the specified month": its name in the regional settings of the session's environment, and in the
    /// session's calendar, as <c>Format</c>'s <c>mmmm</c> and <c>mmm</c> write it, so under the Hijri calendar the 9th month is
    /// Ramadan, whose name has no abbreviation. 👉 A Month other than 1 to 12 is error 5.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> MonthName(VBLongValue month, VBBooleanValue? abbreviate = default)
    {
        var number = month.Value;
        if (number is < 1 or > 12)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(MonthName), $"Month is 1 to 12, not {number}");
        }

        var dates = CalendarFormats.Of(session.Environment.Culture, session.Clock.Calendar);
        return Succeed(new VBStringValue((bool)Given(abbreviate).Value && dates.Calendar is not HijriCalendar
            ? dates.GetAbbreviatedMonthName(number)
            : dates.GetMonthName(number)));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "A String, with substitutions made, that begins at the position specified by Start and concludes at the end of the Expression
    /// string. It is not a copy of the original string from start to finish." A zero-length Expression, or a Start past its end, is
    /// a zero-length string; a zero-length Find or a Count of 0 is the String from Start on, unchanged; a zero-length Replace removes
    /// what is found. Matches are found from left to right as <c>InStr</c> finds them, never overlapping, and what a replacement
    /// puts in is not searched again. A Null Expression is error 94 at the call site ("Expression is Null - An error").
    /// <para>
    /// 👉 A Start below 1 and a Count below -1 are error 5, as a Start below 1 is to the <c>Mid</c> statement
    /// (<strong>MS-VBAL §5.4.3.5</strong>), and they are checked first, so <c>Replace("", "a", "b", 0)</c> is 5 too.
    /// </para>
    /// <para>
    /// 👉 The table gives "Copy of Expression" for a zero-length Find and for a Count of 0. It is read through the prose's "begins at
    /// the position specified by Start", so with a Start above 1 those are the String from Start: <c>Replace("abcabc", "", "X", 3)</c>
    /// is "cabc".
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> Replace(VBStringValue expression, VBStringValue find, VBStringValue replace, VBLongValue? start = default, VBLongValue? count = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare)
    {
        var from = Given(start).Value;
        if (from < 1)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(Replace), $"Start is at least 1, not {from}");
        }

        var most = Given(count).Value;
        if (most < -1)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(Replace), $"Count is -1 (every match) or more, not {most}");
        }

        if (!TryRulesFor(compare, nameof(Replace), out var rules, out var error, takesDatabaseCompare: true))
        {
            return Fail<VBStringValue>(error);
        }

        var characters = Text(expression);
        var rest = from > characters.Length ? string.Empty : characters[(from - 1)..];
        var pattern = Text(find);
        if (pattern.Length == 0 || most == 0)
        {
            return Succeed(new VBStringValue(rest));
        }

        var matches = rules.FindAll(rest, pattern);
        var replaced = new StringBuilder(rest.Length);
        var copied = 0;
        foreach (var (index, length) in most == -1 ? matches : matches.Take(most))
        {
            replaced.Append(rest, copied, index - copied).Append(Text(replace));
            copied = index + length;
        }

        return Succeed(new VBStringValue(replaced.Append(rest, copied, rest.Length - copied).ToString()));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The last Length characters, the mirror of <see cref="Left"/>: "the entire string" when Length is at least its length, a
    /// zero-length string for 0, Null for a Null String whatever Length is, and error 5 for a negative Length.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Right(VBVariantValue @string, VBLongValue length) => Take(nameof(Right), @string, length, fromEnd: true, inBytes: false);

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="Right"/> in bytes: the last Length bytes, so an odd Length gives a String that begins inside a character.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> RightB(VBVariantValue @string, VBLongValue length) => Take(nameof(RightB), @string, length, fromEnd: true, inBytes: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> RightString(VBVariantValue @string, VBLongValue length) => Twin("Right$", Right(@string, length));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> RightBString(VBVariantValue @string, VBLongValue length) => Twin("RightB$", RightB(@string, length));

    /// <inheritdoc/>
    /// <remarks>
    /// "A String consisting of the specified number of spaces." 👉 A negative Number is error 5, as a negative length is to the
    /// <c>Mid</c> statement (<strong>MS-VBAL §5.4.3.5</strong>), and more spaces than a String can hold is error 14.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Space(VBLongValue number)
    {
        var count = number.Value;
        if (count < 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(Space), $"Number cannot be negative, not {count}");
        }

        return TryRepeat(SpaceCharacter, count, nameof(Space), out var spaces, out var error) ? InVariant(spaces) : Fail<VBVariantValue>(error);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> SpaceString(VBLongValue number) => Twin("Space$", Space(number));

    /// <inheritdoc/>
    /// <remarks>
    /// "A zero-based, one-dimensional array containing a specified number of substrings", zero-based whatever the calling module's
    /// <c>Option Base</c>, of <c>String</c> elements. "If expression is a zero-length string (""), Split returns an empty array, that
    /// is, an array with no elements and no data"; the Delimiter, "if omitted, the space character", and "if delimiter is a
    /// zero-length string, a single-element array containing the entire expression string is returned". Every delimiter found
    /// ends an element, so two together make a zero-length one between them; delimiters are found from left to right as
    /// <c>InStr</c> finds them, never overlapping. With a Limit of n, the n-th element is the rest of the String, delimiters and all.
    /// <para>
    /// 👉 The Delimiter is read as a <c>String</c> parameter would Let-coerce it: Empty is a zero-length string, Null is error 94.
    /// A Limit of 0 is an array with no elements, and a Limit below -1 is error 5, as a negative length is to the <c>Mid</c>
    /// statement (<strong>MS-VBAL §5.4.3.5</strong>).
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Split(VBStringValue expression, VBVariantValue? delimiter = default, VBLongValue? limit = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare)
    {
        if (!TryReadDelimiter(delimiter, nameof(Split), out var separator, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        var most = Given(limit).Value;
        if (most < -1)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(Split), $"Limit is -1 (every substring) or more, not {most}");
        }

        if (!TryRulesFor(compare, nameof(Split), out var rules, out error, takesDatabaseCompare: true))
        {
            return Fail<VBVariantValue>(error);
        }

        var characters = Text(expression);
        if (characters.Length == 0 || most == 0)
        {
            return InArray([]);
        }

        var parts = new List<VBStringValue>();
        var from = 0;
        foreach (var (index, length) in rules.FindAll(characters, separator))
        {
            if (parts.Count == most - 1)
            {
                break;
            }

            parts.Add(new VBStringValue(characters[from..index]));
            from = index + length;
        }

        parts.Add(new VBStringValue(characters[from..]));
        return InArray(parts);
    }

    // Join's and Split's Delimiter: a space when it is left out, else its String; a Null one is 94, as it would be to a String parameter.
    private bool TryReadDelimiter(VBVariantValue? delimiter, string member, out string separator, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        separator = SpaceCharacter.ToString();
        error = null;
        if (delimiter is null)
        {
            return true;
        }

        if (!TryReadString(delimiter, member, "Delimiter", out var text, out error))
        {
            return false;
        }

        if (text is null)
        {
            error = Failure(VBRuntimeErrorId.InvalidUseOfNull, member, "Delimiter is Null");
            return false;
        }

        separator = Text(text);
        return true;
    }

    // Join's and Filter's SourceArray, declared SourceArray() and a Variant here only so that a String() reaches the member: a
    // one-dimensional array of any element type, fixed or resizable, or one with no dimensions yet (oracle\s41 M01-M03), and anything
    // else 13, as the declaration would raise.
    private static bool TryReadArray(VBVariantValue sourceArray, string member, [NotNullWhen(true)] out VBArrayValue? array, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        var value = Unwrapped(sourceArray);
        array = value as VBArrayValue;
        error = value switch
        {
            VBArrayValue { Rank: 0 or 1 } => null,
            VBArrayValue { Rank: var rank } => Failure(VBRuntimeErrorId.TypeMismatch, member, $"SourceArray is a one-dimensional array, not one of {rank} dimensions"),
            _ => Failure(VBRuntimeErrorId.TypeMismatch, member, $"SourceArray is a one-dimensional array, not a value of type {value.TypeInfo.Name}"),
        };
        return error is null;
    }

    // an array's elements in index order (MS-VBAL 5.4.2.4.1), each read as a String parameter would Let-coerce it: Null is 94, and
    // Nothing 91, an element of an Object() never assigned included. A cell with no value to read is, in a Variant(), Empty, a
    // zero-length string; in an array of any other type (a record's) it is no value with a String form, 13.
    private bool TryReadElements(VBArrayValue array, string member, [NotNullWhen(true)] out List<VBStringValue>? elements, [NotNullWhen(false)] out IVBRaisableError? error)
    {
        var lower = array.Dimensions[0].LowerBound;
        elements = new List<VBStringValue>(array.Length);
        for (var index = 0; index < array.Length; index++)
        {
            var parameter = $"element {lower + index} of SourceArray";
            if (array.GetElementHandle(lower + index) is not { } cell || !cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue))
            {
                if (array.ItemType is not VBVariantType)
                {
                    error = Failure(VBRuntimeErrorId.TypeMismatch, member, $"{parameter} cannot be a value of type {array.ItemType.Name}");
                    elements = null;
                    return false;
                }

                elements.Add(VBStringValue.ZeroLengthString);
                continue;
            }

            if (!TryReadString(array.ElementAt(index)!, member, parameter, out var text, out error))
            {
                elements = null;
                return false;
            }

            if (text is null)
            {
                error = Failure(VBRuntimeErrorId.InvalidUseOfNull, member, $"{parameter} is Null");
                elements = null;
                return false;
            }

            elements.Add(text);
        }

        error = null;
        return true;
    }

    // what Split and Filter return: a String() from 0, whatever the calling module's Option Base, which is (0 To -1) when it has no
    // elements - an array with one dimension and nothing in it, which For Each walks no times, rather than one with no dimensions.
    private static RuntimeSemanticsEvaluationResult<VBVariantValue> InArray(IReadOnlyList<VBStringValue> elements)
    {
        var array = new VBResizableArrayValue([(0, elements.Count - 1)], VBStringType.TypeInfo);
        for (var index = 0; index < elements.Count; index++)
        {
            array.TrySetElement(BindingKnowledge.NewCell(elements[index]), index);
        }

        return InVariant(array);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// -1, 0 or 1 as String1 "is less than", equal to or "greater than" String2, as an Integer; "String1 or String2 is Null" is Null,
    /// whatever Compare is. Each argument is Let-coerced as a <c>String</c> parameter would be, and the two are compared the way the
    /// relational operators compare them (<strong>MS-VBAL §5.6.9.5</strong>), a trailing byte included, so <c>StrComp(a, b)</c> is 0
    /// exactly when <c>a = b</c> under the <c>Option Compare</c> that Compare names. An explicit Compare is <c>vbBinaryCompare</c>,
    /// <c>vbTextCompare</c> or the identifier of a locale, which orders the strings by that locale's collation
    /// (<see cref="StringComparisonRules.TryForLocale"/>); anything else, <c>vbDatabaseCompare</c> included, is error 5, as MS-VBA's.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> StrComp(VBVariantValue string1, VBVariantValue string2, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare)
    {
        if (!TryReadString(string1, nameof(StrComp), "String1", out var left, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (!TryReadString(string2, nameof(StrComp), "String2", out var right, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (left is null || right is null)
        {
            return InVariant(VBNullValue.Null);
        }

        if (!TryRulesFor(compare, nameof(StrComp), out var rules, out error))
        {
            return Fail<VBVariantValue>(error);
        }

        return InVariant(new VBIntegerValue((short)Math.Sign(rules.Compare(left, right))));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "Returns a String converted as specified", Conversion being "the sum of values specifying the type of conversion to
    /// perform", of which "most can be combined [...] except when they are mutually exclusive". <c>vbUpperCase</c> and
    /// <c>vbLowerCase</c> case as <see cref="UCase"/> and <see cref="LCase"/> do, by the regional settings of LocaleID, or of the
    /// environment when it is left out or 0. <c>vbProperCase</c> "converts the first letter of every word in string to
    /// uppercase", a word beginning where the String does and after each of "the valid word separators for proper casing":
    /// <c>Chr$(0)</c>, <c>Chr$(9)</c> to <c>Chr$(13)</c> and <c>Chr$(32)</c>. <c>vbUnicode</c> reads the String's bytes, a
    /// trailing one included, as text in "the default code page of the system", which is the environment's ANSI code page, and
    /// <c>vbFromUnicode</c> writes its characters as that code page's bytes, a character the code page lacks being what its
    /// encoder maps it to; LocaleID does not change the code page. A Null String is Null.
    /// <para>
    /// 👉 As MS-VBA does, every other conversion reads the String in the ANSI code page of LocaleID's locale (the environment's when
    /// LocaleID is left out or 0, or when the locale has none), and writes it back from it: a character the code page lacks is the
    /// one its encoder maps it to (<c>α</c> is <c>a</c> in Windows-1252, an ideographic space a space) or <c>?</c>, before any
    /// conversion sees it. The casing is not the locale's own: a character is cased only to one that cases back to it, so the
    /// Turkish <c>i</c> is <c>I</c>, and <c>ı</c>, <c>İ</c>, <c>µ</c> and <c>ς</c> stay as they are. <c>vbProperCase</c> also
    /// lowercases the rest of each word, and starts none after punctuation, a no-break space, or an ideographic space outside an
    /// East Asian locale. <c>vbUnicode</c> or <c>vbFromUnicode</c> with any other conversion, both conversions of a pair
    /// (<c>vbWide</c> and <c>vbNarrow</c>, <c>vbKatakana</c> and <c>vbHiragana</c>), a Conversion outside 0 to 255, and a LocaleID
    /// that names no locale are error 5.
    /// </para>
    /// <para>
    /// <c>vbWide</c> and <c>vbNarrow</c> convert between narrow characters and their wide forms, and <c>vbKatakana</c> and
    /// <c>vbHiragana</c> between the two Japanese syllabaries (<see cref="EastAsianText"/>). They "cause run-time errors when used in
    /// locales where they do not apply": the first two apply to "East Asia locales", which are read as the Chinese, Japanese and
    /// Korean ones, and the other two to "Japan only", which is read as the Japanese ones; elsewhere they are error 5. In those
    /// locales "the actual value for a space varies [...] for DBCS": the ideographic space separates words for <c>vbProperCase</c>
    /// as a space does.
    /// </para>
    /// <para>
    /// 👉 The conversions are made in one order, whatever their sum: the case, <c>vbWide</c>, <c>vbKatakana</c> or
    /// <c>vbHiragana</c>, then <c>vbNarrow</c>; so a halfwidth katakana widened is a katakana the syllabary conversion sees, and a
    /// hiragana made a katakana can be narrowed.
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> StrConv(VBVariantValue @string, VBStrConv conversion, VBLongValue? localeID = default)
    {
        if (!TryReadString(@string, nameof(StrConv), "String", out var text, out var error))
        {
            return Fail<VBVariantValue>(error);
        }

        if (text is null)
        {
            return InVariant(VBNullValue.Null);
        }

        // then the values, in the order they are declared: Conversion, then LocaleID.
        var settings = (int)conversion;
        if (settings is < 0 or > 0xFF)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(StrConv), $"Conversion is a sum of VbStrConv values, 0 to 255, not {settings}");
        }

        foreach (var (pair, names) in ExclusiveConversions)
        {
            if ((settings & (int)pair) == (int)pair)
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(StrConv), $"Conversion cannot combine {names}");
            }
        }

        const int byteConversions = (int)(VBStrConv.VBUnicode | VBStrConv.VBFromUnicode);
        if ((settings & byteConversions) != 0 && (settings & ~byteConversions) != 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(StrConv), "Conversion cannot combine vbUnicode or vbFromUnicode with another conversion");
        }

        // LocaleID has no default: left out, it is null, and 0 is the environment's locale too.
        var culture = session.Environment.Culture;
        if (localeID is { Value: not 0 and var lcid })
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(lcid);
            }
            catch (Exception exception) when (exception is CultureNotFoundException or ArgumentOutOfRangeException)
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(StrConv), $"LocaleID {lcid} names no locale");
            }
        }

        var language = culture.TwoLetterISOLanguageName;
        var isEastAsian = language is "ja" or "zh" or "ko";
        if ((settings & (int)(VBStrConv.VBWide | VBStrConv.VBNarrow)) != 0 && !isEastAsian)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(StrConv), $"vbWide and vbNarrow apply to East Asian locales, not {culture.Name}");
        }

        if ((settings & (int)(VBStrConv.VBKatakana | VBStrConv.VBHiragana)) != 0 && language != "ja")
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(StrConv), $"vbKatakana and vbHiragana apply to Japanese locales, not {culture.Name}");
        }

        if (settings == 0)
        {
            // nothing to convert: the String as it was given, a trailing byte and all.
            return InVariant(text);
        }

        var ansi = session.Environment.AnsiEncoding;
        if ((settings & (int)VBStrConv.VBUnicode) != 0)
        {
            return InVariant(ansi.GetString(text.GetBytes()));
        }

        if ((settings & (int)VBStrConv.VBFromUnicode) != 0)
        {
            return InVariant(VBStringValue.FromBytes(ansi.GetBytes(Text(text))));
        }

        // MS-VBA converts in the locale's ANSI code page, so a character it lacks is lost before any conversion sees it.
        var localeCodePage = localeID is { Value: not 0 } ? culture.TextInfo.ANSICodePage : 0;
        var codePage = localeCodePage == 0 ? ansi : CodePagesEncodingProvider.Instance.GetEncoding(localeCodePage) ?? ansi;
        var converted = codePage.GetString(codePage.GetBytes(Text(text)));
        converted = (VBStrConv)(settings & (int)VBStrConv.VBProperCase) switch
        {
            VBStrConv.VBUpperCase => Cased(converted, UpperOf),
            VBStrConv.VBLowerCase => Cased(converted, LowerOf),
            VBStrConv.VBProperCase => ProperCased(converted, isEastAsian),
            _ => converted,
        };

        converted = (settings & (int)VBStrConv.VBWide) != 0 ? EastAsianText.Widen(converted) : converted;
        converted = (settings & (int)VBStrConv.VBKatakana) != 0 ? EastAsianText.ToKatakana(converted)
            : (settings & (int)VBStrConv.VBHiragana) != 0 ? EastAsianText.ToHiragana(converted)
            : converted;
        converted = (settings & (int)VBStrConv.VBNarrow) != 0 ? EastAsianText.Narrowed(converted) : converted;

        return InVariant(codePage.GetString(codePage.GetBytes(converted)));
    }

    // "mutually exclusive, for example, vbUnicode + vbFromUnicode": each pair converts one way and its opposite.
    private static readonly (VBStrConv Pair, string Names)[] ExclusiveConversions =
    [
        (VBStrConv.VBWide | VBStrConv.VBNarrow, "vbWide and vbNarrow"),
        (VBStrConv.VBKatakana | VBStrConv.VBHiragana, "vbKatakana and vbHiragana"),
        (VBStrConv.VBUnicode | VBStrConv.VBFromUnicode, "vbUnicode and vbFromUnicode"),
    ];

    // "the valid word separators for proper casing: Null (Chr$(0)), horizontal tab (Chr$(9)), linefeed (Chr$(10)), vertical tab
    // (Chr$(11)), form feed (Chr$(12)), carriage return (Chr$(13)), space (SBCS) (Chr$(32))".
    private const string WordSeparators = "\0\t\n\v\f\r ";

    // the space of a double-byte character set, which separates words in an East Asian locale.
    private const char IdeographicSpace = '\u3000';

    // vbProperCase: each word's first character up and every other one down, a word beginning where the String does and after
    // a word separator, and nowhere else.
    private static string ProperCased(string text, bool isEastAsian)
    {
        var characters = Cased(text, LowerOf).ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (index == 0 || WordSeparators.Contains(characters[index - 1]) || isEastAsian && characters[index - 1] == IdeographicSpace)
            {
                characters[index] = UpperOf(characters[index]);
            }
        }

        return new string(characters);
    }

    private static string Cased(string text, Func<char, char> casing)
        => string.Create(text.Length, (text, casing), static (characters, state) =>
        {
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = state.casing(state.text[index]);
            }
        });

    // MS-VBA's casing pairs only characters that case back to each other, whatever the locale: the Turkish i is I and I is i, and
    // a character whose case maps elsewhere (ı to I, İ to i, µ to Μ, ς to Σ) stays as it is.
    private static char UpperOf(char character)
    {
        var upper = char.ToUpperInvariant(character);
        return char.ToLowerInvariant(upper) == character ? upper : character;
    }

    private static char LowerOf(char character)
    {
        var lower = char.ToLowerInvariant(character);
        return char.ToUpperInvariant(lower) == character ? lower : character;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "A String containing a repeating character string of the length specified": the first character of a String Character,
    /// or the character a code names, as <see cref="Chr"/> names it - "if Character is a number greater than 255, String
    /// converts the number to a valid character code using the formula: character Mod 256". "If character contains Null, Null is
    /// returned"; a Null Number is error 94 at the call site, Number being a <c>Long</c>, whatever the table says.
    /// <para>
    /// 👉 A Character that is neither a String nor Null is a code, Let-coerced as a <c>Long</c> parameter would be: Empty is 0,
    /// <c>Chr(0)</c>, and True is -1. A zero-length String, a negative code, a code that names no character (a lone lead byte of a
    /// double-byte code page, as <see cref="Chr"/> has it) and a negative Number are error 5, and more characters than a String
    /// can hold error 14.
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Repeat(VBLongValue number, VBVariantValue character)
    {
        const string member = "String";

        // the coercion first: a String is read for its first character, and anything else but Null as a code; an object is its
        // simple data value, read so.
        var code = 0;
        if (!TryReadValue(character, member, "Character", out var value, out var mismatch)
            || value is not (VBStringValue or VBNullValue) && !TryReadLong(value, member, "Character", out code, out mismatch))
        {
            return Fail<VBVariantValue>(mismatch);
        }

        if (value is VBNullValue)
        {
            return InVariant(VBNullValue.Null);
        }

        // then the values, in the order they are declared.
        var count = number.Value;
        if (count < 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"Number cannot be negative, not {count}");
        }

        char repeated;
        if (value is VBStringValue text)
        {
            if (Text(text).Length == 0)
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, "Character is a zero-length string");
            }

            repeated = Text(text)[0];
        }
        else if (code < 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"Character is a character code, which cannot be negative, not {code}");
        }
        else if (!TryCharacterOf(code % 256, out var named))
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, member, $"Character is the code {code}, whose byte {code % 256} names no character in the code page");
        }
        else
        {
            repeated = named[0];
        }

        return TryRepeat(repeated, count, member, out var repeats, out var error) ? InVariant(repeats) : Fail<VBVariantValue>(error);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> RepeatString(VBLongValue number, VBVariantValue character) => Twin("String$", Repeat(number, character));

    /// <inheritdoc/>
    /// <remarks>
    /// "A String in which the character order of a specified String is reversed": its code units, so a surrogate pair comes out
    /// as its two halves swapped, and a combining mark before the letter it followed. A Null Expression is error 94 at the call
    /// site ("If Expression is Null, an error occurs").
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> StrReverse(VBStringValue expression)
    {
        var characters = Text(expression).ToCharArray();
        Array.Reverse(characters);
        return Succeed(new VBStringValue(new string(characters)));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// "Only lowercase letters are converted to uppercase; all uppercase letters and non-letter characters remain unchanged", by
    /// the casing of the environment's regional settings (a Turkish <c>i</c> becomes a dotted <c>I</c>), one character for one,
    /// so "ß" stays "ß". "If String contains the data value Null, Null is returned."
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> UCase(VBVariantValue @string) => Cased(nameof(UCase), @string, upper: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> UCaseString(VBVariantValue @string) => Twin("UCase$", UCase(@string));

    /// <inheritdoc/>
    /// <remarks>
    /// "A String indicating the specified day of the week", Weekday counted from FirstDayOfWeek as 1, as <c>Weekday</c> counts it: its
    /// name in the regional settings of the session's environment, as <c>Format</c>'s <c>dddd</c> and <c>ddd</c> write it.
    /// FirstDayOfWeek left out is the declared <c>vbUseSystemDayOfWeek</c>, the first day of the week of those regional settings.
    /// 👉 A Weekday other than 1 to 7, and a FirstDayOfWeek that is no value of its enumeration, are error 5, in that order.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> WeekdayName(VBLongValue weekday, VBBooleanValue? abbreviate = default, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBUseSystemDayOfWeek)
    {
        var number = weekday.Value;
        if (number is < 1 or > 7)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, nameof(WeekdayName), $"Weekday is 1 to 7, not {number}");
        }

        if (!TryFormatSettings(nameof(WeekdayName), firstDayOfWeek, VBFirstWeekOfYear.VBFirstJan1, out var settings, out var error))
        {
            return Fail<VBStringValue>(error);
        }

        var day = (DayOfWeek)(((int)settings.FirstDayOfWeek + number - 1) % 7);
        return Succeed(new VBStringValue((bool)Given(abbreviate).Value ? settings.Dates.GetAbbreviatedDayName(day) : settings.Dates.GetDayName(day)));
    }

}
