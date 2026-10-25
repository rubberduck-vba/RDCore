using RDCore.SDK.Model;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The checks that the operands of a statement are held to, which the statements that have them
/// (<see cref="MidStatementStaticSemantics"/>, <see cref="FileStatementStaticSemantics"/>, and the assignments and the <c>For</c> loops
/// <see cref="StatementStaticSemanticsEvaluator"/> walks) state once instead of each of them restating.
/// </summary>
/// <remarks>
/// Each check reports into a list rather than returning on the first error, because the operands of a statement are largely independent of one
/// another: a statement with two wrong operands has two errors. And none of them reports what it cannot tell: an operand whose declared type is
/// <see cref="VBUnknownType"/> - a name nothing resolved, a member the library does not declare - is deferred, not rejected, as
/// <see cref="ExpressionStaticSemanticsEvaluator"/> does.
/// </remarks>
internal sealed class StatementOperandChecks(StaticEvaluationContext context)
{
    private readonly ImmutableArray<VBCompileErrorInfo>.Builder _errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

    /// <summary>Every error the checks found, in the order they were found.</summary>
    public ImmutableArray<VBCompileErrorInfo> Errors => _errors.ToImmutable();

    /// <summary>Reports an error a statement's own rule found.</summary>
    public void Add(VBCompileErrorInfo error) => _errors.Add(error);

    /// <summary>
    /// Evaluates <paramref name="expression"/>, reporting what stops it.
    /// </summary>
    /// <returns>Its declared type, or <see langword="null"/> when it has none: the evaluation failed, or the type is not known.</returns>
    public VBType? TypeOf(ExpressionNode expression)
    {
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, expression);
        if (result.IsError)
        {
            _errors.Add(result.ErrorInfo!);
            return null;
        }

        return result.Result is null or VBUnknownType ? null : result.Result;
    }

    /// <summary>
    /// "The declared type of <paramref name="expression"/> MUST be a scalar declared type": every declared type but an array and a UDT
    /// (<strong>MS-VBAL §2.2</strong>).
    /// </summary>
    public void RequireScalar(ExpressionNode expression, VBType? type, string role)
    {
        if (type is VBArrayType or VBUserDefinedType)
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must have a scalar declared type, and this is {(type is VBArrayType ? "an array" : $"the user-defined type {type.Name}")}."));
        }
    }

    /// <summary>
    /// "The declared type MUST be String or Variant."
    /// </summary>
    public void RequireStringOrVariant(ExpressionNode expression, VBType? type, string role)
    {
        if (type is not null && type is not (VBStringType or VBVariantType))
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must be declared as a String or a Variant, and this is a {type.Name}."));
        }
    }

    /// <summary>
    /// "The declared type MUST NOT be Object or a specific named class" - nor, for the record statements, a UDT whose definition
    /// recursively includes one.
    /// </summary>
    public void RequireNotAnObject(ExpressionNode expression, VBType? type, string role, bool throughUserDefinedTypes)
    {
        if (type is not null && IsObject(type, throughUserDefinedTypes))
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must not be declared as an Object or as a class{(throughUserDefinedTypes ? ", nor as a user-defined type that has one in it" : string.Empty)}, and this is a {type.Name}."));
        }
    }

    private static bool IsObject(VBType type, bool throughUserDefinedTypes) => type switch
    {
        VBObjectType or VBClassType => true,
        VBUserDefinedType udt when throughUserDefinedTypes => udt.Fields().Any(field => field.ResolvedType is { } fieldType && IsObject(fieldType, true)),
        _ => false,
    };

    /// <summary>
    /// "The value MUST be Let-coercible to the declared type <paramref name="destination"/>" (<strong>MS-VBAL §5.5.1.1</strong>).
    /// </summary>
    public void RequireLetCoercible(ExpressionNode expression, VBType? type, VBType destination)
    {
        if (type is null)
        {
            return;
        }

        var result = LetCoercionStaticSemantics.Instance.DetermineDeclaredType(context, expression, type, destination);
        if (result.IsError)
        {
            _errors.Add(result.ErrorInfo!);
        }
    }

    /// <summary>
    /// "The expression MUST be classified as a variable" (<strong>MS-VBAL §5.6.1</strong>).
    /// </summary>
    /// <remarks>
    /// Only what is certainly not a variable is rejected: a literal, an operator's result, a constant - by its name, qualified by the project or the
    /// module that declares it, or qualified by its Enum - and a function of a library. A member access, an index and the rest are not told apart
    /// here, since which of them is a variable depends on what they resolve to. A function of the workspace is not rejected either: inside its own
    /// body its name is its result, which is assigned to, and one assigned from outside its body is not judged here. Nor is a property, nor a
    /// variable named like a library's member, which is what the name resolves to - written with a type character too (<c>Left$</c> after
    /// <c>Dim Left$</c>), since a name is matched by its identifier (<strong>MS-VBAL §5.6.10</strong>).
    /// </remarks>
    /// <param name="expression">The operand.</param>
    /// <param name="role">What the operand is to its statement, which the message starts with.</param>
    /// <param name="section">The section of MS-VBAL that requires a variable there, which the message cites, if any.</param>
    public void RequireVariable(ExpressionNode expression, string role, string? section = null)
        => Report(expression, NotAVariable(expression, LibraryFunctions.None), $"{role} must be a variable", section);

    /// <summary>
    /// What <see cref="RequireVariable"/> requires, of the target of a <c>Let</c> statement: a function of a library whose declared type is a class,
    /// <c>Object</c> or <c>Variant</c> will do as well. <strong>MS-VBAL §5.4.3.8</strong> makes the statement invalid when the target "is classified
    /// as a function, does not refer to the enclosing procedure, and the declared type of &lt;l-expression&gt; is any type except a class or Object or
    /// Variant": <c>Left = "x"</c> is valid, and raises error 450 when it runs; <c>Left$ = "x"</c> is not.
    /// </summary>
    /// <param name="expression">The target.</param>
    /// <param name="role">What the operand is to its statement, which the message starts with.</param>
    /// <param name="section">The section of MS-VBAL that requires it, which the message cites.</param>
    public void RequireLetTarget(ExpressionNode expression, string role, string section)
        => Report(expression, NotAVariable(expression, LibraryFunctions.OfAClassObjectOrVariant), $"{role} must be a variable", section);

    /// <summary>
    /// What <see cref="RequireLetTarget"/> or <see cref="RequireVariable"/> require of the target of an assignment, a constant being
    /// <see cref="VBCompileErrorId.AssignmentToConstantNotPermitted"/> rather than <see cref="VBCompileErrorId.VariableRequired"/>, as MS-VBA has it
    /// (Excel-verified: a constant the code declares, and a member of an enumeration).
    /// </summary>
    /// <param name="expression">The target.</param>
    /// <param name="role">What the operand is to its statement, which the message starts with.</param>
    /// <param name="section">The section of MS-VBAL that requires it, which the message cites.</param>
    /// <param name="isLet">Whether the assignment is a <c>Let</c> statement, whose target may be a library's function of an object or <c>Variant</c> type.</param>
    public void RequireAssignmentTarget(ExpressionNode expression, string role, string section, bool isLet)
    {
        if (ConstantNamedBy(expression) is { } constant)
        {
            Report(expression, $"the constant {constant}", $"{role} must be a variable", section, VBCompileErrorId.AssignmentToConstantNotPermitted);
        }
        else if (FunctionNamedBy(expression, isLet ? FunctionTarget.Let : FunctionTarget.Set) is { } function)
        {
            RequireFunctionTarget(expression, function, isLet ? FunctionTarget.Let : FunctionTarget.Set);
        }
        else if (isLet)
        {
            RequireLetTarget(expression, role, section);
        }
        else
        {
            RequireVariable(expression, role, section);
        }
    }

    /// <summary>
    /// What a function named as an element of an <c>Erase</c> statement, outside its own body, must be (<see cref="RequireFunctionTarget(ExpressionNode, VBFunctionMemberSymbol, FunctionTarget)"/>).
    /// </summary>
    /// <param name="expression">The element.</param>
    public void RequireErasable(ExpressionNode expression)
    {
        if (FunctionNamedBy(expression, FunctionTarget.Erase) is { } function)
        {
            RequireFunctionTarget(expression, function, FunctionTarget.Erase);
        }
    }

    // what a function named as a target is used for.
    private enum FunctionTarget
    {
        Let,
        Set,
        Erase,
    }

    // A function named - by its name, or qualified by its module - as the target of an assignment or an element of an Erase statement, outside its own body,
    // is a call of it with no argument: what it returns is what is assigned, through its default member, or erased (MS-VBAL §5.4.3.8, §5.4.3.9, §5.4.3.4).
    // So is a property of a module that has no accessor for the assignment, the library's (Now, Timer) as the workspace's.
    // What cannot be one is a compile error, as MS-VBA has it (Excel-verified, oracle\s28): a Let of one whose declared type is neither a Variant nor an
    // object is "Function call on left-hand side of assignment must return Variant or Object" - "Can't assign to read-only property" for a property -
    // before what its parameters are (`Left$ = "x"`); one that takes an argument is "Argument not optional" (`Left = "x"`, and first for a Set and an
    // Erase: `Set Left$ = Nothing`); a Set of one that returns no object is "Invalid use of property", and so is a Set of the library's Err, which MS-VBA's
    // library declares a property; an Erase of one that cannot return an array is "Expected array". What passes is called when it runs: what it returns is
    // error 424 for a Let or a Set that has no object to assign through, and error 13 for an Erase that has no array.
    private void RequireFunctionTarget(ExpressionNode expression, VBReturningMemberSymbol function, FunctionTarget use)
    {
        var holdsAnything = function.ResolvedType is VBVariantType or VBUnknownType;
        var holdsAnObject = function.ResolvedType is VBObjectType or VBClassType;
        var takesAnArgument = function.Parameters.Any(parameter => parameter.Name != Tokens.Me && !parameter.IsOptional && parameter is not ParamArrayParameterSymbol);
        var isProperty = function is VBPropertyGetMemberSymbol;
        var what = $"the {(isProperty ? "property" : "function")} {function.Name}";

        var (id, requirement) = use switch
        {
            FunctionTarget.Let when !holdsAnything && !holdsAnObject && isProperty
                => (VBCompileErrorId.CantAssignToReadOnlyProperty, "A property assigned to must have a Property Let, or return a Variant or an object"),
            FunctionTarget.Let when !holdsAnything && !holdsAnObject
                => (VBCompileErrorId.FunctionCallOnLeftHandSideMustReturnVariantOrObject, "A function assigned to outside its own body must return a Variant or an object"),
            _ when takesAnArgument => (VBCompileErrorId.ArgumentNotOptional, "A function named as a target is called with no argument, and must take none"),
            FunctionTarget.Set when !holdsAnything && !holdsAnObject
                => (VBCompileErrorId.InvalidUseOfProperty, "A function Set-assigned to outside its own body must return a Variant or an object"),
            FunctionTarget.Set when function.ResolvedType is VBClassType && IsOfALibrary(function)
                => (VBCompileErrorId.InvalidUseOfProperty, "An object a library's function returns is not Set-assigned through it, the way a property's is not"),
            FunctionTarget.Erase when !holdsAnything => (VBCompileErrorId.ExpectedArray, "A function named in an Erase statement must return a Variant, which can hold an array"),
            _ => ((VBCompileErrorId?)null, string.Empty),
        };

        if (id is { } error)
        {
            Report(expression, what, requirement, use == FunctionTarget.Erase ? "5.4.3.4" : use == FunctionTarget.Let ? "5.4.3.8" : "5.4.3.9", error);
        }
    }

    // the function an expression names, by its name or qualified by its module, when it is not the procedure the expression is written in - whose name
    // there is its result; or the property of a module it names, when that property has no accessor for what is done with it: a Let assigns a property
    // that has a Property Let through it, and a Set one that has a Property Set. A class's own property, which is invoked on the object, is none of these.
    private VBReturningMemberSymbol? FunctionNamedBy(ExpressionNode expression, FunctionTarget use)
    {
        var symbol = expression switch
        {
            SimpleNameExpressionNode name => Named(name),
            MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
                => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol,
            _ => null,
        };

        return symbol switch
        {
            VBFunctionMemberSymbol function when function.Uri.AbsoluteUri != context.Scope.Uri.AbsoluteUri => function,
            VBPropertyGetMemberSymbol property when property.Uri.AbsoluteUri != context.Scope.Uri.AbsoluteUri
                && property.Parameters is not [{ Name: Tokens.Me }, ..] && !HasAccessorFor(property, use) => property,
            _ => null,
        };
    }

    // whether the module that declares a property declares the accessor an assignment of the kind goes through, of the same name.
    private bool HasAccessorFor(VBPropertyGetMemberSymbol property, FunctionTarget use)
        => use != FunctionTarget.Erase && context.Scope.SelfAndAncestors()
            .SelectMany(scope => scope.DeclaredAs(property.Name))
            .Any(declared => declared.ParentUri.AbsoluteUri == property.ParentUri.AbsoluteUri
                && (use == FunctionTarget.Let ? declared is VBPropertyLetMemberSymbol : declared is VBPropertySetMemberSymbol));

    // the name of the constant an expression is: a constant the code declares, by its name or through its module, or a member of an enumeration.
    private string? ConstantNamedBy(ExpressionNode expression) => expression switch
    {
        SimpleNameExpressionNode name when Named(name) is VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol => name.IdentifierName,
        MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
            => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol is VBConstantMemberSymbol or VBEnumConstMemberSymbol
                ? access.Member.IdentifierName
                : null,
        MemberAccessExpressionNode { Owner: { } owner } access
            when NameClassificationStaticSemantics.EnumerationOf(context, owner) is { } enumType
            => enumType.Members.FirstOrDefault(member => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase))?.Name,
        _ => null,
    };

    /// <summary>
    /// "MUST be classified as a variable, property, function or unbound member", as an element of an <c>Erase</c> statement is
    /// (<strong>MS-VBAL §5.4.3.4</strong>): anything but a value.
    /// </summary>
    /// <remarks>
    /// What <see cref="RequireVariable"/> rejects, but a function of a library: a literal, an operator's result and a constant are values.
    /// </remarks>
    /// <param name="expression">The operand.</param>
    /// <param name="role">What the operand is to its statement, which the message starts with.</param>
    /// <param name="section">The section of MS-VBAL that requires it, which the message cites.</param>
    public void RequireNotAValue(ExpressionNode expression, string role, string section)
        => Report(expression, NotAVariable(expression, LibraryFunctions.Any), $"{role} must be a variable, a property or a function", section);

    // which of a library's functions an operand may be.
    private enum LibraryFunctions
    {
        // none: what is to be a variable is never a function.
        None,

        // one whose declared type is a class, Object or Variant: the target of a Let statement (MS-VBAL §5.4.3.8).
        OfAClassObjectOrVariant,

        // any: an element of an Erase statement (MS-VBAL §5.4.3.4).
        Any,
    }

    private void Report(ExpressionNode expression, string? what, string requirement, string? section, VBCompileErrorId id = VBCompileErrorId.VariableRequired)
    {
        if (what is not null)
        {
            _errors.Add(VBCompileErrorInfo.For(id, expression.Location,
                $"{requirement}, and this is {what}{(section is null ? string.Empty : $" (MS-VBAL §{section})")}."));
        }
    }

    // what an expression that is certainly not a variable is, in words; null for one that may be - and for a function of a library that will do.
    private string? NotAVariable(ExpressionNode expression, LibraryFunctions functions) => expression switch
    {
        LiteralExpressionNode => "a literal",
        VBOperatorExpression => "the result of an operator",
        SimpleNameExpressionNode name => NotAVariable(Named(name), functions),
        // MS-VBAL §5.6.12: a member of a project or a procedural module is the member it resolves to in the namespace.
        MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
            => NotAVariable(context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol, functions),
        // and a member of an Enum is one of its constants (MS-VBAL §5.2.3.4); a name that is none of them is not found, which is another error.
        MemberAccessExpressionNode { Owner: { } owner } access
            when NameClassificationStaticSemantics.EnumerationOf(context, owner) is { } enumType
            && enumType.Members.FirstOrDefault(member => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)) is { } constant
            => $"the constant {constant.Name}",
        _ => null,
    };

    // what a simple name is. MS-VBAL §5.6.10 matches a name by its identifier, and only then judges its type character: a typed name (Left$) whose
    // identifier is a declaration of the workspace - a variable, a parameter, a procedure - is that declaration, even where the name as written finds
    // the library's member named with the character. An identifier that names no one declaration (two modules declare it) gets no verdict here:
    // which of them it is, is not known, and the library's member is not what it names.
    private Symbol? Named(SimpleNameExpressionNode name)
    {
        var symbol = context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol;
        if (name.TypeHint is { Length: > 0 } && symbol is VBFunctionMemberSymbol && IsOfALibrary(symbol))
        {
            var identified = context.Resolver.ResolveValue(name.IdentifierName, ScopeKind.Local, context.Scope.Uri);
            if (identified.Symbol is { } declaration)
            {
                return IsOfALibrary(declaration) ? symbol : declaration;
            }

            if (!identified.IsUnbound)
            {
                return null;
            }
        }

        return symbol;
    }

    private static bool IsOfALibrary(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) is not null;

    private static string? NotAVariable(Symbol? symbol, LibraryFunctions functions) => symbol switch
    {
        VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol => $"the constant {symbol.Name}",
        VBFunctionMemberSymbol function when function.GetProperty(SymbolProperties.Library) is { } library && !Admits(functions, function)
            => $"the function {function.Name} of the {library} library",
        _ => null,
    };

    // whether a function of a library will do. A declared type that is not known yet (a library's class named before it is read) may be a class.
    private static bool Admits(LibraryFunctions functions, VBFunctionMemberSymbol function) => functions switch
    {
        LibraryFunctions.Any => true,
        LibraryFunctions.OfAClassObjectOrVariant => function.ResolvedType is VBClassType or VBObjectType or VBVariantType or VBUnknownType,
        _ => false,
    };
}
