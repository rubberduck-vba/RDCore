using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.2.1, §5.3.1.11
// A function or a Property Get named without an argument list, where its value is taken, is invoked with an empty argument list (MS-VBAL §5.6.2.1),
// which gives a parameter that is neither Optional nor a ParamArray nothing (§5.3.1.11): "Argument not optional" when it compiles, in any module (MS-VBA
// and VB6, observed for a function of the library). The name of a function or a Property Get in its own body is its result (§5.6.2.1).
// What names a procedure without taking its value is not checked here: the callee of a call, checked with its arguments; the target of an assignment
// and an element of an Erase statement (StatementOperandChecks); the operand of AddressOf; the type of a New or of a TypeOf; the name of a member, after
// its "." or "!"; a label, an event, and a variable a statement writes to.
internal static class BareFunctionReferenceStaticSemantics
{
    public static IEnumerable<VBCompileErrorInfo> Errors(StaticEvaluationContext context, StatementNode statement)
    {
        var notTaken = new HashSet<ExpressionNode>(NotTakenOperandsOf(statement), ReferenceEqualityComparer.Instance);
        var errors = new List<VBCompileErrorInfo>();
        foreach (var operand in statement.Inputs.OfType<ExpressionNode>())
        {
            Visit(context, operand, !notTaken.Contains(operand), errors);
        }

        return errors;
    }

    // the operands of a statement that name what the statement does something to, rather than a value it takes.
    private static IEnumerable<ExpressionNode> NotTakenOperandsOf(StatementNode statement) => statement switch
    {
        AssignmentStatementNode assignment => [assignment.Target],
        CallStatementNode call => [call.Callee],
        ForStatementNode loop => new[] { loop.ControlExpression, loop.NextExpression }.OfType<ExpressionNode>(),
        ForEachStatementNode loop => new[] { loop.ControlExpression, loop.NextExpression }.OfType<ExpressionNode>(),
        MidStatementNode mid => [mid.Target],
        RedimDeclarationNode redim => [redim.Target],
        GoToStatementNode goTo => [goTo.LabelExpression],
        GoSubStatementNode goSub => [goSub.LabelExpression],
        OnGoToStatementNode onGoTo => onGoTo.Labels,
        OnGoSubStatementNode onGoSub => onGoSub.Labels,
        OnErrorGoToStatementNode onError => [onError.LabelExpression],
        ResumeStatementNode { LabelExpression: { } label } => [label],
        KeywordStatementNode { Token: Tokens.Erase } erase => erase.Inputs.OfType<ExpressionNode>(),
        KeywordStatementNode { Token: Tokens.RaiseEvent, Inputs: [ExpressionNode raised, ..] } => [raised],
        _ => StatementStaticSemanticsEvaluator.ReadIntoBy(statement),
    };

    private static void Visit(StaticEvaluationContext context, ExpressionNode expression, bool isTaken, List<VBCompileErrorInfo> errors)
    {
        if (isTaken && expression is SimpleNameExpressionNode or MemberAccessExpressionNode && ErrorOf(context, expression) is { } error)
        {
            errors.Add(error);
        }

        switch (expression)
        {
            // the member is a name, and the object it is a member of is a value.
            case MemberAccessExpressionNode access:
                if (access.Owner is { } owner)
                {
                    Visit(context, owner, isTaken: true, errors);
                }
                break;
            // so is the name after the "!" of a dictionary access (MS-VBAL §5.6.14), which is the String the default member is given.
            case DictionaryAccessExpressionNode dictionary:
                if (dictionary.Owner is { } dictionaryOwner)
                {
                    Visit(context, dictionaryOwner, isTaken: true, errors);
                }
                break;
            case IndexExpressionNode index:
                Visit(context, index.Callee, isTaken: false, errors);
                foreach (var argument in index.Arguments)
                {
                    Visit(context, argument, isTaken: true, errors);
                }
                break;
            case AddressOfExpressionNode addressOf:
                Visit(context, addressOf.Target, isTaken: false, errors);
                break;
            // the type a New or a TypeOf names is not evaluated.
            case NewExpressionNode:
                break;
            case TypeOfIsExpressionNode typeOf:
                Visit(context, typeOf.Operand, isTaken: true, errors);
                break;
            default:
                foreach (var operand in expression.Inputs.OfType<ExpressionNode>())
                {
                    Visit(context, operand, isTaken: true, errors);
                }
                break;
        }
    }

    private static VBCompileErrorInfo? ErrorOf(StaticEvaluationContext context, ExpressionNode expression)
    {
        if (ObjectValueStaticSemantics.MemberOfCallee(context, expression) is not VBReturningMemberSymbol procedure
            || procedure is not (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol)
            || expression is SimpleNameExpressionNode && procedure.Uri.AbsoluteUri == context.Scope.Uri.AbsoluteUri)
        {
            return null;
        }

        return procedure.Parameters.FirstOrDefault(parameter => !(parameter.Name == Tokens.Me && parameter.ParameterKind == ParameterKind.ImplicitByRef)
            && !parameter.IsOptional && parameter is not ParamArrayParameterSymbol) is { } required
            ? VBCompileErrorInfo.For(VBCompileErrorId.ArgumentNotOptional, expression.Location,
                string.Format(Exceptions.VBCompileError_NamedWithoutItsArguments_Verbose, procedure.Name, required.Name))
            : null;
    }
}
