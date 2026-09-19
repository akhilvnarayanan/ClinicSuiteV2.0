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
<div class="detail-head"><div><h2>{{patient.name}}</h2><p class="muted">{{patient.code}} · {{visit.date}}</p></div></div>
{{invoice.rows}}
""";

    public const string LegacyInvoiceHtml = """
<div class="print-only"><h1>{{clinic.name}}</h1></div>
<div class="detail-head"><div><span class="eyebrow">{{invoice.title}} · Visit {{visit.id}}</span><h2>{{patient.name}}</h2><p class="muted">{{patient.code}} · {{visit.date}}</p></div><span class="badge {{invoice.statusClass}}">{{invoice.status}}</span></div>
{{invoice.rows}}
""";
}