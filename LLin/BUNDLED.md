# Bundled LLin (Hikariii) ruleset plugin

This directory contains the **source** of LLin, vendored from
<https://github.com/MATRIX-feather/LLin> (branch `ruleset`, commit
`1362a05053136bffcd34cad197b5ccb24341d5dd`, 2026-08-22, MIT licence).

## Why the source is vendored instead of shipping upstream binaries

Upstream releases and CI builds reference `ppy.osu.Game` `2026.730.0`. osu! commit
`38b9aa5401` ("Improve settings sidebar and ruleset visibility", 2026-08-19) made
`RulesetSettingsSubsection.Header` / `CreateHeader()` **sealed**, and LLin's
`HikariiiMainSection` overrides `Header`. Loading any upstream LLin binary against a newer
osu! therefore fails at type load:

```
System.TypeLoadException: Declaration referenced in a method implementation cannot be a final method.
  Type: 'osu.Game.Rulesets.Hikariii.Graphics.Settings.HikariiiMainSection'
```

Compiling the vendored source against this repository's own `osu.Game` project produces a
plugin that matches this osu! version exactly.

## Deviations from upstream

- `osu.Game.Rulesets.Hikariii` and the two supporting projects target `net10.0` and
  `ProjectReference` this repository's `osu.Game` / `osu.Framework` instead of consuming
  `ppy.osu.Game` from NuGet.
- `Directory.Build.props` and `.editorconfig` in this directory intentionally opt out of the
  repository's analyzer/code-style strictness, which is not appropriate for third-party code.
- `HikariiiMainSection.Header` override removed (the member is sealed upstream now).
- The Windows-specific integrations (SMTC / toasts) are `#if WINDOWS`-guarded and are not
  compiled by the `net10.0` target used here.

## Upstream caution

Upstream warns that connecting to the official osu! servers with ruleset plugins such as LLin
may put your account at risk of being banned. See `README-upstream.md`.
