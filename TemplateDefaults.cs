public static class TemplateDefaults
{
    public const string PrescriptionHtml = """
<header class="letterhead">{{clinic.logo}}<div class="clinic-copy"><h1 class="clinic-name">{{clinic.name}}</h1>{{clinic.type}}{{clinic.address}}{{clinic.phone}}{{clinic.email}}</div></header>
<hr class="rule">
<section class="patient-grid top"><span>Patient ID: {{patient.code}}</span><span>Date: {{visit.date}}</span><span>Valid Till: {{visit.validTill}}</span></section>
<section class="patient-grid bottom"><span>Name: {{patient.name}}</span><span>Age: {{patient.age}}</span><span>Gender: {{patient.gender}}</span></section>
<hr class="rule doctor-rule">
<section class="clinical-info"><div><div class="doctor-heading">{{doctor.heading}}</div><div class="doctor-details">{{doctor.specialization}}{{doctor.registration}}</div></div><div><div class="vitals-title">VITALS</div><div class="vitals">{{vitals}}</div></div></section>
<div class="rx-space" aria-label="Blank prescription writing area"></div>
""";

    public const string InvoiceHtml = """
<header class="letterhead">{{clinic.logo}}<div class="clinic-copy"><h1 class="clinic-name">{{clinic.name}}</h1>{{clinic.type}}{{clinic.address}}{{clinic.phone}}{{clinic.email}}</div></header>
<hr class="rule">
<h1 class="service-bill-title">Service Bill</h1>
<section class="bill-info">
<div><span>Bill ID</span><b>{{bill.id}}</b></div><div><span>Date</span><b>{{bill.date}}</b></div><div><span>Patient ID</span><b>{{patient.code}}</b></div><div><span>Patient Name</span><b>{{patient.name}}</b></div><div><span>Age</span><b>{{patient.age}}</b></div><div><span>Gender</span><b>{{patient.gender}}</b></div><div><span>Phone</span><b>{{patient.phone}}</b></div><div><span>Payment Mode</span><b>{{bill.paymentMode}}</b></div>
</section>
<section class="bill-details"><h2>Bill details</h2><table class="service-bill-table"><thead><tr><th>Service</th><th>Category</th><th>Rate</th><th>Qty</th><th>Amount</th></tr></thead><tbody>{{invoice.serviceRows}}</tbody><tfoot><tr><th colspan="4">Total</th><th class="money">{{invoice.total}}</th></tr></tfoot></table></section>
""";

    public const string PreviousInvoiceHtml = """
<header class="letterhead">{{clinic.logo}}<div class="clinic-copy"><h1 class="clinic-name">{{clinic.name}}</h1>{{clinic.type}}{{clinic.address}}{{clinic.phone}}{{clinic.email}}</div></header>
<hr class="rule">
<div class="detail-head"><div><h2>{{patient.name}}</h2><p class="muted">{{patient.code}} · {{visit.date}}</p></div></div>
{{invoice.rows}}
""";

    public const string LegacyInvoiceHtml = """
<div class="print-only"><h1>{{clinic.name}}</h1></div>
<div class="detail-head"><div><span class="eyebrow">{{invoice.title}} · Visit {{visit.id}}</span><h2>{{patient.name}}</h2><p class="muted">{{patient.code}} · {{visit.date}}</p></div><span class="badge {{invoice.statusClass}}">{{invoice.status}}</span></div>
{{invoice.rows}}
""";
}