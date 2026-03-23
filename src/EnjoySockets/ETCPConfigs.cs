using System.Security.Cryptography;

namespace EnjoySockets
{
    public class ETCPConfig
    {
        /// <summary>
        /// Message buffer size for send/receive in KB. 
        /// Minimum: 2 KB. Default: 300 KB.
        /// </summary>
        public ushort MessageBuffer { get; set; } = 300;

        /// <summary>
        /// Max packet size for socket.send in bytes. 
        /// Minimum: 1200 bytes. Default: 1300 bytes.
        /// 
        /// IMPORTANT: This value must be identical on both client and server.
        /// </summary>
        public short MaxPacketSize { get; set; } = 1300;

        /// <summary>
        /// Time in milliseconds to wait for a packet acknowledgment (connect).
        /// If no acknowledgment is received within this time, the connection is dropped.
        /// Default: 2000 ms.
        /// Valid range: 100–8000 ms.
        /// </summary>
        public int ResponseTimeout { get; set; } = 2000;

        /// <summary>
        /// Gets or sets the heartbeat interval in seconds.
        /// Values less than 1 or greater than 3600 are reset to the default value.
        /// 
        /// Server default: 30 seconds.
        /// Client default: 0 seconds (disabled).
        /// 
        /// On the client, a value of 0 disables the heartbeat.
        /// </summary>
        public int Heartbeat { get; set; } = 30;

        /// <summary>
        /// ECDH curve used for key agreement. 
        /// Default: nistP384.
        /// </summary>
        public ECCurve Curve { get; set; } = ECCurve.NamedCurves.nistP384;

        /// <summary>
        /// Creates a shallow copy of this configuration.
        /// </summary>
        public virtual ETCPConfig Clone()
        {
            return new();
        }
    }

    public sealed class ETCPClientConfig : ETCPConfig
    {
        /// <summary>
        /// The maximum time, in seconds, to wait for a server response when calling 'Connect'. Defaults is 3 seconds. Valid range is 1–30 seconds.
        /// </summary>
        public int ConnectTimeout { get; set; } = 3;

        public ETCPClientConfig()
        {
            Heartbeat = 0;
        }

        public override ETCPClientConfig Clone()
        {
            return new ETCPClientConfig
            {
                MessageBuffer = MessageBuffer < 2 ? (ushort)2 : MessageBuffer,
                MaxPacketSize = MaxPacketSize < 1200 ? (short)1300 : MaxPacketSize,
                ResponseTimeout = ResponseTimeout < 100 || ResponseTimeout > 8000 ? 2500 : ResponseTimeout,
                ConnectTimeout = ConnectTimeout < 1 || ConnectTimeout > 30 ? 3 : ConnectTimeout,
                Heartbeat = Heartbeat < 0 || Heartbeat > 3600 ? 0 : Heartbeat,
                Curve = Curve
            };
        }
    }

    public sealed class ETCPServerConfig : ETCPConfig
    {
        /// <summary>
        /// Indicates the number of sockets in queue to accept (Socket.Listen). Default is 128. Valid range is 1–4096.
        /// </summary>
        public int QueueSocketToAccept { get; set; } = 128;
        /// <summary>
        /// Indicates the max number of sockets connect to server. Default is 5000.
        /// </summary>
        public int MaxSockets { get; set; } = 5000;
        /// <summary>
        /// Gets or sets the keep-alive duration in seconds.
        /// <para/>
        /// Valid range is 10–43200 seconds (12 hours).
        /// Values outside this range are automatically reset to the default value.
        /// <para/>
        /// Default value: 60 seconds.
        /// </summary>
        public int KeepAlive { get; set; } = 60;

        public override ETCPServerConfig Clone()
        {
            return new ETCPServerConfig
            {
                QueueSocketToAccept = QueueSocketToAccept < 1 || QueueSocketToAccept > 4096 ? 128 : QueueSocketToAccept,
                MaxSockets = MaxSockets,
                MessageBuffer = MessageBuffer < 2 ? (ushort)2 : MessageBuffer,
                MaxPacketSize = MaxPacketSize < 1200 ? (short)1300 : MaxPacketSize,
                KeepAlive = KeepAlive < 10 || KeepAlive > 43200 ? 60 : KeepAlive,
                ResponseTimeout = ResponseTimeout < 100 || ResponseTimeout > 8000 ? 1500 : ResponseTimeout,
                Heartbeat = Heartbeat < 1 || Heartbeat > 3600 ? 30 : Heartbeat,
                Curve = Curve
            };
        }
    }
}
