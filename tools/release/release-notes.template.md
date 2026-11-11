**Preview build.** RDCore {VERSION} is a prerelease for evaluation, not a certified build.

Built from commit [`{SHORT}`]({REPOSITORY}/commit/{COMMIT}) by [this workflow run]({RUN}).

### Downloads

| System | Archive |
|---|---|
| Windows x64 | `rdcore-{VERSION}-win-x64.zip` |
| Linux x64 | `rdcore-{VERSION}-linux-x64.tar.gz` |
| macOS on Intel (x64) | `rdcore-{VERSION}-osx-x64.tar.gz` |
| macOS on Apple silicon (arm64) | `rdcore-{VERSION}-osx-arm64.tar.gz` |

Each archive has its release metadata beside it, `rdcore-{VERSION}-<system>.release.json` (the `release.json` it contains), and `SHA256SUMS` covers them all.

### Requirements

- The .NET 10 runtime (`Microsoft.NETCore.App`) for the archive's architecture. Every archive is framework-dependent and does not include it; its `release.json` records the minimum version.
- **Windows x64.** RDCore runs as a 64-bit process, so UserForms need 64-bit Office (the 64-bit Microsoft Forms 2.0), a `Declare` statement's library must be a 64-bit DLL, and Office automation needs the Office application installed.
- **Linux and macOS.** The platform has no automation servers there: a program that creates an object of one (`CreateObject`, or `New` of a library's class such as `Scripting.Dictionary`) gets error 429. It can still be analyzed.

### Installing

- Extract the archive into a folder of its own. The archive's top level is the platform root (`rdcore.json`); keep the tree together. On Linux and macOS, extract it with `tar -xzf`, which keeps the executables' execute bit. On macOS, then clear the extracted folder's quarantine (see **Verifying** below).
- In the extracted folder's `RDCore.CLI` folder, run `.\rdc install` (Windows) or `./rdc install` (Linux and macOS). It copies the platform into `RDCore/Platform/versions/{VERSION}/` in your local application data folder, where the RDCore add-in for HexIDE installs it too, and puts `rdc` on your PATH: open a new terminal to use it. [Installing the platform](https://rubberduck-vba.github.io/RDCore/rdc.html#installing-the-platform) describes its options.
- An IDE launches the language server with its own folder as the working directory, as `launch.languageServer` in `release.json` describes: `RDCore.LanguageServer.exe --client-process-id <client pid> --pipe-name <pipe> --workspace <workspace>` on Windows, and `RDCore.LanguageServer` with the same arguments elsewhere. Pass the client's own process ID: without it, the server and the processes it starts keep running if the client dies.
- Do not set `RDCORE_PLATFORM_ROOT` globally: each server finds its platform root from its own location, and a global value would point every installed version at one tree.

### Verifying

```sh
sha256sum -c --ignore-missing SHA256SUMS
gh attestation verify rdcore-{VERSION}-win-x64.zip -R {REPO} --signer-workflow {REPO}/.github/workflows/release.yml --source-ref {GITREF}
```

The second line takes any of the archives, or a `.release.json`. On macOS, `shasum -a 256 -c SHA256SUMS` checks the sums once every asset is downloaded.

The binaries are **not code-signed**. On Windows, SmartScreen may warn about them, and Smart App Control may block them. On macOS, Gatekeeper refuses to start a downloaded program that Apple has not notarized: once you trust the archive, clear the quarantine of the extracted folder with `xattr -dr com.apple.quarantine <folder>`.

### Source and licences

`SOURCE.md` in each archive says where the corresponding source is: {REPOSITORY}/tree/{REF}. RDCore is licensed under the GPLv3 or later (`LICENSE-GPLv3.md`), with `NOTICE.md` saying what it covers; `THIRD-PARTY-NOTICES.txt` lists the packages the archive contains and their licences, the MIT-licensed RDCore.SDK package among them, with the RDCore.SDK.Protocol assembly inside it.
