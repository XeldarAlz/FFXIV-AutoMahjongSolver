## What

<!-- One or two sentences on what this PR changes. -->

## Why

<!-- The motivating problem, linked issue, or user-visible behavior this fixes. -->

Closes #

## How to test

<!--
Minimum steps a reviewer can run to verify the change. For engine/policy changes, this is usually a test name. For UI changes, describe what to click in-game.
-->

## Checklist

- [ ] `dotnet build Mahjong.Plugin.Dalamud.sln` passes
- [ ] `dotnet test Mahjong.Plugin.Dalamud.sln` passes
- [ ] Added or updated tests (if behavior changed)
- [ ] If this changes user-visible behavior, README is updated
- [ ] If this bumps the plugin version, both `Directory.Build.props` and `repo/repo.json` are in sync
