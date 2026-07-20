namespace Nexo.Invoice
{
    public class InvoiceEntity
    {
        public string TaxId { get; set; } // NIP bez kraju 
        public string Symbol { get; set; } // Symbol klienta
        public string Name { get; set; } // Nazwa 
        public string FullName { get; set; } // Pelna nazwa
        public string Street { get; set; } // street
        public string City { get; set; } // city
        public string PostalCode { get; set; } // postal code
        public string CountrySymbol { get; set; } // country (country symbol, e.g. PL)
        public string Phone { get; set; }
        public string Email { get; set; }
        public string HomeNumber { get; set; }
        public string ApartmentNumber { get; set; }
    }
}
