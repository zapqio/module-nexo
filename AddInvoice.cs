using InsERT.Moria.Dokumenty.Logistyka;
using InsERT.Moria.Klienci;
using InsERT.Moria.ModelDanych;
using InsERT.Moria.PolaWlasne2;
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

        public AddInvoice(NexoClient client, Settings settings)
        {
            _client = client;
            _settings = settings;
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
            }
            doc.Odbiorca = GetEntity(input.Recipient);
            doc.Podmiot = GetEntity(input.Buyer);
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
            invoice.Platnosci.DodajPlatnoscNatychmiastowa(GetPaymentType(input.Payment), doc.KwotaDoZaplaty);
            // Ustawienie pola własnego dla VIES
            if (!string.IsNullOrEmpty(_settings.ViesOwnField) && !string.IsNullOrEmpty(input.Vies))
            {
                var ownFieldZ = _client.Uchwyt.PodajObiektTypu<IZaawansowanePolaWlasne>();
                var u = ownFieldZ.PosiadaZaawansowanePoleWlasne<DokumentDS>(_settings.ViesOwnField);
                if (u)
                {
                    var ownFieldAccesor = _client.Uchwyt.UtworzPolaWlasneAdv2Accessor(doc);
                    ownFieldAccesor.UstawWartoscTypuTekst(_settings.ViesOwnField, input.Vies);
                }
                else
                {
                    Console.WriteLine($"Nie znaleziono pola własnego '{_settings.ViesOwnField}' dla dokumentu");
                }
            }
            var saved = invoice.Zapisz();
            if (!saved)
            {
                throw new Exception($"Nie udało się zapisać faktury: {Error(invoice)}");
            }
            else
            {
                Console.WriteLine($"Utworzono fakturę: {invoice.Dane.NumerWewnetrzny.PelnaSygnatura}");
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
            if (!string.IsNullOrEmpty(entity.Symbol))
            {
                using var podmiot = _client.Uchwyt.Podmioty().Znajdz(entity.Symbol);
                if (podmiot != null)
                {
                    Console.WriteLine($"Wyszukano podmiot {entity.Symbol} po symbolu");
                    return podmiot.Dane;
                }
            }

            if (!string.IsNullOrEmpty(entity.Name))
            {
                using var podmiot = _client.Uchwyt.Podmioty().Znajdz(x => x.NazwaSkrocona == entity.Name);
                if (podmiot != null)
                {
                    Console.WriteLine($"Wyszukano podmiot {entity.Name} po nazwie");
                    return podmiot.Dane;
                }
            }

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
        private Podmiot CreateEntity(InvoiceEntity entity)
        {
            Console.WriteLine($"Tworzenie podmiotu, nazwa: {entity.Name}, symbol: {entity.Symbol}, NIP: {entity.TaxId}");
            using IPodmiot newEntity = _client.Uchwyt.Podmioty().UtworzFirme();

            if (!string.IsNullOrEmpty(entity.Symbol))
            {
                newEntity.Dane.Sygnatura = new Sygnatura
                {
                    PelnaSygnatura = entity.Symbol
                };
            }
            newEntity.Dane.NazwaSkrocona = entity.Name;
            newEntity.Dane.Firma.Nazwa = !string.IsNullOrEmpty(entity.FullName) ? entity.FullName : entity.Name;         
            var country = _client.Uchwyt.Panstwa().Dane.Pierwszy(x => x.KodPanstwaUE == entity.CountrySymbol.ToUpper());
            var glownyTyp = _client.Uchwyt.TypyAdresu().DaneDomyslne.Glowny;
            var address = newEntity.Dane.AdresPodstawowy ?? newEntity.DodajAdres(glownyTyp);
            address.Szczegoly.Ulica = entity.Street;
            address.Szczegoly.Miejscowosc = entity.City;
            address.Szczegoly.KodPocztowy = entity.PostalCode;
            address.Szczegoly.NrDomu = entity.HomeNumber;
            address.Szczegoly.NrLokalu = entity.ApartmentNumber ?? string.Empty;
            address.Panstwo = country;

            var taxIdDecoded = DecodeTaxId(entity.TaxId);
            if (taxIdDecoded != null)
            {
                if (taxIdDecoded.Item1 == "PL")
                {
                    newEntity.Dane.NIP = taxIdDecoded.Item2;
                }
                else
                {
                    newEntity.Dane.PanstwoRejestracji = country;
                    newEntity.Dane.NIPUE = taxIdDecoded.Item1 + taxIdDecoded.Item2;
                }
            }

            if (!string.IsNullOrEmpty(entity.Phone))
            {
                var k = new Kontakt();
                newEntity.Dane.Kontakty.Add(k);
                k.Wartosc = entity.Phone;
                k.Rodzaj = _client.Uchwyt.RodzajeKontaktu().DaneDomyslne.Telefon;
                k.Podstawowy = true;
            }
            if (!string.IsNullOrEmpty(entity.Email))
            {
                var k = new Kontakt();
                newEntity.Dane.Kontakty.Add(k);
                k.Wartosc = entity.Email.ToLower();
                k.Rodzaj = _client.Uchwyt.RodzajeKontaktu().DaneDomyslne.Email;
                k.Podstawowy = true;
            }

            var saved = newEntity.Zapisz();
            if (!saved)
            {
                throw new Exception($"Nie udało się zapisać podmiotu: {Error(newEntity)}");
            }
            Console.WriteLine($"Utworzono podmiot - Nazwa: {newEntity.Dane.NazwaSkrocona}, NIP: {newEntity.Dane.NIPUE}, Symbol: {newEntity.Dane.Sygnatura.PelnaSygnatura}");
            return newEntity.Dane;
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
        private string Error(IObiektBiznesowy doc)
        {
            var errors = new List<string>();
            foreach (var itemE in doc.Bledy)
            {
                string e = "";
                if (itemE.Errors.Any())
                {
                    e = "Obiekty: " + string.Join(", ", itemE.Errors.Select(y => $"{y} - {itemE.GetType().Name}"));
                }
                string p = "";
                if (itemE.MemberErrors.Any())
                {
                    p = "Pola: ";
                    var temp = new List<string>();
                    foreach (var itemP in itemE.MemberErrors)
                    {
                        var w = $"{itemP.Key} - {string.Join(" ", itemP.Select(x => itemE.GetType().Name + "." + x))}";
                        temp.Add(w);
                    }
                    p += string.Join("\n   ", temp);
                }
                errors.Add(string.Join(" ", e, p));

            }

            return "\n" + string.Join('\n', errors.Select((x, i) => $"{i + 1}. {x}"));
        }
    }
}
