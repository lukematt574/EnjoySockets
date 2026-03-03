// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using EnjoySockets.DTO;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace EnjoySockets
{
    public class ESocketResourceClient : ESocketResource
    {
        internal byte[] NewTokenToReconnect = new byte[32];

        internal ETCPClientConfig ConfigClient { get; private set; }
        internal ECacheSender MsgCache { get; private set; }

        internal ESocketResourceClient(ETCPClientConfig config, ERSA ersa) : base(ETCPSocketType.Client, config, ersa)
        {
            ConfigClient = config.Clone();
            MsgCache = new();
            if (Heartbeat > 0)
                _ = StartHeartbeat();
        }

        internal bool SetSalt()
        {
            try
            {
                return SetAesGcmKey(NewTokenToReconnect);
            }
            catch { return false; }
        }

        private protected sealed override ERCell? GetReceiveCell(PartDataDTO dto)
        {
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

            return rCell;
        }

        private protected sealed override void RunReceiveMsg(ERCell rCell, PartDataDTO dto)
        {
            if (rCell.AttrMethod.MaxParamSize != 0 && dto.TotalBytes > rCell.AttrMethod.MaxParamSize)
                return;

            var msg = EReceiveMsg.Get(UserObj, this, null, dto, rCell);
            if (msg != null)
            {
                lock (_Lock)
                {
                    if (!ReceiveDataSessions.TryAdd(dto.Session, msg))
                    {
                        msg.Dispose();
                        return;
                    }
                }
            }
            else return;

            RunReceiveMsg(msg, dto);
        }

        private protected override void ClearReceiveDataSessions()
        {
            DisposeReceiveDataSessions();
        }

        internal override sealed void DisposeReceiveDataFromChannel(EReceiveData? eData)
        {
            base.DisposeReceiveDataFromChannel(eData);
        }

        private protected sealed override void RunReceiveMsg(EReceiveData eData, PartDataDTO dto)
        {
            var result = TryPushReceiveDTO(eData, dto);
            if (result > 1)//over 1 - error
            {
                lock (_Lock)
                {
                    ReceiveDataSessions.Remove(eData.Session, out _);
                }
                eData.Dispose();
            }
        }

        private protected sealed override bool ReceiveSpecial(PartDataDTO dto)
        {
            switch (dto.Target)
            {
                case 0: break;
                case 1: MsgCache.SetEndMsg(dto.Session, GetMsgFromDTO(dto)); break;//method executed. The result is stored in msg. Possible error: -1 = method execution failed.
                case 2:
                    var msg = GetMsgFromDTO(dto);
                    MsgCache.SetEndMsg(dto.Session, msg == 2 ? -2 : -5);
                    break;
                case 3: MsgCache.SetEndMsg(dto.Session, -3); break;//response could not be retrieved (session expired)
                case 4: MsgCache.SetEndMsg(dto.Session, -2); break;//buffer full
                case 5: MsgCache.SetEndMsg(dto.Session, -4); break;//access denied
                case 6: MsgCache.SetEndMsg(dto.Session, -5); break;//invalid payload
                case 7: SetBrokeMsgToSend(dto); break;//resend remaining bytes starting from the position specified in msg (if use 'SendWithResponse' method)
            }
            return true;
        }

        long? GetMsgFromDTO(PartDataDTO dto)
        {
            if (dto.Data.Count == 8)
                return BinaryPrimitives.ReadInt64LittleEndian(CollectionsMarshal.AsSpan(dto.Data));

            return null;
        }

        void SetBrokeMsgToSend(PartDataDTO dto)
        {
            var offset = GetMsgFromDTO(dto);
            var sender = MsgCache.SetBrokeMsgToSend(dto.Session, offset);
            if (sender != null)
            {
                var obj = ESendMsg.Rent();
                if (sender.Msg == null)
                    obj.RunPrepare(RunObjMsgSend, sender.Target, sender.MsgBytes, sender.Instance);
                else
                    obj.RunPrepare(RunObjMsgSend, sender.Target, sender.Msg, sender.Instance);
                obj.SetToWriteAndSession((long)offset!, sender.Session);
                _ = ChannelSend.TrySendMsgAndGetSession(obj);
            }
            else
                MsgCache.SetEndMsg(dto.Session, -3);
        }

        internal async ValueTask<bool> RebuildSessions()
        {
            var maxSession = GetSession();
            var list = MsgCache.GetNonReceivedSessions();
            bool sendMax = true;
            foreach (var item in list)
            {
                if (!await SendSpecial(item, 1, null))
                {
                    sendMax = false;
                    break;
                }
            }
            if (sendMax)
            {
                return await SendSpecial(maxSession, 2, null);
            }
            else
                return false;
        }

        internal override sealed void Dispose()
        {
            base.Dispose();
            MsgCache.Clear();
        }

        private protected sealed override void DisposeReceiveDataSessions()
        {
            base.DisposeReceiveDataSessions();
        }
    }
}
