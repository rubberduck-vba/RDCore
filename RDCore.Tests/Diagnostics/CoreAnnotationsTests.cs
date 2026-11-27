using System.Collections;
using System.Globalization;
using System.Resources;
using RDCore.Diagnostics;
using RDCore.SDK.Extensibility.Annotations;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Diagnostics;

[TestClass]
public sealed class CoreAnnotationsTests
{
    private static AnnotationSet Read() => ExtensionAnnotationsAttribute.Read(typeof(CoreAnnotations).Assembly)
        ?? throw new AssertFailedException("the core extension's assembly declares no annotations");

    [TestMethod]
    public void TheCoreExtensionsAssembly_DeclaresItsAnnotations()
        => CollectionAssert.AreEqual(new CoreAnnotations().Declarations.Select(annotation => annotation.Name).ToArray(),
            Read().Declarations.Select(annotation => annotation.Name).ToArray());

    [TestMethod]
    public void EveryCoreAnnotation_IsWellFormed_AndCalledByNoOtherOnesName()
    {
        var declarations = Read().Declarations.ToArray();

        Assert.IsEmpty(declarations.Where(annotation => !annotation.IsWellFormed).Select(annotation => annotation.Name));
        Assert.HasCount(declarations.Length,new AnnotationCatalogue([Read()]).Declarations);
    }

    [TestMethod]
    public void EveryCoreAnnotationAndParameter_IsDescribed_InEveryLanguageOfThePlatform()
    {
        var set = Read();
        var keys = set.Declarations.SelectMany(annotation => annotation.Parameters.Select(parameter => AnnotationSet.KeyOf(annotation, parameter))
            .Prepend(AnnotationSet.KeyOf(annotation))).ToArray();
        var resources = new ResourceManager(CoreAnnotations.DescriptionsResource, typeof(CoreAnnotations).Assembly);

        foreach (var language in SettingsSchema.Languages)
        {
            var written = resources.GetResourceSet(CultureInfo.GetCultureInfo(language), createIfNotExists: true, tryParents: false)
                ?? throw new AssertFailedException($"no '{language}' descriptions");
            var writtenKeys = written.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToArray();

            CollectionAssert.AreEquivalent(keys, writtenKeys, $"'{language}': a key with no description, or a description of no annotation");
        }
    }

    // the annotations legacy Rubberduck read, unit testing's aside: RDCore never knows fewer
    [TestMethod]
    [DataRow("Description"), DataRow("ModuleDescription"), DataRow("VariableDescription"), DataRow("Folder"), DataRow("Ignore"), DataRow("IgnoreModule")]
    [DataRow("Obsolete"), DataRow("Exposed"), DataRow("PredeclaredId"), DataRow("Interface"), DataRow("NoIndent"), DataRow("DefaultMember")]
    [DataRow("Enumerator"), DataRow("EntryPoint"), DataRow("ExcelHotkey"), DataRow("ModuleAttribute"), DataRow("MemberAttribute")]
    public void ALegacyAnnotation_IsACoreOne(string name)
        => Assert.IsNotNull(new AnnotationCatalogue([Read()]).Find(name.ToUpperInvariant()));
}
