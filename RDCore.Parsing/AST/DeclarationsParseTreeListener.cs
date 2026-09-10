using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using RDCore.Parsing;
using RDCore.Parsing.Syntax;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace RDCore.Parsing.AST;

/// <summary>
/// A <em>listener</em> that builds the AST nodes representing all the directives and declarations in a module.
/// </summary>
/// <param name="moduleNode">The root AST module node.</param>
/// <param name="errors">Collects the token-semantic syntax errors this pass raises (e.g. literal overflow).</param>
internal class DeclarationsParseTreeListener(Uri sourceUri, ModuleNode moduleNode, ErrorListener errors) : VBAParserBaseListener, ISyntaxNodeProvider
{
    private readonly Uri _rootUri = sourceUri;
    private readonly ModuleNode _root = moduleNode;
    private readonly ErrorListener _errors = errors;
    private readonly Stack<DeclarationNodeBuilder> _builderStack = new([new(sourceUri, moduleNode.Identity)]);
    private DeclarationNodeBuilder CurrentBuilder => _builderStack.Peek();

    private SyntaxNodeId GetCurrentNodeId() => CurrentBuilder.NodeId.Add(CurrentBuilder.ChildCount);

    public ImmutableArray<SyntaxNode> SyntaxNodes => [BuildModuleNode()];

    // pathological nesting recurses through the expression rule to an uncatchable stack overflow;
    // this turns it into a catchable exception the boundary net degrades to a located failure.
    public override void EnterEveryRule([NotNull] ParserRuleContext context)
        => RuntimeHelpers.EnsureSufficientExecutionStack();

    public ModuleNode BuildModuleNode()
    {
        // ANTLR error recovery can leave scopes open (an Enter with no matching Exit); unwind to the
        // module builder so a module with syntax errors still yields whatever declarations parsed.
        while (_builderStack.Count > 1)
        {
            _builderStack.Pop();
        }
        return _root with { Children = [.. CurrentBuilder.GetChildren] };
    }

    private void OnEnterParent()
    {
        _builderStack.Push(new(_rootUri, GetCurrentNodeId()));
    }

    private void OnExitParent(Func<DeclarationNodeBuilder, SyntaxNode> provider)
    {
        // error recovery can fire an Exit with no matching Enter — never pop the module builder.
        if (_builderStack.Count <= 1)
        {
            return;
        }
        var node = provider.Invoke(_builderStack.Pop());
        CurrentBuilder.AddChild(node);
    }

    private bool _isInsideProcedure = false;
    private bool _isAfterArgsList = false;
    private bool IsDeclarationPassExpression => !_isInsideProcedure || !_isAfterArgsList;

    private void OnModuleOptionDirective(SourceLocation location, ModuleOptions value) 
        => CurrentBuilder.AddChild(new ModuleOptionDirectiveNode(GetCurrentNodeId(), location, value));
    private void OnTypeDefDirective(string token, SourceLocation location, IEnumerable<(char from, char? to)> mappings, DefTypeUniversalPrefixMapping? universalMapping = default)
    {
        var prefixMappings = (universalMapping is null ? [] : new[] { universalMapping }).Concat(
            mappings.Select(map => new DefTypePrefixMapping(map.from, map.to))).ToImmutableArray();
        CurrentBuilder.AddChild(new TypeDefDirectiveNode(GetCurrentNodeId(), location, token, prefixMappings));
    }

    public override void EnterAttributeStmt([NotNull] VBAParser.AttributeStmtContext context)
        => OnEnterParent();
    public override void ExitAttributeStmt([NotNull] VBAParser.AttributeStmtContext context)
        => OnExitParent(builder => builder.BuildAttributeDirective(context));
    public override void ExitOptionBaseStmt([NotNull] VBAParser.OptionBaseStmtContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        // Option Base only accepts a bare 0 or 1; anything else the grammar's numberLiteral admits
        // (hex/oct/float, a suffix, an out-of-range value) is not base 1 and must not throw here.
        var isBase1 = int.TryParse(context.numberLiteral()?.GetText(), out var value) && value == 1;
        OnModuleOptionDirective(location, isBase1 ? ModuleOptions.OptionBase1 : ModuleOptions.OptionBase0);
    }
    public override void ExitOptionCompareStmt([NotNull] VBAParser.OptionCompareStmtContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        var value = context.TEXT() is not null ? ModuleOptions.OptionCompareText
                : context.DATABASE() is not null ? ModuleOptions.OptionCompareDatabase
                : ModuleOptions.OptionCompareBinary;

        OnModuleOptionDirective(location, value);
    }
    public override void ExitOptionExplicitStmt([NotNull] VBAParser.OptionExplicitStmtContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        OnModuleOptionDirective(location, ModuleOptions.OptionExplicit);
    }
    public override void ExitOptionPrivateModuleStmt([NotNull] VBAParser.OptionPrivateModuleStmtContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        OnModuleOptionDirective(location, ModuleOptions.OptionPrivateModule);
    }
    public override void ExitDefDirective([NotNull] VBAParser.DefDirectiveContext context)
    {
        var token = context.defType().GetText();
        var location = context.GetSourceLocation(_rootUri);

        DefTypeUniversalPrefixMapping? universalMapping = default;
        var mappings = new List<(char, char?)>([]);
        foreach (var spec in context.letterSpec())
        {
            if (spec.universalLetterRange() is not null)
            {
                universalMapping = new();
            }
            else if(spec.letterRange() is VBAParser.LetterRangeContext rangeContext)
            {
                var from = rangeContext.singleLetter()[0].GetText()[0];
                var to = rangeContext.singleLetter()[1].GetText()[0];
                mappings.Add(new(from, to));
            }
            else if (spec.singleLetter() is VBAParser.SingleLetterContext specContext)
            {
                mappings.Add(new(specContext.GetText()[0], null));
            }
        }
        OnTypeDefDirective(token, location, mappings, universalMapping);
    }

    public override void EnterImplementsStmt([NotNull] VBAParser.ImplementsStmtContext context) 
        => OnEnterParent();
    public override void ExitImplementsStmt([NotNull] VBAParser.ImplementsStmtContext context)
        => OnExitParent(builder => builder.BuildImplementsDirective(context));


    public override void EnterDeclareStmt([NotNull] VBAParser.DeclareStmtContext context)
        => OnEnterParent();
    public override void ExitDeclareStmt([NotNull] VBAParser.DeclareStmtContext context)
        => OnExitParent(builder => builder.BuildExternalDeclaration(context));

    public override void EnterEventStmt([NotNull] VBAParser.EventStmtContext context)
        => OnEnterParent();
    public override void ExitEventStmt([NotNull] VBAParser.EventStmtContext context)
        => OnExitParent(builder => builder.BuildEventDeclaration(context));

    public override void EnterUdtDeclaration([NotNull] VBAParser.UdtDeclarationContext context)
        => OnEnterParent();
    public override void ExitUdtDeclaration([NotNull] VBAParser.UdtDeclarationContext context)
        => OnExitParent(builder => builder.BuildUserDefinedTypeDeclaration(context));

    public override void EnterUdtMember([NotNull] VBAParser.UdtMemberContext context)
        => OnEnterParent();
    public override void ExitUdtMember([NotNull] VBAParser.UdtMemberContext context)
        => OnExitParent(builder => builder.BuildUserDefinedTypeMember(context));

    public override void EnterEnumerationStmt([NotNull] VBAParser.EnumerationStmtContext context)
        => OnEnterParent();
    public override void ExitEnumerationStmt([NotNull] VBAParser.EnumerationStmtContext context)
        => OnExitParent(builder => builder.BuildEnumDeclaration(context));

    public override void EnterEnumerationStmt_Constant([NotNull] VBAParser.EnumerationStmt_ConstantContext context)
        => OnEnterParent();

    public override void ExitEnumerationStmt_Constant([NotNull] VBAParser.EnumerationStmt_ConstantContext context)
        => OnExitParent(builder => builder.BuildEnumConstDeclaration(context));

    public override void EnterVariableSubStmt([NotNull] VBAParser.VariableSubStmtContext context)
        => OnEnterParent();
    public override void ExitVariableSubStmt([NotNull] VBAParser.VariableSubStmtContext context)
    {
        // recovery can leave Parent.Parent not pointing at the VariableStmt that carries the
        // visibility / Static token (`variableStmt : (DIM | STATIC | visibility) ...`).
        var parent = context.Parent?.Parent as VBAParser.VariableStmtContext;
        var modifier = NodeBuilder.ParseAccessModifier(parent?.visibility()?.GetText());
        var isStatic = parent?.STATIC() is not null;
        OnExitParent(builder => builder.BuildVariableDeclaration(context, modifier, isStatic));
    }

    public override void EnterConstSubStmt([NotNull] VBAParser.ConstSubStmtContext context)
        => OnEnterParent();
    public override void ExitConstSubStmt([NotNull] VBAParser.ConstSubStmtContext context)
    {
        var parent = context.Parent as VBAParser.ConstStmtContext;
        var modifier = NodeBuilder.ParseAccessModifier(parent?.visibility()?.GetText());
        OnExitParent(builder => builder.BuildConstDeclaration(context, _isInsideProcedure ? ConstKind.Local : ConstKind.ModuleMember, modifier));
    }

    // one node per `ReDim` target. a body-only statement — a module-level `ReDim` is illegal, and
    // the push/pop stays balanced because `_isInsideProcedure` cannot flip inside one target.
    public override void EnterRedimVariableDeclaration([NotNull] VBAParser.RedimVariableDeclarationContext context)
    {
        if (_isInsideProcedure)
        {
            OnEnterParent();
        }
    }
    public override void ExitRedimVariableDeclaration([NotNull] VBAParser.RedimVariableDeclarationContext context)
    {
        if (!_isInsideProcedure)
        {
            return;
        }
        // recovery can leave Parent.Parent not pointing at the redimStmt that carries `Preserve`.
        var isPreserve = (context.Parent?.Parent as VBAParser.RedimStmtContext)?.PRESERVE() is not null;
        OnExitParent(builder => builder.BuildRedimDeclaration(context, isPreserve));
    }

    private bool _isPropertyWriterMember = false;
    private void OnEnterProcedure(bool isPropertyWriter = false)
    {
        _isInsideProcedure = true;
        _isAfterArgsList = false;
        _isPropertyWriterMember = isPropertyWriter;
        OnEnterParent();
    }
    private void OnExitProcedure(Func<DeclarationNodeBuilder, SyntaxNode> provider)
    {
        OnExitParent(provider);
        _isInsideProcedure = false;
        _isAfterArgsList = false;
        _isPropertyWriterMember = false;
    }

    public override void EnterPropertyGetStmt([NotNull] VBAParser.PropertyGetStmtContext context)
        => OnEnterProcedure();

    public override void ExitPropertyGetStmt([NotNull] VBAParser.PropertyGetStmtContext context) 
        => OnExitProcedure(builder => builder.BuildPropertyGetDeclaration(context));

    public override void EnterPropertyLetStmt([NotNull] VBAParser.PropertyLetStmtContext context) 
        => OnEnterProcedure(isPropertyWriter: true);
    public override void ExitPropertyLetStmt([NotNull] VBAParser.PropertyLetStmtContext context) 
        => OnExitProcedure(builder => builder.BuildPropertyLetDeclaration(context));
    public override void EnterPropertySetStmt([NotNull] VBAParser.PropertySetStmtContext context) 
        => OnEnterProcedure(isPropertyWriter: true);
    public override void ExitPropertySetStmt([NotNull] VBAParser.PropertySetStmtContext context) 
        => OnExitProcedure(builder => builder.BuildPropertySetDeclaration(context));

    public override void EnterSubStmt([NotNull] VBAParser.SubStmtContext context)
        => OnEnterProcedure();
    public override void ExitSubStmt([NotNull] VBAParser.SubStmtContext context)
        => OnExitProcedure(builder => builder.BuildProcedureDeclaration(context));

    public override void EnterFunctionStmt([NotNull] VBAParser.FunctionStmtContext context)
        => OnEnterProcedure();
    public override void ExitFunctionStmt([NotNull] VBAParser.FunctionStmtContext context)
        => OnExitProcedure(builder => builder.BuildFunctionDeclaration(context));

    private void OnExpression(ExpressionNode expression) => CurrentBuilder.AddChild(expression);

    public override void ExitAsTypeClause([NotNull] VBAParser.AsTypeClauseContext context)
    {
        // a body-level `ReDim x(1) As Long` carries an asTypeClause; `EnterRedimVariableDeclaration`
        // has pushed a builder for it, so this node now lands on that RedimDeclarationNode (not the
        // enclosing member) — no special-casing needed here.

        // `As` with no type token (half-typed / recovery): the LL error listener already located the
        // "missing type" error — just don't build a broken node.
        if (context.type() is not { } type)
        {
            return;
        }

        // recovery can still leave `type` synthetic: a "<missing …>" placeholder (`Dim a As, b As
        // Long`), or the bare NEW keyword for `As New` with no class name (via complexType's ctNewExpr).
        var typeText = type.GetText();

        // `type` is `(baseType | complexType) ( '(' ')' )?` — keep the trailing array-of `()`
        // (`As Long()`) off the name; IsArrayDef carries it.
        if (type.LPAREN() is not null && typeText.IndexOf('(') is var paren and >= 0)
        {
            typeText = typeText[..paren].TrimEnd();
        }
        if (IdentifierNameExtensions.IsRecoveryPlaceholder(typeText)
            || string.IsNullOrEmpty(typeText)
            || string.Equals(typeText, "New", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var location = context.GetSourceLocation(_rootUri);
        var value = typeText.Split('.');

        var qualifier = value.Length > 1 ? value[0] : null;
        var name = value.Last();

        OnExpression(new AsTypeExpressionNode(GetCurrentNodeId(), location, name, qualifier,
            AsAutoObject: context.NEW() is not null,
            IsArrayDef: type.LPAREN() is not null));
    }

    public override void ExitSimpleNameExpr([NotNull] VBAParser.SimpleNameExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        var value = context.identifier().untypedIdentifier()?.GetText()
            ?? context.identifier().typedIdentifier().untypedIdentifier().GetText();
        var location = context.GetSourceLocation(_rootUri);
        OnExpression(new SimpleNameExpressionNode(GetCurrentNodeId(), location, value));
    }
    public override void ExitLiteralIdentifier([NotNull] VBAParser.LiteralIdentifierContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        var location = context.GetSourceLocation(_rootUri);
        if (context.booleanLiteralIdentifier() is VBAParser.BooleanLiteralIdentifierContext boolLiteralContext)
        {
            if (boolLiteralContext.FALSE() is not null)
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBBooleanValue.False));
            }
            else if (boolLiteralContext.TRUE() is not null)
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBBooleanValue.True));
            }
        }
        else if (context.objectLiteralIdentifier() is VBAParser.ObjectLiteralIdentifierContext objLiteralContext)
        {
            if (objLiteralContext.NOTHING() is not null)
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBObjectValue.Nothing));
            }
        }
        else if (context.variantLiteralIdentifier() is VBAParser.VariantLiteralIdentifierContext variantLiteralContext)
        {
            if (variantLiteralContext.EMPTY() is not null)
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBEmptyValue.Empty));
            }
        }
    }
    public override void ExitNumberLiteral([NotNull] VBAParser.NumberLiteralContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        var location = context.GetSourceLocation(_rootUri);
        var token = (context.INTEGERLITERAL() ?? context.FLOATLITERAL() ?? context.HEXLITERAL() ?? context.OCTLITERAL())?.GetText()
            ?? string.Empty;

        var (value, overflow) = NumericLiteral.Resolve(token);
        if (overflow)
        {
            _errors.Report(location, VBCompileErrorId.NumericLiteralOverflow, Exceptions.VBCompileError_NumericLiteralOverflow_Verbose);
        }
        OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, value));
    }
    public override void ExitUnaryMinusOp([NotNull] VBAParser.UnaryMinusOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        // VBA has no negative-literal token; `Const N = -1` is MINUS over the literal 1. This pass
        // captures leaf literals only, so fold the sign into the last one it added.
        if (CurrentBuilder.LastChild is LiteralExpressionNode literal
            && NumericLiteral.Negate(literal.StaticValue) is { } negated)
        {
            CurrentBuilder.UpdateLastChild(literal with { StaticValue = negated });
        }
    }
    public override void ExitLiteralExpression([NotNull] VBAParser.LiteralExpressionContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        var location = context.GetSourceLocation(_rootUri);
        if (context.DATELITERAL() is ITerminalNode dateLiteral)
        {
            if (DateTime.TryParse(dateLiteral.Symbol.Text.Trim('#'), out var rawValue))
            {
                OnExpression(new LiteralExpressionNode(
                    GetCurrentNodeId(), 
                    location, 
                    new VBDateValue(rawValue.ToOADate())));
            }
        }
        else if (context.STRINGLITERAL() is ITerminalNode stringLiteral)
        {
            OnExpression(new LiteralExpressionNode(
                GetCurrentNodeId(), 
                location,
                new VBStringValue(stringLiteral.Symbol.Text[1..^1])));
        }
    }

    private int _parameterIndex = 0;

    public override void EnterArgList([NotNull] VBAParser.ArgListContext context)
    {
        _parameterIndex = 0;
        _isAfterArgsList = false;
    }
    public override void ExitArgList([NotNull] VBAParser.ArgListContext context)
    {
        _parameterIndex = 0;
        _isAfterArgsList = true;
        // NOTE: do NOT set _isInsideProcedure here. It is already set by OnEnterProcedure for a real
        // Sub/Function/Property body; a Declare or Event has an argList but no body, and setting it
        // here left the flag poisoned (declaration-pass expression capture off) for every module-level
        // declaration between a Declare/Event and the next procedure. This whole flag disappears once
        // the listener also builds statement nodes (TokenSemanticsListener).

        if (_isPropertyWriterMember)
        {
            if (CurrentBuilder.LastChild is ParameterDeclarationNode node
                && node.ParameterKind != ParameterKind.ExplicitByVal)
            {
                // we cannot do this before knowing how many parameters there are,
                // because it's only applicable to the RHS/value parameter (last).
                CurrentBuilder.UpdateLastChild(
                    node with { ParameterKind = ParameterKind.ImplicitByVal });
            }
        }
    }
    public override void EnterArg([NotNull] VBAParser.ArgContext context)
        => OnEnterParent();
    public override void ExitArg([NotNull] VBAParser.ArgContext context)
    {
        var isPropertyWriter = _isPropertyWriterMember;
        OnExitParent(builder => builder.BuildParameterDeclaration(context, isPropertyWriter));
        _parameterIndex++;
    }

    public override void ExitStatementLabelDefinition([NotNull] VBAParser.StatementLabelDefinitionContext context)
    {
        string? name = default;
        int? number = default;
        var labelLocation = context.GetSourceLocation(_rootUri);
        var lineNumberLocation = labelLocation;

        if (context.standaloneLineNumberLabel()?.lineNumberLabel() is VBAParser.LineNumberLabelContext numContextA)
        {
            lineNumberLocation = numContextA.GetSourceLocation(_rootUri);
            number = LineNumber(numContextA);
        }
        else if (context.combinedLabels()?.lineNumberLabel() is VBAParser.LineNumberLabelContext numContextB)
        {
            lineNumberLocation = numContextB.GetSourceLocation(_rootUri);
            number = LineNumber(numContextB);
        }
        else if (context.identifierStatementLabel().legalLabelIdentifier().identifier() is VBAParser.IdentifierContext labelContextA)
        {
            labelLocation = labelContextA.GetSourceLocation(_rootUri);
            name = labelContextA.GetText();
        }
        else if (context.combinedLabels()?.identifierStatementLabel().legalLabelIdentifier().identifier() is VBAParser.IdentifierContext labelContextB)
        {
            labelLocation = labelContextB.GetSourceLocation(_rootUri);
            name = labelContextB.GetText();
        }

        if (number.HasValue)
        {
            CurrentBuilder.AddChild(new LineNumberNode(GetCurrentNodeId(), lineNumberLocation, number.Value));
        }
        if (name is not null)
        {
            CurrentBuilder.AddChild(new LineLabelNode(GetCurrentNodeId(), labelLocation, name));
        }

        // lineNumberLabel is `MINUS? numberLiteral`, so it can carry a sign or a non-decimal token;
        // only a bare non-negative integer is a line number, anything else contributes no node.
        static int? LineNumber(VBAParser.LineNumberLabelContext context)
            => context.MINUS() is null && int.TryParse(context.numberLiteral()?.GetText(), out var value)
                ? value
                : null;
    }
}
