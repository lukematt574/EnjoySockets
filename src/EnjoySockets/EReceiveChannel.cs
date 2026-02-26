using System.Threading.Channels;

namespace EnjoySockets
{
    internal class EReceiveChannel
    {
        readonly Channel<EReceiveData> _channel;
        readonly bool _chPrivate;

        internal EReceiveChannel(bool chPrivate = true, ushort tasks = 1)
        {
            _chPrivate = chPrivate;
            _channel = Channel.CreateUnbounded<EReceiveData>(new UnboundedChannelOptions { SingleReader = tasks < 2, SingleWriter = _chPrivate });
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

        internal void Push(EReceiveData data)
        {
            if (_chPrivate)
                _channel.Writer.TryWrite(data);
            else
                _ = _channel.Writer.WriteAsync(data);
        }
    }
}
