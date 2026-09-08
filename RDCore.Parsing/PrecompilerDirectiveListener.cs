using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using RDCore.Parsing.AST;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;

namespace RDCore.Parsing;

internal class PrecompilerNodeBuilder(Uri rootUri, SyntaxNodeId nodeId) : NodeBuilder(rootUri, nodeId)
{
    public SyntaxNode BuildPrecompilerConstDeclaration(VBAConditionalCompilationParser.CcConstContext context, ConstKind kind)
        => new PrecompilerConstantDeclarationNode(NodeId, context.GetSourceLocation(_rootUri), kind, [.. _children]);

    public SyntaxNode BuildPrecompilerBlock(VBAConditionalCompilationParser.CcBlockContext context)
        => new PrecompilerTriviaNode(NodeId, context.GetSourceLocation(_rootUri), [.. _children], context.GetText());

    public SyntaxNode BuildConditionalExpression(VBAConditionalCompilationParser.CcExpressionContext context)
        => new ConditionalExpressionNode(NodeId, context.GetSourceLocation(_rootUri), [.. _children]);

    public SyntaxNode BuildPrecompilerConditional(VBAConditionalCompilationParser.CcIfBlockContext context)
        => new PrecompilerIfBlockStatementNode(NodeId, context.GetSourceLocation(_rootUri), [.. _children]);

    public SyntaxNode BuildPrecompilerConditional(VBAConditionalCompilationParser.CcIfContext context)
        => new PrecompilerInlineIfStatementNode(NodeId, context.GetSourceLocation(_rootUri), [.. _children]);
}

internal class PrecompilerDirectiveListener(Uri sourceUri) : VBAConditionalCompilationBaseListener, ISyntaxNodeProvider
{
    private readonly Uri _rootUri = sourceUri;
    private readonly Stack<PrecompilerNodeBuilder> _builderStack = new([new(sourceUri, new($"{sourceUri}/conditional", []))]);

    private PrecompilerNodeBuilder CurrentBuilder => _builderStack.Peek();

    private SyntaxNodeId GetCurrentNodeId() => CurrentBuilder.NodeId.Add(CurrentBuilder.ChildCount);

    public ImmutableArray<SyntaxNode> SyntaxNodes => BuildModuleNode();

    public ImmutableArray<SyntaxNode> BuildModuleNode()
    {
        // error recovery may leave scopes open — unwind to the root builder.
        while (_builderStack.Count > 1)
        {
            _builderStack.Pop();
        }
        return [.. CurrentBuilder.GetChildren];
    }

    private void OnEnterParent() => _builderStack.Push(new(_rootUri, GetCurrentNodeId()));
    private void OnExitParent(Func<PrecompilerNodeBuilder, SyntaxNode> provider)
    {
        // an Exit with no matching Enter (error recovery) must not pop the root builder.
        if (_builderStack.Count <= 1)
        {
            return;
        }
        var node = provider.Invoke(_builderStack.Pop());
        CurrentBuilder.AddChild(node);
    }
    private void OnExpression(SyntaxNode node) => CurrentBuilder.AddChild(node);
    public override void EnterCcBlock([NotNull] VBAConditionalCompilationParser.CcBlockContext context)
        => OnEnterParent();
    public override void ExitCcBlock([NotNull] VBAConditionalCompilationParser.CcBlockContext context)
        => OnExitParent(provider => provider.BuildPrecompilerBlock(context));

    public override void EnterCcIf([NotNull] VBAConditionalCompilationParser.CcIfContext context)
        => OnEnterParent();
    public override void ExitCcIf([NotNull] VBAConditionalCompilationParser.CcIfContext context)
        => OnExitParent(provider => provider.BuildPrecompilerConditional(context));

    public override void EnterCcIfBlock([NotNull] VBAConditionalCompilationParser.CcIfBlockContext context)
        => OnEnterParent();
    public override void ExitCcIfBlock([NotNull] VBAConditionalCompilationParser.CcIfBlockContext context)
        => OnExitParent(provider => provider.BuildPrecompilerConditional(context));

    public override void EnterCcElseIfBlock([NotNull] VBAConditionalCompilationParser.CcElseIfBlockContext context)
        => OnEnterParent();
    public override void ExitCcElseIfBlock([NotNull] VBAConditionalCompilationParser.CcElseIfBlockContext context)
        => OnExitParent(provider => new PrecompilerElseIfBlockStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [..provider.GetChildren]));

    public override void EnterCcElseBlock([NotNull] VBAConditionalCompilationParser.CcElseBlockContext context)
        => OnEnterParent();
    public override void ExitCcElseBlock([NotNull] VBAConditionalCompilationParser.CcElseBlockContext context)
        => OnExitParent(provider => new PrecompilerElseBlockStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterCcConst([NotNull] VBAConditionalCompilationParser.CcConstContext context)
        => OnEnterParent();
    public override void ExitCcConst([NotNull] VBAConditionalCompilationParser.CcConstContext context)
        => OnExitParent(provider => provider.BuildPrecompilerConstDeclaration(context, ConstKind.ModuleMember));


    public override void EnterCcExpression([NotNull] VBAConditionalCompilationParser.CcExpressionContext context)
        => OnEnterParent();
    public override void ExitCcExpression([NotNull] VBAConditionalCompilationParser.CcExpressionContext context)
        => OnExitParent(provider => provider.BuildConditionalExpression(context));

    public override void ExitNameExpr([NotNull] VBAConditionalCompilationParser.NameExprContext context)
    {
        var node = new PrecompilerNameExpressionNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), context.name().nameValue().GetText());
        OnExpression(node);
    }
    public override void EnterAddOp([NotNull] VBAConditionalCompilationParser.AddOpContext context)
        => OnEnterParent();
    public override void ExitAddOp([NotNull] VBAConditionalCompilationParser.AddOpContext context)
    {
        var token = context.MINUS() is not null ? Tokens.SubtractionOp : Tokens.AdditionOp;
        OnExitParent(provider => new VBBinaryOperatorExpressionNode(token, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));
    }

    public override void EnterMultOp([NotNull] VBAConditionalCompilationParser.MultOpContext context)
        => OnEnterParent();
    public override void ExitMultOp([NotNull] VBAConditionalCompilationParser.MultOpContext context)
    {
        var token = context.DIV() is not null ? Tokens.DivisionOp : Tokens.MultiplicationOp;
        OnExitParent(provider => new VBBinaryOperatorExpressionNode(token, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));
    }

    public override void EnterConcatOp([NotNull] VBAConditionalCompilationParser.ConcatOpContext context)
        => OnEnterParent();
    public override void ExitConcatOp([NotNull] VBAConditionalCompilationParser.ConcatOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.ConcatOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterUnaryMinusOp([NotNull] VBAConditionalCompilationParser.UnaryMinusOpContext context)
        => OnEnterParent();
    public override void ExitUnaryMinusOp([NotNull] VBAConditionalCompilationParser.UnaryMinusOpContext context)
        => OnExitParent(provider => new VBUnaryOperatorExpressionNode(Tokens.NegationOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));
    // FIXME RD-VBA defines an explicit unary '+' operator that has no reason to not be a supported precompilation node. Grammar needs a tweak.

    public override void EnterIntDivOp([NotNull] VBAConditionalCompilationParser.IntDivOpContext context)
        => OnEnterParent();
    public override void ExitIntDivOp([NotNull] VBAConditionalCompilationParser.IntDivOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.IntegerDivisionOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterModOp([NotNull] VBAConditionalCompilationParser.ModOpContext context)
        => OnEnterParent();
    public override void ExitModOp([NotNull] VBAConditionalCompilationParser.ModOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.ModuloOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterPowOp([NotNull] VBAConditionalCompilationParser.PowOpContext context)
        => OnEnterParent();
    public override void ExitPowOp([NotNull] VBAConditionalCompilationParser.PowOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.PowerOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));


    public override void EnterRelationalOp([NotNull] VBAConditionalCompilationParser.RelationalOpContext context)
        => OnEnterParent();
    public override void ExitRelationalOp([NotNull] VBAConditionalCompilationParser.RelationalOpContext context)
    {
        var token = context.EQ() is not null ? Tokens.CompareEqualOp
            : context.NEQ() is not null ? Tokens.CompareNotEqualOp
            : context.GT() is not null ? Tokens.CompareGreaterThanOp
            : context.GEQ() is not null ? Tokens.CompareGreaterThanOrEqualOp
            : context.LT() is not null ? Tokens.CompareLessThanOp
            : context.LEQ() is not null ? Tokens.CompareLessThanOrEqualOp
            : context.IS() is not null ? Tokens.CompareIsOp
            : context.LIKE() is not null ? Tokens.CompareLikeOp
            : context.GetText();
        OnExitParent(provider => new VBBinaryOperatorExpressionNode(token, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));
    }

    public override void EnterLogicalAndOp([NotNull] VBAConditionalCompilationParser.LogicalAndOpContext context)
        => OnEnterParent();
    public override void ExitLogicalAndOp([NotNull] VBAConditionalCompilationParser.LogicalAndOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.LogicalAndOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterLogicalOrOp([NotNull] VBAConditionalCompilationParser.LogicalOrOpContext context)
        => OnEnterParent();
    public override void ExitLogicalOrOp([NotNull] VBAConditionalCompilationParser.LogicalOrOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.LogicalOrOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterLogicalNotOp([NotNull] VBAConditionalCompilationParser.LogicalNotOpContext context)
        => OnEnterParent();
    public override void ExitLogicalNotOp([NotNull] VBAConditionalCompilationParser.LogicalNotOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.LogicalNotOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterLogicalXorOp([NotNull] VBAConditionalCompilationParser.LogicalXorOpContext context)
        => OnEnterParent();
    public override void ExitLogicalXorOp([NotNull] VBAConditionalCompilationParser.LogicalXorOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.LogicalXOrOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterLogicalEqvOp([NotNull] VBAConditionalCompilationParser.LogicalEqvOpContext context)
        => OnEnterParent();
    public override void ExitLogicalEqvOp([NotNull] VBAConditionalCompilationParser.LogicalEqvOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.LogicalEqvOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));

    public override void EnterLogicalImpOp([NotNull] VBAConditionalCompilationParser.LogicalImpOpContext context)
        => OnEnterParent();
    public override void ExitLogicalImpOp([NotNull] VBAConditionalCompilationParser.LogicalImpOpContext context)
        => OnExitParent(provider => new VBBinaryOperatorExpressionNode(Tokens.LogicalImpOp, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), [.. provider.GetChildren]));


    public override void ExitCcVarLhs([NotNull] VBAConditionalCompilationParser.CcVarLhsContext context)
        => OnExpression(new PrecompilerNameExpressionNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), context.name().GetText()));

    public override void ExitLiteral([NotNull] VBAConditionalCompilationParser.LiteralContext context)
    {
        try
        {
            ResolveCcLiteral(context);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            // a #Const literal value that does not fit its type degrades to no expression rather than
            // forfeiting the whole module's precompiler trivia.
        }
    }

    private void ResolveCcLiteral(VBAConditionalCompilationParser.LiteralContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        if (context.FALSE() is not null)
        {
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBBooleanValue.False));
        }
        else if (context.TRUE() is not null)
        {
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBBooleanValue.True));
        }
        else if (context.INTEGERLITERAL() is ITerminalNode intNode)
        {
            var rawValue = Int64.Parse(intNode.Symbol.Text);
            VBTypedValue value = (rawValue <= Int16.MaxValue && rawValue >= Int16.MinValue)
                ? new VBIntegerValue(new ConstantBindingHandle(new VBRuntimeValue<short>(Convert.ToInt16(rawValue))))
                : (rawValue <= Int32.MaxValue && rawValue >= Int32.MinValue)
                    ? new VBLongValue(new ConstantBindingHandle(new VBRuntimeValue<int>(Convert.ToInt32(rawValue))))
                    : new VBDoubleValue(new ConstantBindingHandle(new VBRuntimeValue<double>(Convert.ToDouble(rawValue))));
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, value));
        }
        else if (context.STRINGLITERAL() is ITerminalNode stringNode)
        {
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, new VBStringValue(stringNode.Symbol.Text[1..^1])));
        }
        else if (context.FLOATLITERAL() is ITerminalNode floatNode)
        {
            // MS-VBAL 3.3.2: the literal's decimal separator is '.', independent of the host locale.
            var rawValue = double.Parse(floatNode.Symbol.Text, CultureInfo.InvariantCulture);
            VBTypedValue value = (rawValue <= Single.MaxValue && rawValue >= Single.MinValue)
                ? new VBSingleValue(new ConstantBindingHandle(new VBRuntimeValue<Single>(Convert.ToSingle(rawValue))))
                : new VBDoubleValue(new ConstantBindingHandle(new VBRuntimeValue<double>(rawValue)));
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, value));
        }
        else if (context.DATELITERAL() is ITerminalNode dateNode)
        {
            if (DateTime.TryParse(dateNode.Symbol.Text.Trim('#'), out var rawValue))
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, new VBDateValue(new ConstantBindingHandle(new VBRuntimeValue<double>(rawValue.ToOADate())))));
            }
        }
        else if (context.HEXLITERAL() is ITerminalNode hexNode)
        {
            var rawValue = Convert.ToInt64(hexNode.Symbol.Text[2..], fromBase: 16);
            VBTypedValue value = (rawValue <= Int16.MaxValue && rawValue >= Int16.MinValue) 
                ? new VBIntegerValue(new ConstantBindingHandle(new VBRuntimeValue<short>(Convert.ToInt16(rawValue))))
                : (rawValue <= Int32.MaxValue && rawValue >= Int32.MinValue) 
                    ? new VBLongValue(new ConstantBindingHandle(new VBRuntimeValue<int>(Convert.ToInt32(rawValue))))
                : new VBDoubleValue(new ConstantBindingHandle(new VBRuntimeValue<double>(Convert.ToDouble(rawValue))));
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, value));
        }
        else if (context.OCTLITERAL() is ITerminalNode octNode)
        {
            var rawValue = Convert.ToInt64(octNode.Symbol.Text[2..], fromBase: 8);
            VBTypedValue value = (rawValue <= Int16.MaxValue && rawValue >= Int16.MinValue)
                ? new VBIntegerValue(new ConstantBindingHandle(new VBRuntimeValue<short>(Convert.ToInt16(rawValue))))
                : (rawValue <= Int32.MaxValue && rawValue >= Int32.MinValue)
                    ? new VBLongValue(new ConstantBindingHandle(new VBRuntimeValue<int>(Convert.ToInt32(rawValue))))
                : new VBDoubleValue(new ConstantBindingHandle(new VBRuntimeValue<double>(Convert.ToDouble(rawValue))));
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, value));
        }
        else if (context.NOTHING() is not null)
        {
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBObjectValue.Nothing));
        }
        else if (context.EMPTY() is not null)
        {
            OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBEmptyValue.Empty));
        }
    }
}
