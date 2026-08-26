using InsERT.Moria.Dokumenty.Logistyka;
using InsERT.Moria.Klienci;
using InsERT.Moria.ModelDanych;
using InsERT.Moria.Sfera;
using InsERT.Mox.ObiektyBiznesowe;
using Nexo.Invoice;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Zapqio.Runner.Core;

namespace Nexo
{
    public class AddInvoice : IRunnerMethod
    {
        private NexoClient _client;
        private readonly Settings _settings;
        private readonly SlackClient _slack;

        public AddInvoice(NexoClient client, Settings settings, SlackClient slack)
        {           
            _client = client;
            _settings = settings;
            _slack = slack;
            NexoExtensions.Client = _client;
        }
        public Type InData()
        {
            return typeof(InvoiceIn);
        }

        public string NameMethod()
        {
            return "Add invoice and pdf";
        }

        public Type OutData()
        {
            return typeof(InvoiceOut);
        }

        public async Task<string> Run(string data)
        {
            var input = System.Text.Json.JsonSerializer.Deserialize<InvoiceIn>(data);
            using IDokumentSprzedazy invoice = _client.Uchwyt.DokumentySprzedazy().UtworzFaktureSprzedazy();
            var doc = invoice.Dane;
            doc.Magazyn = _client.Uchwyt.Magazyny().Dane.Pierwszy(x => x.Symbol == _settings.Warehouse);
            doc.Uwagi = input.Comment;
            if (input.SaleDate.HasValue)
            {
                doc.DataSprzedazy = input.SaleDate.Value;
                doc.DataWprowadzenia = input.SaleDate.Value;

            }

            // Nabywca
            doc.Podmiot = GetEntity(input.Buyer);

            // Odbiorca
            if (input.Recipient != null)
            {
                doc.Odbiorca = GetEntity(input.Recipient);
                if (input.Recipient.BindWithBuyer ?? false)
                {
                    doc.Podmiot.BindWithByuer(doc.Odbiorca, input.Recipient);
                }
            }

            // Dla nabywcy z Polski nie ruszamy transakcji handlowej - dokument ma domyślnie ustawione "S"
            if (doc.Podmiot.AdresPodstawowy.Panstwo.KodISOAlfa2() != "PL")
            {
                Console.WriteLine($"Nabywca spoza Polski (państwo: {doc.Podmiot.AdresPodstawowy.Panstwo.KodISOAlfa2()}), ustalanie transakcji handlowej");
                doc.TransakcjaHandlowa = GetTH(doc.Podmiot);
            }

            if (!string.IsNullOrEmpty(input.Currency))
            {
                doc.Waluta = GetCurrency(input.Currency);
            }

            if (input.Positions == null || input.Positions.Count == 0)
            {
                throw new Exception("Nie uzupełniono pozycji dokumentu");
            }

            foreach (var pos in input.Positions)
            {
                ValidatePosition(pos);
                var ip = invoice.Pozycje.Dodaj(pos.Symbol);
                ip.Ilosc = pos.Quantity ?? 0;
                ip.StawkaVat = GetTax(pos);
                ip.Cena.NettoPrzedRabatem = pos.NetPrice ?? 0;
            }
            invoice.Przelicz();

            invoice.SetPayment(GetPaymentType(input.Payment), doc.KwotaDoZaplaty);

            // Ustawienie pól własnych (VIES, daty licencji)
            invoice.SetOwnFields(
                OwnField.Text(_settings.ViesOwnField, input.Vies),
                OwnField.Date(_settings.StartLicenceDateOwnField, input.StartLicenceDate),
                OwnField.Date(_settings.EndLicenceDateOwnField, input.EndLicenceDate));


            // Licencja na przełomie lat (np. start 2026, koniec 2027) - powiadomienie idzie
            // dopiero po udanym zapisie, żeby było w nim czym się posłużyć: numer dokumentu.
            var crossYearLicence = input.StartLicenceDate.HasValue
                && input.EndLicenceDate.HasValue
                && input.StartLicenceDate.Value.Year != input.EndLicenceDate.Value.Year;

            if (crossYearLicence)
            {
                if (!string.IsNullOrEmpty(_settings.RMPFlagName))
                    invoice.Dane.SetFlag(_settings.RMPFlagName);
                else
                    Console.Error.WriteLine($"Nie ustawiono flagi: Rozliczenia międzyokresowe (RMP)");

            }

            if (input.StartLicenceDate.HasValue && input.EndLicenceDate.HasValue)
            {
                var licencePeriod = $"Okres licencji: {input.StartLicenceDate.Value:yyyy-MM-dd} - {input.EndLicenceDate.Value:yyyy-MM-dd}";
                invoice.Dane.Uwagi = string.IsNullOrWhiteSpace(invoice.Dane.Uwagi)
                    ? licencePeriod
                    : invoice.Dane.Uwagi + "\n" + licencePeriod;
            }

            var saved = invoice.Zapisz();
            if (!saved)
            {
                throw new Exception($"Nie udało się zapisać faktury: {invoice.Error()}");
            }
            else
            {
                Console.WriteLine($"Utworzono fakturę: {invoice.Dane.NumerWewnetrzny.PelnaSygnatura}");

                if (crossYearLicence)
                {
                    // Send nie rzuca - nieudane powiadomienie nie ma prawa wywrócić wystawionej faktury.
                    await _slack.Send(
                        $"*Faktura {invoice.Dane.NumerWewnetrzny.PelnaSygnatura}* - licencja na przełomie lat "
                        + $"({input.StartLicenceDate.Value.Year} - {input.EndLicenceDate.Value.Year})\n"
                        + $"Okres: {input.StartLicenceDate.Value:yyyy-MM-dd} - {input.EndLicenceDate.Value:yyyy-MM-dd}\n"
                        + $"Nabywca: {doc.Podmiot?.NazwaSkrocona}");
                }
            }

            var namefile = BitConverter.ToString(MD5.HashData(Encoding.UTF8.GetBytes(invoice.Dane.NumerWewnetrzny.PelnaSygnatura))).Replace("-", "");
            var dir = Path.Combine(Path.GetTempPath(), "runner");
            Directory.CreateDirectory(dir);
            Console.WriteLine(Path.Combine(dir, namefile));
            using var print = _client.Uchwyt.Wydruki().Utworz(InsERT.Moria.Wydruki.Enums.TypWzorcaWydruku.FakturaSprzedazy);
            print.ObiektDoWydruku = invoice.Dane;

            // Wybór szablonu wydruku: MapLaguageToTemplatePrint → DefaultTemplatePrint → systemowy
            bool templateSet = false;

            if (_settings.MapLaguageToTemplatePrint != null
                && !string.IsNullOrEmpty(input.TemplatePrintLanguage)
                && _settings.MapLaguageToTemplatePrint.TryGetValue(input.TemplatePrintLanguage, out var mappedTemplateName))
            {
                var template = print.ParametryDrukowania.DostepneWzorce.FirstOrDefault(x => x.Nazwa == mappedTemplateName);
                if (template != null)
                {
                    print.ParametryDrukowania.WybranyWzorzec = template;
                    templateSet = true;
                }
                else
                {
                    Console.WriteLine($"Nie znaleziono szablonu '{mappedTemplateName}' dla języka '{input.TemplatePrintLanguage}'");
                }
            }

            if (!templateSet && !string.IsNullOrEmpty(_settings.DefaultTemplatePrint))
            {
                var template = print.ParametryDrukowania.DostepneWzorce.FirstOrDefault(x => x.Nazwa == _settings.DefaultTemplatePrint);
                if (template != null)
                {
                    print.ParametryDrukowania.WybranyWzorzec = template;
                    templateSet = true;
                }
                else
                {
                    Console.WriteLine($"Nie znaleziono domyślnego szablonu '{_settings.DefaultTemplatePrint}'");
                }
            }

            if (!templateSet)
            {
                Console.WriteLine($"Użyto szablonu systemowego: {print.ParametryDrukowania.WybranyWzorzec.Nazwa}");
            }

            print.ParametryDrukowania.NazwaDokumentuUzytkownika = namefile;
            print.ParametryDrukowania.SciezkaEksportu = dir;
            print.ParametryDrukowania.FormatEksportu = "pdf";
            print.ParametryDrukowania.ZastapPliki = true;
            print.Eksport();
            var error = print.PobierzListeBledow();
            if (error != null && error.Count() > 0)
            {
                throw new Exception($"Nie udało się utworzyć pliku PDF: {string.Join('|', error)}");
            }
            else
            {
                Console.WriteLine($"Utworzono plik PDF");
            }
            var filePath = Path.Combine(dir, namefile) + ".pdf";
            var pdf = Convert.ToBase64String(File.ReadAllBytes(filePath));
            File.Delete(filePath);
            return System.Text.Json.JsonSerializer.Serialize(new InvoiceOut
            {
                Number = invoice.Dane.NumerWewnetrzny.PelnaSygnatura,
                Pdf = pdf,
            });         
        }

        private void ValidatePosition(InvoicePosition pos)
        {
            if (string.IsNullOrEmpty(pos.Symbol))
            {
                throw new Exception($"Symbol nie może mieć wartości pustej lub null");
            }
            var asortyment = _client.Uchwyt.Asortymenty().Dane.Wszystkie(t => t.Symbol == pos.Symbol).FirstOrDefault();
            if (asortyment == null)
            {
                throw new Exception($"Nie znaleziono asortymentu o symbolu {pos.Symbol}");
            }

            if (pos.Quantity == null || pos.Quantity == 0)
            {
                throw new Exception($"Ilość na pozycji: {pos.Symbol} nie może być pusta lub zerowa");
            }

            if (pos.NetPrice == null)
            {
                throw new Exception($"Nie uzupełniono ceny netto na pozycji {pos.Symbol}");
            }
        }

        private Waluta GetCurrency(string currency)
        {
            var c = _client.Uchwyt.Waluty().Znajdz(currency);
            if (c == null)
            {
                throw new Exception($"Nie znaleziono waluty: {currency}");
            }
            return c.Dane;
        }

        private Podmiot GetEntity(InvoiceEntity entity)
        {
            if (entity == null) return null;

            if (!string.IsNullOrEmpty(entity.TaxId))
            {
                var taxIdDecoded = DecodeTaxId(entity.TaxId);
                if (taxIdDecoded == null)
                {
                    throw new Exception($"Nieprawidłowy NIP: {entity.TaxId}");
                }
                var podmiot = _client.Uchwyt.Podmioty().Dane.Pierwszy(x => (taxIdDecoded.Item1 == "PL" ? x.NIP == taxIdDecoded.Item2 : x.NIPUE == taxIdDecoded.Item1 + taxIdDecoded.Item2));
                if (podmiot != null)
                {
                    Console.WriteLine($"Wyszukano podmiot po NIP-ie: {taxIdDecoded.Item1 + taxIdDecoded.Item2}");
                    return podmiot;
                }
            }
            if (!string.IsNullOrEmpty(entity.Symbol))
            {
                using var podmiot = _client.Uchwyt.Podmioty().Znajdz(entity.Symbol);
                if (podmiot != null)
                {
                    Console.WriteLine($"Wyszukano podmiot {entity.Symbol} po symbolu");
                    return podmiot.Dane;
                }
            }
            return CreateEntity(entity);
        }

        private Tuple<string, string> DecodeTaxId(string taxId)
        {
            if (string.IsNullOrEmpty(taxId))
            {
                return null;
            }
            var reqex = new System.Text.RegularExpressions.Regex(@"^(\D{2})?(.+)");
            var match = reqex.Match(taxId.Replace("-", ""));
            var code = "PL";
            if (match != null && match.Success)
            {
                if (match.Groups[1].Success)
                {
                    code = match.Groups[1].Value.ToUpper();
                }
                return new Tuple<string, string>(code, match.Groups[2].Value);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Dzieli nazwę osoby fizycznej na imię i nazwisko po pierwszej spacji.
        /// "Jan Kowalski" -> ("Jan", "Kowalski"), "Anna Maria Nowak-Kowalska" -> ("Anna", "Maria Nowak-Kowalska"),
        /// pojedynczy człon trafia w całości do nazwiska.
        /// </summary>
        private static (string FirstName, string LastName) SplitPersonName(string name)
        {
            var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return (string.Empty, string.Empty);
            }
            if (parts.Length == 1)
            {
                return (string.Empty, parts[0]);
            }
            return (parts[0], string.Join(" ", parts.Skip(1)));
        }

        private Podmiot CreateEntity(InvoiceEntity entity)
        {
            Console.WriteLine($"Tworzenie podmiotu - nazwa: '{entity.Name}', pełna nazwa: '{entity.FullName}', symbol: '{entity.Symbol}', NIP: '{entity.TaxId}', kraj: '{entity.CountrySymbol}'");
            if (string.IsNullOrWhiteSpace(entity.CountrySymbol))
            {
                throw new Exception($"Nie uzupełniono kodu kraju (CountrySymbol) dla podmiotu '{entity.Name}' (symbol: '{entity.Symbol}', NIP: '{entity.TaxId}') - bez niego nie da się ustalić państwa adresu");
            }

            var taxIdDecoded = DecodeTaxId(entity.TaxId);
            var country = _client.Uchwyt.Panstwa().Dane.Pierwszy(x => x.KodPanstwaUE == entity.CountrySymbol.ToUpper());
            if (country != null)
            {
                Console.WriteLine($"Znaleziono państwo {country.KodPanstwaUE} (ISO: {country.KodISOAlfa2()}), członek UE: {country.CzlonekUE}");
            }
            else
            {
                Console.WriteLine($"UWAGA: nie znaleziono państwa o kodzie '{entity.CountrySymbol}' - podmiot powstanie bez państwa w adresie, zapis prawdopodobnie się nie powiedzie");
            }

            IPodmiot newEntity;
            if (entity.IsCompany)
            {
                // tworze firme
                newEntity = _client.Uchwyt.Podmioty().UtworzFirme();
                if (taxIdDecoded == null)
                {
                    Console.WriteLine("Tworzenie firmy - NIP nieuzupełniony");
                    newEntity.Dane.NIP = "";
                }
                else
                {
                    if (taxIdDecoded.Item1 == "PL")
                    {
                        newEntity.Dane.NIP = taxIdDecoded.Item2;
                        Console.WriteLine($"Tworzenie firmy - NIP krajowy: {newEntity.Dane.NIP}");
                    }
                    else
                    {
                        newEntity.Dane.PanstwoRejestracji = country;
                        newEntity.Dane.NIPUE = taxIdDecoded.Item1 + taxIdDecoded.Item2;
                        Console.WriteLine($"Tworzenie firmy - NIP UE: {newEntity.Dane.NIPUE}, państwo rejestracji: {entity.CountrySymbol}");
                    }
                }
                newEntity.Dane.Firma.Nazwa = !string.IsNullOrEmpty(entity.FullName) ? entity.FullName : entity.Name;
                newEntity.Dane.NazwaSkrocona = entity.Name;
                Console.WriteLine($"Nazwa firmy: '{newEntity.Dane.Firma.Nazwa}', nazwa skrócona: '{newEntity.Dane.NazwaSkrocona}'");
            }
            else
            {
                // tworze osobe fizyczną

                newEntity = _client.Uchwyt.Podmioty().UtworzOsobe();
                var personName = SplitPersonName(entity.Name);
                Console.WriteLine($"Tworzenie osoby fizycznej - imię: '{personName.FirstName}', nazwisko: '{personName.LastName}'");
                if (taxIdDecoded != null)
                {
                    Console.WriteLine($"UWAGA: podano NIP '{entity.TaxId}', ale IsCompany = false - NIP nie zostanie zapisany na osobie fizycznej");
                }

                newEntity.Dane.Osoba.Imie = personName.FirstName;
                newEntity.Dane.Osoba.Nazwisko = personName.LastName;

            }
            
            if (!string.IsNullOrEmpty(entity.Symbol))
            {
                newEntity.Dane.Sygnatura = new Sygnatura
                {
                    PelnaSygnatura = entity.Symbol
                };
                Console.WriteLine($"Ustawiono symbol podmiotu: {entity.Symbol}");
            }
            else
            {
                Console.WriteLine("Symbol nieuzupełniony - zostanie nadany automatycznie przez Nexo");
            }
            
            var glownyTyp = _client.Uchwyt.TypyAdresu().DaneDomyslne.Glowny;
            var address = newEntity.Dane.AdresPodstawowy ?? newEntity.DodajAdres(glownyTyp);
            address.Szczegoly.Ulica = entity.Street;
            address.Szczegoly.Miejscowosc = entity.City;
            address.Szczegoly.KodPocztowy = entity.PostalCode;
            address.Szczegoly.NrDomu = entity.HomeNumber ?? string.Empty;
            address.Szczegoly.NrLokalu = entity.ApartmentNumber ?? string.Empty;
            address.Panstwo = country;
            Console.WriteLine($"Adres podstawowy - ulica: '{address.Szczegoly.Ulica}', nr domu: '{address.Szczegoly.NrDomu}', nr lokalu: '{address.Szczegoly.NrLokalu}', kod: '{address.Szczegoly.KodPocztowy}', miejscowość: '{address.Szczegoly.Miejscowosc}'");

            if (!string.IsNullOrEmpty(entity.Phone))
            {
                var k = new Kontakt();
                newEntity.Dane.Kontakty.Add(k);
                k.Wartosc = entity.Phone;
                k.Rodzaj = _client.Uchwyt.RodzajeKontaktu().DaneDomyslne.Telefon;
                k.Podstawowy = true;
                Console.WriteLine($"Dodano kontakt podstawowy - telefon: {k.Wartosc}");
            }
            if (!string.IsNullOrEmpty(entity.Email))
            {
                var k = new Kontakt();
                newEntity.Dane.Kontakty.Add(k);
                k.Wartosc = entity.Email.ToLower();
                k.Rodzaj = _client.Uchwyt.RodzajeKontaktu().DaneDomyslne.Email;
                k.Podstawowy = true;
                Console.WriteLine($"Dodano kontakt podstawowy - e-mail: {k.Wartosc}");
            }

            var saved = newEntity.Zapisz();
            if (!saved)
            {
                throw new Exception($"Nie udało się zapisać podmiotu '{entity.Name}' (symbol: '{entity.Symbol}', NIP: '{entity.TaxId}', kraj: '{entity.CountrySymbol}'): {newEntity.Error()}");
            }

            var created = newEntity.Dane;
            var createdTaxId = !string.IsNullOrEmpty(created.NIPUE) ? created.NIPUE : created.NIP;
            Console.WriteLine($"Utworzono podmiot - symbol: {created.Sygnatura?.PelnaSygnatura ?? "(brak)"}, nazwa: '{created.NazwaSkrocona}', NIP: {(string.IsNullOrEmpty(createdTaxId) ? "(brak)" : createdTaxId)}, firma: {created.JestFirma()}");
            return created;
        }

        private StawkaVat GetTax(InvoicePosition position)
        {
            var tax = _client.Uchwyt.StawkiVat().Dane.Pierwszy(x => x.Symbol == position.TaxSymbol);
            if (tax != null)
            {
                Console.WriteLine($"Wyszukano stawkę VAT {position.TaxSymbol} po symbolu");
                return tax;
            }
            tax = _client.Uchwyt.StawkiVat().Dane.Pierwszy(x => x.Stawka == position.TaxPercent / 100M);            
            if (tax == null)
            {
                throw new Exception($"Nie znaleziono stawki VAT: {position.TaxSymbol}-{position.TaxPercent}");
            }
            Console.WriteLine($"Wyszukano stawkę VAT {position.TaxPercent} po wartości");
            return tax;
        }
        private FormaPlatnosci GetPaymentType(string payment)
        {
            var p = _client.Uchwyt.FormyPlatnosci().Dane.Pierwszy(x => x.Nazwa == payment);
            if (p == null)
            {
                throw new Exception($"Nie znaleziono formy płatności: {payment}");
            }
            Console.WriteLine($"Wyszukano formę płatności {payment} po nazwie");
            return p;
        }

        /// <summary>
        /// 1-transakcja przedsiębiorca z UE - SUPTK
        /// 2-transakcja przedsiębiorca spoza UE - DPTK
        /// 3-Konsument z UE - WSTO-OSS
        /// 4-konsumet spoza UE - DPTK
        /// 5 dla Polski - S 
        /// </summary>
        /// <param name="podmiot"></param>
        /// <returns></returns>
        private TransakcjaHandlowa GetTH(Podmiot podmiot)
        {
            var country = podmiot?.AdresPodstawowy?.Panstwo;
            if (country == null)
            {
                throw new Exception($"Nie można ustalić transakcji handlowej - podmiot {podmiot?.NazwaSkrocona} nie ma państwa w adresie podstawowym");
            }

            var isCompany = podmiot.JestFirma();
            string symbol;
            if (country.KodISOAlfa2() == "PL")
            {
                symbol = "S";
            }
            else if (!country.CzlonekUE)
            {
                symbol = "DPTK";
            }
            else if (isCompany)
            {
                symbol = "SUPTK";
            }
            else
            {
                symbol = "WSTO-OSS";
            }

            ITransakcjeHandlowe mgr = _client.Uchwyt.PodajObiektTypu<InsERT.Moria.Dokumenty.Logistyka.ITransakcjeHandlowe>();
            using var th = mgr.Znajdz(x => x.Symbol == symbol);
            if (th == null)
            {
                throw new Exception($"Nie znaleziono transakcji handlowej o symbolu: {symbol}");
            }
            Console.WriteLine($"Wybrano transakcję handlową {symbol} - państwo: {country.KodISOAlfa2()}, członek UE: {country.CzlonekUE}, firma: {isCompany}");
            return th.Dane;
        }
    }
}
