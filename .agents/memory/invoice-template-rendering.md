---
name: Invoice template rendering
description: The rendering boundary between saved invoice HTML, the in-app bill view, and the print window.
---

Invoice templates can be saved either as an HTML fragment or as a complete document with html, head, style, and body elements. The renderer must detect the form, avoid nesting a complete document inside the application DOM, preserve head styles for the in-app view, and send complete documents directly to the print window.

**Why:** The template editor accepts arbitrary HTML, and a complete saved document becomes invalid when inserted inside a div. That caused saved invoice designs and print styles to be ignored or rendered inconsistently.

**How to apply:** Keep template retrieval fresh when opening or printing an invoice. Support both full documents and fragments, and keep the invoice placeholder map aligned with the data returned by the invoice endpoint.