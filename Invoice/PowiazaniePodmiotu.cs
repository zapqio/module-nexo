using System;
using System.Text.Json.Serialization;
using InsERT.Moria.Klienci;

namespace Nexo.Invoice
{
    /// <summary>
    /// Rodzaj powiązania odbiorcy z nabywcą - wartość pola <see cref="InvoiceEntity.BindType"/>.
    ///
    /// MAPOWANIE: to jest nasz odpowiednik enuma <see cref="RodzajPowiazaniaPodmiotu"/> z SDK Nexo
    /// (InsERT.Moria.Klienci). Wartości liczbowe są celowo IDENTYCZNE jak w SDK, ale nie polegamy
    /// na tej zbieżności - przełożenie robi jawnie <see cref="PowiazaniePodmiotuExtensions.ToNexo(PowiazaniePodmiotu)"/>.
    ///
    /// Po co własna kopia, skoro SDK ma już swój enum:
    ///  - na własnym typie można powiesić [JsonConverter(typeof(JsonStringEnumConverter))], dzięki
    ///    czemu wejście przyjmuje i nazwę ("Inny"), i liczbę (0). Na typie z SDK się nie da, a
    ///    domyślny JsonSerializer w AddInvoice.Run przyjmował WYŁĄCZNIE liczby - "BindType": "Inny"
    ///    wywracało całą fakturę wyjątkiem JsonException, jeszcze przed jakąkolwiek pracą,
    ///  - kontrakt wejściowy modułu (InvoiceIn) przestaje zależeć od typów z SDK.
    ///
    /// Gdy w SDK przybędzie nowa wartość, trzeba ją dopisać TUTAJ oraz w ToNexo().
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PowiazaniePodmiotu : byte
    {
        /// <summary>Odpowiada <see cref="RodzajPowiazaniaPodmiotu.Inny"/>.</summary>
        Inny = 0,

        /// <summary>Odpowiada <see cref="RodzajPowiazaniaPodmiotu.JednostkaSamorzaduTerytorialnego"/>.</summary>
        JednostkaSamorzaduTerytorialnego = 1,

        /// <summary>Odpowiada <see cref="RodzajPowiazaniaPodmiotu.CzlonekGrupyVAT"/>.</summary>
        CzlonekGrupyVAT = 2,
    }

    public static class PowiazaniePodmiotuExtensions
    {
        /// <summary>
        /// Przekłada wartość z kontraktu modułu na enum SDK.
        ///
        /// Świadomie jest to switch, a nie rzutowanie (byte): JsonStringEnumConverter przepuszcza
        /// dowolną liczbę mieszczącą się w typie bazowym, więc z JSON-a potrafi przyjechać np. 99,
        /// którego w enumie nie ma. Rzutowanie wpisałoby te 99 do bazy, switch od razu to zatrzyma.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Wartość spoza enuma (np. liczba z JSON-a).</exception>
        public static RodzajPowiazaniaPodmiotu ToNexo(this PowiazaniePodmiotu value)
        {
            switch (value)
            {
                case PowiazaniePodmiotu.Inny:
                    return RodzajPowiazaniaPodmiotu.Inny;
                case PowiazaniePodmiotu.JednostkaSamorzaduTerytorialnego:
                    return RodzajPowiazaniaPodmiotu.JednostkaSamorzaduTerytorialnego;
                case PowiazaniePodmiotu.CzlonekGrupyVAT:
                    return RodzajPowiazaniaPodmiotu.CzlonekGrupyVAT;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        $"Nieznany rodzaj powiązania podmiotu: {(byte)value}. Dozwolone: " +
                        string.Join(", ", Enum.GetNames(typeof(PowiazaniePodmiotu))) + ".");
            }
        }

        /// <summary>Wariant dla pola opcjonalnego - null zostaje nullem.</summary>
        public static RodzajPowiazaniaPodmiotu? ToNexo(this PowiazaniePodmiotu? value)
        {
            return value.HasValue ? value.Value.ToNexo() : (RodzajPowiazaniaPodmiotu?)null;
        }
    }
}
