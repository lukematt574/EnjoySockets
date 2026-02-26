using System.Net;

namespace EnjoySockets
{
    public class EAddress
    {
        static string LocalIP { get; set; } = "127.0.0.1";

        public IPEndPoint? EndPoint { get; }
        private EAddress() { }
        private EAddress(IPEndPoint? endPoint)
        {
            EndPoint = endPoint;
        }

        /// <summary>
        /// Get default address - 127.0.0.1:3001
        /// </summary>
        public static EAddress Get()
        {
            return new EAddress(new IPEndPoint(IPAddress.Parse(LocalIP), 3001));
        }

        /// <summary>
        /// Get default address - 127.0.0.1:port
        /// </summary>
        public static EAddress Get(int port)
        {
            return Get(LocalIP, port);
        }

        public static EAddress Get(string ipAddress, int port)
        {
            try
            {
                return new EAddress(new IPEndPoint(IPAddress.Parse(ipAddress), port));
            }
            catch
            {
                return new EAddress();
            }
        }

        public static EAddress Get(IPAddress ipAddress, int port)
        {
            try
            {
                return new EAddress(new IPEndPoint(ipAddress, port));
            }
            catch
            {
                return new EAddress();
            }
        }

        public static EAddress Get(IPEndPoint? endPoint)
        {
            return new EAddress(endPoint);
        }
    }
}
