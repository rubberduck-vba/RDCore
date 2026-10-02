using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// The static semantics of the statements that have operands to check: where an <c>Exit</c> statement may be written
/// (<strong>MS-VBAL §5.4.2.5, .7, .17-.19</strong>), <c>Mid</c> (<strong>§5.4.3.5</strong>) and the file statements (<strong>§5.4.5</strong>) with <c>Name</c>.
/// </summary>
[TestClass]
public sealed class StatementOperandStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static int _id = 100;

    private static SyntaxNodeId NextId() => new(TestUri.TestModuleUri().AbsolutePath, [_id++]);

    private static SimpleNameExpressionNode NameOf(string identifier) => new(NextId(), TestLocations.TestLocation, identifier);

    private static LiteralExpressionNode Literal(short value) => new(NextId(), TestLocations.TestLocation, new VBIntegerValue(value));

    private static LiteralExpressionNode Literal(string value) => new(NextId(), TestLocations.TestLocation, new VBStringValue(value));

    private static KeywordStatementNode Keyword(string token, params SyntaxNode[] inputs) => new(NextId(), TestLocations.TestLocation, token, [.. inputs]);

    private static StatementBlock Block(params StatementNode[] statements) => new([.. statements]);

    private static ForStatementNode ForOf(params StatementNode[] body)
        => new(NextId(), TestLocations.TestLocation, NameOf("i"), Literal(1), Literal(2), null, Block(body));

    private static ForEachStatementNode ForEachOf(params StatementNode[] body)
        => new(NextId(), TestLocations.TestLocation, NameOf("i"), NameOf("items"), Block(body));

    private static DoLoopStatementNode DoOf(params StatementNode[] body) => new(NextId(), TestLocations.TestLocation, Block(body));

    private static WhileWendStatementNode WhileOf(params StatementNode[] body) => new(NextId(), TestLocations.TestLocation, NameOf("flag"), Block(body));

    private static VBUserDefinedType Udt(string name, params (string Name, VBType Type)[] fields)
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri(name);
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, R, R, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [.. fields.Select(field =>
        {
            var fieldUri = TestUri.TestUserDefinedTypeMemberUri(field.Name, name);
            return (VBTypeMemberSymbol)new VBUserDefinedTypeFieldSymbol(fieldUri, fieldUri, field.Name, field.Type, R, R, AccessModifier.Public);
        })]);
    }

    private static VBClassType SomeClass()
    {
        var owner = new VBClassModuleSymbol(Root, Root, "Widget");
        return new VBClassType(owner, []);
    }

    // a module with a variable of each of the types, named for them, and the context its statements are evaluated in.
    private static StaticEvaluationContext Context()
    {
        var module = new VBStandardModuleSymbol(Root, Root, "Caller");
        var fields = new (string Name, VBType Type)[]
        {
            ("aLong", VBLongType.TypeInfo),
            ("anInteger", VBIntegerType.TypeInfo),
            ("aString", VBStringType.TypeInfo),
            ("aFixedString", new VBFixedStringType(8)),
            ("aVariant", VBVariantType.TypeInfo),
            ("anObject", VBObjectType.TypeInfo),
            ("aWidget", SomeClass()),
            ("anArray", new VBResizableArrayType(VBLongType.TypeInfo)),
            ("aRecord", Udt("Plain", ("X", VBLongType.TypeInfo))),
            ("aRecordWithAnObject", Udt("Holder", ("Item", VBObjectType.TypeInfo))),
        }.Select(field => (VBModuleFieldVariableMemberSymbol)new VBModuleFieldVariableMemberSymbol(
            Root, module.Uri, field.Name, ScopeKind.Module, field.Type, R, R, AccessModifier.Implicit)).ToArray();

        var constant = new VBConstantMemberSymbol(Root, module.Uri, "Limit", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);

        var tree = ScopeTreeBuilder.Build([module with { Members = [.. fields, constant] }, .. fields, constant]);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(module.Uri));
    }

    private static ImmutableArray<VBCompileErrorInfo> Evaluate(StatementNode statement, MemberKind? procedure = null)
        => StatementStaticSemanticsEvaluator.Evaluate(Context(), Block(statement), procedure);

    private static void AssertNoErrors(ImmutableArray<VBCompileErrorInfo> errors)
        => Assert.IsEmpty(errors, string.Join("; ", errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));

    private static void AssertError(VBCompileErrorId expected, ImmutableArray<VBCompileErrorInfo> errors)
    {
        Assert.HasCount(1, errors, string.Join("; ", errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));
        Assert.AreEqual(expected, errors[0].VBCompileErrorId);
    }

    // ---- Exit statements ----

    [TestMethod]
    public void ExitFor_OutsideALoop_IsAnError()
        => AssertError(VBCompileErrorId.ExitForNotWithinForNext, Evaluate(Keyword(Tokens.ExitFor)));

    [TestMethod]
    public void ExitDo_OutsideALoop_IsAnError()
        => AssertError(VBCompileErrorId.ExitDoNotWithinDoLoop, Evaluate(Keyword(Tokens.ExitDo)));

    [TestMethod]
    public void ExitFor_InAForLoop_AndInAForEachLoop_IsValid()
    {
        AssertNoErrors(Evaluate(ForOf(Keyword(Tokens.ExitFor))));
        AssertNoErrors(Evaluate(ForEachOf(Keyword(Tokens.ExitFor))));
    }

    [TestMethod]
    public void ExitDo_InADoLoop_IsValid()
        => AssertNoErrors(Evaluate(DoOf(Keyword(Tokens.ExitDo))));

    [TestMethod]
    public void AnExit_InALoopOfAnotherKind_IsAnError()
    {
        AssertError(VBCompileErrorId.ExitDoNotWithinDoLoop, Evaluate(ForOf(Keyword(Tokens.ExitDo))));
        AssertError(VBCompileErrorId.ExitForNotWithinForNext, Evaluate(DoOf(Keyword(Tokens.ExitFor))));
        AssertError(VBCompileErrorId.ExitForNotWithinForNext, Evaluate(WhileOf(Keyword(Tokens.ExitFor))));
    }

    [TestMethod]
    public void AnExit_IsLexicallyInsideTheLoops_NoMatterHowDeep()
        => AssertNoErrors(Evaluate(ForOf(DoOf(WhileOf(Keyword(Tokens.ExitFor), Keyword(Tokens.ExitDo))))));

    [TestMethod]
    public void AnExit_AfterTheLoopItWasInside_IsNotInsideItAnyMore()
    {
        var errors = StatementStaticSemanticsEvaluator.Evaluate(Context(), Block(ForOf(Keyword(Tokens.ExitFor)), Keyword(Tokens.ExitFor)));

        AssertError(VBCompileErrorId.ExitForNotWithinForNext, errors);
    }

    [TestMethod]
    [DataRow(Tokens.ExitSub, MemberKind.Procedure, null)]
    [DataRow(Tokens.ExitSub, MemberKind.Function, VBCompileErrorId.ExitSubNotAllowedInFunctionOrProperty)]
    [DataRow(Tokens.ExitSub, MemberKind.PropertyGet, VBCompileErrorId.ExitSubNotAllowedInFunctionOrProperty)]
    [DataRow(Tokens.ExitSub, MemberKind.PropertyLet, VBCompileErrorId.ExitSubNotAllowedInFunctionOrProperty)]
    [DataRow(Tokens.ExitSub, MemberKind.PropertySet, VBCompileErrorId.ExitSubNotAllowedInFunctionOrProperty)]
    [DataRow(Tokens.ExitFunction, MemberKind.Function, null)]
    [DataRow(Tokens.ExitFunction, MemberKind.PropertyGet, null, DisplayName = "MS-VBA accepts an Exit Function in a Property Get")]
    [DataRow(Tokens.ExitFunction, MemberKind.Procedure, VBCompileErrorId.ExitFunctionNotAllowedInSubOrProperty)]
    [DataRow(Tokens.ExitFunction, MemberKind.PropertyLet, VBCompileErrorId.ExitFunctionNotAllowedInSubOrProperty)]
    [DataRow(Tokens.ExitFunction, MemberKind.PropertySet, VBCompileErrorId.ExitFunctionNotAllowedInSubOrProperty)]
    [DataRow(Tokens.ExitProperty, MemberKind.PropertyGet, null)]
    [DataRow(Tokens.ExitProperty, MemberKind.PropertyLet, null)]
    [DataRow(Tokens.ExitProperty, MemberKind.PropertySet, null)]
    [DataRow(Tokens.ExitProperty, MemberKind.Procedure, VBCompileErrorId.ExitPropertyNotAllowedInSubOrFunction)]
    [DataRow(Tokens.ExitProperty, MemberKind.Function, VBCompileErrorId.ExitPropertyNotAllowedInSubOrFunction)]
    public void AnExitOfAProcedure_MustMatchTheKindOfTheProcedure(string token, MemberKind procedure, VBCompileErrorId? expected)
    {
        var errors = Evaluate(Keyword(token), procedure);

        if (expected is { } error)
        {
            AssertError(error, errors);
        }
        else
        {
            AssertNoErrors(errors);
        }
    }

    [TestMethod]
    [DataRow(Tokens.ExitSub)]
    [DataRow(Tokens.ExitFunction)]
    [DataRow(Tokens.ExitProperty)]
    public void AnExitOfAProcedure_IsNotChecked_WhenTheKindOfProcedureIsNotKnown(string token)
        => AssertNoErrors(Evaluate(Keyword(token)));

    // ---- Mid ----

    private static MidStatementNode MidOf(string target, ExpressionNode? start = null, ExpressionNode? length = null, ExpressionNode? value = null)
        => new(NextId(), TestLocations.TestLocation, false, false, NameOf(target), start ?? Literal(1), length, value ?? Literal("x"));

    [TestMethod]
    [DataRow("aString")]
    [DataRow("aFixedString")]
    [DataRow("aVariant")]
    public void Mid_OnAStringOrAVariant_IsValid(string target)
        => AssertNoErrors(Evaluate(MidOf(target, length: Literal(2))));

    [TestMethod]
    [DataRow("aLong")]
    [DataRow("anArray")]
    [DataRow("aRecord")]
    [DataRow("aWidget")]
    public void Mid_OnAnythingButAStringOrAVariant_IsATypeMismatch(string target)
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(MidOf(target)));

    [TestMethod]
    public void Mid_OnALiteral_NeedsAVariable()
    {
        var mid = new MidStatementNode(NextId(), TestLocations.TestLocation, false, false, Literal("abc"), Literal(1), null, Literal("x"));

        AssertError(VBCompileErrorId.VariableRequired, Evaluate(mid));
    }

    [TestMethod]
    public void Mid_OnAConstant_NeedsAVariable()
    {
        // Limit is a Long constant: it is not a variable, and it is not a String either.
        var errors = Evaluate(MidOf("Limit"));

        CollectionAssert.AreEqual(
            new[] { VBCompileErrorId.VariableRequired, VBCompileErrorId.TypeMismatch },
            errors.Select(error => error.VBCompileErrorId).ToArray());
    }

    [TestMethod]
    public void Mid_WithAPositionThatCannotBeLong_IsATypeMismatch()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(MidOf("aString", start: NameOf("aRecord"))));

    [TestMethod]
    public void Mid_WithALengthThatCannotBeLong_IsATypeMismatch()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(MidOf("aString", length: NameOf("anArray"))));

    [TestMethod]
    public void Mid_WithAValueThatCannotBeAString_IsATypeMismatch()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(MidOf("aString", value: NameOf("aRecord"))));

    [TestMethod]
    public void Mid_ReportsEveryWrongOperand()
    {
        var errors = Evaluate(MidOf("aLong", start: NameOf("aRecord"), value: NameOf("anArray")));

        Assert.HasCount(3, errors);
    }

    [TestMethod]
    public void Mid_ThatIsInsideABlock_IsChecked()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(ForOf(MidOf("aLong"))));

    // ---- File numbers and the scalar operands ----

    [TestMethod]
    [DataRow("aLong")]
    [DataRow("aString")]
    [DataRow("aVariant")]
    [DataRow("anInteger")]
    public void AFileNumber_OfAScalarType_IsValid(string fileNumber)
        => AssertNoErrors(Evaluate(Keyword(Tokens.Close, NameOf(fileNumber))));

    [TestMethod]
    [DataRow("anArray")]
    [DataRow("aRecord")]
    public void AFileNumber_OfAnArrayOrAUserDefinedType_IsATypeMismatch(string fileNumber)
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.Close, NameOf(fileNumber))));

    [TestMethod]
    public void Close_WithNoFileNumber_IsValid()
        => AssertNoErrors(Evaluate(Keyword(Tokens.Close)));

    [TestMethod]
    public void Reset_IsValid()
        => AssertNoErrors(Evaluate(Keyword(Tokens.Reset)));

    [TestMethod]
    public void Seek_WithAnArrayPosition_IsATypeMismatch()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.Seek, Literal(1), NameOf("anArray"))));

    [TestMethod]
    public void Seek_WithAScalarPosition_IsValid()
        => AssertNoErrors(Evaluate(Keyword(Tokens.Seek, Literal(1), NameOf("aLong"))));

    [TestMethod]
    public void Width_WithAUserDefinedTypeLineWidth_IsATypeMismatch()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.Width, Literal(1), NameOf("aRecord"))));

    [TestMethod]
    public void Lock_WithAnArrayRecordNumber_IsATypeMismatch()
    {
        var lockStatement = new FileLockStatementNode(NextId(), TestLocations.TestLocation, Tokens.Lock, Literal(1), NameOf("anArray"), null);

        AssertError(VBCompileErrorId.TypeMismatch, Evaluate(lockStatement));
    }

    [TestMethod]
    public void Unlock_WithScalarRecordNumbers_IsValid()
    {
        var unlock = new FileLockStatementNode(NextId(), TestLocations.TestLocation, Tokens.Unlock, Literal(1), Literal(1), NameOf("aLong"));

        AssertNoErrors(Evaluate(unlock));
    }

    // ---- Open ----

    private static OpenStatementNode OpenOf(VBFileMode? mode, VBFileAccessMode? access, ExpressionNode? path = null, ExpressionNode? length = null)
        => new(NextId(), TestLocations.TestLocation, path ?? Literal("file.txt"), mode, access, null, Literal(1), length);

    [TestMethod]
    [DataRow(VBFileMode.Output, VBFileAccessMode.Write)]
    [DataRow(VBFileMode.Input, VBFileAccessMode.Read)]
    [DataRow(VBFileMode.Append, VBFileAccessMode.ReadWrite)]
    [DataRow(VBFileMode.Append, VBFileAccessMode.Write)]
    [DataRow(VBFileMode.Random, VBFileAccessMode.Read)]
    [DataRow(VBFileMode.Random, VBFileAccessMode.Write)]
    [DataRow(VBFileMode.Binary, VBFileAccessMode.Read)]
    [DataRow(VBFileMode.Binary, VBFileAccessMode.ReadWrite)]
    public void Open_WithAnAccessTheModeAllows_IsValid(VBFileMode mode, VBFileAccessMode access)
        => AssertNoErrors(Evaluate(OpenOf(mode, access)));

    [TestMethod]
    [DataRow(VBFileMode.Output, VBFileAccessMode.Read)]
    [DataRow(VBFileMode.Output, VBFileAccessMode.ReadWrite)]
    [DataRow(VBFileMode.Input, VBFileAccessMode.Write)]
    [DataRow(VBFileMode.Input, VBFileAccessMode.ReadWrite)]
    [DataRow(VBFileMode.Append, VBFileAccessMode.Read)]
    public void Open_WithAnAccessTheModeDoesNotAllow_IsAnError(VBFileMode mode, VBFileAccessMode access)
        => AssertError(VBCompileErrorId.FileAccessNotValidForMode, Evaluate(OpenOf(mode, access)));

    [TestMethod]
    public void Open_WithNoModeOrNoAccess_IsValid()
    {
        AssertNoErrors(Evaluate(OpenOf(null, null)));
        AssertNoErrors(Evaluate(OpenOf(VBFileMode.Output, null)));
    }

    [TestMethod]
    public void Open_WithAPathThatCannotBeAString_IsATypeMismatch()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(OpenOf(null, null, path: NameOf("aRecord"))));

    [TestMethod]
    public void Open_WithARecordLengthThatCannotBeAnInteger_IsATypeMismatch()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(OpenOf(null, null, length: NameOf("aRecord"))));

    // ---- Print, Write and their output lists ----

    private static PrintStatementNode PrintOf(string token, params PrintOutputItemNode[] items)
        => new(NextId(), TestLocations.TestLocation, token, Literal(1), [.. items]);

    private static PrintOutputItemNode Item(ExpressionNode? value) => new(NextId(), TestLocations.TestLocation, value, null);

    [TestMethod]
    [DataRow(Tokens.Print)]
    [DataRow(Tokens.Write)]
    public void AnOutputList_OfOrdinaryValues_IsValid(string token)
        => AssertNoErrors(Evaluate(PrintOf(token, Item(NameOf("aLong")), Item(NameOf("aRecord")), Item(Literal("x")))));

    [TestMethod]
    public void Spc_WithAnArrayNumber_IsATypeMismatch()
    {
        var spc = new PrintSpcClauseNode(NextId(), TestLocations.TestLocation, NameOf("anArray"));

        AssertError(VBCompileErrorId.TypeMismatch, Evaluate(PrintOf(Tokens.Print, Item(spc))));
    }

    [TestMethod]
    public void Tab_WithAUserDefinedTypeNumber_IsATypeMismatch_AndABareTabIsValid()
    {
        var tab = new PrintTabClauseNode(NextId(), TestLocations.TestLocation, NameOf("aRecord"));
        var bare = new PrintTabClauseNode(NextId(), TestLocations.TestLocation, null);

        AssertError(VBCompileErrorId.TypeMismatch, Evaluate(PrintOf(Tokens.Print, Item(tab))));
        AssertNoErrors(Evaluate(PrintOf(Tokens.Print, Item(bare))));
    }

    [TestMethod]
    public void Print_WithAFileNumberThatIsAnArray_IsATypeMismatch()
    {
        var print = new PrintStatementNode(NextId(), TestLocations.TestLocation, Tokens.Print, NameOf("anArray"), []);

        AssertError(VBCompileErrorId.TypeMismatch, Evaluate(print));
    }

    // ---- Line Input, Input, Put, Get ----

    [TestMethod]
    [DataRow("aString")]
    [DataRow("aVariant")]
    [DataRow("aFixedString")]
    public void LineInput_IntoAStringOrAVariant_IsValid(string variable)
        => AssertNoErrors(Evaluate(Keyword(Tokens.LineInput, Literal(1), NameOf(variable))));

    [TestMethod]
    public void LineInput_IntoALong_IsATypeMismatch()
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.LineInput, Literal(1), NameOf("aLong"))));

    [TestMethod]
    public void LineInput_IntoAnExpressionThatIsNotAVariable_NeedsAVariable()
        => AssertError(VBCompileErrorId.VariableRequired, Evaluate(Keyword(Tokens.LineInput, Literal(1), Literal("x"))));

    [TestMethod]
    public void Input_IntoScalarVariables_IsValid()
        => AssertNoErrors(Evaluate(Keyword(Tokens.Input, Literal(1), NameOf("aLong"), NameOf("aString"), NameOf("aVariant"))));

    [TestMethod]
    [DataRow("anObject")]
    [DataRow("aWidget")]
    public void Input_IntoAnObjectOrAClass_IsATypeMismatch(string variable)
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.Input, Literal(1), NameOf("aLong"), NameOf(variable))));

    [TestMethod]
    public void Input_IntoAConstant_NeedsAVariable()
        => AssertError(VBCompileErrorId.VariableRequired, Evaluate(Keyword(Tokens.Input, Literal(1), NameOf("Limit"))));

    [TestMethod]
    [DataRow("aLong")]
    [DataRow("aRecord")]
    [DataRow("anArray")]
    [DataRow("aVariant")]
    public void Put_OfDataThatHasNoObjectInIt_IsValid(string data)
    {
        AssertNoErrors(Evaluate(Keyword(Tokens.Put, Literal(1), NameOf(data))));
        AssertNoErrors(Evaluate(Keyword(Tokens.Put, Literal(1), Literal(2), NameOf(data))));
    }

    [TestMethod]
    [DataRow("anObject")]
    [DataRow("aWidget")]
    [DataRow("aRecordWithAnObject")]
    public void Put_OfAnObjectAClassOrARecordHoldingOne_IsATypeMismatch(string data)
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.Put, Literal(1), Literal(2), NameOf(data))));

    [TestMethod]
    [DataRow("aLong")]
    [DataRow("aRecord")]
    [DataRow("aString")]
    public void Get_IntoAVariableThatHasNoObjectInIt_IsValid(string variable)
        => AssertNoErrors(Evaluate(Keyword(Tokens.Get, Literal(1), NameOf(variable))));

    [TestMethod]
    [DataRow("anObject")]
    [DataRow("aRecordWithAnObject")]
    public void Get_IntoAnObjectOrARecordHoldingOne_IsATypeMismatch(string variable)
        => AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.Get, Literal(1), Literal(2), NameOf(variable))));

    [TestMethod]
    public void Get_IntoALiteral_NeedsAVariable()
        => AssertError(VBCompileErrorId.VariableRequired, Evaluate(Keyword(Tokens.Get, Literal(1), Literal(5))));

    // ---- Name ----

    [TestMethod]
    public void Name_WithStringOperands_IsValid()
        => AssertNoErrors(Evaluate(Keyword(Tokens.Name, Literal("a.txt"), NameOf("aString"))));

    [TestMethod]
    public void Name_WithAnOperandThatCannotBeAString_IsATypeMismatch()
    {
        AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.Name, NameOf("aRecord"), Literal("b.txt"))));
        AssertError(VBCompileErrorId.TypeMismatch, Evaluate(Keyword(Tokens.Name, Literal("a.txt"), NameOf("anArray"))));
    }

    // ---- Deferred, not rejected ----

    [TestMethod]
    public void AnOperandThatIsAnUndeclaredName_IsDeferred_NotRejected()
    {
        // nothing says what type it is, so there is no type for the statement's rule to judge: whether the name is declared is another rule's.
        AssertNoErrors(Evaluate(Keyword(Tokens.Seek, Literal(1), NameOf("nothingDeclaresThis"))));
        AssertNoErrors(Evaluate(MidOf("nothingDeclaresThis")));
        AssertNoErrors(Evaluate(Keyword(Tokens.LineInput, Literal(1), NameOf("nothingDeclaresThis"))));
    }
}
