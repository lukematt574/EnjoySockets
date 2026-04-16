// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Threading.Channels;

namespace EnjoySockets
{
    internal class EReceiveChannel
    {
        readonly Channel<EReceiveData> _channel;

        internal EReceiveChannel(bool chPrivate = true, ushort tasks = 1)
        {
            _channel = Channel.CreateUnbounded<EReceiveData>(new UnboundedChannelOptions { SingleReader = tasks < 2, SingleWriter = chPrivate });
            for (int i = 0; i < tasks; i++)
                _ = StartChannelReceive();
        }

        async Task StartChannelReceive()
        {
            while (await _channel.Reader.WaitToReadAsync())
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    if (item.ESocketResourceObj != null)
                    {
                        var vt = item.Run();
                        if (!vt.IsCompletedSuccessfully)
                            await vt;
                        item.ESocketResourceObj.DisposeReceiveDataFromChannel(item);
                    }
                }
            }
        }

        internal bool Push(EReceiveData data)
        {
            return _channel.Writer.TryWrite(data);
        }
    }
}
