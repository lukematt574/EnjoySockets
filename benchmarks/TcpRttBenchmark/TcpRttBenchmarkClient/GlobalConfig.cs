using EnjoySockets;
using System.Diagnostics;

namespace TcpRttBenchmarkClient
{
    public static class GlobalConfig
    {
        public static string IP { get; set; } = "192.168.1.1";
        public static int Port { get; set; } = 8001;

        public const int MaxClients = 100;
        public const int Warmup = 10000;
        public const int TrueTest = 25000;

        public static string PublicPemKey = "-----BEGIN PUBLIC KEY-----MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA0KtdY+YWfC8cZl6MkYjgJlsoXv961lientvI9kHNwfUjwVgzQDMRUYkphGXUU8qLlnIaloZvZ/Mzd8F6WEXIvc6Nl34U/iecFI3NHh+OyM+C4d898w7IdN8HrO7yjsxELySt6XRgtKFbr2SAzJo9Ub3L2DVGpU+oxTNTeppI+sng6kESkeHMsHiunEczsCZkFAqhRzaN9QQQCfwJWLmXCJntU6Cv2IzYVBntgOr2+ieoZSpD1awcvi6zPJ/XVyYOmxhjT7thnpsabUtQbJ4woq2h+HQv+bIGvrQrjF5zVAE9eYka5aQcspZJgCpJ8SBv0EPLlw+z09azrCo64Pa9qyzg9hdx6IhkS/M3IacIpRI+NNibH77o4zvdrvLa/I71zKwWFlfCGGWz4LvpNbK4UQXxnDT8Iw5KvSLi0Emdi6n5C5qDntGSZFL7ALar0h105B2fgaSDRA5eAfoUvyWs0Wk2XNhcB1eoqOCAXfkVsSWlSleMNCyCFZKkmifbgjlOi29uxPs+Z9/Ed1V1gJsGO2e6ZjNL/TcoD9Vu6ivE/SbsnOt2TbpHepJTjoKd5VaOAAm/wu/yYvYAVfoV+GwkIOM6e9t7+zR4IUTjBWYEUiAZUPFOFgq1Gqi3b5pIaMyA0qGElLbtnWt09bBSys/CQ+X7uccPt11Ewg7r4voIiVUCAwEAAQ==-----END PUBLIC KEY-----";
        public static string PublicPemKeyToSign = "-----BEGIN PUBLIC KEY-----MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA07hMWC7U9llKwfmdBYtk5wUtBYcqJIB7ufpJZsIryYWX2p2OV6oEQh7jjCcSYISgf7/u6BGNKjYGUm2vqkp/lJC04ZOIpw0zi7d/cRXNUknJ7A34pDutG1EmapWhDeoG9VmhZFT2w1dumM89yf49akjUSJCP+uC+nvhxIfMEPNJkQ9c595aWDHbjfbNwKvE9ZAmz+nen79pgzNc6NpoLKUFbOxZsLBrAgCtDQoLIW0PtC/rmucdj3RI7VFz9cd1CWFWV47pr/2XrqLUE7ncSIwfrG3906xOTxjZgGUDbHvU5FwU5p35LxoFs3rfby9UWuOA7vAq18bco9/+EnI1Cbgkhn0PwMCISmhPgQG0gbgGFTtdnKqST18h7cwgcxkjcoLms4TE9tNaFF8IkmRyG38f69chmiVzYO8fDw/EQ7PfWsV78wE0puonNj2iQRhhkECxzJvm4erQjqlbpBtOtlAZXL3GBgZHDG0eyJq3L7fU0tMfoAQrWtKGWHQI4wyWUEyQQYAJjD3IYTt/r8K3za/a2bpMyMKcL2eWtDvxceEV1Tuo2pNoD8kmYfs5aS1xJWwtxapVR43TnnRzoH7cv8lYNGzlzjgU+88E7VPknti0yqrQZ1VJXAV7/LfUI2VMmfVyp4wkQi5/ujiIsK7haS6QZCg48o5HDkC7vvz2LZaUCAwEAAQ==-----END PUBLIC KEY-----";

        public static List<long> Payload { get; private set; } = [];
        public static int PayloadInBytes { get; private set; }
        static GlobalConfig()
        {
            //Init payload
            for (int i = 0; i < 128; i++)
                Payload.Add(i);

            //Serialized payload bytes
            PayloadInBytes = ESerial.Serialize(Payload)?.Length ?? 0;
        }

        public static void WriteResult(List<IClientArea> clients, string libraryName)
        {
            var totalTime = clients.Max(c => c.Seconds);
            var totalMsgs = TrueTest * MaxClients;
            var rps = totalMsgs / totalTime;
            var throughput = rps * (PayloadInBytes / (1024.0 * 1024.0));

            double tickToMs = 1000.0 / Stopwatch.Frequency;

            var allSamples = clients.SelectMany(c => c.RTTSamples)
                .Select(t => t * tickToMs)
                .ToArray();

            if (totalTime == 0)
            {
                Console.WriteLine("Test failure!");
                return;
            }

            Array.Sort(allSamples);

            double p50 = allSamples[allSamples.Length / 2];
            double p95 = allSamples[(int)(allSamples.Length * 0.95)];
            double p99 = allSamples[(int)(allSamples.Length * 0.99)];
            double p999 = allSamples[(int)(allSamples.Length * 0.999)];

            double min = allSamples[0];
            double max = allSamples[^1];

            Console.WriteLine($"=== GLOBAL STATS ({MaxClients} clients) - {libraryName} ===");
            Console.WriteLine($"Total time: {totalTime:F2} s");
            Console.WriteLine($"RTT: {rps:F0} msg/s");
            Console.WriteLine($"Throughput: {throughput:F2} MB/s");

            Console.WriteLine($"\nLatency (ms):");
            Console.WriteLine($"p50: {p50:F3}");
            Console.WriteLine($"p95: {p95:F3}");
            Console.WriteLine($"p99: {p99:F3}");
            Console.WriteLine($"p999: {p999:F3}");
            Console.WriteLine($"min: {min:F3}");
            Console.WriteLine($"max: {max:F3}");
        }
    }
}
