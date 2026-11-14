# Disabling a Diagnostic
<sup>_Ce document est disponible en [français](disabling-diagnostics.fr.html)_</sup>

A diagnostic you do not want to see can be turned off by its code. RDCore then stops reporting it, and the analyzer that finds it skips that work. The
codes you disable are a setting of yours, `Analysis.DisabledDiagnostics`, and apply to every workspace you open.

The diagnostics of the RDCore analyzers (`RDC`) and of an extension's analyzers can be disabled. A compile error (`VBC`) or a run-time error (`VBR`, `VBA`)
cannot: code that has one does not compile, or stops when it runs, whatever the settings say.

## With a quick fix

A diagnostic that can be disabled comes with a quick fix, after its other fixes: **Disable RDC01001 in every workspace**. Choosing it adds the code to your
list. In the shell, `DUCKY` offers it with the other actions for a line; in an editor, it is one of the diagnostic's quick fixes.

## With CONFIG

`CONFIG` sets the whole list at once, as codes separated by commas:

```text
CONFIG Analysis.DisabledDiagnostics RDC01001, RDC01002
```

- `CONFIG Analysis.DisabledDiagnostics` shows the codes you disabled.
- To enable one code again, set the list without it.
- `CONFIG RESET Analysis.DisabledDiagnostics` enables them all again.

A code that cannot be disabled is refused. `rdc config` sets the same list from a command line.

## When it takes effect

At once, while the platform runs: the next analysis of a document leaves the disabled codes out, and the diagnostics already shown are refreshed. A list
set with `rdc config`, with no platform running, applies when the platform next starts.

> [!NOTE]
> **Not implemented.** A diagnostic cannot be disabled for one workspace only.

See [Diagnostics](diagnostics/index.md) for the codes, and
[**RD-VBAL §2.3.3** Configuration](specs/rd-vbal.2.3.3.configuration.md#disabled-diagnostics) for the setting.

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.md) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.md) | [**RD-VBAL**](specs/rd-vbal.md) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)
