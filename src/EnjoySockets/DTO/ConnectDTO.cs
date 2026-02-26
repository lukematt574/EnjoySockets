using MemoryPack;

namespace EnjoySockets.DTO
{
    [MemoryPackable]
    public partial class ConnectDTO
    {
        public Guid UserId { get; set; }
        public List<byte> TokenToReconnect { get; set; } = new(32);
        public List<byte> NewTokenToReconnect { get; set; } = new(32);
        public List<byte> Key { get; set; } = new(158);
    }
}
