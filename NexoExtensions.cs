using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using InsERT.Moria.Dokumenty.Logistyka;
using InsERT.Moria.ModelDanych;

namespace Nexo
{
    public static class NexoExtensions
    {
        public static void SetPayment(this IDokumentSprzedazy invoice, FormaPlatnosci paymentType, decimal paymentValue)
        {
            if (paymentType.Nazwa == "Zapłacono przelewem")
            {
                invoice.Platnosci.DodajPlatnoscOdroczona(paymentType, paymentValue);
            }
            else
            {
                invoice.Platnosci.DodajPlatnoscNatychmiastowa(paymentType, paymentValue);
            }
        }
    }
}
