// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    internal class ServerBufferQuota
    {
        internal int MaxBufferBytes { get; }
        internal int ReservedBufferBytes { get; private set; }

        readonly object _lock = new();

        internal ServerBufferQuota(int maxBuffer)
        {
            MaxBufferBytes = maxBuffer;
        }

        internal bool TryRent(int bytes)
        {
            if (bytes < 1) return true;
            lock (_lock)
            {
                if (MaxBufferBytes >= ReservedBufferBytes + bytes)
                {
                    ReservedBufferBytes += bytes;
                    return true;
                }
                else
                    return false;
            }
        }

        internal void Return(int rentBytes)
        {
            if (rentBytes < 1) return;

            lock (_lock)
            {
                if (ReservedBufferBytes - rentBytes >= 0)
                    ReservedBufferBytes -= rentBytes;
            }
        }

        internal void Reset()
        {
            lock (_lock)
            {
                ReservedBufferBytes = 0;
            }
        }
    }
}
