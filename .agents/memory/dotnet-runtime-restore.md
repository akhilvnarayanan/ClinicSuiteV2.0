---
name: Runtime-specific .NET restore
description: Restore the target runtime graph before using no-restore publish commands.
---

When a release script publishes with `--runtime` and `--no-restore`, its preceding restore must include the same runtime identifier.

**Why:** A normal restore can leave `project.assets.json` without the runtime-specific target, causing `NETSDK1047` during the publish step.

**How to apply:** Pair `dotnet restore` with `--runtime win-x64` before the Windows packaging script uses `dotnet publish --runtime win-x64 --no-restore`.