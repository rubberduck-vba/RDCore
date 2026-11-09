using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// What the tests of the <c>Strings</c> module share: a module over a session of a given environment, the arguments a call can
/// be given, what a result holds, and a body run from source with the standard library.
/// </summary>
/// <remarks>
/// A direct call is C# against <see cref="Module"/>, so it sees exactly what the member returns; a call from source goes through
/// the call site's own argument coercion first, which is what decides a typed parameter's Null and Empty.
/// </remarks>
internal static class StdStringsTestHarness
{
    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    /// <summary>
    /// The module, over a session whose environment has <paramref name="lcid"/>'s culture (invariant at 0), <paramref name="codePage"/>'s ANSI encoding,
    /// and <paramref name="regional"/> settings - en-US's unless said otherwise - which a number or a date is written in.
    /// </summary>
    public static StdStrings Module(int lcid = 0, int codePage = 1252, RegionalSettings? regional = null)
        => new(RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, lcid, codePage, SupportsOptionCompareDatabase: false, RegionalSettings: regional), new NoSymbols()));

    /// <summary>
    /// The regional settings of de-DE: <c>,</c> before a fraction, <c>.</c> between groups of digits, and dates written <c>dd.MM.yyyy HH:mm:ss</c>.
    /// </summary>
    public static RegionalSettings German { get; } = new(",", ".", "€", 2049) { ShortDatePattern = "dd.MM.yyyy", LongTimePattern = "HH:mm:ss" };

    #region arguments

    public static VBVariantValue V(string text) => new(new VBStringValue(text));

    public static VBVariantValue V(int number) => new(new VBLongValue(number));

    public static VBVariantValue V(double number) => new(new VBDoubleValue(number));

    public static VBVariantValue V(bool value) => new(new VBBooleanValue(value));

    public static VBVariantValue V(VBTypedValue value) => new(value);

    public static VBVariantValue VNull => new(VBNullValue.Null);

    public static VBVariantValue VEmpty => new(VBEmptyValue.Empty);

    /// <summary>What a required <c>Variant</c> parameter receives for a caller's Missing: the Missing value itself.</summary>
    public static VBVariantValue VMissing => new(VBMissingValue.Missing);

    /// <summary>A <c>String</c> of exactly these bytes, an odd number of them leaving a trailing byte.</summary>
    public static VBStringValue Bytes(params byte[] bytes) => VBStringValue.FromBytes(bytes);

    public static VBLongValue L(int value) => new(value);

    #endregion

    #region results

    /// <summary>The value a successful result holds, out of any <c>Variant</c>.</summary>
    public static VBTypedValue ValueOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
    {
        Assert.IsTrue(result.IsSuccess, $"{result.ErrorInfo?.ErrorId} {result.ErrorInfo?.Verbose}");
        VBTypedValue value = result.Result!;
        while (value is VBVariantValue variant)
        {
            value = variant.TypedValue;
        }

        return value;
    }

    public static VBStringValue StringOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
        => ValueOf(result) as VBStringValue ?? throw new AssertFailedException($"not a String: {ValueOf(result).TypeInfo.Name}");

    public static string TextOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
        => StringOf(result).Value;

    public static byte[] BytesOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
        => StringOf(result).GetBytes();

    public static bool IsNull<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
        => ValueOf(result) is VBNullValue;

    public static short IntegerOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
        => ValueOf(result) is VBIntegerValue number ? number.Value : throw new AssertFailedException($"not an Integer: {ValueOf(result).TypeInfo.Name}");

    public static int LongOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
        => ValueOf(result) is VBLongValue number ? number.Value : throw new AssertFailedException($"not a Long: {ValueOf(result).TypeInfo.Name}");

    /// <summary>Asserts that the result is an error, and gives its number.</summary>
    public static int ErrorOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
    {
        Assert.IsTrue(result.IsError, result.IsSuccess ? $"succeeded with {result.Result}" : "an internal error");
        return result.ErrorInfo!.ErrorId;
    }

    #endregion

    #region arrays

    /// <summary>A <c>Variant</c> holding a <c>String()</c> from 0 of these elements.</summary>
    public static VBVariantValue StringArray(params string[] elements)
    {
        var array = new VBResizableArrayValue([(0, elements.Length - 1)], VBStringType.TypeInfo);
        for (var index = 0; index < elements.Length; index++)
        {
            array.TrySetElement(BindingKnowledge.NewCell(new VBStringValue(elements[index])), index);
        }

        return new(array);
    }

    /// <summary>A <c>Variant</c> holding a <c>Variant()</c> from 0 of these elements, an element never assigned where one is <c>null</c>.</summary>
    public static VBVariantValue VariantArray(params VBTypedValue?[] elements)
    {
        var array = new VBResizableArrayValue([(0, elements.Length - 1)], VBVariantType.TypeInfo);
        for (var index = 0; index < elements.Length; index++)
        {
            if (elements[index] is { } element)
            {
                array.TrySetElement(BindingKnowledge.NewCell(new VBVariantValue(element)), index);
            }
        }

        return new(array);
    }

    /// <summary>A <c>Variant</c> holding a fixed-size <c>String(lower To ...)</c> of these elements.</summary>
    public static VBVariantValue FixedStringArray(int lower, params string[] elements)
    {
        var array = new VBFixedSizeArrayValue([(lower, lower + elements.Length - 1)], VBStringType.TypeInfo);
        for (var index = 0; index < elements.Length; index++)
        {
            array.TrySetElement(BindingKnowledge.NewCell(new VBStringValue(elements[index])), lower + index);
        }

        return new(array);
    }

    /// <summary>The array a successful result holds.</summary>
    public static VBArrayValue ArrayOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
        => ValueOf(result) as VBArrayValue ?? throw new AssertFailedException($"not an array: {ValueOf(result).TypeInfo.Name}");

    /// <summary>The elements of the <c>String()</c> a successful result holds, after asserting that it is one, from 0.</summary>
    public static string[] ElementsOf<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
    {
        var array = ArrayOf(result);
        Assert.AreEqual(VBStringType.TypeInfo, array.ItemType);
        Assert.HasCount(1, array.Dimensions);
        Assert.AreEqual(0, array.Dimensions[0].LowerBound);
        return [.. Enumerable.Range(0, array.Length).Select(index => ((VBStringValue)array.ElementAt(index)!).Value)];
    }

    #endregion

    #region from source

    /// <summary>A module-level variable of the module the body runs in.</summary>
    public static VBModuleFieldVariableMemberSymbol Field(string name, VBType type)
        => new(TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    // every body can read V, a Variant nothing has assigned (Empty), and S, a String nothing has assigned (vbNullString).
    private static Symbol[] Fields(Symbol[] more) => [Field("V", VBVariantType.TypeInfo), Field("S", VBStringType.TypeInfo), .. more];

    private static (RuntimeExecutionOutcome Outcome, IReadOnlyList<string> Lines) Execute(Symbol[] fields, RuntimeSourceHarness.Surroundings surroundings, string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, Fields(fields), output, standardLibrary: true, arrange: null, surroundings, body);
        return (outcome, output.Lines);
    }

    /// <summary>Runs <paramref name="body"/> to its end, and gives what it printed, each line trimmed.</summary>
    /// <remarks>So a result that is or may be whitespace is printed bracketed: <c>"[" &amp; x &amp; "]"</c>.</remarks>
    public static string[] Run(params string[] body) => RunWith([], body);

    /// <inheritdoc cref="Run(string[])"/>
    public static string[] RunWith(Symbol[] fields, params string[] body) => RunIn(new RuntimeSourceHarness.Surroundings(), fields, body);

    /// <inheritdoc cref="Run(string[])"/>
    public static string[] RunIn(RuntimeSourceHarness.Surroundings surroundings, Symbol[] fields, params string[] body)
    {
        var (outcome, lines) = Execute(fields, surroundings, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind,
            $"{outcome.ErrorInfo?.ErrorId} {outcome.ErrorInfo?.Description} | {outcome.ErrorInfo?.Verbose}");
        return [.. lines.Select(line => line.Trim())];
    }

    /// <summary>
    /// Runs <paramref name="body"/> to its end in a module whose <c>Option Compare</c> is <paramref name="mode"/>, or that states none,
    /// and gives what it printed, each line trimmed.
    /// </summary>
    public static string[] RunUnder(OptionCompare? mode, params string[] body) => RunIn(Under(mode), [], body);

    /// <summary>Runs <paramref name="body"/>, asserts that it ends in an error, and gives the error's number.</summary>
    public static int RunToError(params string[] body) => RunToErrorWith([], body);

    /// <inheritdoc cref="RunToError(string[])"/>
    public static int RunToErrorWith(Symbol[] fields, params string[] body) => RunToErrorIn(new RuntimeSourceHarness.Surroundings(), fields, body);

    /// <summary>Runs <paramref name="body"/> like <see cref="RunUnder"/>, asserts that it ends in an error, and gives the error's number.</summary>
    public static int RunToErrorUnder(OptionCompare? mode, params string[] body) => RunToErrorIn(Under(mode), [], body);

    private static int RunToErrorIn(RuntimeSourceHarness.Surroundings surroundings, Symbol[] fields, string[] body)
    {
        var (outcome, lines) = Execute(fields, surroundings, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind, string.Join(" / ", lines));
        return outcome.ErrorInfo!.ErrorId;
    }

    private static RuntimeSourceHarness.Surroundings Under(OptionCompare? mode)
        => new() { Directives = mode is { } compare ? new ModuleDirectives(Compare: compare) : ModuleDirectives.None };

    /// <summary>Runs <paramref name="body"/> to its end with a spy in front of the dispatcher, and gives the members it was asked for.</summary>
    public static (string[] Lines, string[] Members) RunSpied(params string[] body)
    {
        RuntimeSourceHarness.RecordingDispatcher? spy = null;
        var lines = RunIn(new RuntimeSourceHarness.Surroundings { Dispatcher = inner => spy = new RuntimeSourceHarness.RecordingDispatcher(inner) }, [], body);
        return (lines, [.. spy!.Requests.Select(request => request.Member.Name)]);
    }

    #endregion
}
