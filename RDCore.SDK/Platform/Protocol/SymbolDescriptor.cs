using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// A transport-friendly projection of one declared member <see cref="Symbol"/>. Declared types
/// travel as names, not resolved <c>VBType</c>s — the environment host is the resolution authority.
/// </summary>
public record class SymbolDescriptor
{
    /// <summary>
    /// The member's identifier name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Which kind of member symbol to reconstruct.
    /// </summary>
    public SymbolDescriptorKind Kind { get; init; }

    /// <summary>
    /// The declared access modifier; <see cref="AccessModifier.Implicit"/> when none was written.
    /// </summary>
    public AccessModifier AccessModifier { get; init; } = AccessModifier.Implicit;

    /// <summary>
    /// The allocation scope the symbol is defined in.
    /// </summary>
    public ScopeKind Scope { get; init; } = ScopeKind.Instance;

    /// <summary>
    /// The declared type's name — from an <c>As</c> clause or a type-declaration character — or
    /// <c>null</c> when there is none, or the type is not known (an array definition needs a later semantic pass).
    /// A type of a referenced library other than the standard library (VBA) carries the library's name, and a
    /// user-defined type or an Enum of another module of the workspace the module's (<c>Mod2.TRec</c>), which the host
    /// resolves within that library or module. A type of the standard library carries its own name.
    /// For an array, the name of its <em>element</em> type, and <see cref="Array"/> says what kind of array it is.
    /// </summary>
    public string? DeclaredTypeName { get; init; }

    /// <summary>
    /// What is declared is an array of <see cref="DeclaredTypeName"/>: how it is sized, which a type name has no room for.
    /// <c>null</c> for anything that is not an array.
    /// </summary>
    public ArrayDescriptor? Array { get; init; }

    /// <summary>
    /// The length of a fixed-length <c>String * n</c> (<strong>MS-VBAL §5.2.3.1.4</strong>) - for an array, of its elements - which the name
    /// <c>String</c> has no room for. <c>null</c> for any other type.
    /// </summary>
    public int? FixedLength { get; init; }

    /// <summary>
    /// The constant <see cref="FixedLength"/> is the value of, as the declaration names it (<see cref="SymbolProperties.FixedLengthName"/>); <c>null</c> when
    /// the length is written as a number, and for any other type.
    /// </summary>
    public string? FixedLengthName { get; init; }

    /// <summary>
    /// The source span of the whole declaration — the primary site (the first branch) when the
    /// member has multiple <see cref="Definitions"/>.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The source span to select when navigating to the symbol (typically its name token) — the
    /// primary site when the member has multiple <see cref="Definitions"/>.
    /// </summary>
    public SourceRange SelectionRange { get; init; }

    /// <summary>
    /// The declaration sites of this member, in source order — populated only when the same name is
    /// declared in more than one conditional-compilation branch. Empty for the common
    /// single-declaration case, in which <see cref="Range"/>/<see cref="SelectionRange"/> are the
    /// sole site. A consumer that does not support multi-branch symbols can ignore this and use
    /// <see cref="Range"/>.
    /// </summary>
    public ImmutableArray<DefinitionDescriptor> Definitions { get; init; } = [];

    /// <summary>
    /// Parameters, for the procedure, function, property and event kinds.
    /// </summary>
    public ImmutableArray<ParameterDescriptor> Parameters { get; init; } = [];

    /// <summary>
    /// The procedure-extent variables the member declares — one per <c>Dim</c> or <c>Static</c> — for
    /// the procedure, function and property kinds.
    /// </summary>
    /// <remarks>
    /// A local is not a member of the module and is never resolvable from outside its procedure, so it
    /// rides on its own procedure's descriptor rather than appearing beside it. It has to travel at
    /// all because the procedure symbol is what an invocation allocates frame storage from
    /// (<strong>MS-VBAL §5.4.3</strong> step 4): a procedure reconstructed without its locals has a
    /// body that cannot assign to any of them.
    /// </remarks>
    public ImmutableArray<LocalDescriptor> Locals { get; init; } = [];

    /// <summary>
    /// The procedure-local <c>Const</c> declarations the member owns, for the procedure, function and
    /// property kinds — and, for a <see cref="SymbolDescriptorKind.ModuleConstant"/> descriptor, the
    /// single entry that is the module-level constant's own value.
    /// </summary>
    /// <remarks>
    /// A constant is not a variable: it has no storage, so it cannot ride on <see cref="Locals"/>, whose
    /// every entry an activation allocates frame storage for. It travels instead because it has no
    /// storage — with nowhere to read a value back from, the host can only substitute the declaration's
    /// own expression at each use site, and needs that expression to do it.
    /// </remarks>
    public ImmutableArray<ConstantDescriptor> Constants { get; init; } = [];

    /// <summary>
    /// Whether a module field is declared <c>WithEvents</c> (<strong>MS-VBAL §5.2.3.1.2</strong>): the procedures of its
    /// module named for it and an event of its class handle that event of the object it holds.
    /// </summary>
    public bool IsWithEvents { get; init; }

    /// <summary>
    /// Whether a member is one of a form's controls: declared by the form's designer, not by its code (<see cref="SymbolProperties.DesignerControl"/>).
    /// </summary>
    public bool IsDesignerControl { get; init; }

    /// <summary>
    /// Whether a form's control is a Visual Basic 6.0 control array. <see cref="DeclaredTypeName"/> then names the class of its controls, and its type is
    /// the array's (<see cref="Workspace.Forms.FormControlArray.TypeOf"/>), which no library names.
    /// </summary>
    public bool IsDesignerControlArray { get; init; }

    /// <summary>
    /// What runs a member whose code is not the module's (<see cref="SymbolProperties.ExternalTarget"/>): a member a form's designer declares on its class - a
    /// control, a member of the form object - is dispatched to whatever hosts the form (<see cref="Workspace.Forms.FormObject.TargetOf"/>).
    /// </summary>
    public string? ExternalTarget { get; init; }

    /// <summary>
    /// The member's <c>VB_UserMemId</c> (<strong>MS-VBAL §5.2.1</strong>), when its module says one: <c>0</c> marks the default member of a class, <c>-4</c> its enumeration
    /// member (<c>_NewEnum</c>). <see langword="null"/> for a member with none.
    /// </summary>
    /// <remarks>
    /// It travels because the host finds the default member and the enumeration member of a class by it - for <c>c(1)</c> and for <c>For Each</c> - and the host reads no
    /// source of its own to find the attribute in.
    /// </remarks>
    public int? UserMemId { get; init; }

    /// <summary>
    /// Whether a module field is an automatic instantiation variable (<strong>MS-VBAL §2.5.1</strong>): declared
    /// <c>As New</c>, so that referring to it while it is <c>Nothing</c> creates the object.
    /// </summary>
    public bool IsAutoInstantiated { get; init; }

    /// <summary>
    /// Members parented to this descriptor rather than the module: <c>Enum</c> constants and
    /// user-defined-<c>Type</c> fields.
    /// </summary>
    public ImmutableArray<SymbolDescriptor> Members { get; init; } = [];

    /// <summary>
    /// How an <c>Enum</c> member's value is declared (<strong>MS-VBAL §5.2.3.4</strong>); <c>null</c> unless <see cref="Kind"/> is
    /// <see cref="SymbolDescriptorKind.EnumMember"/>.
    /// </summary>
    /// <remarks>
    /// Like a constant's (<see cref="ConstantDescriptor.Value"/>), the expression travels rather than a value: the host is where the
    /// evaluator that reduces it lives.
    /// </remarks>
    public EnumMemberValue? EnumValue { get; init; }

    /// <summary>
    /// <c>Declare</c>-statement metadata; <c>null</c> unless <see cref="Kind"/> is
    /// <see cref="SymbolDescriptorKind.ExternalProcedure"/> or <see cref="SymbolDescriptorKind.ExternalFunction"/>.
    /// </summary>
    public ExternalDescriptor? External { get; init; }
}

/// <summary>
/// A transport-friendly projection of one <c>SymbolDefinition</c> — a single declaration site of a
/// member declared in more than one conditional-compilation branch.
/// </summary>
public record class DefinitionDescriptor
{
    /// <summary>
    /// The full source span of this declaration site.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The source span to select when navigating to this site (typically its name token).
    /// </summary>
    public SourceRange SelectionRange { get; init; }

    /// <summary>
    /// Whether this site's conditional-compilation branch compiles.
    /// <see cref="DefinitionState.Unknown"/> until a precompiler-evaluation pass resolves it.
    /// </summary>
    public DefinitionState State { get; init; } = DefinitionState.Unknown;
}

/// <summary>
/// A transport-friendly projection of a <c>VBLocalVariableSymbol</c> — one <c>Dim</c> or <c>Static</c>
/// of a procedure.
/// </summary>
public record class LocalDescriptor
{
    /// <summary>
    /// The variable's identifier name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The declared type's name, or <c>null</c> — resolved host-side like a member's. For an array, its element type's.
    /// </summary>
    public string? DeclaredTypeName { get; init; }

    /// <summary>The array the variable is, when it is one: see <see cref="SymbolDescriptor.Array"/>.</summary>
    public ArrayDescriptor? Array { get; init; }

    /// <summary>The length of a fixed-length string the variable is, or the elements of the array it is: see <see cref="SymbolDescriptor.FixedLength"/>.</summary>
    public int? FixedLength { get; init; }

    /// <summary>The constant <see cref="FixedLength"/> is the value of: see <see cref="SymbolDescriptor.FixedLengthName"/>.</summary>
    public string? FixedLengthName { get; init; }

    /// <summary>
    /// Whether the declaration carries the <c>Static</c> token (<strong>MS-VBAL §5.4.3.1</strong>):
    /// module-extent storage that outlives one activation, not procedure-extent.
    /// </summary>
    public bool IsStatic { get; init; }

    /// <summary>
    /// Whether the variable is an automatic instantiation variable (<strong>MS-VBAL §2.5.1</strong>): declared
    /// <c>As New</c>.
    /// </summary>
    public bool IsAutoInstantiated { get; init; }

    /// <summary>
    /// How the variable entered the procedure scope — a real declaration, or an implicit one.
    /// </summary>
    public LocalDeclarationKind DeclaredBy { get; init; } = LocalDeclarationKind.Dim;

    /// <summary>
    /// The source span of the declaration.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The source span to select when navigating to the variable.
    /// </summary>
    public SourceRange SelectionRange { get; init; }

    /// <summary>
    /// Where the declarations are that keep this implicit or <c>ReDim</c> local from being declared, each in a branch of a <c>#If</c> block: the host
    /// declares it only when every one of them is in a dead branch (<see cref="SymbolProperties.UnlessLive"/>). Empty for a local that is always declared.
    /// </summary>
    public ImmutableArray<SourcePosition> UnlessLive { get; init; } = [];
}

/// <summary>
/// A transport-friendly projection of a <c>Const</c> declaration — a module-level
/// <c>VBConstantMemberSymbol</c> or a procedure-local <c>VBLocalConstantSymbol</c>.
/// </summary>
public record class ConstantDescriptor
{
    /// <summary>
    /// The constant's identifier name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The declared type's name, or <c>null</c> — resolved host-side like a member's.
    /// </summary>
    public string? DeclaredTypeName { get; init; }

    /// <summary>
    /// The declaration's own constant expression, or <c>null</c> when it carried none.
    /// </summary>
    /// <remarks>
    /// The expression, not a value: a constant expression is not always a literal (<c>Const K = 3 * 5</c>,
    /// or one constant written in terms of another), and the host is where the evaluator that can reduce
    /// it lives.
    /// </remarks>
    public ExpressionNode? Value { get; init; }

    /// <summary>
    /// The source span of the declaration.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The source span to select when navigating to the constant.
    /// </summary>
    public SourceRange SelectionRange { get; init; }
}

/// <summary>
/// A transport-friendly projection of a <c>VBParameterSymbol</c>.
/// </summary>
public record class ParameterDescriptor
{
    /// <summary>
    /// The parameter's identifier name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// How an argument is passed to the parameter.
    /// </summary>
    public ParameterKind ParameterKind { get; init; } = ParameterKind.ImplicitByRef;

    /// <summary>
    /// Whether the parameter carries an <c>Optional</c> token.
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>
    /// Whether the parameter is a <c>ParamArray</c>.
    /// </summary>
    public bool IsParamArray { get; init; }

    /// <summary>
    /// The value of the parameter's <c>default-value</c> clause (MS-VBAL 5.3.1.5), or <c>null</c> when the
    /// declaration has none — the host then falls back to the declared type's own default value.
    /// </summary>
    /// <remarks>
    /// The expression, not a value: a constant expression is not always a literal (<c>Optional k As
    /// Long = 3 * 5</c>, or one written in terms of a <c>Const</c>), and the host is where the evaluator
    /// that can reduce it lives — the same reason <see cref="ConstantDescriptor.Value"/> travels
    /// unreduced, and it is reduced by the same fold.
    /// </remarks>
    public ExpressionNode? DefaultValue { get; init; }

    /// <summary>
    /// The declared type's name, or <c>null</c> — resolved host-side like a member's.
    /// </summary>
    public string? DeclaredTypeName { get; init; }

    /// <summary>The array the parameter is, when it is one: see <see cref="SymbolDescriptor.Array"/>.</summary>
    public ArrayDescriptor? Array { get; init; }

    /// <summary>
    /// The source span of the parameter declaration.
    /// </summary>
    public SourceRange Range { get; init; }
}

/// <summary>
/// What an array declaration says beyond its element type (<strong>MS-VBAL §5.2.3.1.3</strong>): whether it is fixed-size, and the
/// bounds it was declared with.
/// </summary>
public record class ArrayDescriptor
{
    /// <summary>
    /// Whether the array is fixed-size - declared with bounds - rather than resizable, which has no dimensions until a <c>ReDim</c>
    /// gives it some.
    /// </summary>
    public bool IsFixedSize { get; init; }

    /// <summary>
    /// The bounds of a fixed-size array, one for each dimension and outermost first; empty for a resizable one.
    /// </summary>
    public ImmutableArray<ArrayBoundDescriptor> Bounds { get; init; } = [];
}

/// <summary>
/// The bounds of one dimension of a fixed-size array.
/// </summary>
/// <remarks>
/// Expressions, not numbers: a bound is a constant expression that may name a <c>Const</c>, and the host is where the evaluator that
/// reduces one lives (see <see cref="ConstantDescriptor.Value"/>).
/// </remarks>
public record class ArrayBoundDescriptor
{
    /// <summary>The lower bound, or <c>null</c> when the dimension was declared with an upper bound alone, which <c>Option Base</c> decides.</summary>
    public ExpressionNode? Lower { get; init; }

    /// <summary>The upper bound.</summary>
    public ExpressionNode? Upper { get; init; }
}

/// <summary>
/// The <c>Declare</c>-statement metadata of an external member.
/// </summary>
public record class ExternalDescriptor
{
    /// <summary>
    /// Whether the declaration carries the <c>PtrSafe</c> token.
    /// </summary>
    public bool IsPtrSafe { get; init; }

    /// <summary>
    /// The library name from the <c>Lib "…"</c> clause.
    /// </summary>
    public string Library { get; init; } = string.Empty;

    /// <summary>
    /// The exported name from the <c>Alias "…"</c> clause, or <c>null</c>.
    /// </summary>
    public string? Alias { get; init; }
}

/// <summary>
/// The member-symbol kinds a <see cref="SymbolDescriptor"/> can carry. Mirrors the subset of
/// <see cref="MemberKind"/> the language server's AST symbol provider emits, split so <c>Property</c>
/// accessors and <c>Enum</c>/<c>Type</c> members each map to one runtime symbol type.
/// </summary>
public enum SymbolDescriptorKind
{
    /// <summary>
    /// A <c>Sub</c> procedure.
    /// </summary>
    Procedure,

    /// <summary>
    /// A <c>Function</c> procedure.
    /// </summary>
    Function,

    /// <summary>
    /// A <c>Property Get</c> accessor.
    /// </summary>
    PropertyGet,

    /// <summary>
    /// A <c>Property Let</c> accessor.
    /// </summary>
    PropertyLet,

    /// <summary>
    /// A <c>Property Set</c> accessor.
    /// </summary>
    PropertySet,

    /// <summary>
    /// A <c>Declare Sub</c>.
    /// </summary>
    ExternalProcedure,

    /// <summary>
    /// A <c>Declare Function</c>.
    /// </summary>
    ExternalFunction,

    /// <summary>
    /// An <c>Event</c> declaration.
    /// </summary>
    Event,

    /// <summary>
    /// A <c>Type … End Type</c> declaration.
    /// </summary>
    UserDefinedType,

    /// <summary>
    /// A field of a user-defined <c>Type</c>.
    /// </summary>
    UserDefinedTypeField,

    /// <summary>
    /// An <c>Enum … End Enum</c> declaration.
    /// </summary>
    Enum,

    /// <summary>
    /// A member of an <c>Enum</c>.
    /// </summary>
    EnumMember,

    /// <summary>
    /// A module-scoped variable (<c>Dim</c>/<c>Private</c>/<c>Public</c> field).
    /// </summary>
    ModuleField,

    /// <summary>
    /// A module-scoped <c>Const</c>.
    /// </summary>
    ModuleConstant,
}
