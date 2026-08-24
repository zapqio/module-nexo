namespace Nexo.Invoice
{
    public class InvoiceEntity
    {
        public string City { get; set; } // city
        public string Name { get; set; } // Nazwa 
        public string Email { get; set; }
        public string Phone { get; set; }
        public string TaxId { get; set; } // NIP bez kraju 
        public string Street { get; set; } // street
        public string Symbol { get; set; } // Symbol klienta
        public string FullName { get; set; } // Pelna nazwa
        public string HomeNumber { get; set; }
        public string PostalCode { get; set; } // postal code
        public string CountrySymbol { get; set; } // country (country symbol, e.g. PL)
        public string ApartmentNumber { get; set; }
        public bool IsCompany { get; set; } // True jeśli jest firmą, false jeśli jest osobą fizyczną 

        // dane do powiązania oddziałów
        public bool? BindWithBuyer { get; set; }
        public PowiazaniePodmiotu? BindType { get; set; } // mapowane na RodzajPowiazaniaPodmiotu z SDK - patrz PowiazaniePodmiotu
        public string InternalId { get; set; }
    }
}
