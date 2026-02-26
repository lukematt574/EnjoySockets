namespace EnjoySockets
{
    public class EBufferControl
    {
        public int MaxBuffer { get; private set; }
        public int CurrentEngagedBuffer { get; private set; }

        readonly object _lock = new();

        internal EBufferControl(int maxBuffer)
        {
            MaxBuffer = maxBuffer;
        }

        /// <summary>
        /// Check if buffer available
        /// </summary>
        /// <param name="needBuffer">in bytes</param>
        /// <returns>if rent correctly - true</returns>
        public bool TryRent(int needBuffer)
        {
            if (needBuffer < 1) return true;
            lock (_lock)
            {
                if (MaxBuffer >= CurrentEngagedBuffer + needBuffer)
                {
                    CurrentEngagedBuffer += needBuffer;
                    return true;
                }
                else
                    return false;
            }
        }

        public void Return(int rentBuffer)
        {
            if (rentBuffer < 1) return;

            lock (_lock)
            {
                if (CurrentEngagedBuffer - rentBuffer >= 0)
                    CurrentEngagedBuffer -= rentBuffer;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                CurrentEngagedBuffer = 0;
            }
        }
    }
}
