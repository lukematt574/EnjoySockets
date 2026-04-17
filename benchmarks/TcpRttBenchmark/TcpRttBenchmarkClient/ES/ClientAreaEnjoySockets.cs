using EnjoySockets;
using System.Diagnostics;

namespace TcpRttBenchmarkClient.ES
{
    public class ClientAreaEnjoySockets : IClientArea
    {
        EUserClient _client;
        public long[] RTTSamples { get; set; } = new long[GlobalConfig.TrueTest];
        public double Seconds { get; set; }
        public bool Failure { get; set; }

        public ClientAreaEnjoySockets()
        {
            _client = new EUserClient(new ERSA(GlobalConfig.PublicPemKey, GlobalConfig.PublicPemKeyToSign));
        }

        public async Task<byte> Connect()
        {
            return await _client.Connect(EAddress.Get(GlobalConfig.IP, GlobalConfig.Port));
        }

        public async Task Run()
        {
            //warmup
            for (int i = 0; i < GlobalConfig.Warmup; i++)
                await _client.SendWithResponse("ReceivePayloadTest", GlobalConfig.Payload);

            var timer = Stopwatch.StartNew();

            //true test
            for (int i = 0; i < GlobalConfig.TrueTest; i++)
            {
                var sw = Stopwatch.StartNew();

                var result = await _client.SendWithResponse("ReceivePayloadTest", GlobalConfig.Payload);
                if (result != 0)
                {
                    Failure = true;
                    sw.Stop();
                    timer.Stop();
                    return;
                }

                sw.Stop();
                RTTSamples[i] = sw.ElapsedTicks;
            }

            timer.Stop();

            Seconds = timer.Elapsed.TotalSeconds;
        }
    }
}
