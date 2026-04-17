namespace TcpRttBenchmarkClient.SS
{
    public class SuperSocketClass
    {
        List<IClientArea> _clients = new();
        public async Task StartTest()
        {
            Console.WriteLine("Loading clients ...");

            for (int i = 0; i < GlobalConfig.MaxClients; i++)
            {
                var _client = new ClientAreaSuperSocket();
                if (await _client.Connect())
                    _clients.Add(_client);
                else
                {
                    Console.WriteLine($"Connect problem");
                    return;
                }
            }

            await Task.Delay(1000);

            Console.WriteLine("Start test ...");

            var listTasks = new List<Task>();

            foreach (ClientAreaSuperSocket client in _clients)
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

            GlobalConfig.WriteResult(_clients, "SuperSocket");
        }
    }
}
