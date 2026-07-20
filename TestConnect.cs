using InsERT.Moria.Uzytkownicy;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Zapqio.Runner.Core;

namespace Nexo
{
    public class TestConnect : IRunnerMethod
    {
        private readonly NexoClient _client;

        public TestConnect(NexoClient client)
        {
            _client = client;
        }
        public Type InData()
        {
            return null;
        }

        public string NameMethod()
        {
            return "Who am I (test)";
        }

        public Type OutData()
        {
            return typeof(Out);
        }

        public async Task<string> Run(string data)
        {
            var userData = _client.Uchwyt.PodajObiektTypu<IZalogowanyUzytkownik>().Dane;
            var output = new Out
            {
                Name = userData.Sygnatura
            };
            var j = JsonSerializer.Serialize(output);
            Console.WriteLine(j);
            return j;
        }
        public class Out
        {
            public string Name { get; set; }
        }
    }
}
