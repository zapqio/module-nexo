namespace Nexo.Invoice
{
    public class InvoicePosition
    {
        public string Symbol { get; set; }
        public int? Quantity { get; set; }
        public string TaxSymbol { get; set; }
        public int? TaxPercent { get; set; }
        public decimal? NetPrice { get; set; }

    }
}