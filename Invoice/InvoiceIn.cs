using System;
using System.Collections.Generic;

namespace Nexo.Invoice
{
    public class InvoiceIn
    {
        public string Comment { get; set; }
        public InvoiceEntity Buyer { get; set; } //podmiot/nabywca
        public InvoiceEntity Recipient { get; set; } //odbiorca
        public List<InvoicePosition> Positions { get; set; }
        public string Payment { get; set; } //forma płatności
        public string Currency { get; set; }
        public string TemplatePrintLanguage { get; set; } //język, używany do wyboru szablonu wydruku
        public string Vies { get; set; } //odpowiedź na pytanie, czy nabywca jest podatnikiem VAT UE
        public DateTime? SaleDate { get; set; } //data sprzedaży, jeśli nie jest podana, to będzie data wystawienia
    }
}