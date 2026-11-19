using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Formatting;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Semantics.References;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// What Encapsulate Field refuses or would change of the field named at a position, and the edit that encapsulates it.
/// </summary>
internal interface IEncapsulateFieldService
{
    /// <summary>
    /// What the encapsulation of the field named at a position asks its user, and why it is not made or what it would change of what the code does, with the
    /// defaults.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document: on the field's declaration or on a reference to it.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The choices, each with its default, and the notices: a refusal, or the changes the user is asked to accept.</returns>
    Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token);

    /// <summary>
    /// Whether the name answered for the backing field can be declared in the field's module, before the encapsulation is applied.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="answers">The answers so far.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The refusal of the name, if it is refused.</returns>
    Task<RefactorIdentifierCheckResult> CheckIdentifierAsync(Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, CancellationToken token);

    /// <summary>
    /// The edit that encapsulates the field named at a position with the user's answers, verified against the workspace as it leaves it; or why it is not
    /// made.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="answers">The answers to the choices; a choice that is not answered takes its default.</param>
    /// <param name="accepted">The codes of the changes the user accepted.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The edit, or why it is not made.</returns>
    Task<EncapsulateFieldAnswer> ApplyAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token);
}

/// <summary>
/// The edit that encapsulates a field, or why it is not made.
/// </summary>
/// <param name="Edit">The edit; <see langword="null"/> when it is not made.</param>
/// <param name="Refusal">Why it is not made, in the language of the user interface; <see langword="null"/> when it is.</param>
internal sealed record class EncapsulateFieldAnswer(RefactorWorkspaceEdit? Edit, string? Refusal);

internal sealed class EncapsulateFieldService(IRenameService rename, IParsingClientService parsing, IOptions<SdkAppOptions> options) : IEncapsulateFieldService
{
    /// <summary>
    /// The code of the change a reference makes that passes the field to a parameter that may be <c>ByRef</c>: the procedure writes to a copy of the
    /// property's value.
    /// </summary>
    public const string PassedByRefCode = "encapsulateField.passedByRef";

    /// <summary>
    /// The code of the refusal of a position that names no field: a code action offers nothing there.
    /// </summary>
    public const string NotAFieldCode = "encapsulateField.notAField";

    /// <summary>
    /// The choice of what holds the value: <see cref="ThisMode"/> or <see cref="BackingFieldMode"/>.
    /// </summary>
    public const string ModeChoice = "mode";

    /// <summary>
    /// The value held by a member of the module's private type, through its single variable: the default.
    /// </summary>
    public const string ThisMode = "this";

    /// <summary>
    /// The value held by a <c>Private</c> field of the module, named by <see cref="BackingFieldChoice"/>.
    /// </summary>
    public const string BackingFieldMode = "backingField";

    /// <summary>
    /// The name of the backing field, in <see cref="BackingFieldMode"/>: <c>m</c> and the field's name by default.
    /// </summary>
    public const string BackingFieldChoice = "backingField";

    /// <summary>
    /// Whether only a <c>Property Get</c> is written: <c>false</c> by default.
    /// </summary>
    public const string ReadOnlyChoice = "readOnly";

    /// <summary>
    /// The access modifier of the <c>Property Get</c> (<strong>MS-VBAL §5.3.1.1</strong>): <see cref="PublicAccess"/> by default,
    /// <see cref="FriendAccess"/> in an object module only, or <see cref="PrivateAccess"/>.
    /// </summary>
    public const string ReadAccessChoice = "readAccess";

    /// <summary>
    /// The access modifier of the <c>Property Let</c> and <c>Property Set</c> (<strong>MS-VBAL §5.3.1.1</strong>): <see cref="FriendAccess"/> by default
    /// in an object module, where a <c>Private</c> field with <c>Friend</c> writers is the field only its project assigns; <see cref="PublicAccess"/> by
    /// default in a standard module, which has no <c>Friend</c> procedure; or <see cref="PrivateAccess"/>. Not read when <see cref="ReadOnlyChoice"/> is
    /// <c>true</c>.
    /// </summary>
    public const string WriteAccessChoice = "writeAccess";

    /// <summary>
    /// A property declared <c>Public</c>: reached from any module, and from the projects that reference its own.
    /// </summary>
    public const string PublicAccess = "public";

    /// <summary>
    /// A property declared <c>Friend</c> (<strong>MS-VBAL §5.3.1.1</strong>): reached from the modules of its own project only.
    /// </summary>
    public const string FriendAccess = "friend";

    /// <summary>
    /// A property declared <c>Private</c>: reached from its own module only.
    /// </summary>
    public const string PrivateAccess = "private";

    /// <summary>
    /// What the references inside the module go through: <see cref="StateInside"/> or <see cref="PropertiesInside"/>.
    /// </summary>
    public const string InsideChoice = "inside";

    /// <summary>
    /// The references inside the module read and write the value where it is held: the default, which keeps what a call that writes through a <c>ByRef</c>
    /// parameter does.
    /// </summary>
    public const string StateInside = "state";

    /// <summary>
    /// The references inside the module go through the properties, but for those that assign the field where no property takes the assignment.
    /// </summary>
    public const string PropertiesInside = "properties";

    /// <summary>
    /// The other <c>Public</c> fields of the module, a list whose items are encapsulated in the same edit when the user picks them
    /// (<see cref="EncapsulateChoice"/>): each with its own <see cref="ReadOnlyChoice"/>, <see cref="ReadAccessChoice"/>, <see cref="WriteAccessChoice"/>
    /// and <see cref="BackingFieldChoice"/>, held as the field named is
    /// (<see cref="ModeChoice"/>). An item is named by its field's name.
    /// </summary>
    public const string FieldsChoice = "fields";

    /// <summary>
    /// Whether an item of <see cref="FieldsChoice"/> is encapsulated with the field named: <c>false</c> by default.
    /// </summary>
    public const string EncapsulateChoice = "encapsulate";

    /// <summary>
    /// The code of the change a reference makes that passes an element of the field, an array, to a call that may take it <c>ByRef</c>: the call writes to an
    /// element of a copy of the property's value.
    /// </summary>
    public const string ElementPassedCode = "encapsulateField.elementPassed";

    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.EncapsulateFieldMessages", typeof(EncapsulateFieldService).Assembly);

    /// <summary>
    /// The title of the code action that encapsulates a field, in the language of the user interface.
    /// </summary>
    public static string Title() => Format("EncapsulateField_Title");

    /// <summary>
    /// The title of the code action that encapsulates the field named and every other <c>Public</c> field of its module, in the language of the user
    /// interface.
    /// </summary>
    public static string AllTitle() => Format("EncapsulateField_AllTitle");

    /// <summary>
    /// The answers that pick every item of the list of the other fields, with the defaults of every other choice: none when the choices have no list.
    /// </summary>
    /// <param name="choices">The choices prepared.</param>
    public static IReadOnlyList<RefactorAnswer> AllFields(IReadOnlyList<RefactorChoice> choices)
        => choices.FirstOrDefault(choice => choice.Id == FieldsChoice) is { Items.Count: > 0 } fields
            ?
            [
                new RefactorAnswer
                {
                    Id = FieldsChoice,
                    Items = [.. fields.Items.Select(item => new RefactorItemAnswer { Id = item.Id, Answers = [new RefactorAnswer { Id = EncapsulateChoice, Value = "true" }] })],
                },
            ]
            : [];

    /// <summary>
    /// Why an encapsulation offered for one version of a document is not made for another, in the language of the user interface.
    /// </summary>
    public static string DocumentChanged() => Format("EncapsulateField_DocumentChanged");

    public async Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        var (planned, part, refusal) = await PlannedAsync(documentUri, position, [], token);
        if (planned?.Encapsulation is not { } encapsulation)
        {
            return new RefactorPrepareResult { Notices = [Refused(refusal!.Value, part)] };
        }

        // each other field is planned alone, with the defaults: what refuses it, or what it would change, is of the answer that picks it.
        var notices = Changes(encapsulation, part!).ToList();
        foreach (var (other, _) in planned.Others)
        {
            var (alone, refused) = EncapsulateField.Plan(part!.Context, planned.Module.Module, planned.Module.Parse, other);
            var ofOther = alone is null ? [Refused((refused!, other.Name), part)] : Changes(alone, part);
            notices.AddRange(ofOther.Select(notice => notice with { Choice = EncapsulateChoice, Item = other.Name }));
        }

        return new RefactorPrepareResult
        {
            Choices = Choices(planned.Field.Name, planned.Others, EncapsulateField.IsOfAnObjectModule(planned.Field)), Notices = notices,
        };
    }

    public async Task<RefactorIdentifierCheckResult> CheckIdentifierAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, CancellationToken token)
    {
        // only a name typed for a backing field is checked: the plan of the other choices refuses nothing a name changes. The name typed for the field named
        // is its answer's, and one typed for another field its item's.
        if (!string.Equals(Answer(answers, ModeChoice), BackingFieldMode, StringComparison.OrdinalIgnoreCase))
        {
            return new RefactorIdentifierCheckResult();
        }

        var (planned, part, refusal) = await PlannedAsync(documentUri, position, answers, token);
        if (refusal is not ({ Kind: EncapsulationRefusalKind.NotAName or EncapsulationRefusalKind.NameTaken, Name: { } name } kind, _) refused)
        {
            return new RefactorIdentifierCheckResult();
        }

        var item = kind.Field is { } field && planned?.Field is { } named && field.SemanticId != named.SemanticId ? field.Name : null;
        var typed = item is null ? Answer(answers, BackingFieldChoice) : Answer(ItemAnswers(answers, item), BackingFieldChoice);
        return typed is { Length: > 0 } && string.Equals(name, typed.Trim(), StringComparison.OrdinalIgnoreCase)
            ? new RefactorIdentifierCheckResult { Notices = [Refused(refused, part) with { Choice = BackingFieldChoice, Item = item }] }
            : new RefactorIdentifierCheckResult();
    }

    public async Task<EncapsulateFieldAnswer> ApplyAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token)
    {
        if (UnknownAnswer(answers) is { } unknown)
        {
            return new(null, unknown);
        }

        var (planned, part, refusal) = await PlannedAsync(documentUri, position, answers, token);
        if (planned is not null
            && answers.FirstOrDefault(answer => answer.Id == FieldsChoice)?.Items.FirstOrDefault(item => !planned.Others.Any(other => Named(other.Field, item.Id)))
                is { } unlisted)
        {
            return new(null, Format("EncapsulateFieldChoice_UnknownField", unlisted.Id));
        }

        if (planned?.Encapsulation is not { } encapsulation)
        {
            return new(null, Refused(refusal!.Value, part).Message);
        }

        if (Changes(encapsulation, part!).FirstOrDefault(change => !accepted.Contains(change.Code)) is { } declined)
        {
            return new(null, Format("EncapsulateField_NotAccepted", declined.Message));
        }

        // the lines the edits write are indented as the user indents the code, then the module is parsed and bound again as the edits leave it.
        var module = planned.Module;
        var source = module.Source;
        var unindented = DeclarationEdits.ApplyTo(source, encapsulation.Edits);
        var edits = ModuleIndenter.IndentWritten(source, encapsulation.Edits, await ParseAsync(module, unindented, token), options.Value.Formatting.ToOptions());
        var text = DeclarationEdits.ApplyTo(source, edits);
        if (Verified(part!, encapsulation, module, text, await ParseAsync(module, text, token), edits) is { } failure)
        {
            return new(null, Format("EncapsulateField_NotVerified", failure));
        }

        return new(new RefactorWorkspaceEdit
        {
            Documents = [new RefactorDocumentEdit { Uri = module.Document.Id.Uri.ToUri(), Version = module.Document.Version, Edits = [.. edits] }],
        }, null);
    }

    // the field named, its module and the other Public fields of the module, each with the text that declares it; and how the fields picked are encapsulated,
    // or null when they are not.
    private sealed record class Planned(
        Symbol Field, RenameWorkspace.DocumentModule Module, IReadOnlyList<(Symbol Field, string Declaration)> Others, Encapsulation? Encapsulation);

    // the choices of the encapsulation of a field, each with its default: the other fields of the module are a list when it has any. Friend is offered in an
    // object module only.
    private static IReadOnlyList<RefactorChoice> Choices(string field, IReadOnlyList<(Symbol Field, string Declaration)> others, bool objectModule)
    {
        List<RefactorChoice> choices =
        [
            new()
            {
                Id = ModeChoice,
                Kind = RefactorChoiceKind.Option,
                Label = Format("EncapsulateFieldChoice_Mode"),
                Description = Format("EncapsulateFieldChoice_ModeDescription"),
                Default = ThisMode,
                Options =
                [
                    new() { Value = ThisMode, Label = Format("EncapsulateFieldChoice_ModeThis") },
                    new() { Value = BackingFieldMode, Label = Format("EncapsulateFieldChoice_ModeBackingField") },
                ],
            },
            BackingField(field),
            ReadOnly(),
            ReadAccess(objectModule),
            WriteAccess(objectModule),
            new()
            {
                Id = InsideChoice,
                Kind = RefactorChoiceKind.Option,
                Label = Format("EncapsulateFieldChoice_Inside"),
                Description = Format("EncapsulateFieldChoice_InsideDescription"),
                Default = StateInside,
                Options =
                [
                    new() { Value = StateInside, Label = Format("EncapsulateFieldChoice_InsideState") },
                    new() { Value = PropertiesInside, Label = Format("EncapsulateFieldChoice_InsideProperties") },
                ],
            },
        ];

        if (others.Count > 0)
        {
            choices.Add(new()
            {
                Id = FieldsChoice,
                Kind = RefactorChoiceKind.List,
                Label = Format("EncapsulateFieldChoice_Fields"),
                Description = Format("EncapsulateFieldChoice_FieldsDescription"),
                Items =
                [
                    .. others.Select(other => new RefactorItem
                    {
                        Id = other.Field.Name,
                        Label = other.Declaration,
                        Choices =
                        [
                            new()
                            {
                                Id = EncapsulateChoice,
                                Kind = RefactorChoiceKind.Flag,
                                Label = Format("EncapsulateFieldChoice_Encapsulate"),
                                Default = "false",
                            },
                            ReadOnly(),
                            ReadAccess(objectModule),
                            WriteAccess(objectModule),
                            BackingField(other.Field.Name),
                        ],
                    }),
                ],
            });
        }

        return choices;

        static RefactorChoice BackingField(string field) => new()
        {
            Id = BackingFieldChoice,
            Kind = RefactorChoiceKind.Name,
            Label = Format("EncapsulateFieldChoice_BackingField"),
            Description = Format("EncapsulateFieldChoice_BackingFieldDescription"),
            Default = DefaultBackingField(field),
        };

        static RefactorChoice ReadOnly() => new()
        {
            Id = ReadOnlyChoice,
            Kind = RefactorChoiceKind.Flag,
            Label = Format("EncapsulateFieldChoice_ReadOnly"),
            Description = Format("EncapsulateFieldChoice_ReadOnlyDescription"),
            Default = "false",
        };

        static RefactorChoice ReadAccess(bool objectModule) => Access(ReadAccessChoice, "EncapsulateFieldChoice_ReadAccess", PublicAccess, objectModule);

        static RefactorChoice WriteAccess(bool objectModule)
            => Access(WriteAccessChoice, "EncapsulateFieldChoice_WriteAccess", DefaultWriteAccess(objectModule), objectModule);

        static RefactorChoice Access(string id, string label, string defaultAccess, bool objectModule) => new()
        {
            Id = id,
            Kind = RefactorChoiceKind.Option,
            Label = Format(label),
            Description = Format($"{label}Description"),
            Default = defaultAccess,
            Options =
            [
                .. AccessesOf(objectModule).Select(access
                    => new RefactorOption { Value = access, Label = Format($"EncapsulateFieldChoice_Access_{access}") }),
            ],
        };
    }

    // the access modifiers a property of the module may be declared with: no Friend in a standard module (MS-VBAL §5.3.1.1).
    private static string[] AccessesOf(bool objectModule) => objectModule ? [PublicAccess, FriendAccess, PrivateAccess] : [PublicAccess, PrivateAccess];

    private static string DefaultWriteAccess(bool objectModule) => objectModule ? FriendAccess : PublicAccess;

    // `m` and the field's name, its first letter capitalized: `count` is held by `mCount`.
    private static string DefaultBackingField(string field) => $"m{char.ToUpperInvariant(field[0])}{field[1..]}";

    private static string? Answer(IReadOnlyList<RefactorAnswer> answers, string choice) => answers.FirstOrDefault(answer => answer.Id == choice)?.Value;

    // the answers to the choices of an item of the list of the other fields; none when the item is not answered.
    private static IReadOnlyList<RefactorAnswer> ItemAnswers(IReadOnlyList<RefactorAnswer> answers, string item)
        => answers.FirstOrDefault(answer => answer.Id == FieldsChoice)?.Items.FirstOrDefault(each => string.Equals(each.Id, item, StringComparison.OrdinalIgnoreCase))
            ?.Answers ?? [];

    private static bool Named(Symbol field, string name) => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase);

    // an answer that is not a value of its choice, of the encapsulation or of an item of its list: why the encapsulation is not made; null when every answer
    // is one.
    private static string? UnknownAnswer(IReadOnlyList<RefactorAnswer> answers)
    {
        string[] accesses = [PublicAccess, FriendAccess, PrivateAccess];
        (string Choice, string[] Values)[] known =
        [
            (ModeChoice, [ThisMode, BackingFieldMode]), (InsideChoice, [StateInside, PropertiesInside]), (ReadOnlyChoice, ["true", "false"]),
            (ReadAccessChoice, accesses), (WriteAccessChoice, accesses),
        ];
        (string Choice, string[] Values)[] itemKnown =
            [(EncapsulateChoice, ["true", "false"]), (ReadOnlyChoice, ["true", "false"]), (ReadAccessChoice, accesses), (WriteAccessChoice, accesses)];
        var items = answers.FirstOrDefault(answer => answer.Id == FieldsChoice)?.Items ?? [];
        foreach (var (choices, given) in items.Select(item => (itemKnown, item.Answers)).Prepend((known, answers)))
        {
            foreach (var (choice, values) in choices)
            {
                if (Answer(given, choice) is { } answered && !values.Contains(answered, StringComparer.OrdinalIgnoreCase))
                {
                    return Format("EncapsulateFieldChoice_Unknown", answered, choice);
                }
            }
        }

        return null;
    }

    // the choices answered for a field, as the plan takes them: the defaults for those that are not. What holds the value and what the references inside the
    // module go through are the encapsulation's answers; whether it is read-only, the access modifiers of its properties and the name of its backing field
    // are its own. A write access left unanswered is the module's default.
    private static EncapsulationOptions OptionsOf(IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<RefactorAnswer> own, string field) => new(
        BackingField: string.Equals(Answer(answers, ModeChoice), BackingFieldMode, StringComparison.OrdinalIgnoreCase)
            ? Answer(own, BackingFieldChoice) is { Length: > 0 } typed && typed.Trim() is { Length: > 0 } trimmed ? trimmed : DefaultBackingField(field)
            : null,
        ReadOnly: bool.TryParse(Answer(own, ReadOnlyChoice), out var readOnly) && readOnly,
        InsideThroughProperties: string.Equals(Answer(answers, InsideChoice), PropertiesInside, StringComparison.OrdinalIgnoreCase),
        ReadAccess: AccessOf(Answer(own, ReadAccessChoice)) ?? AccessModifier.Public,
        WriteAccess: AccessOf(Answer(own, WriteAccessChoice)));

    private static AccessModifier? AccessOf(string? answer) => answer?.ToLowerInvariant() switch
    {
        PublicAccess => AccessModifier.Public,
        FriendAccess => AccessModifier.Friend,
        PrivateAccess => AccessModifier.Private,
        _ => null,
    };

    // the field named at a position of a document, the other Public fields of its module, and how the field and those the answers pick are encapsulated in
    // their module, or why they are not.
    private async Task<(Planned? Planned, RenameWorkspace.ProjectPart? Part, (EncapsulationRefusal Refusal, string Field)? Refusal)> PlannedAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, CancellationToken token)
    {
        var none = (new EncapsulationRefusal(EncapsulationRefusalKind.NotAField), string.Empty);
        if (await rename.ComposeAsync(token) is not { } workspace
            || workspace.DocumentOf(documentUri) is not ({ } part, { } named)
            || BasicProgramText.IsProgram(named.Document.Id.Uri.GetFileSystemPath())
            || EncapsulateField.FieldAt(part.Context, named.Module, position) is not { } field
            || part.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == NameOccurrences.ModuleOf(field.Uri).AbsoluteUri) is not { } module)
        {
            return (null, null, none);
        }

        var others = OtherFields(part, module, field);
        var picked = others
            .Where(other => bool.TryParse(Answer(ItemAnswers(answers, other.Field.Name), EncapsulateChoice), out var encapsulated) && encapsulated)
            .Select(other => (other.Field, OptionsOf(answers, ItemAnswers(answers, other.Field.Name), other.Field.Name)));
        var (encapsulation, refusal) = EncapsulateField.Plan(
            part.Context, module.Module, module.Parse, [(field, OptionsOf(answers, answers, field.Name)), .. picked]);
        var planned = new Planned(field, module, others, encapsulation);
        return encapsulation is null ? (planned, part, (refusal!, refusal!.Field?.Name ?? field.Name)) : (planned, part, null);
    }

    // the Public fields of a module other than one, in the order they are declared, each with the text that declares it.
    private static IReadOnlyList<(Symbol Field, string Declaration)> OtherFields(RenameWorkspace.ProjectPart part, RenameWorkspace.DocumentModule module, Symbol field)
    {
        if (module.Parse.SyntaxTree is not { } tree)
        {
            return [];
        }

        var text = new ExtractionText(module.Source);
        return
        [
            .. tree.Children.OfType<VariableDeclarationNode>()
                .Where(variable => variable.AccessModifier is AccessModifier.Public or AccessModifier.Global && variable.NameRange is not null)
                .Select(variable => (Field: EncapsulateField.FieldAt(part.Context, module.Module, variable.NameRange!.Value.Start), Variable: variable))
                .Where(each => each.Field is not null && each.Field.SemanticId != field.SemanticId)
                .Select(each => (each.Field!, text.Of(each.Variable.SourceLocation.Range).Trim())),
        ];
    }

    private static RefactorNotice Refused((EncapsulationRefusal Refusal, string Field) refused, RenameWorkspace.ProjectPart? part) => new()
    {
        Code = Code(refused.Refusal.Kind.ToString()),
        Kind = RefactorNoticeKind.Refusal,
        Message = Format($"EncapsulateFieldRefusal_{refused.Refusal.Kind}", refused.Field,
            refused.Refusal is { Module: { } at, Range: { } where } && part is not null ? RenameWorkspace.Locator(part)(at, where.Start) : refused.Refusal.Name),
        Location = refused.Refusal is { Module: { } module, Range: { } range } ? LocationOf(part, module, range) : null,
    };

    // each reference that a call may write through, of each field: it would write to a copy of the property's value, or to an element of one.
    private static IEnumerable<RefactorNotice> Changes(Encapsulation encapsulation, RenameWorkspace.ProjectPart part)
    {
        return encapsulation.Fields.SelectMany(field => field.PassedByRef
            .Select(reference => Change(PassedByRefCode, "EncapsulateFieldChange_PassedByRef", field.Field, reference))
            .Concat(field.ElementsPassed.Select(reference => Change(ElementPassedCode, "EncapsulateFieldChange_ElementPassed", field.Field, reference))));

        RefactorNotice Change(string code, string message, Symbol field, SymbolReference reference) => new()
        {
            Code = code,
            Kind = RefactorNoticeKind.Change,
            Message = Format(message, field.Name, RenameWorkspace.Locator(part)(reference.Module, reference.Range.Start)),
            Location = LocationOf(part, reference.Module, reference.Range),
        };
    }

    // the edits verified against the project composed again with the module as they leave it: every reference the edits leave in place still refers to the
    // field it referred to: to its properties outside the module, and to what holds its value inside it. Why they are not made, or null.
    private static string? Verified(
        RenameWorkspace.ProjectPart part, Encapsulation encapsulation, RenameWorkspace.DocumentModule module, string text, ModuleParseResult parse,
        IReadOnlyList<RefactorTextEdit> edits)
    {
        if (!parse.IsSuccess || part.Recompose is not { } recompose)
        {
            return Format("EncapsulateFieldVerification_DoesNotParse");
        }

        var composition = recompose(new Dictionary<string, ModuleParseResult> { [module.Module.AbsoluteUri] = parse });
        var after = part.Context with
        {
            Resolver = composition.Resolver,
            ScopeTree = composition.ScopeTree,
            Modules = [.. part.Context.Modules.Select(each => each.Module.AbsoluteUri == module.Module.AbsoluteUri ? new RenameModule(each.Module, parse.SyntaxTree!, text) : each)],
        };

        var locate = RenameWorkspace.Locator(part);
        foreach (var change in BindingCheck.Changes(part.Context, after, new Dictionary<string, IReadOnlyList<RefactorTextEdit>> { [module.Module.AbsoluteUri] = edits }))
        {
            var field = encapsulation.Fields.FirstOrDefault(each => each.Field.SemanticId == change.Was)?.Field ?? encapsulation.Field;
            switch (change.Kind)
            {
                case BindingChangeKind.Lost or BindingChangeKind.Unknown:
                    return Format("EncapsulateFieldVerification_Lost", field.Name, locate(change.Module, change.Before!.Value.Start));
                case BindingChangeKind.Retargeted when !IsExpected(change, encapsulation, after):
                    return Format("EncapsulateFieldVerification_Retargeted", field.Name, locate(change.Module, change.Before!.Value.Start));
            }
        }

        return null;
    }

    // a reference to one of the fields that refers to its property, or, inside its module, to the member of the private type that holds its value.
    private static bool IsExpected(BindingChange change, Encapsulation encapsulation, RenameContext after)
        => encapsulation.Fields.FirstOrDefault(each => each.Field.SemanticId == change.Was) is { Field: var field }
            && change.Is is { } now
            && NameOccurrences.SymbolOf(after, now) is { } symbol
            && string.Equals(symbol.Name, field.Name, StringComparison.OrdinalIgnoreCase)
            && (symbol is VBUserDefinedTypeFieldSymbol
                ? change.Module.AbsoluteUri == encapsulation.Module.AbsoluteUri
                : NameOccurrences.ModuleOf(symbol.Uri).AbsoluteUri == encapsulation.Module.AbsoluteUri);

    // a text of a module, parsed as its document is: the designer of a form is its document's, which the text of its code does not hold.
    private async Task<ModuleParseResult> ParseAsync(RenameWorkspace.DocumentModule module, string text, CancellationToken token)
        => await parsing.ParseFragmentAsync(module.Document.Id.Uri.ToUri(), text, token) with { Designer = module.Parse.Designer };

    // where a range of a module is, in the document that module is the text of.
    private static SourceLocation LocationOf(RenameWorkspace.ProjectPart? part, Uri module, SourceRange range)
        => new(part?.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri)?.Document.Id.Uri.ToUri() ?? module, range);

    private static string Code(string kind) => $"encapsulateField.{char.ToLowerInvariant(kind[0])}{kind[1..]}";

    private static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentUICulture, Messages.GetString(key, CultureInfo.CurrentUICulture) ?? key, arguments);
}
