---
name: Invoice pending-charge allocation
description: The accounting rule used when generating invoices from visit-level payments and charge rows.
---

Visit payments are not linked to individual services in the database, so invoice generation applies the aggregate payment amount oldest-charge-first across the consultation fee and added visit services. Fully covered charges are omitted, partially covered charges emit only their outstanding remainder, and a fully paid visit emits no pending service rows.

**Why:** The existing data model stores payments by visit, while users expect a new invoice to represent only the current unpaid work rather than repeat settled historical charges.

**How to apply:** Keep billing totals and invoice totals based on the same charge list and payment allocation rule. If payments ever gain direct service links, replace FIFO allocation with those explicit allocations.