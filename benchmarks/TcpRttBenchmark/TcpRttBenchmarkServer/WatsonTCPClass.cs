using EnjoySockets;
using WatsonTcp;

namespace TcpRttBenchmarkServer
{
    public class WatsonTCPClass
    {
        WatsonTcpServer _server;
        Type _typeArg;
        EObjPool _objPool;

        public WatsonTCPClass()
        {
            _server = new WatsonTcpServer(GlobalConfig.IP, GlobalConfig.Port, "cert.pfx", "password");
            _server.Settings.NoDelay = true;
            _server.Settings.AcceptInvalidCertificates = true;
            _server.Settings.MutuallyAuthenticate = true;
            _server.Events.ClientConnected += ClientConnected;
            _server.Events.ClientDisconnected += ClientDisconnected;
            _server.Events.MessageReceived += MessageReceived;
            _typeArg = typeof(List<long>);
            _objPool = new EObjPool(_typeArg, 0);
        }

        public void CreateServer()
        {
            Console.Clear();

            _server.Start();

            Console.WriteLine("Server WatsonTCP started");
        }

        void ClientConnected(object sender, ConnectionEventArgs args) { }

        void ClientDisconnected(object sender, DisconnectionEventArgs args) { }

        void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            var obj = _objPool.Rent();
            if (ESerial.Deserialize(_typeArg, args.Data, ref obj))
                _ = CallBack(args.Client.Guid, obj);
        }

        byte[] response = [1];
        public async ValueTask CallBack(Guid guid, object? data)
        {
            var list = data as List<long>;
            if (list != null)
            {
                await _server.SendAsync(guid, response);
                _objPool.Return(data);
            }
        }
    }
}
