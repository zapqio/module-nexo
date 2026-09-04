using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using InsERT.Moria.Dokumenty.Logistyka;
using InsERT.Moria.Flagi;
using InsERT.Moria.Klienci;
using InsERT.Moria.KsiegowoscPelna;
using InsERT.Moria.ModelDanych;
using InsERT.Moria.PolaWlasne2;
using InsERT.Moria.Sfera;
using InsERT.Mox.ObiektyBiznesowe;
using Nexo.Invoice;

namespace Nexo
{
    public static class NexoExtensions
    {
        public static NexoClient Client { get; set; }

        public static void SetPayment(this IDokumentSprzedazy invoice, FormaPlatnosci paymentType, decimal paymentValue)
        {
            if (paymentType.Nazwa == "Zapłacono przelewem")
            {
                invoice.Platnosci.DodajPlatnoscOdroczona(paymentType, paymentValue);
            }
            if (paymentType.Nazwa == "Stripe RCP" || 
                paymentType.Nazwa == "Stripe RCP EUR"
            )
            {
                invoice.Platnosci.DodajPlatnoscNatychmiastowa(paymentType, paymentValue);
            }
        }

        public static void SetOwnFields(this IDokumentSprzedazy invoice, params OwnField[] fields)
        {
            var toSet = fields.Where(x => x != null && !x.IsEmpty).ToList();
            if (toSet.Count == 0)
            {
                return;
            }

            var ownFieldZ = Client.Uchwyt.PodajObiektTypu<IZaawansowanePolaWlasne>();
            var ownFieldAccesor = Client.Uchwyt.UtworzPolaWlasneAdv2Accessor(invoice.Dane);

            foreach (var field in toSet)
            {
                if (!ownFieldZ.PosiadaZaawansowanePoleWlasne<DokumentDS>(field.Name))
                {
                    Console.Error.WriteLine($"Nie znaleziono pola własnego '{field.Name}' dla dokumentu");
                    continue;
                }

                switch (field.Type)
                {
                    case OwnFieldType.Text:
                        ownFieldAccesor.UstawWartoscTypuTekst(field.Name, (string)field.Value);
                        break;
                    case OwnFieldType.Date:
                        ownFieldAccesor.UstawWartoscTypuData(field.Name, (DateTime?)field.Value);
                        break;
                    default:
                        Console.Error.WriteLine($"Nieobsługiwany typ pola własnego '{field.Name}': {field.Type}");
                        break;
                }
            }
        }

        public static void BindWithByuer(this Podmiot nexoBuyer, Podmiot nexoRecipinet, InvoiceEntity recipient)
        {
            Console.WriteLine($"Wiązanie podmiotów - nabywca: {nexoBuyer?.NazwaSkrocona}, odbiorca: {nexoRecipinet?.NazwaSkrocona}");
            using var iNexoBuyer = Client.Uchwyt.Podmioty().Znajdz(nexoBuyer);
            using var iNexoRecipient = Client.Uchwyt.Podmioty().Znajdz(nexoRecipinet);

            var existing = iNexoBuyer.Dane.PodmiotyPodrzedne.FirstOrDefault(x => x.PodmiotPodrzedny.Id == iNexoRecipient.Dane.Id);
            if (existing != null)
            {
                Console.WriteLine($"Powiązanie już istnieje, pomijam");
                return;
            }

            var relacjaPodmiotow = new RelacjaPodmiotow();
            iNexoBuyer.Dane.PodmiotyPodrzedne.Add(relacjaPodmiotow);

            relacjaPodmiotow.PodmiotPodrzedny = iNexoRecipient.Dane;
            // ToNexo() przeklada nasz PowiazaniePodmiotu na enum SDK i odrzuca wartosci spoza enuma
            relacjaPodmiotow.RodzajPowiazania = (byte?)recipient.BindType.ToNexo();
            relacjaPodmiotow.IdentyfikatorWewnetrzny = recipient.InternalId ?? "";

            if (iNexoBuyer.Zapisz())
            {
                Console.WriteLine($"Powiązano podmioty - rodzaj powiązania: {recipient.BindType}, identyfikator wewnętrzny: {recipient.InternalId}");
            }
            else
            {
                Console.Error.WriteLine($"Nie udało się powiązać podmiotów: {iNexoBuyer.Error()}");
            }
        }

        public static string Error(this IObiektBiznesowy doc)
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
    
    
        /// <summary>
        /// Ustawia flage wlasna na obiekcie (dokument, podmiot, ...). Brak flagi w slowniku
        /// ani blad przypisania nie przerywaja operacji biznesowej - zostaje wpis w logu.
        /// </summary>
        public static void SetFlag(this IFlagsSupport obj, string flagName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(flagName))
            {
                return;
            }

            try
            {
                var newFlag = Client.Uchwyt.FlagiWlasne().Dane.Pierwszy(x => x.Nazwa == flagName);
                if (newFlag == null)
                {
                    Console.Error.WriteLine($"Nie znaleziono flagi własnej o nazwie '{flagName}'");
                    return;
                }

                var flagBefore = obj.FlagaWlasna?.Nazwa;
                if (flagBefore == flagName)
                {
                    Console.WriteLine($"Flaga własna '{flagName}' jest już ustawiona, pomijam");
                    return;
                }

                obj.FlagaWlasna = newFlag;

                if (string.IsNullOrEmpty(flagBefore))
                {
                    Console.WriteLine($"Ustawiono flagę własną '{flagName}'");
                }
                else
                {
                    Console.WriteLine($"Zmieniono flagę własną z '{flagBefore}' na '{flagName}'");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Nie udało się ustawić flagi własnej '{flagName}': {ex.Message}");
            }
        }
    }
}
