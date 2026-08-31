using System;
using System.Collections.Generic;

namespace Nexo.Invoice
{
    public class InvoiceIn
    {
        public string UniqueId { get; set; }
        public string Vies { get; set; } //odpowiedź na pytanie, czy nabywca jest podatnikiem VAT UE
        public InvoiceEntity Buyer { get; set; } //podmiot/nabywca
        public string Comment { get; set; }
        public string Payment { get; set; } //forma płatności
        public string Currency { get; set; }
        public DateTime? SaleDate { get; set; } //data sprzedaży, jeśli nie jest podana, to będzie data wystawienia
        public List<InvoicePosition> Positions { get; set; }
        public InvoiceEntity Recipient { get; set; } //odbiorca
        public string TemplatePrintLanguage { get; set; } //język, używany do wyboru szablonu wydruku
        public DateTime? StartLicenceDate { get; set; }
        public DateTime? EndLicenceDate { get; set; }

    }
}