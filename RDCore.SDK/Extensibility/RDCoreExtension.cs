using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// Represents any <c>RDCore</c> extension.
/// </summary>
/// <param name="Name">The name of this extension.</param>
/// <param name="Version">The semantic version string of this extension.</param>
/// <param name="MarketplaceId">The <em>globally unique identifier</em> of a <strong>first-party or authorized</strong> <c>RDCore</c> extension.</param>
/// <param name="Uri">The <c>Uri</c> namespace of this extension.</param>
internal record class RDCoreExtension(string Name, string Version, Guid MarketplaceId, Uri Uri) 
{

}

/// <summary>
/// Describes and registers the capabilities of a <c>RDCore</c> extension.
/// </summary>
/// <param name="Semantics">Describes the registered <em>semantics</em> capabilities.</param>
public record class RDCoreExtensionCapabilities(
    RDCoreExtensionSemanticCapabilities Semantics

    ) 
{ }

/// <summary>
/// Describes and registers the <em>semantics</em> capabilities of a <c>RDCore</c> extension.
/// </summary>
/// <param name="TokenSemantics">Describes the registered <em>token semantics</em> capabilities.</param>
/// <param name="TypeConversionSemantics">Describes the registered <em>type conversion semantics</em> capabilities.</param>
public record class RDCoreExtensionSemanticCapabilities(
    RDCoreExtensionTokenSemanticsCapabilities TokenSemantics,
    RDCoreExtensionTypeConversionSemanticCapabilities TypeConversionSemantics
    ) 
{ }

/// <summary>
/// Describes and registers the <em>token semantics</em> capabilities of a <c>RDCore</c> extension.
/// </summary>
/// <param name="DateTokenSemantics">Describes the registered <em>date token semantics</em> capabilities.</param>
/// <param name="IdentifierTokenSemantics">Describes the registered <em>identifier token semantics</em> capabilities.</param>
/// <param name="NumberTokenSemanticCapabilities">Describes the registered <em>number token semantics</em> capabilities.</param>
/// <param name="StringTokenSemanticCapabilities">Describes the registered <em>string token semantics</em> capabilities.</param>
/// <param name="SupportedFlags">Describes the supported <c>TokenSemanticOperationFlags</c> semantic flags.</param>
public record class RDCoreExtensionTokenSemanticsCapabilities(
    RDCoreExtensionDateTokenSemanticCapabilities DateTokenSemantics,
    RDCoreExtensionIdentifierTokenSemanticCapabilities IdentifierTokenSemantics,
    RDCoreExtensionNumberTokenSemanticCapabilities NumberTokenSemanticCapabilities,
    RDCoreExtensionStringTokenSemanticCapabilities StringTokenSemanticCapabilities,
    TokenSemanticOperationFlags SupportedFlags = TokenSemanticOperationFlags.All)
{ }

/// <summary>
/// Describes and registeres the <em>date token semantics</em> capabilities of a <c>RDCore</c> extension.
/// </summary>
/// <param name="DateTokenSemanticsSupported"><c>true</c> if the extension registers date token semantics capabilities, <c>false</c> otherwise.</param>
/// <param name="SupportedFlags">The supported <c>DateTokenSemanticFlags</c> values.</param>
public record class RDCoreExtensionDateTokenSemanticCapabilities(bool DateTokenSemanticsSupported = false, DateTokenSemanticFlags SupportedFlags = DateTokenSemanticFlags.All) { }

public record class RDCoreExtensionIdentifierTokenSemanticCapabilities(bool IdentifierTokenSemanticsSupported = false, IdentifierTokenSemanticFlags SupportedFlags = IdentifierTokenSemanticFlags.All) { }

public record class RDCoreExtensionNumberTokenSemanticCapabilities(bool NumberTokenSemanticsSupported = false, NumberTokenSemanticOperationFlags SupportedFlags = NumberTokenSemanticOperationFlags.All) { }
public record class RDCoreExtensionStringTokenSemanticCapabilities(bool StringTokenSemanticsSupported = false, StringTokenSemanticFlags SupportedFlags = StringTokenSemanticFlags.All) { }
public record class RDCoreExtensionTypeConversionSemanticCapabilities(bool TypeConversionSemanticsSupported = false, ConversionSemanticFlags SupportedFlags = ConversionSemanticFlags.All) { }