using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;
using RDCore.Symbols;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The standard library's symbols, and the globals the language environment provides itself. Nothing
/// declares <c>Debug</c> — it is not in MS-VBAL §6.1 at all — so RDCore synthesizes it, and the reason
/// that matters is resolution: an unbound name in a module with no <c>Option Explicit</c> is a
/// declaration (§5.6.10), so without this <c>Debug.Print x</c> quietly declares a local called
/// <c>Debug</c>.
/// </summary>
[TestClass]
public sealed class StdLibSymbolProviderTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static IReadOnlyList<Symbol> Provide() => [.. new StdLibSymbolProvider(Root).ProvideSymbols()];

    // the class rides on its own predeclared instance rather than being a symbol of its own; see the
    // provider's own note on why yielding both would make the name ambiguous.
    private static VBClassModuleSymbol DebugClass()
        => Provide().OfType<VBPredeclaredInstanceSymbol>().Single().ClassModule;

    // MS-VBAL §6.1.1 Predefined Enums, each constant as the specification spells it: a completion list and a hover show the name the library gives, and a
    // name is read in any case but shown in one (vbOKOnly, vbOK, vbIMEHiragana - not vbOkOnly, vbOk, vbIMEIragana).
    private static readonly string[] PredefinedEnums =
    [
        "FormShowConstants.vbModal", "FormShowConstants.vbModeless",
        "VbAppWinStyle.vbHide", "VbAppWinStyle.vbMaximizedFocus", "VbAppWinStyle.vbMinimizedFocus", "VbAppWinStyle.vbMinimizedNoFocus",
        "VbAppWinStyle.vbNormalFocus", "VbAppWinStyle.vbNormalNoFocus",
        "VbCalendar.vbCalGreg", "VbCalendar.vbCalHijri",
        "VbCallType.vbGet", "VbCallType.vbLet", "VbCallType.vbMethod", "VbCallType.vbSet",
        "VbCompareMethod.vbBinaryCompare", "VbCompareMethod.vbTextCompare",
        "VbDateTimeFormat.vbGeneralDate", "VbDateTimeFormat.vbLongDate", "VbDateTimeFormat.vbLongTime", "VbDateTimeFormat.vbShortDate",
        "VbDateTimeFormat.vbShortTime",
        "VbDayOfWeek.vbFriday", "VbDayOfWeek.vbMonday", "VbDayOfWeek.vbSaturday", "VbDayOfWeek.vbSunday", "VbDayOfWeek.vbThursday", "VbDayOfWeek.vbTuesday",
        "VbDayOfWeek.vbUseSystemDayOfWeek", "VbDayOfWeek.vbWednesday",
        "VbFileAttribute.vbAlias", "VbFileAttribute.vbArchive", "VbFileAttribute.vbDirectory", "VbFileAttribute.vbHidden", "VbFileAttribute.vbNormal",
        "VbFileAttribute.vbReadOnly", "VbFileAttribute.vbSystem", "VbFileAttribute.vbVolume",
        "VbFirstWeekOfYear.vbFirstFourDays", "VbFirstWeekOfYear.vbFirstFullWeek", "VbFirstWeekOfYear.vbFirstJan1", "VbFirstWeekOfYear.vbUseSystem",
        "VbIMEStatus.vbIMEAlphaDbl", "VbIMEStatus.vbIMEAlphaSng", "VbIMEStatus.vbIMEDisable", "VbIMEStatus.vbIMEHiragana", "VbIMEStatus.vbIMEKatakanaDbl",
        "VbIMEStatus.vbIMEKatakanaSng", "VbIMEStatus.vbIMEModeAlpha", "VbIMEStatus.vbIMEModeAlphaFull", "VbIMEStatus.vbIMEModeHangul",
        "VbIMEStatus.vbIMEModeHangulFull", "VbIMEStatus.vbIMEModeHiragana", "VbIMEStatus.vbIMEModeKatakana", "VbIMEStatus.vbIMEModeKatakanaHalf",
        "VbIMEStatus.vbIMEModeNoControl", "VbIMEStatus.vbIMEModeOff", "VbIMEStatus.vbIMEModeOn", "VbIMEStatus.vbIMENoOp", "VbIMEStatus.vbIMEOff",
        "VbIMEStatus.vbIMEOn",
        "VbMsgBoxResult.vbAbort", "VbMsgBoxResult.vbCancel", "VbMsgBoxResult.vbIgnore", "VbMsgBoxResult.vbNo", "VbMsgBoxResult.vbOK",
        "VbMsgBoxResult.vbRetry", "VbMsgBoxResult.vbYes",
        "VbMsgBoxStyle.vbAbortRetryIgnore", "VbMsgBoxStyle.vbApplicationModal", "VbMsgBoxStyle.vbCritical", "VbMsgBoxStyle.vbDefaultButton1",
        "VbMsgBoxStyle.vbDefaultButton2", "VbMsgBoxStyle.vbDefaultButton3", "VbMsgBoxStyle.vbDefaultButton4", "VbMsgBoxStyle.vbExclamation",
        "VbMsgBoxStyle.vbInformation", "VbMsgBoxStyle.vbMsgBoxHelpButton", "VbMsgBoxStyle.vbMsgBoxRight", "VbMsgBoxStyle.vbMsgBoxRtlReading",
        "VbMsgBoxStyle.vbMsgBoxSetForeground", "VbMsgBoxStyle.vbOKCancel", "VbMsgBoxStyle.vbOKOnly", "VbMsgBoxStyle.vbQuestion",
        "VbMsgBoxStyle.vbRetryCancel", "VbMsgBoxStyle.vbSystemModal", "VbMsgBoxStyle.vbYesNo", "VbMsgBoxStyle.vbYesNoCancel",
        "VbQueryClose.vbAppTaskManager", "VbQueryClose.vbAppWindows", "VbQueryClose.vbFormCode", "VbQueryClose.vbFormControlMenu",
        "VbQueryClose.vbFormMDIForm",
        "VbStrConv.vbFromUnicode", "VbStrConv.vbHiragana", "VbStrConv.vbKatakana", "VbStrConv.vbLowerCase", "VbStrConv.vbNarrow", "VbStrConv.vbProperCase",
        "VbStrConv.vbUnicode", "VbStrConv.vbUpperCase", "VbStrConv.vbWide",
        "VbTriState.vbFalse", "VbTriState.vbTrue", "VbTriState.vbUseDefault",
        "VbVarType.vbArray", "VbVarType.vbBoolean", "VbVarType.vbByte", "VbVarType.vbCurrency", "VbVarType.vbDataObject", "VbVarType.vbDate",
        "VbVarType.vbDecimal", "VbVarType.vbDouble", "VbVarType.vbEmpty", "VbVarType.vbError", "VbVarType.vbInteger", "VbVarType.vbLong",
        "VbVarType.vbLongLong", "VbVarType.vbNull", "VbVarType.vbObject", "VbVarType.vbSingle", "VbVarType.vbString", "VbVarType.vbUserDefinedType",
        "VbVarType.vbVariant",
    ];

    [TestMethod]
    public void ThePredefinedEnums_AndTheirConstants_AreSpelledAsTheSpecificationSpellsThem()
    {
        var spelled = Provide().OfType<VBEnumMemberSymbol>()
            .SelectMany(enumeration => ((SDK.Model.Types.Complex.VBEnumType)enumeration.ResolvedType).Members.Select(constant => $"{enumeration.Name}.{constant.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // in this case and no other: a name in any case would pass a comparison VBA makes, and show the wrong one.
        CollectionAssert.AreEquivalent(PredefinedEnums.Order(StringComparer.Ordinal).ToArray(), spelled,
            string.Join(", ", spelled.Except(PredefinedEnums, StringComparer.Ordinal)));
    }

    [TestMethod]
    public void TheDebugObject_IsAClassWithADefaultInstanceNamedAfterIt()
    {
        var instance = Provide().OfType<VBPredeclaredInstanceSymbol>().Single();
        var debugClass = instance.ClassModule;

        // MS-VBAL 5.2.4.1.2's shape: the class is what a type reference names, the default instance is
        // what the name resolves to as a value.
        Assert.AreEqual("Debug", instance.Name);
        Assert.AreEqual(ScopeKind.Global, instance.ScopeKind);
        Assert.AreEqual(debugClass.Uri, instance.ClassModule.Uri);
        Assert.IsTrue(debugClass.GetProperty(SymbolProperties.PredeclaredId));
        // `New Debug` names nothing.
        Assert.IsFalse(debugClass.GetProperty(SymbolProperties.Creatable));
    }

    [TestMethod]
    public void TheDebugObject_HasPrintAndAssert_AndNothingElse()
    {
        // and that is the whole object: VBA's Debug has exactly these two members.
        var debugClass = DebugClass();

        CollectionAssert.AreEquivalent(
            new[] { "Print", "Assert" },
            debugClass.Members.Select(member => member.Name).ToArray());
    }

    [TestMethod]
    public void Assert_TakesOneBooleanExpression()
    {
        var assert = (VBProcedureMemberSymbol)DebugClass().Members.Single(member => member.Name == "Assert");

        var parameter = assert.Parameters.Single();
        Assert.AreEqual(VBBooleanType.TypeInfo, parameter.ResolvedType);
        Assert.IsFalse(parameter.IsOptional);
    }

    [TestMethod]
    public void Print_TakesAnOutputList_AsAParamArray()
    {
        // the output list is a grammar construct of its own (MS-VBAL 5.4.5.8.1) rather than an argument
        // list — Spc, Tab and the ; / , separators are not expressions. The ParamArray is what makes
        // the member resolvable at any arity; the statement reads the list off the syntax.
        var print = (VBProcedureMemberSymbol)DebugClass().Members.Single(member => member.Name == "Print");

        Assert.IsInstanceOfType<ParamArrayParameterSymbol>(print.Parameters.Single());
    }

    [TestMethod]
    public void AcrossTheWorkspaceResolver_DebugResolvesAsAValue()
    {
        var resolver = WorkspaceSymbolResolver.Compose(Root, [Module("Sub Foo()\r\nEnd Sub\r\n")], new IntrinsicSymbolResolver());

        var resolved = resolver.ResolveValue("Debug", ScopeKind.Local, new UriBuilder(Root) { Fragment = "Mod1.Foo" }.Uri);

        Assert.IsTrue(resolved.IsResolved, "Debug must bind, or every module that uses it declares a local called Debug");
        Assert.IsInstanceOfType<VBPredeclaredInstanceSymbol>(resolved.Symbol);
    }

    [TestMethod]
    public void AcrossTheWorkspaceResolver_DebugPrintDeclaresNoLocal()
    {
        // the wart this closes: with Debug unbound, the declaration pass took it for an undeclared name.
        var module = Module("Sub Foo()\r\nDebug.Print 1\r\nEnd Sub\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(Root, [module], new IntrinsicSymbolResolver());

        var locals = new SyntaxTreeSymbolProvider(Root, module.Uri, ModuleType.StdModule, module.Parse, resolver)
            .ProvideSymbols()
            .OfType<VBLocalVariableSymbol>()
            .ToArray();

        // `o` is undeclared and becomes an implicit local; `Debug` resolves, so it does not.
        Assert.IsEmpty(locals.Where(local => local.Name == "Debug"),
            $"declared: [{string.Join(", ", locals.Select(local => local.Name))}]");
    }

    [TestMethod]
    public void AcrossTheWorkspaceResolver_AStandardLibraryFunctionResolvesUnqualified()
    {
        // MS-VBAL §6.1.2.7's Information is a standard module, so its members are promoted to the project
        // scope: `IsNumeric(x)` binds without naming the module. Nothing references the library — every
        // VBA project has it (RD-VBAL §6.1) — so the provider is unconditional and this needs no setup.
        var resolver = WorkspaceSymbolResolver.Compose(Root, [Module("Sub Foo()\r\nEnd Sub\r\n")], new IntrinsicSymbolResolver());

        var resolved = resolver.ResolveValue("IsNumeric", ScopeKind.Local, new UriBuilder(Root) { Fragment = "Mod1.Foo" }.Uri);

        Assert.IsTrue(resolved.IsResolved);
        Assert.IsInstanceOfType<VBFunctionMemberSymbol>(resolved.Symbol);
    }

    [TestMethod]
    public void AcrossTheWorkspaceResolver_ErrResolvesToAFunctionReturningTheErrObject()
    {
        // MS-VBAL §6.1.3.2's error object is reached through the Err function of Information, which is
        // what makes a bare `Err` bind (and `Err.Number` resolve through the class it returns).
        var resolver = WorkspaceSymbolResolver.Compose(Root, [Module("Sub Foo()\r\nEnd Sub\r\n")], new IntrinsicSymbolResolver());

        var resolved = resolver.ResolveValue("Err", ScopeKind.Local, new UriBuilder(Root) { Fragment = "Mod1.Foo" }.Uri);

        Assert.IsTrue(resolved.IsResolved);
        var err = (VBFunctionMemberSymbol)resolved.Symbol!;
        Assert.AreEqual("ErrObject", err.ResolvedType.Name);
    }

    [TestMethod]
    public void AcrossTheWorkspaceResolver_ErrObjectResolvesAsAType()
    {
        // `Dim e As ErrObject` — which is the other half of modelling the error object as MS-VBA does,
        // rather than as a class module whose name its own default instance would shadow.
        var resolver = WorkspaceSymbolResolver.Compose(Root, [Module("Sub Foo()\r\nEnd Sub\r\n")], new IntrinsicSymbolResolver());

        var resolved = resolver.ResolveType("ErrObject", ScopeKind.Local, new UriBuilder(Root) { Fragment = "Mod1.Foo" }.Uri);

        Assert.IsTrue(resolved.IsResolved);
        Assert.IsInstanceOfType<VBClassModuleSymbol>(resolved.Symbol);
    }

    [TestMethod]
    public void AcrossTheWorkspaceResolver_APredefinedEnumConstantResolvesUnqualified()
    {
        // MS-VBAL §6.1.1's predefined enums are the library's rather than any module's, and §5.2.3.4
        // makes an enum's members accessible wherever its type is — which is what makes `vbSunday` a
        // name on its own, without `VbDayOfWeek.vbSunday`.
        var resolver = WorkspaceSymbolResolver.Compose(Root, [Module("Sub Foo()\r\nEnd Sub\r\n")], new IntrinsicSymbolResolver());
        var procedure = new UriBuilder(Root) { Fragment = "Mod1.Foo" }.Uri;

        Assert.IsTrue(resolver.ResolveValue("vbSunday", ScopeKind.Local, procedure).IsResolved);
        Assert.IsTrue(resolver.ResolveType("VbDayOfWeek", ScopeKind.Local, procedure).IsResolved);
    }

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Module(string body)
        => (new UriBuilder(Root) { Fragment = "Mod1" }.Uri, ModuleType.StdModule,
            new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Attribute VB_Name = \"Mod1\"\r\n{body}"));
}
