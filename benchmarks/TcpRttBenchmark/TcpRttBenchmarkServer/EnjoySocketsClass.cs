using EnjoySockets;

namespace TcpRttBenchmarkServer
{
    public static class PoolIds
    {
        [EAttrPool]//register pool id
        public const ushort Basic = 1;
    }

    public class ReceiveTestES
    {
        [EAttr(PoolId = PoolIds.Basic)]
        public long ReceivePayloadTest(EUserServer user, List<long> payload)
        {
            return 0;
        }
    }

    public class EnjoySocketsClass
    {
        public void CreateServer()
        {
            Console.Clear();
            Console.WriteLine("Server EnjoySockets starting ...");

            var serv = new ETCPServer(new ERSA(GlobalConfig.PrivatePemKey, GlobalConfig.PrivatePemKeyToSign));
            if (serv.Start(EAddress.Get(GlobalConfig.IP, GlobalConfig.Port)))
                Console.WriteLine("Server started!");
            else
                Console.WriteLine("Cannot start server, check configuration");
        }
    }
}
