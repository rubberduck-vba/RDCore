using RDCore.Runtime.Execution;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.StdLib;
using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a Debug.Print of Strings, and of the calls of the standard library that give it a String: TypeName of an object, and Replace.
internal sealed partial class ProcedureAnalysis
{
    // MS-VBAL §5.4.5.8, as PrintOutputEvaluator writes it: each item a String, written as it is, followed by a semicolon or by nothing, and the line ended
    // after the last item when nothing follows it. A comma, Spc, Tab, and an item of any other type (a number, which is written with a space on each side,
    // a Date, a Boolean, a Variant, an object) are the interpreter's.
    private PrintStep? PrintOf(int offset, DebugPrintStatementNode print)
    {
        var items = new List<TextNode>(print.Items.Length);
        foreach (var item in print.Items)
        {
            if (item.Separator is not (null or ";") || item.Value is PrintSpcClauseNode or PrintTabClauseNode)
            {
                Refuse(IneligibilityKind.Statement, StatementNameOf(print), item.Location);
                return null;
            }

            if (item.Value is null)
            {
                continue;
            }

            var bound = Bind(item.Value);
            if (!IsStringItem(bound))
            {
                Refuse(IneligibilityKind.Statement, StatementNameOf(print), item.Location);
                return null;
            }

            if (Text(bound) is not { } text)
            {
                return null;
            }

            items.Add(text);
        }

        return new PrintStep(offset, items, EndsLine: print.Items.IsEmpty || print.Items[^1].Separator is null);
    }

    // whether an output item is an expression of Strings Text translates: a String literal, a String variable, a member or a call that gives one, or Strings
    // joined; one in parentheses is the one inside them. A number Text writes as its digits, which an output item is not: it is written with a space on
    // each side (PrintOutputEvaluator.Padded).
    private bool IsStringItem(BoundNode node)
    {
        while (node is BoundUnary { Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed })
        {
            node = enclosed;
        }

        return node switch
        {
            BoundLiteral { Literal.StaticValue: VBStringValue } => true,
            BoundVariable variable => TextSlotOf(variable) is not null,
            BoundName { Symbol: var symbol } => IsTextConstant(node, symbol),
            BoundQualifiedName { Member: var member } => IsTextConstant(node, member),
            BoundBinary { Plan: { IsDeferred: false, EffectiveType: VBStringType } } => true,
            BoundCall { Procedure: var called } => IsChrString(called) || IsTypeName(called) || IsTextLibrary(called),
            BoundMember member => ErrTextOf(member) is not null || MemberTextOf(member) is not null,
            _ => false,
        };
    }

    // the library's TypeName (IStdInformationModule.TypeName), by the member the library declares it as.
    private static readonly string TypeNameFunction
        = StdLibSymbolReader.ExternalTargetOf(typeof(IStdInformationModule), typeof(IStdInformationModule).GetMethod(nameof(IStdInformationModule.TypeName))!);

    private static bool IsTypeName(Symbol symbol)
        => StandardLibrary.IsLibraryMember(symbol) && symbol.GetProperty(SymbolProperties.ExternalTarget) == TypeNameFunction;

    // the functions of the library that return a String and take Strings, which compiled code calls with the texts of its String arguments; each writes
    // none of them, so that a copy of a variable's text is what it would read of the variable passed by reference.
    private static readonly string[] TextLibraryFunctions =
    [
        StdLibSymbolReader.ExternalTargetOf(typeof(IStdStringsModule), typeof(IStdStringsModule).GetMethod(nameof(IStdStringsModule.Replace))!),
    ];

    private static bool IsTextLibrary(Symbol symbol)
        => StandardLibrary.IsLibraryMember(symbol) && symbol is VBTypeMemberSymbol
            && Array.IndexOf(TextLibraryFunctions, symbol.GetProperty(SymbolProperties.ExternalTarget)) >= 0;

    // TypeName of an object variable of the activation, of Me, or of Nothing (MS-VBAL §6.1.2.7.1.12): the library's function names it when compiled code
    // calls it. Any other argument, and a call inside an instruction that calls inside an expression, is the interpreter's.
    private TextNode? TypeNameOf(BoundCall call)
    {
        if (_nesting is not null || call.BoundArguments is not [{ Passing: ArgumentPassing.Value or ArgumentPassing.Reference, Value: { } named }]
            || !TryObjectArgument(named, out var slot) || call.Procedure is not VBTypeMemberSymbol function)
        {
            _ = Unsupported(call);
            return null;
        }

        Libraries.Add(new CompiledLibraryCall(function, RuntimeProcedureInvoker.GetParameters(function), call.Expression.SourceLocation));
        return new TypeNameTextNode(Libraries.Count - 1, slot);
    }

    // a call of a function of TextLibraryFunctions, given at most three Strings, each an expression of Strings Text translates, and nothing for the rest of
    // its parameters, which are optional: each it is given none is given what an omitted argument is when the call is made.
    private TextNode? LibraryTextOf(BoundCall call)
    {
        var function = (VBTypeMemberSymbol)call.Procedure;
        var parameters = RuntimeProcedureInvoker.GetParameters(function);
        if (_nesting is not null || call.BoundArguments is not { } arguments)
        {
            _ = Unsupported(call);
            return null;
        }

        var given = arguments.Length;
        while (given > 0 && arguments[given - 1].Passing is ArgumentPassing.Omitted)
        {
            given--;
        }

        if (given > 3 || given > parameters.Length)
        {
            _ = Unsupported(call);
            return null;
        }

        var texts = new List<TextNode>(given);
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i >= given)
            {
                if (!parameters[i].IsOptional)
                {
                    _ = Unsupported(call);
                    return null;
                }

                continue;
            }

            if (parameters[i].ResolvedType is not VBStringType || parameters[i].ResolvedType is VBFixedStringType
                || arguments[i] is not { Passing: ArgumentPassing.Value or ArgumentPassing.Reference, Value: { } value })
            {
                _ = Unsupported(call);
                return null;
            }

            if (Text(value) is not { } text)
            {
                return null;
            }

            texts.Add(text);
        }

        Libraries.Add(new CompiledLibraryCall(function, parameters, call.Expression.SourceLocation));
        return new LibraryTextNode(Libraries.Count - 1, texts);
    }
}
