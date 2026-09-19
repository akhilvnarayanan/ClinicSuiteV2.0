---
name: Inno Setup file string types
description: Inno Setup 6.7 LoadStringFromFile requires an AnsiString output variable.
---

In Inno Setup 6.7 Pascal Script, declare the output variable passed to `LoadStringFromFile` as `AnsiString`, not Unicode `String`; call `StringChangeEx` without assigning its integer return value.

**Why:** The compiler reports type mismatches when a `String` variable is passed by reference or when `StringChangeEx`'s replacement count is assigned to a string.

**How to apply:** Keep the file buffer as `AnsiString`, convert it to `String` for Unicode helpers, and treat `StringChangeEx` as an in-place mutation whose integer return value can be ignored.