using MemoryPack;

namespace EnjoySockets.DTO
{
    [MemoryPackable]
    public partial class ConnectResponseDTO
    {
        /// <summary>
        /// 3 - server full, 1 or 2 - need auth, 4 or 5 - without auth
        /// </summary>
        public byte Control { get; set; }
        public ReadOnlyMemory<byte> PublicKey { get; set; }
        public ReadOnlyMemory<byte> Sign { get; set; }
    }
}
