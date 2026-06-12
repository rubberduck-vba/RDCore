namespace RDCore.SDK.Model.AST.Expressions;

// TODO migrate to types dedicated to the runtime semantics for member and dictionary access

public static class VBMemberAccessOperatorExpression
{
    /*
    public static VBTypedValue EvaluateBinaryMemberAccess(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs)
    {
        if (lhs.TypeInfo is VBVariantType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.LateBoundMemberAccess(lhs.Symbol?.Range!));
        }

        if (lhs is IVBMemberOwnerType lhsOwner)
        {
            if (rhs.Symbol is not null)
            {
                var members = lhsOwner.Members.ToLookup(e => e.Name);
                var candidates = members[rhs.Symbol.Name];
                if (candidates.Any())
                {
                    // TODO make this statically deterministic, not based on where we found it in the source document.
                    var member = candidates.OrderBy(e => e.Uri).First();
                    var value = context.Memory.GetValue(member);

                    return value;
                }
                else
                {
                    // NOTE: LHS member owner could be a class, a stdmodule, an enum, or a UDT.
                    if (lhsOwner is not VBClassType lhsClassType)
                    {
                        throw VBCompileErrorException.MethodOrDataMemberNotFound(rhs.Symbol.SelectionRange!);
                    }

                    // if LHS is a class type, let's be nice and work with a deferred member instead:
                    return new VBDeferredMemberValue(expression.Symbol)
                           .WithContext(lhs)
                           .WithName(rhs.Symbol.Name)
                           .WithDiagnostic(RDCoreDiagnostic.UnresolvedLateBoundMemberAccess(rhs.Symbol.SelectionRange!));
                }
            }
            else
            {
                // user has typed the dot, but not the member name yet.
                // the members should be returned to the client to populate a completion list.
                // we're using VBVoidValue here to signal this:
                return VBVoidValue.Void;
            }
        }
        else
        {
            // Given a `NonExistingModule.NonExistingMember` member call where neither is defined:
            if (context.CurrentScope.ScopeSymbol is VBTypeMemberSymbol)
            {
                // VBA throws a compile error (variable not defined) if the code is inside the editor (scoped context)
                throw VBCompileErrorException.VariableNotDefined(lhs.Symbol?.SelectionRange!);
            }
            else
            {
                // VBA throws a runtime error (VBR00424 object required) if the same code is inside the immediate pane (default context)
                throw VBRuntimeErrorException.ObjectRequired(lhs.Symbol?.SelectionRange!);
            }
        }

        throw VBCompileErrorException.SyntaxError(expression.Location.Range, "An identifier is expected");
    }

    public static VBTypedValue EvaluateBinaryDictionaryAccess(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBStringValue rhs)
    {
        context.AddDiagnostic(RDCoreDiagnostic.LateBoundMemberAccess(expression.Symbol.SelectionRange!));

        if (lhs is not IVBMemberOwnerType lhsOwner ||
            lhsOwner.Members.FirstOrDefault(member => member.Get(SymbolProperties.UserMemId) == 0) is not VBTypeMemberSymbol defaultMember)
        {
            throw VBRuntimeErrorException.ObjectDoesntSupportPropertyOrMethod(lhs.Symbol?.SelectionRange!);
        }

        if (rhs.Symbol is null)
        {
            // user has typed the bang, but not the member name yet (NOTE: it may have been parsed as a type hint)
            // the members could be returned to the client to populate a completion list.
            // we're using VBVoidValue here to signal this:
            return VBVoidValue.Void;
        }

        return new VBDeferredMemberValue(expression.Symbol)
               .WithContext(lhs)
               .WithName(rhs.Symbol.Name)
               .WithDiagnostic(RDCoreDiagnostic.UnresolvedLateBoundMemberAccess(rhs.Symbol.SelectionRange!));
    }
    */
}