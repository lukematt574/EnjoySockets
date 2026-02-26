using MemoryPack;

namespace EnjoySockets.DTO
{
    [MemoryPackable]
    public partial class PartDataDTO
    {
        public EDataForm DForm { get; set; }
        public int TotalBytes { get; set; }
        public ulong Session { get; set; }
        public long Instance { get; set; }
        public ulong Target { get; set; }
        public List<byte> Data { get; set; } = new(ETCPSocket.MaxPayloadBytes + 50);
    }
}
