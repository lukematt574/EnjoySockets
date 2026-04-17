namespace TcpRttBenchmarkClient.WTCP
{
    public class WatsonTCPClass
    {
        public WatsonTCPClass() { }

        List<IClientArea> _clients = new();
        public async Task StartTest()
        {
            Console.WriteLine("Loading clients ...");

            for (int i = 0; i < GlobalConfig.MaxClients; i++)
            {
                var _client = new ClientAreaWatsonTCP();
                _client.Connect();
                _clients.Add(_client);
            }

            if (_clients.Any(x => x.Failure))
            {
                Console.WriteLine($"Connect problem");
                return;
            }

            await Task.Delay(1000);

            Console.WriteLine("Start test ...");

            var listTasks = new List<Task>();

            foreach (ClientAreaWatsonTCP client in _clients)
            {
                Task t = client.Run();
                listTasks.Add(t);
            }

            Task.WaitAll(listTasks.ToArray());

            if (_clients.Any(x => x.Failure))
            {
                Console.WriteLine("Test failure!");
                return;
            }

            GlobalConfig.WriteResult(_clients, "WatsonTCP");
        }
    }
}
