namespace TcpRttBenchmarkClient
{
    public interface IClientArea
    {
        public long[] RTTSamples { get; set; }
        public double Seconds { get; set; }
        public bool Failure { get; set; }
    }
}
