using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.LanguageServer.Diagnostics;

// the legacy Rubberduck inspections that '@Ignore and '@IgnoreModule name, without their Inspection suffix, and the codes that took the place of each:
// what a module written for legacy Rubberduck suppresses, it suppresses here. A name whose inspection is a compile error is known, and suppresses nothing.
// An inspection with no counterpart yet, or whose code reports more than it did, is not here. An extension's codes are written as codes.
internal static class LegacyInspectionNames
{
    private static readonly Dictionary<string, RDCoreDiagnosticCode[]> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ArgumentWithIncompatibleObjectType"] = [Core(11052)],
        ["AssignedByValParameter"] = [Core(1014)],
        ["AssignmentNotUsed"] = [Core(1022)],
        ["BooleanAssignedInIfElse"] = [Core(209)],
        ["ConstantNotUsed"] = [Core(1015)],
        ["DefaultProjectName"] = [Core(1024)],
        ["DefTypeStatement"] = [Core(103)],
        ["EmptyCaseBlock"] = [Core(1004)],
        ["EmptyDoWhileBlock"] = [Core(1004)],
        ["EmptyElseBlock"] = [Core(1004)],
        ["EmptyForEachBlock"] = [Core(1004)],
        ["EmptyForLoopBlock"] = [Core(1004)],
        ["EmptyIfBlock"] = [Core(1003)],
        ["EmptyMethod"] = [Core(1009)],
        ["EmptyModule"] = [Core(1010)],
        ["EmptyStringLiteral"] = [Core(312)],
        ["EmptyWhileWendBlock"] = [Core(1004)],
        ["EncapsulatePublicField"] = [Core(409)],
        ["FunctionReturnValueAlwaysDiscarded"] = [Core(217)],
        ["FunctionReturnValueDiscarded"] = [Core(216)],
        ["HostSpecificExpression"] = [Compile(9383)],
        ["HungarianNotation"] = [Core(1002)],
        ["IIfSideEffect"] = [Core(11039)],
        ["ImplementedInterfaceMember"] = [Core(411)],
        ["ImplicitByRefModifier"] = [Core(104)],
        ["ImplicitDefaultMemberAccess"] = [Core(119)],
        ["ImplicitlyTypedConst"] = [Core(117)],
        ["ImplicitPublicMember"] = [Core(105)],
        ["ImplicitUnboundDefaultMemberAccess"] = [Core(120)],
        ["ImplicitVariantReturnType"] = [Core(107)],
        ["IndexedDefaultMemberAccess"] = [Core(121)],
        ["IndexedRecursiveDefaultMemberAccess"] = [Core(126)],
        ["IndexedUnboundDefaultMemberAccess"] = [Core(122)],
        ["IntegerDataType"] = [Core(201)],
        ["IsMissingOnInappropriateArgument"] = [Core(207), Core(218)],
        ["IsMissingWithNonArgumentParameter"] = [Core(218), Core(207)],
        ["KeywordsUsedAsMember"] = [Core(1018), Core(11033)],
        ["LineContinuationBetweenKeywords"] = [Core(213)],
        ["LineLabelNotUsed"] = [Core(1016)],
        ["MemberNotOnInterface"] = [Core(11053), Compile(9316)],
        ["MisleadingByRefParameter"] = [Core(205)],
        ["ModuleScopeDimKeyword"] = [Core(202)],
        ["MoveFieldCloserToUsage"] = [Core(413)],
        ["MultilineParameter"] = [Core(203)],
        ["MultipleDeclarations"] = [Core(204)],
        ["NegativeLineNumber"] = [Core(11036)],
        ["NonBreakingSpaceIdentifier"] = [Core(11037)],
        ["NonReturningFunction"] = [Core(206)],
        ["ObsoleteCallStatement"] = [Core(302)],
        ["ObsoleteCommentSyntax"] = [Core(303)],
        ["ObsoleteErrorSyntax"] = [Core(304)],
        ["ObsoleteGlobal"] = [Core(305)],
        ["ObsoleteLetStatement"] = [Core(306)],
        ["ObsoleteTypeHint"] = [Core(307)],
        ["ObsoleteWhileWendStatement"] = [Core(308)],
        ["OnErrorGoToMinusOne"] = [Core(211)],
        ["OnLocalError"] = [Core(309)],
        ["OptionBase"] = [Core(102)],
        ["OptionExplicit"] = [Core(101)],
        ["ParameterCanBeByVal"] = [Core(1023)],
        ["ParameterNotUsed"] = [Core(1013)],
        ["ProcedureCanBeWrittenAsFunction"] = [Core(214)],
        ["ProcedureNotUsed"] = [Core(1025)],
        ["PublicImplementationShouldBePrivate"] = [Core(405)],
        ["ReadOnlyPropertyAssignment"] = [Core(11046), Compile(9341)],
        ["RedundantByRefModifier"] = [Core(314)],
        ["RedundantOption"] = [Core(315)],
        ["SelfAssignedDeclaration"] = [Core(215)],
        ["SetAssignmentWithIncompatibleObjectType"] = [Core(11051)],
        ["ShadowedDeclaration"] = [Core(208)],
        ["StepIsNotSpecified"] = [Core(116)],
        ["StepOneIsRedundant"] = [Core(313)],
        ["StopKeyword"] = [Core(210)],
        ["SuspiciousLetAssignment"] = [Core(125)],
        ["SuspiciousPredeclaredInstanceAccess"] = [Core(410)],
        ["UnassignedVariableUsage"] = [Core(1021)],
        ["UndeclaredVariable"] = [Core(128), Compile(9302)],
        ["UnderscoreInPublicClassModuleMember"] = [Core(408)],
        ["UnhandledOnErrorResumeNext"] = [Core(212)],
        ["UnreachableCase"] = [Core(219)],
        ["UntypedFunctionUsage"] = [Core(118)],
        ["UseMeaningfulName"] = [Core(1001)],
        ["UseOfBangNotation"] = [Core(123)],
        ["UseOfRecursiveBangNotation"] = [Core(127)],
        ["UseOfUnboundBangNotation"] = [Core(124)],
        ["ValueRequired"] = [Core(11015)],
        ["VariableNotAssigned"] = [Core(1012)],
        ["VariableNotUsed"] = [Core(1011)],
        ["VariableTypeNotDeclared"] = [Core(106)],
        ["WriteOnlyProperty"] = [Core(1017)],
    };

    public static bool TryGetCodes(string name, out IReadOnlyList<RDCoreDiagnosticCode> codes)
    {
        var found = Codes.TryGetValue(name, out var known);
        codes = known ?? [];
        return found;
    }

    private static RDCoreDiagnosticCode Core(int number) => new(RDCoreDiagnosticCode.CorePrefix, number);

    private static RDCoreDiagnosticCode Compile(int number) => new(RDCoreDiagnosticCode.CompilePrefix, number);
}
