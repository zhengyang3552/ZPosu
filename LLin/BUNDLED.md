# Bundled LLin (Hikariii) ruleset plugin

- Source: https://github.com/MATRIX-feather/LLin (branch `ruleset`, MIT licence)
- Bundled release: `2026.711.0` — `Hikariii.WindowsDesktop.zip` (published 2026-07-10)
- Files are verbatim from that archive; only `BUNDLED.md` and `LICENCE` are added on top.

## Why the files live here

`osu.Desktop.csproj` copies every `*.dll` in this directory (and `zh/*.dll`) into the
build output **root**, because `RulesetStore` discovers ruleset assemblies by scanning
the startup directory for `osu.Game.Rulesets.*.dll`:

```csharp
// osu.Game/Rulesets/RulesetStore.cs
string[] files = Directory.GetFiles(RuntimeInfo.StartupDirectory, @$"{ruleset_library_prefix}.*.dll");
```

## What LLin adds

- An in-game music player (reachable from the main menu and song select).
- Download acceleration for beatmap previews (Sayobot).
- Its own playable ruleset.

## Upstream caution

Upstream warns that connecting to the official osu! servers with ruleset plugins such
as LLin may put your account at risk of being banned. See the LLin README for details.
