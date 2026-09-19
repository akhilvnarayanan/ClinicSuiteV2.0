---
name: Inno Setup file string types
description: Inno Setup 6.7 LoadStringFromFile requires an AnsiString output variable.
---

In Inno Setup 6.7 Pascal Script, declare the output variable passed to `LoadStringFromFile` as `AnsiString`, not Unicode `String`.

**Why:** The compiler reports a type mismatch at the call when a `String` variable is passed by reference.

**How to apply:** Keep the file buffer as `AnsiString`, then convert it to `String` only when passing it to Unicode string helpers or assigning it to installer UI/config values.