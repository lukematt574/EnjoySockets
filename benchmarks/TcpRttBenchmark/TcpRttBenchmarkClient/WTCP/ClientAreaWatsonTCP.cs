using EnjoySockets;
using System.Diagnostics;
using WatsonTcp;

namespace TcpRttBenchmarkClient.WTCP
{
    public class ClientAreaWatsonTCP : IClientArea
    {
        WatsonTcpClient _client;
        public long[] RTTSamples { get; set; } = new long[GlobalConfig.TrueTest];
        public double Seconds { get; set; }
        public bool Failure { get; set; }

        EArrayBufferWriter _arrayBufferWriter = new EArrayBufferWriter(GlobalConfig.PayloadInBytes);
        byte[] _msgToSend;

        public ClientAreaWatsonTCP()
        {
            _client = new WatsonTcpClient(GlobalConfig.IP, GlobalConfig.Port, "cert.pfx", "password");
            _client.Settings.NoDelay = true;
            _client.Settings.AcceptInvalidCertificates = true;
            _client.Settings.MutuallyAuthenticate = true;
            _client.Events.ServerConnected += ServerConnected;
            _client.Events.ServerDisconnected += ServerDisconnected;
            _client.Events.MessageReceived += MessageReceived;

            _msgToSend = new byte[GlobalConfig.PayloadInBytes];
        }

        public void Connect()
        {
            _client.Connect();
        }

        CancellationTokenSource? _cts;
        public async Task Run()
        {
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

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
            _arrayBufferWriter.WrittenMemory.CopyTo(_msgToSend);//to avoid allocation, I have to copy to an array of a specific length
            await _client.SendAsync(_msgToSend);//no allow readonlymemory<byte> as arg 
        }

        bool _trueTest;
        int _id;
        Stopwatch? _timer;
        Stopwatch? _sw;
        void MessageReceived(object sender, MessageReceivedEventArgs args)
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
                return;
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
        }

        void ServerConnected(object sender, ConnectionEventArgs args)
        {
            Failure = false;
        }

        void ServerDisconnected(object sender, DisconnectionEventArgs args)
        {
            Failure = true;
        }
    }
}
