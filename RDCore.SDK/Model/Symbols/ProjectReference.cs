namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// One entry in a workspace's ordered reference list, as the runtime sees it: a reference's
/// source-visible name and its position in that list. Nothing more — a referenced library's members
/// are <em>not</em> carried here. They are contributed by an <c>ISymbolProvider</c> and resolved
/// through <c>ISymbolResolver</c> like any other symbol (today the <c>VBA</c> intrinsics come from
/// the environment host; a dedicated library scope tier is planned). This type exists only so a
/// name-resolution pass can break a cross-reference tie by list position.
/// </summary>
/// <remarks>
/// The list is the one the <c>.rdproj</c> declares, in declaration order (<strong>RD-VBAL
/// §2.3.1.2</strong>), surfaced on <c>IRuntimeSession.References</c> exactly as the language server
/// provides it — the runtime does not re-sort it. <see cref="Priority"/> is the position: rank
/// <c>0</c> appears first and is the <em>lowest</em> precedence — always the <c>VBA</c> standard
/// library — so any later reference that exports an identically-named global-scope member shadows it,
/// which the semantic layer surfaces as a <em>shadowed declaration</em> diagnostic. A workspace's own
/// declarations shadow every reference.
/// </remarks>
/// <param name="Name">The identifier a workspace uses to qualify this reference's members (<c>VBA</c>, <c>Excel</c>, …).</param>
/// <param name="Priority">The reference's position in the workspace's ordered reference list; <c>0</c> appears first and is the lowest precedence.</param>
public sealed record class ProjectReference(string Name, int Priority);
