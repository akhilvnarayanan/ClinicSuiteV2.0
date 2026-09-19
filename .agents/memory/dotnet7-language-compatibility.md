---
name: .NET 7 language compatibility
description: Compiler constraint for the Clinic Suite project’s conditional net7.0 targets.
---

Use C# syntax supported by the project’s .NET 7 toolchain unless the target framework and language version are intentionally upgraded.

**Why:** Collection-expression syntax compiled only with a newer language mode and broke the normal .NET 7 build during audit work.

**How to apply:** Prefer explicit arrays and existing C# 11-compatible forms in shared server code and verify with the normal `dotnet build` command.