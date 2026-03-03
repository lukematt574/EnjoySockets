// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using EnjoySockets.DTO;

namespace EnjoySockets
{
    public class ESocketResourceServer : ESocketResource
    {
        private protected EBufferControl BufferToReceiveMsg;
        internal ReadOnlyMemory<byte> PublicKeySign { get; private set; }

        internal ETCPServerConfig ConfigServer { get; private set; }

        internal int KeepAlive { get; private set; }
        internal bool FirstConnect { get; set; } = true;

        EResponseCache ResponseCache { get; set; } = new(ETCPSocket.MaxCachedResponses);

        internal Func<long, bool>? CheckAccessEvent { get; set; }

        internal ESocketResourceServer(ETCPServerConfig config, ERSA ersa) : base(ETCPSocketType.Server, config, ersa)
        {
            var sig = new byte[512];
            int countSig = Ersa.SignDataRsa(PublicKey, sig).Result;
            PublicKeySign = sig.AsMemory(0, countSig);
            ConfigServer = config.Clone();
            KeepAlive = ConfigServer.KeepAlive * 1000;
            BufferToReceiveMsg = new(MessageBuffer);
            _ = StartHeartbeat();
        }

        internal bool TryReconnectToken(ReadOnlySpan<byte> token, ReadOnlySpan<byte> salt)
        {
            if (salt.Length != 32)
                return false;
            if (TokenToReconnect.AsSpan().SequenceEqual(token))
            {
                try
                {
                    return SetAesGcmKey(salt);
                }
                catch { return false; }
            }
            return false;
        }

        private protected sealed override ERCell? GetReceiveCell(PartDataDTO dto)
        {
            if (dto.TotalBytes > MessageBuffer)
            {
                _ = SendSpecialWithCache(dto.Session, 4, null);
                return null;
            }

            ERCell? rCell = null;
            if (dto.Instance > 0)
            {
                lock (_Lock)
                {
                    if (_privateInstances.TryGetValue(dto.Instance, out (object, Dictionary<ulong, ERCell>) val))
                        rCell = EReceiveCells.GetCellToInstanceId(val.Item1, dto.Target);
                }
            }
            else
                rCell = EReceiveCells.GetCellToBasic(dto.Target);

            if (rCell == null)
            {
                _ = SendSpecialWithCache(dto.Session, 6, null);
                return null;
            }

            return rCell;
        }

        /// <summary>
        /// Removes and disposes all receive data sessions 
        /// with a session ID less than or equal to the specified value.
        /// </summary>
        /// <param name="session">
        /// Maximum session ID (inclusive). All sessions with ID less or equal this value will be removed.
        /// </param>
        private protected void ClearReceiveDataSessions(ulong session)
        {
            List<EReceiveMsg> sessionsToRemove;
            lock (_Lock)
            {
                sessionsToRemove = ReceiveDataSessions
                    .Where(x => x.Value.Session <= session && !x.Value.InChannel)
                    .Select(x => x.Value)
                    .ToList();

                foreach (var item in sessionsToRemove)
                    ReceiveDataSessions.Remove(item.Session);
            }
            foreach (var item in sessionsToRemove)
                item.Dispose();
        }

        private protected override void ClearReceiveDataSessions() { }

        private protected sealed override void RunReceiveMsg(ERCell rCell, PartDataDTO dto)
        {
            if (rCell.AttrMethod.MaxParamSize != 0 && dto.TotalBytes > rCell.AttrMethod.MaxParamSize)
            {
                _ = SendSpecialWithCache(dto.Session, 4, null);
                return;
            }

            var access = rCell.AttrMethod.Access;
            if (access != 0)
            {
                if (!(CheckAccessEvent?.Invoke(access) ?? false))
                {
                    _ = SendSpecialWithCache(dto.Session, 5, null);
                    return;
                }
            }

            var msg = EReceiveMsg.Get(UserObj, this, BufferToReceiveMsg, dto, rCell);
            if (msg != null)
            {
                bool added = false;
                lock (_Lock)
                {
                    if (ReceiveDataSessions.TryAdd(dto.Session, msg))
                        added = true;
                }
                if (!added)
                {
                    _ = SendSpecialWithCache(msg.Session, 6, null);
                    msg.Dispose();
                }
                else
                    RunReceiveMsg(msg, dto);
            }
            else
            {
                _ = SendSpecialWithCache(dto.Session, 6, null);
            }
        }

        private protected sealed override void RunReceiveMsg(EReceiveData eData, PartDataDTO dto)
        {
            var result = TryPushReceiveDTO(eData, dto);
            if (result > 1)//over 1 - error
            {
                eData.Response = result;
                RunDisposeReceiveData(eData, 2);
            }
        }

        /// <summary>
        /// Handles internal control messages received from the peer.
        /// </summary>
        /// <remarks>
        /// Target values:
        /// 0 = heartbeat,
        /// 1 = request session status,
        /// 2 = clear related session data.
        /// </remarks>
        private protected sealed override bool ReceiveSpecial(PartDataDTO dto)
        {
            switch (dto.Target)
            {
                case 0: break;
                case 1: ResponseStatusSession(dto.Session); break;
                case 2: ClearReceiveDataSessions(dto.Session); break;
            }
            return true;
        }

        void ResponseStatusSession(ulong session)
        {
            ulong typeMsg = 0;
            long? msg = null;
            lock (_Lock)
            {
                if (ResponseCache.TryGet(session, out var result))
                {
                    typeMsg = result.Item1;
                    msg = result.Item2;
                }
                else
                {
                    if (ReceiveDataSessions.TryGetValue(session, out EReceiveMsg? rMsg))
                    {
                        if (!rMsg.InChannel)
                        {
                            typeMsg = 7;
                            msg = rMsg.WroteBytes;
                        }
                    }
                    else
                    {
                        if (!FirstConnect)
                        {
                            typeMsg = 7;
                            msg = 0;
                        }
                        else
                            typeMsg = 3;
                    }
                }
            }
            _ = SendSpecial(session, typeMsg, msg);
        }

        internal override sealed void DisposeReceiveDataFromChannel(EReceiveData? eData)
        {
            if (eData != null)
                RunDisposeReceiveData(eData, 1);
        }

        void RunDisposeReceiveData(EReceiveData eData, ulong typeMsg)
        {
            lock (_Lock)
            {
                ReceiveDataSessions.Remove(eData.Session, out _);
                ResponseCache.Add(eData.Session, typeMsg, eData.Response);
            }
            _ = SendSpecial(eData.Session, typeMsg, eData.Response);
            eData.Dispose();
        }

        internal ValueTask<bool> SendSpecialWithCache(ulong session, ulong typeMsg, long? msg)
        {
            lock (_Lock)
                ResponseCache.Add(session, typeMsg, msg);
            return SendSpecial(session, typeMsg, msg);
        }

        int _maxSendSpecialCount;
        int _limitSendSpecial = ETCPSocket.MaxCachedResponses * 2;
        internal override sealed ValueTask<bool> SendSpecial(ulong session, ulong typeMsg, long? msg)
        {
            if (BasicSocket == null)
                return ValueTask.FromResult(false);

            if (Interlocked.Increment(ref _maxSendSpecialCount) > _limitSendSpecial)
            {
                RunOnPotentialSabotageEvent?.Invoke(2);
                RunDisposeEvent?.Invoke();
                Interlocked.Decrement(ref _maxSendSpecialCount);
                return ValueTask.FromResult(false);
            }

            var vt = ChannelSend.TrySendSpecial(RunSpecialSend, session, typeMsg, msg);
            if (vt.IsCompletedSuccessfully)
            {
                Interlocked.Decrement(ref _maxSendSpecialCount);
                return vt;
            }
            return AwaitedSendSpecial(vt);
        }

        async ValueTask<bool> AwaitedSendSpecial(ValueTask<bool> pending)
        {
            try
            {
                return await pending;
            }
            finally
            {
                Interlocked.Decrement(ref _maxSendSpecialCount);
            }
        }

        internal override sealed void Dispose()
        {
            base.Dispose();
            BufferToReceiveMsg.Reset();
            FirstConnect = true;
        }

        private protected sealed override void DisposeReceiveDataSessions()
        {
            var listToRemove = new List<EReceiveMsg>();
            lock (_Lock)
            {
                foreach (var item in ReceiveDataSessions.Values)
                {
                    if (!item.InChannel)
                        listToRemove.Add(item);
                }

                foreach (var item in listToRemove)
                    ReceiveDataSessions.Remove(item.Session);
            }

            foreach (var item in listToRemove)
                item.Dispose();
        }

        internal void ClearResponseCache()
        {
            _maxSendSpecialCount = 0;
            lock (_Lock)
                ResponseCache.Clear();
        }
    }
}
