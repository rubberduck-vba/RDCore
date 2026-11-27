using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Hosting.Json;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The JSON value of a value a program holds, and the value a JSON value is for a declared type: what a program invoked by name is given and gives back.
/// </summary>
[TestClass]
public sealed class JsonValuesTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static readonly VBModuleFieldVariableMemberSymbol V = Variable("V");

    private static readonly VBModuleFieldVariableMemberSymbol C = Variable("C");

    private static VBModuleFieldVariableMemberSymbol Variable(string name)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, VBVariantType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    // runs the body, which leaves the value in V, and writes V as JSON.
    private static (IVBRaisableError? Error, string? Json) Written(params string[] body)
    {
        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [V, C], output: null, standardLibrary: true, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);

        var error = new JsonValues(session, session.Symbols.Resolver)
            .ToJson(VBVariantType.TypeInfo.CreateValue(session.Symbols.Resolver.GetValue(V)), out var json);
        return (error, error is null ? json?.ToJsonString() ?? "null" : null);
    }

    private static string Json(params string[] body)
    {
        var (error, json) = Written(body);
        Assert.IsNull(error, error?.Verbose);
        return json!;
    }

    private static void AssertError(VBRuntimeErrorId expected, IVBRaisableError? error)
    {
        Assert.IsNotNull(error, "the value converted");
        Assert.AreEqual((int)expected, error.ErrorId, error.Verbose);
    }

    // reads json as the declared type in a session of its own, which the body then runs in with the value in V: what the program sees of it is printed.
    private static string[] Read(string json, Func<IRuntimeSession, VBType>? declared = null, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [V, C], output, standardLibrary: true, session =>
        {
            var type = declared?.Invoke(session) ?? VBVariantType.TypeInfo;
            var error = new JsonValues(session, session.Symbols.Resolver).FromJson(JsonNode.Parse(json), type, out var value);
            Assert.IsNull(error, error?.Verbose);
            var held = type is VBVariantType ? value! : new VBVariantValue(value!);
            session.Symbols.Resolver.GetValue(V).SetValue(session.Symbols.Resolver, held.RuntimeValue);
        }, body);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return [.. output.Lines.Select(line => line.Trim())];
    }

    // reads json as the declared type in a session where nothing runs.
    private static (VBRuntimeErrorInfo? Error, VBTypedValue? Value) Value(string json, Func<IRuntimeSession, VBType> declared)
    {
        (VBRuntimeErrorInfo?, VBTypedValue?) read = default;
        RuntimeSourceHarness.Run(fileSystem: null, [], output: null, standardLibrary: true, session =>
        {
            var error = new JsonValues(session, session.Symbols.Resolver).FromJson(JsonNode.Parse(json), declared(session), out var value);
            read = (error, value);
        }, "Debug.Print 1");
        return read;
    }

    private static VBTypedValue Converted(string json, VBType declared)
    {
        var (error, value) = Value(json, _ => declared);
        Assert.IsNull(error, error?.Verbose);
        return value!;
    }

    private static VBClassType Collection(IRuntimeSession session)
        => VBClassType.Of((VBClassModuleSymbol)VBProjectSymbol.ResolveQualifiedType(
            session.Symbols.Resolver, StandardLibrary.LibraryName, "Collection", StaticSymbol.GlobalUri).Symbol!);

    // a. a Dictionary is an object of its keys, in the order they were added; binary keys that differ by case are two names.
    [TestMethod]
    public void ADictionary_IsAnObjectOfItsStringKeys_InTheirOrder()
        => Assert.AreEqual("""{"b":1,"a":"x","B":true}""", Json(
            "Set V = CreateObject(\"Scripting.Dictionary\")", "V.Add \"b\", 1", "V.Add \"a\", \"x\"", "V.Add \"B\", True"));

    // a. a JSON name is a String: a Dictionary keeps 1 and "1" apart, which a JSON object cannot.
    [TestMethod]
    [DataRow("1", DisplayName = "a number")]
    [DataRow("True", DisplayName = "a Boolean")]
    [DataRow("Null", DisplayName = "Null")]
    public void ADictionaryKey_ThatIsNoString_IsATypeMismatch(string key)
        => AssertError(VBRuntimeErrorId.TypeMismatch, Written(
            "Set V = CreateObject(\"Scripting.Dictionary\")", "V.Add \"a\", 1", $"V.Add {key}, 2").Error);

    // a. a JSON object is a Dictionary of String keys that compares them binary: "a" and "A" are two keys, and null is a Null item.
    [TestMethod]
    public void AJsonObject_IsADictionary_ComparingItsKeysBinary()
        => CollectionAssert.AreEqual(
            new[] { "Dictionary 3 0 1 x True" },
            Read("""{"a":1,"A":"x","n":null}""", 
                body: "Debug.Print TypeName(V) & \" \" & V.Count & \" \" & V.CompareMode & \" \" & V(\"a\") & \" \" & V(\"A\") & \" \" & IsNull(V(\"n\"))"));

    // a. what an Object or a Dictionary is declared to hold, and no other class.
    [TestMethod]
    public void AJsonObject_IsADictionaryForAnObject_AndATypeMismatchForAnotherClass()
    {
        Assert.IsFalse(((VBObjectValue)Converted("""{"a":1}""", VBObjectType.TypeInfo)).IsNothing());

        AssertError(VBRuntimeErrorId.TypeMismatch, Value("""{"a":1}""", Collection).Error);
    }

    // b. the first dimension outermost; lower bounds are dropped.
    [TestMethod]
    public void AMultiDimensionalArray_IsNestedArrays_TheFirstDimensionOutermost()
        => Assert.AreEqual("[[10,11,12],[20,21,22]]", Json(
            "Dim A(1 To 2, 0 To 2) As Long, R As Long, K As Long",
            "For R = 1 To 2: For K = 0 To 2: A(R, K) = R * 10 + K: Next K: Next R",
            "V = A"));

    [TestMethod]
    public void AnArrayWithNoElements_IsAnEmptyArray()
        => Assert.AreEqual("[[],[]]", Json("Dim E() As Long", "V = Array(E, Split(\"\"))"));

    // b. a JSON array is a Variant() from 0, and one in it another.
    [TestMethod]
    public void AJsonArray_IsAVariantArrayFrom0_AndANestedOneAVariantArrayInIt()
        => CollectionAssert.AreEqual(
            new[] { "Variant() 0 2 Variant() x -1" },
            Read("""[1,[2,"x"],[]]""", 
                body: "Debug.Print TypeName(V) & \" \" & LBound(V) & \" \" & UBound(V) & \" \" & TypeName(V(1)) & \" \" & V(1)(1) & \" \" & UBound(V(2))"));

    // b. an array of another element type is not one a JSON array makes.
    [TestMethod]
    public void AJsonArray_ForALongArrayOrALong_IsATypeMismatch()
    {
        AssertError(VBRuntimeErrorId.TypeMismatch, Value("[1]", _ => new VBResizableArrayType(VBLongType.TypeInfo)).Error);
        AssertError(VBRuntimeErrorId.TypeMismatch, Value("[1]", _ => VBLongType.TypeInfo).Error);
    }

    // c. a Date has no zone: ISO 8601 with no offset, a day before 1899 included.
    [TestMethod]
    [DataRow("V = DateSerial(2026, 11, 26) + TimeSerial(14, 30, 0)", "\"2026-11-26T14:30:00\"", DisplayName = "an afternoon")]
    [DataRow("V = #3/4/1800 6:00:00 AM#", "\"1800-03-04T06:00:00\"", DisplayName = "a morning before 1899")]
    public void ADate_IsIsoTextWithNoOffset(string assignment, string expected)
        => Assert.AreEqual(expected, Json(assignment));

    [TestMethod]
    public void IsoTextWithNoOffset_IsTheDateForADate()
        => Assert.AreEqual(new DateTime(1800, 3, 4, 6, 0, 0), ((VBDateValue)Converted("\"1800-03-04T06:00:00\"", VBDateType.TypeInfo)).Value);

    // c. an offset would be read in the zone the program runs in.
    [TestMethod]
    [DataRow("\"2026-11-26T14:30:00Z\"", DisplayName = "UTC")]
    [DataRow("\"2026-11-26T14:30:00+01:00\"", DisplayName = "an offset")]
    [DataRow("\"26/11/2026\"", DisplayName = "no ISO 8601")]
    public void TextThatIsNoIsoMomentWithNoOffset_ForADate_IsATypeMismatch(string json)
        => AssertError(VBRuntimeErrorId.TypeMismatch, Value(json, _ => VBDateType.TypeInfo).Error);

    // c. a Variant is not told it holds a Date: the text stays a String.
    [TestMethod]
    public void IsoText_InAVariant_IsAString()
        => CollectionAssert.AreEqual(new[] { "String" }, Read("\"2026-11-26T14:30:00\"", body: "Debug.Print TypeName(V)"));

    // d. every digit: through a Double, both would lose their last ones.
    [TestMethod]
    [DataRow("V = 922337203685477.5807@", "922337203685477.5807", DisplayName = "Currency")]
    [DataRow("V = CDec(\"79228162514264337593543950335\")", "79228162514264337593543950335", DisplayName = "Decimal")]
    public void ACurrencyOrADecimal_IsANumberOfEveryDigit(string assignment, string expected)
        => Assert.AreEqual(expected, Json(assignment));

    [TestMethod]
    public void ANumberOfEveryDigit_IsThatCurrencyOrDecimal()
    {
        Assert.AreEqual(922337203685477.5807m, ((VBCurrencyValue)Converted("922337203685477.5807", VBCurrencyType.TypeInfo)).Value.Value);
        Assert.AreEqual(79228162514264337593543950335m, ((VBDecimalValue)Converted("79228162514264337593543950335", VBDecimalType.TypeInfo)).Value);
    }

    // e. no value, whichever of the three.
    [TestMethod]
    [DataRow("V = Empty", DisplayName = "Empty")]
    [DataRow("V = Null", DisplayName = "Null")]
    [DataRow("Set V = Nothing", DisplayName = "Nothing")]
    public void EmptyNullAndNothing_AreNull(string assignment)
        => Assert.AreEqual("null", Json(assignment));

    [TestMethod]
    public void Null_IsNullInAVariant_AndNothingInAnObject()
    {
        Assert.IsInstanceOfType<VBNullValue>(((VBVariantValue)Converted("null", VBVariantType.TypeInfo)).TypedValue);
        Assert.IsTrue(((VBObjectValue)Converted("null", VBObjectType.TypeInfo)).IsNothing());
    }

    // e. MS-VBAL §5.5.1.2.10: Null let to a resizable array or a UDT is a type mismatch. A fixed-size array is not a let target, for Null as for any value.
    [TestMethod]
    [DataRow("Variant()")]
    [DataRow("Long()")]
    [DataRow("UDT")]
    [DataRow("a fixed-size Long array")]
    public void Null_InAnArrayOrAUserDefinedType_IsATypeMismatch(string type)
        => AssertError(VBRuntimeErrorId.TypeMismatch, Value("null", _ => type switch
        {
            "Variant()" => new VBResizableArrayType(VBVariantType.TypeInfo),
            "Long()" => new VBResizableArrayType(VBLongType.TypeInfo),
            "UDT" => Point(),
            _ => new VBFixedSizeArrayType(VBLongType.TypeInfo),
        }).Error);

    private static VBUserDefinedType Point()
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri("Point");
        return new VBUserDefinedType(
            new VBUserDefinedTypeMemberSymbol(uri, uri, "Point", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), []);
    }

    // e. MS-VBAL §5.5.1.2.10: Null let to any other type that is not a Variant is an invalid use of Null.
    [TestMethod]
    [DataRow("Long")]
    [DataRow("String")]
    [DataRow("Date")]
    [DataRow("Boolean")]
    public void Null_InAnyOtherType_IsInvalidUseOfNull(string type)
        => AssertError(VBRuntimeErrorId.InvalidUseOfNull, Value("null", _ => type switch
        {
            "Long" => VBLongType.TypeInfo,
            "String" => VBStringType.TypeInfo,
            "Date" => VBDateType.TypeInfo,
            _ => VBBooleanType.TypeInfo,
        }).Error);

    // f. a Collection is an array of its members, an empty one too.
    [TestMethod]
    public void ACollection_IsAnArrayOfItsMembers()
        => Assert.AreEqual("""[1,"two",[3,4],[]]""", Json(
            "Set V = New Collection", "Set C = New Collection", "V.Add 1", "V.Add \"two\"", "V.Add Array(3, 4)", "V.Add C"));

    // f. an object held twice is written twice; one that holds itself has no end.
    [TestMethod]
    public void AnObjectHeldTwice_IsWrittenTwice_AndOneThatHoldsItselfIsATypeMismatch()
    {
        Assert.AreEqual("""[[1],[1]]""", Json("Set C = New Collection", "C.Add 1", "V = Array(C, C)"));

        AssertError(VBRuntimeErrorId.TypeMismatch, Written("Set V = New Collection", "V.Add V").Error);
    }

    // f. a library's object that is neither a Dictionary nor a Collection has no JSON value (an instance of a class of the workspace has: JsonInstancesTests).
    [TestMethod]
    public void AnotherObject_IsATypeMismatch()
        => AssertError(VBRuntimeErrorId.TypeMismatch, Written("Set V = CreateObject(\"Scripting.FileSystemObject\")").Error);

    // g. MS-VBAL §3.3.2: the subtype a literal of the same digits has; the sign is not part of the literal.
    [TestMethod]
    [DataRow("5", "Integer", 5d)]
    [DataRow("32767", "Integer", 32767d)]
    [DataRow("32768", "Long", 32768d)]
    [DataRow("-32768", "Long", -32768d)]
    [DataRow("2147483647", "Long", 2147483647d)]
    [DataRow("2147483648", "Double", 2147483648d)]
    [DataRow("-2147483648", "Double", -2147483648d)]
    [DataRow("5.0", "Double", 5d)]
    [DataRow("1e2", "Double", 100d)]
    public void AJsonNumber_InAVariant_IsTheSubtypeOfALiteralOfItsDigits(string json, string subtype, double expected)
    {
        var held = ((VBVariantValue)Converted(json, VBVariantType.TypeInfo)).TypedValue;

        Assert.AreEqual(subtype, held.TypeInfo.Name);
        Assert.AreEqual(expected, Convert.ToDouble(held.RuntimeValue.BoxedValue, System.Globalization.CultureInfo.InvariantCulture));
    }

    // into a type of its own, a JSON value is let-coerced, with the errors MS-VBA raises: 13 for text that is not a number, 6 past the type's range.
    [TestMethod]
    [DataRow("\"12\"", 12)]
    [DataRow("true", -1)]
    public void AJsonValue_ForALong_IsLetCoerced(string json, int expected)
        => Assert.AreEqual(expected, ((VBLongValue)Converted(json, VBLongType.TypeInfo)).Value);

    [TestMethod]
    [DataRow("\"abc\"", VBRuntimeErrorId.TypeMismatch)]
    [DataRow("3000000000", VBRuntimeErrorId.Overflow)]
    public void AJsonValue_ThatNoLongIs_IsTheErrorOfItsLetCoercion(string json, VBRuntimeErrorId expected)
        => AssertError(expected, Value(json, _ => VBLongType.TypeInfo).Error);

    // the scalars: a Single as its own shortest digits, and a String's quotes escaped.
    [TestMethod]
    [DataRow("V = CByte(7)", "7")]
    [DataRow("V = 1.1!", "1.1")]
    [DataRow("V = 0.1", "0.1")]
    [DataRow("V = True", "true")]
    [DataRow("V = \"a\"\"b\"", "\"a\\u0022b\"")]
    public void AScalar_IsItsJsonValue(string assignment, string expected)
        => Assert.AreEqual(expected, Json(assignment));
}
