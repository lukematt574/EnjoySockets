// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Threading.Channels;

namespace EnjoySockets
{
    internal class ReceiveDispatcher
    {
        readonly Channel<DataReceiveOperation> _channel;

        internal ReceiveDispatcher(bool chPrivate = true, ushort tasks = 1)
        {
            _channel = Channel.CreateUnbounded<DataReceiveOperation>(new UnboundedChannelOptions { SingleReader = tasks < 2, SingleWriter = chPrivate });
            for (int i = 0; i < tasks; i++)
                _ = StartWorker();
        }

        async Task StartWorker()
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

        internal bool Dispatch(DataReceiveOperation data)
        {
            return _channel.Writer.TryWrite(data);
        }
    }
}
