# Releasing RDCore

This is the maintainer runbook for publishing a **preview build** of the RDCore platform.

## What a release is

- A GitHub **prerelease** carrying three assets: `rdcore-<version>-win-x64.zip`, `release.json` and `SHA256SUMS`.
- The zip is the **platform root** itself (`rdcore.json` at its top level), used as extracted. It is not an installer, and it is not a certified build.
- Release configuration, `win-x64`, _framework-dependent_: users need the .NET 10 x64 runtime.
- Everything is built by the **Release** workflow on `windows-latest`, from a `v*` tag a maintainer pushes.

The version depends on what started the workflow:

| Trigger | Version | What it produces |
|---|---|---|
| tag `v0.1.0` | `0.1.0` | the release |
| tag `v0.1.0-rc.1` | `0.1.0-rc.1` | the release |
| **Run workflow** (dry run, run number 42) | `0.1.0-ci.42` | a short-lived workflow artifact, no release |
| pull request #350 (release-related paths only) | `0.1.0-pr.350` | build and package; only the metadata is uploaded, no zip |

Whatever the suffix, the binaries carry `AssemblyVersion` `<VersionPrefix>.0` (e.g. `0.1.0.0`) and `InformationalVersion` `<version>+<commit sha>`; `rdcore.json` carries `platformVersion` (the version, without the `+<sha>`), `commit` and `rid`.

---

## One-time setup (before the first tag)

Do both before pushing any `v*` tag.

### 1. The `release` environment

**Settings → Environments → New environment**, name it `release`, then **Configure environment**:

- **Deployment protection rules**: tick **Required reviewers** and add the maintainers who may approve a release.
  - Untick **Allow administrators to bypass configured protection rules**, so the approval can't be skipped.
  - **Prevent self-review** stops the person who pushed the tag from approving their own run: only tick it when a second maintainer is always available.
- **Deployment branches and tags**: choose **Selected branches and tags** → **Add deployment branch or tag rule** → ref type **Tag**, name pattern `v*` → **Add rule**. Add no branch rule.
- **Save protection rules**.

👉 Why first: when a job refers to an environment that doesn't exist, GitHub creates it on the spot, **with no protection at all**. The workflow's preflight therefore refuses a tag run unless it can read `release` and finds that it requires reviewers and restricts deployment branches and tags. If GitHub doesn't answer, the preflight fails too: re-run the job. It only warns when administrators can bypass the protection rules, so untick that yourself.

### 2. A tag ruleset for `v*`

**Settings → Rules → Rulesets → New ruleset → New tag ruleset**:

- **Ruleset name**: e.g. `Release tags`; **Enforcement status**: **Active**.
- **Bypass list**: the maintainers only (e.g. the **Repository admin** role, or a maintainers team). Do **not** add GitHub Actions, any other app, or deploy keys.
- **Target tags** → **Add target** → **Include by pattern** → `v*` (that is, `refs/tags/v*`).
- **Rules**: tick **Restrict creations**, **Restrict updates** and **Restrict deletions**.
- **Create**.

Only people on the bypass list can then create, move or delete a release tag, and no workflow token can mint one. With the environment's `v*` tag policy, only a maintainer's tag can reach the `release` environment.

### Optional, later

- **Immutable releases** (**Settings → General → Releases → Enable release immutability**): published assets and their tag can no longer change, and `gh release verify` becomes available. Only turn this on after a clean rehearsal (e.g. a full `v0.1.0-rc.1` run): a tag name used by an immutable release can **never be reused**, even after deleting the release, so a botched first attempt would burn that version for good.
- **Require actions to be pinned to a full-length commit SHA** (**Settings → Actions → General**): every workflow in this repository pins its actions to a full SHA (with the version in a comment), so this policy can be turned on; it then rejects any new unpinned `uses:`.

---

## Cutting a release

1. **Check the version.** `<VersionPrefix>` in `Directory.Build.props` is the single source of the version (e.g. `0.1.0`). If it needs to change, change it in a normal pull request first.
1. **Pick the tag.** It must be `v<VersionPrefix>` (e.g. `v0.1.0`) or `v<VersionPrefix>-<suffix>` for a candidate (e.g. `v0.1.0-rc.1`). The suffix becomes the SemVer prerelease part of the version.
1. **Tag a commit that is already on `main`**, ideally one whose _Build and Test_ run is green. With `origin` pointing at `rubberduck-vba/RDCore`:

   ```sh
   git fetch origin
   git merge-base --is-ancestor <commit> origin/main && echo "on main"
   git tag -a v0.1.0 -m "RDCore 0.1.0" <commit>
   git push origin v0.1.0
   ```

1. **Let the build job run.** Open the run under **Actions → Release**. The build job runs the tests, publishes the platform tree (`PlatformPublish.ps1`, Release, `win-x64`), writes `THIRD-PARTY-NOTICES.txt`, `SOURCE.md` and `release.json` into it (`tools/release/New-ThirdPartyNotices.ps1`, `New-ReleaseMetadata.ps1`), gates it (`Test-PlatformTree.ps1`), smoke-launches the language server on a copy (`Test-LanguageServerStartup.ps1`) and zips it (`New-ReleaseArchive.ps1`). There is nothing to approve until it has finished.
1. **Review, then approve.** Once the build job is green, the run pauses on the `release` environment. Before approving, open the build job: **Resolve version** must print `Version <version> (tag run: True)` with the version you meant, and **Write the release notes** prints the notes the release will carry. Then **Review deployments** → tick `release` → **Approve and deploy**, within 30 days (the build job's assets are kept that long on a tag run).
   - **Tagged the wrong commit?** **Reject** the waiting deployment (**Review deployments** → **Reject**) before you delete or move the tag: runs for the same tag go one at a time, so the run the new tag starts waits behind this one. Should this run be approved after the tag has moved, its publish job stops without publishing.
1. **Let the publish job run.** It checks that the tag still points at the commit the build job built and that the tag has no release or draft yet, attests the build provenance of the zip and `release.json`, then creates a **draft** prerelease, uploads the three assets and publishes it.
1. **If the publish job fails**, what to do depends on the step that stopped it. A re-run (**Re-run failed jobs**) waits for approval again.
   - **At or after *Create the draft prerelease*, or at *Check the tag has no release yet* reporting drafts**: a draft may be left behind, even when the *Create* step itself failed, and a re-run won't clean it up; it stops at **Check the tag has no release yet** instead. Delete the **drafts** (not the tag): `gh release delete <tag> -R rubberduck-vba/RDCore --yes` (without `--cleanup-tag`) deletes one draft per call, so repeat it until `gh release list -R rubberduck-vba/RDCore` shows none for the tag. Then re-run.
   - **At *Check the tag has no release yet*, saying the tag is already published**: there is nothing to re-run. Check the release's assets.
   - **At *Check the tag still points at the built commit***: the tag was moved after the build. Don't re-run this run: the run the moved tag started builds and publishes it.
   - **Any other step**: re-run.
1. **Check the result.** The release is marked **Pre-release** and carries the three assets. Download them and [verify](#verifying-a-release) them.

## Dry run

**Actions → Release → Run workflow** (on `main`) → **Run workflow**. It builds, tests and packages like a release, then uploads the zip and its metadata as a short-lived workflow artifact. No tag, no release.

A dry run doesn't exercise the release itself (draft, upload, publish): only a pushed tag does.

---

## Verifying a release

Download the assets into one folder.

**Checksums.** `SHA256SUMS` covers the zip and `release.json`. In Git Bash (or on Linux/macOS):

```sh
sha256sum -c SHA256SUMS
sha256sum -c --ignore-missing SHA256SUMS   # when only the zip was downloaded
```

In PowerShell (`True` means the hash matches):

```powershell
Get-Content .\SHA256SUMS | ForEach-Object {
    $hash, $name = $_ -split '  ', 2
    if (Test-Path $name) { '{0}: {1}' -f $name, ((Get-FileHash $name).Hash -eq $hash) }
}
```

**Provenance.** With the GitHub CLI:

```sh
gh attestation verify rdcore-<version>-win-x64.zip -R rubberduck-vba/RDCore --signer-workflow rubberduck-vba/RDCore/.github/workflows/release.yml --source-ref refs/tags/<tag>
```

This checks that the zip was built by the Release workflow (`.github/workflows/release.yml`) of `rubberduck-vba/RDCore`, running for the tag `<tag>`. With `-R` alone, an attestation made by any workflow of the repository, on any branch, would pass.

**Per-file hashes.** Once extracted, `release.json` lists every file of the tree with its size and SHA-256 (except `release.json` itself, `SHA256SUMS` and anything under `Logs/`). In PowerShell, this prints the path of every listed file that is missing or differs, then of every file that isn't listed (nothing means the tree matches `release.json`):

```powershell
$root = "$env:LOCALAPPDATA\RDCore\0.1.0"
$release = Get-Content (Join-Path $root 'release.json') -Raw | ConvertFrom-Json
$listed = $release.files.path + 'release.json', 'SHA256SUMS'
$release.files | Where-Object { (Get-FileHash (Join-Path $root $_.path) -ErrorAction SilentlyContinue).Hash -ne $_.sha256 } | ForEach-Object path
Get-ChildItem $root -Recurse -File -Force -Name | ForEach-Object { $_.Replace('\', '/') } | Where-Object { $_ -notin $listed -and $_ -notlike 'Logs/*' }
```

From a clone, `tools/release/Test-PlatformTree.ps1 -PlatformRoot <extracted folder> -ExpectedCommit <sha> -RequireReleaseFiles` (PowerShell 7) runs the platform-tree gate the workflow uses, which also catches added files. It is for a freshly extracted tree only: it fails once `Logs/` holds files, that is, once the tree has been run.

**Immutable releases.** Once immutability is on:

```sh
gh release verify v0.1.0 -R rubberduck-vba/RDCore
gh release verify-asset v0.1.0 rdcore-0.1.0-win-x64.zip -R rubberduck-vba/RDCore
```

---

## After a release

- When work on the next version starts, bump `<VersionPrefix>` in `Directory.Build.props` in a normal pull request (e.g. `0.1.0` → `0.2.0`). Release candidates of the same version (`-rc.2`, …) don't need a bump.
- 👉 Mind the workspace version: `rdc` stamps each new workspace's `.rdproj` with its own version, and the language server has a guard (`WorkspaceService.LoadAsync`) meant to refuse a `.rdproj` whose version is greater than its own. That guard is currently a **no-op**: `ProjectFileLoader.LoadAsync` returns `project.WithUri(...)`, a record `with` that runs `ProjectFile`'s copy constructor, which re-stamps `Version` with the running server's own version, so the guard only ever compares the server with itself. A bump therefore breaks nothing today. The risk is latent: if the copy constructor is ever changed to keep `source.Version`, the guard starts working and rejects every `.rdproj` stamped `1.0.0` by builds before 0.1.0 (their assemblies were `1.0.0.0`; they are now `0.1.0.0`), and once `main` is bumped, the workspaces its prerelease builds (dry runs, pull request builds) create could no longer be opened by the last released language server.

## Withdrawing a release

There is no yank. To withdraw a release:

1. Edit it: start the title and the notes with **WITHDRAWN**, say why, and link to the release that fixes it.
1. Make sure **Set as a pre-release** is ticked and **Set as the latest release** is not.
1. Fix forward: tag a new version (`v0.1.1`, after bumping `<VersionPrefix>` in a pull request; or `v0.1.0-rc.2` for a candidate, which needs no bump).

Never delete or move a released tag: the attestation records it, and so do the release and anyone who downloaded it. The tag ruleset won't stop a maintainer, since those on its bypass list can still delete or move a tag; and with immutable releases the name could never be reused. Leave the assets in place so they can still be verified.

---

## What's in the zip

```
rdcore.json                  platform manifest: platformVersion, commit, rid, generatedUtc, entry points
release.json                 release metadata: version, runtime, launch, source, SHA-256 of every file
SOURCE.md                    where to get the corresponding source
NOTICE.md                    copyright, licence map, third-party code, trademarks
LICENSE-GPLv3.md
LICENSE-MIT.md
THIRD-PARTY-NOTICES.txt      third-party packages and their licences
RDCore.CLI/                  rdc.exe (hostService)
RDCore.LanguageServer/       RDCore.LanguageServer.exe (langService)
RDCore.Parsing/              RDCore.ParseServer.exe (parseServer)
Extensions/                  extensionsDirectory
  RDCore.Diagnostics/        RDCore.Diagnostics.exe, extension.manifest.json
```

There is no `Logs/` and no `Config/` folder: `Logs/` is created on first run.

`release.json` (`"schema": 1`) is the machine-readable description of the release: `version`, `commit`, `rid`, `deployment` (`framework-dependent`), `runtime` (`Microsoft.NETCore.App` and its minimum version), `entryPoints` (same as `rdcore.json`), `launch.languageServer` (path, working directory, arguments), `source` (repository, tag, commit), and `files` (path, size and SHA-256 of every file, sorted by path).

## Known limitations

- **Unsigned.** SmartScreen may warn about the executables, and Windows 11 _Smart App Control_ may block them.
- **Framework-dependent.** The .NET 10 x64 runtime (`Microsoft.NETCore.App`) must be installed; `release.json` records the minimum version.
- **`win-x64` only.**
- **Logs are written inside the extracted tree** (`<platform root>/Logs/<Component>-<yyyyMMdd>.log`), so the tree must be extracted to a user-writable folder (not under `Program Files`).
- **Verbose logging.** The shipped `appsettings.json` files set `"TraceLevel": "Trace"` and `"Verbose": true`: logs grow quickly, and verbose messages may contain relatively sensitive information such as document locations, symbol names and execution stack traces.
