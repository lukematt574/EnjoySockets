namespace TcpRttBenchmarkClient.ES
{
    public class EnjoySocketsClass
    {
        public EnjoySocketsClass() { }

        List<IClientArea> _clients = new();
        public async Task StartTest()
        {
            Console.WriteLine("Loading clients ...");

            for (int i = 0; i < GlobalConfig.MaxClients; i++)
            {
                var _client = new ClientAreaEnjoySockets();
                var connectStatus = await _client.Connect();
                if (connectStatus != 0)
                {
                    Console.WriteLine($"Error code connect to server: {connectStatus}");
                    return;
                }
                else
                    _clients.Add(_client);
            }

            await Task.Delay(1000);

            Console.WriteLine("Start test ...");

            var listTasks = new List<Task>();

            foreach (ClientAreaEnjoySockets client in _clients)
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

            GlobalConfig.WriteResult(_clients, "EnjoySockets");
        }
    }
}
