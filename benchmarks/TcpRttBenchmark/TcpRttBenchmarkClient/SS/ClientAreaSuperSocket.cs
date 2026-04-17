using EnjoySockets;
using SuperSocket.Client;
using SuperSocket.ProtoBase;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace TcpRttBenchmarkClient.SS
{
    public class MyClientPackage
    {
        public ReadOnlySequence<byte> Data { get; set; }
    }

    public class MyFixedSizePipelineFilter : FixedSizePipelineFilter<MyClientPackage>
    {
        private readonly MyClientPackage _package = new();

        public MyFixedSizePipelineFilter() : base(1) { }

        protected override MyClientPackage DecodePackage(ref ReadOnlySequence<byte> buffer)
        {
            _package.Data = buffer;

            buffer = buffer.Slice(buffer.End);

            return _package;
        }
    }

    public class EasyClientExtended<TReceivePackage> : EasyClient<TReceivePackage>
    where TReceivePackage : class
    {

        public EasyClientExtended(IPipelineFilter<TReceivePackage> pipelineFilter) : base(pipelineFilter)
        {
        }

        protected override IConnector GetConnector()
        {
            List<IConnector> list = new List<IConnector>();
            IConnector proxy = Proxy;
            if (proxy != null)
            {
                list.Add(proxy);
            }
            else
            {
                list.Add(new NoDelaySocketConnector(LocalEndPoint));
            }

            SecurityOptions security = Security;
            if (security != null && security.EnabledSslProtocols != 0)
            {
                list.Add(new NoDelaySslStreamConnector(security));
            }

            //if (CompressionLevel != CompressionLevel.NoCompression)
            //{
            //    list.Add(new GZipConnector(CompressionLevel));
            //}

            return BuildConnectors(list);
        }
    }

    public class NoDelaySslStreamConnector : SslStreamConnector
    {
        public NoDelaySslStreamConnector(SslClientAuthenticationOptions options) : base(options) { }

        protected override async ValueTask<ConnectState> ConnectAsync(EndPoint remoteEndPoint, ConnectState state, CancellationToken cancellationToken)
        {
            string text = Options.TargetHost;
            if (string.IsNullOrEmpty(text))
            {
                if (remoteEndPoint is DnsEndPoint dnsEndPoint)
                {
                    text = dnsEndPoint.Host;
                }
                else if (remoteEndPoint is IPEndPoint iPEndPoint)
                {
                    text = iPEndPoint.Address.ToString();
                }

                Options.TargetHost = text;
            }

            Socket socket = state.Socket;
            if (socket == null)
            {
                throw new Exception("Socket from previous connector is null.");
            }
            socket.NoDelay = true;
            try
            {
                SslStream stream = new SslStream(new NetworkStream(socket, ownsSocket: true), leaveInnerStreamOpen: false);
                await stream.AuthenticateAsClientAsync(Options, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return ConnectState.CancelledState;
                }

                state.Stream = stream;
                return state;
            }
            catch (Exception exception)
            {
                return new ConnectState
                {
                    Result = false,
                    Exception = exception
                };
            }
        }
    }

    public class NoDelaySocketConnector : SocketConnector
    {
        public NoDelaySocketConnector(IPEndPoint localEndPoint) : base(localEndPoint)
        {
        }

        protected override async ValueTask<ConnectState> ConnectAsync(EndPoint remoteEndPoint, ConnectState state, CancellationToken cancellationToken)
        {
            AddressFamily addressFamily = remoteEndPoint.AddressFamily;
            if (addressFamily == AddressFamily.Unspecified)
            {
                addressFamily = AddressFamily.InterNetwork;
            }

            Socket socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                IPEndPoint localEndPoint = LocalEndPoint;
                if (localEndPoint != null)
                {
                    socket.ExclusiveAddressUse = false;
                    socket.NoDelay = true;
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.IpTimeToLive, 1);
                    socket.Bind(localEndPoint);
                }

                await socket.ConnectAsync(remoteEndPoint, cancellationToken);
            }
            catch (Exception exception)
            {
                return new ConnectState
                {
                    Result = false,
                    Exception = exception
                };
            }

            return new ConnectState
            {
                Result = true,
                Socket = socket
            };
        }
    }

    public class ClientAreaSuperSocket : IClientArea
    {
        IEasyClient<MyClientPackage> _client;
        EArrayBufferWriter _arrayBufferWriter = new EArrayBufferWriter(GlobalConfig.PayloadInBytes);

        public long[] RTTSamples { get; set; } = new long[GlobalConfig.TrueTest];
        public double Seconds { get; set; }
        public bool Failure { get; set; }

        public ClientAreaSuperSocket()
        {
            var filter = new MyFixedSizePipelineFilter();
            var easyClient = new EasyClientExtended<MyClientPackage>(filter);
            easyClient.Security = new SecurityOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                AllowTlsResume = true,
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
            };
            _client = easyClient.AsClient();
            _client.PackageHandler += _client_PackageHandler;
        }

        public async Task<bool> Connect()
        {
            return await _client.ConnectAsync(new IPEndPoint(IPAddress.Parse(GlobalConfig.IP), GlobalConfig.Port));
        }

        CancellationTokenSource? _cts;
        public async Task Run()
        {
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _client.StartReceive();

            //start
            await Send();

            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(1000, token);
                }
            }
            catch { }
        }

        async ValueTask Send()
        {
            ESerial.Serialize(_arrayBufferWriter, GlobalConfig.Payload);
            await _client.SendAsync(_arrayBufferWriter.WrittenMemory);
        }

        bool _trueTest;
        int _id;
        Stopwatch? _timer;
        Stopwatch? _sw;
        private ValueTask _client_PackageHandler(EasyClient<MyClientPackage> sender, MyClientPackage package)
        {
            if (!_trueTest && _id >= GlobalConfig.Warmup)
            {
                _trueTest = true;
                _id = 0;
                _timer = Stopwatch.StartNew();
            }
            else if (!_trueTest)
            {
                _ = Send();
                _id++;
                return ValueTask.CompletedTask;
            }

            if (_trueTest && _id >= GlobalConfig.TrueTest)
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    Seconds = _timer.Elapsed.TotalSeconds;
                }
                _cts?.Cancel();
            }
            else
            {
                if (_sw != null)
                {
                    _sw.Stop();
                    RTTSamples[_id] = _sw.ElapsedTicks;
                    _id++;
                }
                _sw = Stopwatch.StartNew();
                _ = Send();
            }
            return ValueTask.CompletedTask;
        }
    }
}
