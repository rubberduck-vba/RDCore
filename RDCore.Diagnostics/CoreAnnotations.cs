using RDCore.Diagnostics;
using RDCore.SDK.Extensibility.Annotations;

[assembly: ExtensionAnnotations(typeof(CoreAnnotations), CoreAnnotations.DescriptionsResource)]

namespace RDCore.Diagnostics;

/// <summary>
/// The annotations of the core extension: the ones legacy Rubberduck read, unit testing's aside (the unit-testing extension declares its own).
/// </summary>
public sealed class CoreAnnotations : IAnnotationDeclarations
{
    /// <summary>
    /// The base name of the resources that describe the annotations.
    /// </summary>
    public const string DescriptionsResource = "RDCore.Diagnostics.AnnotationDescriptions";

    private const AnnotationModuleKinds NotDocument = AnnotationModuleKinds.Standard | AnnotationModuleKinds.Class | AnnotationModuleKinds.Form;
    private const AnnotationModuleKinds ClassOrForm = AnnotationModuleKinds.Class | AnnotationModuleKinds.Form;

    /// <inheritdoc/>
    public IReadOnlyList<AnnotationDeclaration> Declarations { get; } =
    [
        // a module's or a block's: with blocks, '@Description above a block header describes the block (RDC00416 suggests it for '@ModuleDescription)
        new()
        {
            Name = "Description", Targets = AnnotationTargets.Module | AnnotationTargets.Member,
            Parameters = [Required("Text", AnnotationArgumentKind.Text)],
        },
        new() { Name = "ModuleDescription", Targets = AnnotationTargets.Module, Parameters = [Required("Text", AnnotationArgumentKind.Text)] },
        new() { Name = "VariableDescription", Targets = AnnotationTargets.Variable, Parameters = [Required("Text", AnnotationArgumentKind.Text)] },
        new()
        {
            Name = "Folder", Targets = AnnotationTargets.Module, IsDeprecated = true,
            Parameters = [Required("Path", AnnotationArgumentKind.Text)],
        },
        // the file's or the block's module only: there is no per-line form
        new()
        {
            Name = "Ignore", Targets = AnnotationTargets.Module, Repeats = true,
            Parameters = [Required("Codes", AnnotationArgumentKind.DiagnosticCode) with { Repeats = true }],
        },
        new()
        {
            Name = "IgnoreModule", Targets = AnnotationTargets.Module, Repeats = true,
            Parameters = [Optional("Codes", AnnotationArgumentKind.DiagnosticCode) with { Repeats = true }],
        },
        new()
        {
            Name = "Obsolete", Targets = AnnotationTargets.Member | AnnotationTargets.Variable,
            Parameters = [Optional("Message", AnnotationArgumentKind.Text)],
        },
        new() { Name = "Exposed", Targets = AnnotationTargets.Module, ModuleKinds = NotDocument },
        new() { Name = "PredeclaredId", Targets = AnnotationTargets.Module, ModuleKinds = ClassOrForm },
        new() { Name = "Interface", Targets = AnnotationTargets.Module, ModuleKinds = AnnotationModuleKinds.Class },
        new() { Name = "NoIndent", Targets = AnnotationTargets.Module },
        new() { Name = "DefaultMember", Targets = AnnotationTargets.Member, ModuleKinds = ClassOrForm },
        new() { Name = "Enumerator", Targets = AnnotationTargets.Member, ModuleKinds = NotDocument },
        new()
        {
            Name = "EntryPoint", Targets = AnnotationTargets.Member, ModuleKinds = AnnotationModuleKinds.Standard | AnnotationModuleKinds.Document,
            Parameters = [Optional("Caller", AnnotationArgumentKind.Text)],
        },
        new()
        {
            Name = "ExcelHotkey", Targets = AnnotationTargets.Member, ModuleKinds = AnnotationModuleKinds.Standard,
            Parameters = [Required("Key", AnnotationArgumentKind.Text)],
        },
        new()
        {
            Name = "ModuleAttribute", Targets = AnnotationTargets.Module, ModuleKinds = NotDocument, Repeats = true,
            Parameters = [Required("Attribute", AnnotationArgumentKind.Identifier), Required("Values", AnnotationArgumentKind.Literal) with { Repeats = true }],
        },
        new()
        {
            Name = "MemberAttribute", Targets = AnnotationTargets.Member | AnnotationTargets.Variable, ModuleKinds = NotDocument, Repeats = true,
            Parameters = [Required("Attribute", AnnotationArgumentKind.Identifier), Required("Values", AnnotationArgumentKind.Literal) with { Repeats = true }],
        },
    ];

    private static AnnotationParameter Required(string name, AnnotationArgumentKind takes) => new() { Name = name, Takes = takes };

    private static AnnotationParameter Optional(string name, AnnotationArgumentKind takes) => new() { Name = name, Takes = takes, IsOptional = true };
}
