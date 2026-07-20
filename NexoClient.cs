using InsERT.Moria.Sfera;
using System;
using Zapqio.Runner.Core;

namespace Nexo
{
    public class NexoClient : IRunnerInjection, IDisposable
    {
        internal class ConnectingStatusSfery : IPostepLadowaniaSfery
        {
            public ConnectingStatusSfery()
            {
            }
            private int _percentMod = -1;
            private string _descryption;

            public void RaportujPostep(PostepLadowaniaSferyEventArgs args)
            {
                var perMod = args.BiezacyProcent / 10;
                var des = args.Opis;
                if (_percentMod != perMod || _descryption != des)
                {
#if DEBUG
                    Console.WriteLine($"Nexo - {des}:{perMod * 10}");
#endif
                    _percentMod = perMod;
                    _descryption = des;

                }
            }
        }
        private readonly Settings _settings;
        private Uchwyt _uchwyt;
        public Uchwyt Uchwyt
        {
            get
            {
                if (_uchwyt == null)
                {
                    Connection();
                }
                return _uchwyt;
            }
        }
        public NexoClient(Settings settings)
        {
            _settings = settings;
        }

        public void Dispose()
        {
            _uchwyt?.Dispose();
            _uchwyt = null;
        }

        private void Connection()
        {
            lock (this)
            {
                if (_uchwyt != null)
                {
                    return;
                }
                Console.WriteLine("Connecting Nexo");
                DanePolaczenia connectingData;
                if (_settings.Connect.WindowsLogin)
                {
                    connectingData = DanePolaczenia.Jawne(
                    serwer: _settings.Connect.DatabaseServer,
                        baza: _settings.Connect.DatabaseName,
                        autentykacjaWindowsDoSerwera: true);
                }
                else
                {
                    connectingData = DanePolaczenia.Jawne(
                        serwer: _settings.Connect.DatabaseServer,
                        baza: _settings.Connect.DatabaseName,
                        uzytkownikSerwera: _settings.Connect.DatabaseUser,
                        hasloUzytkownikaSerwera: _settings.Connect.DatabasePassword);
                }
                _uchwyt = new MenedzerPolaczen().Polacz(connectingData, InsERT.Mox.Product.ProductId.Subiekt, new ConnectingStatusSfery());
                var logeed = _uchwyt.ZalogujOperatora(_settings.Connect.UserName, _settings.Connect.UserPassword);
                if (!logeed)
                {
                    throw new Exception("Nexo login failed");
                }
            }
        }
    }
}
