// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using EnjoySockets.DTO;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace EnjoySockets
{
    public abstract class ESocketResource
    {
        internal Socket? BasicSocket { get; set; }
        internal bool Running { get; private set; }
        private protected object _Lock = new();

        public ETCPSocketType SocketType { get; private set; }
        /// <summary>
        /// Buffer in bytes
        /// </summary>
        public int MessageBuffer { get; private set; }
        internal object? UserObj { get; set; }

        internal byte[] TokenToReconnect = new byte[32];
        private protected PartDataDTO PreparedSendObj = new() { Data = new(ETCPSocket.MaxPayloadBytes) };
        private protected EArrayBufferWriter ArrayBufferWriterSend = new(ETCPSocket.MaxPacketSizeBytes);
        private protected ESerializeMsg ESerializeMsgObj;

        private protected EAesGcm AESgcm { get; private set; }

        private protected ulong LastSessionReceive;
        private protected PartDataDTO? ReceivePartObj = new() { Data = new(ETCPSocket.MaxPayloadBytes + 50) };
        private protected Dictionary<ulong, EReceiveMsg> ReceiveDataSessions = [];

        internal ESendChannel ChannelSend { get; private set; }
        private protected EReceiveChannel ChannelReceiveBasic { get; private set; }
        readonly Dictionary<ushort, EReceiveChannel> _privateChannels = [];

        internal ESendArgs SendArgs { get; private set; } = new();
        internal EReceiveArgs ReceiveArgs { get; private set; } = new();

        private protected ECDiffieHellman ECDH { get; private set; }
        internal ReadOnlyMemory<byte> PublicKey { get; private set; }
        internal ERSA Ersa { get; private set; }

        readonly ECDiffieHellman _outPublicKey;
        readonly byte[] AESKey = new byte[32];

        internal ETCPConfig Config { get; private set; }
        internal int Heartbeat { get; private set; }

        private protected Dictionary<long, (object, Dictionary<ulong, ERCell>)> _privateInstances = [];
        private protected Dictionary<Type, object> _localInstances = [];

        internal Action<int>? RunOnPotentialSabotageEvent { get; set; }
        internal Action? RunDisposeEvent { get; set; }

        internal ESocketResource(ETCPSocketType socketType, ETCPConfig config, ERSA rsa)
        {
            AESgcm = new EAesGcm(socketType);
            ESerializeMsgObj = new ESerializeMsg(config);
            Config = config.Clone();
            MessageBuffer = Config.MessageBuffer * 1024;
            Heartbeat = Config.Heartbeat * 1000;
            SocketType = socketType;
            Ersa = rsa;
            _outPublicKey = ECDiffieHellman.Create(Config.Curve);
            ECDH = ECDiffieHellman.Create(Config.Curve);
            var pk = new byte[158];
            var pkLength = EAesGcm.ExportSpki(ECDH, pk);
            PublicKey = pk.AsMemory(0, pkLength);

            ChannelSend = new(this);
            ChannelReceiveBasic = new();
        }

        internal bool AppendSocket(Socket socket)
        {
            lock (_Lock)
            {
                if (!Running)
                {
                    ETCPSocket.Close(BasicSocket);
                    BasicSocket = socket;
                    return true;
                }
            }
            _ = ChannelSend.TrySendHeartbeat(RunHeartbeatSend);
            return false;
        }

        internal bool SetAesGcmKey(ReadOnlySpan<byte> outPublicKey, ReadOnlySpan<byte> salt)
        {
            if (salt.Length != 32)
                return false;
            try
            {
                _outPublicKey.ImportSubjectPublicKeyInfo(outPublicKey, out _);
                ECDH.DeriveKeyMaterial(_outPublicKey.PublicKey).CopyTo(AESKey, 0);
                return SetAesGcmKey(salt);
            }
            catch
            {
                return false;
            }
        }

        private protected bool SetAesGcmKey(ReadOnlySpan<byte> salt)
        {
            salt.CopyTo(TokenToReconnect);
            return AESgcm.SetKey(AESKey, salt);
        }

        /// <summary>
        /// Get encrypt bytes with timeout (Config.ResponseTimeout)
        /// </summary>
        internal ValueTask<ReadOnlyMemory<byte>> ReceiveEncryptWithTimeout()
        {
            if (Running || BasicSocket == null)
                return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

            return ETCPSocket.ReceiveWithTimeout(ETCPSocket.Receive(BasicSocket, AESgcm, ReceiveArgs), Config.ResponseTimeout);
        }

        /// <summary>
        /// Send plain bytes
        /// </summary>
        internal ValueTask<bool> SendPlainBytes(ReadOnlyMemory<byte> bytes)
        {
            return ETCPSocket.Send(BasicSocket, SendArgs, bytes);
        }

        /// <summary>
        /// Send plain bytes as obj
        /// </summary>
        internal ValueTask<bool> SendPlainBytesObj<T>(T obj)
        {
            if (obj == null) return ValueTask.FromResult(false);
            try
            {
                if (ESerial.Serialize(ArrayBufferWriterSend, obj, obj.GetType()) == 0)
                    return ValueTask.FromResult(false);
                return ETCPSocket.Send(BasicSocket, SendArgs, ArrayBufferWriterSend.WrittenMemory);
            }
            finally
            {
                ArrayBufferWriterSend.ResetWrittenCount();
            }
        }

        /// <summary>
        /// Send encrypt bytes (aesGcm)
        /// </summary>
        internal ValueTask<bool> SendBytes<T>(T obj)
        {
            if (obj == null) return ValueTask.FromResult(false);
            try
            {
                if (ESerial.Serialize(ArrayBufferWriterSend, obj, obj.GetType()) == 0)
                    return ValueTask.FromResult(false);
                return ETCPSocket.Send(BasicSocket, SendArgs, AESgcm, ArrayBufferWriterSend.WrittenMemory);
            }
            finally
            {
                ArrayBufferWriterSend.ResetWrittenCount();
            }
        }

        /// <summary>
        /// Serialize object
        /// </summary>
        /// <returns>You must 'Clear' after use</returns>
        internal EMemorySegment? ObjToSegments<T>(T obj)
        {
            if (obj == null) return null;
            return ESerializeMsgObj!.ObjToSegments(obj, typeof(T));
        }

        internal virtual void DisposeReceiveDataFromChannel(EReceiveData? eData)
        {
            if (eData != null)
            {
                lock (_Lock)
                {
                    ReceiveDataSessions.Remove(eData.Session, out _);
                }
                eData.Dispose();
            }
        }

        internal virtual ValueTask<bool> SendSpecial(ulong session, ulong typeMsg, long? msg)
        {
            if (BasicSocket == null)
                return ValueTask.FromResult(false);

            return ChannelSend.TrySendSpecial(RunSpecialSend, session, typeMsg, msg);
        }

        internal bool Run()
        {
            lock (_Lock)
            {
                if (Running || BasicSocket == null)
                    return false;
                Running = true;
            }

            _ = StartReceive();
            return true;
        }

        async Task StartReceive()
        {
            while (Running)
            {
                if (BasicSocket == null)
                    break;

                //prefix
                var readed = ETCPSocket.TryRead(BasicSocket, ReceiveArgs, ETCPSocket.PacketPrefixLength);
                if (readed < 0)
                    break;

                if (readed != ETCPSocket.PacketPrefixLength)
                {
                    if (!await ETCPSocket.ReadContinueAsync(BasicSocket, ReceiveArgs, readed))
                        break;
                }

                var dataLength = BinaryPrimitives.ReadInt16LittleEndian(ReceiveArgs.GetSaveBytes().Span);
                if (dataLength < 37 || dataLength > ETCPSocket.MaxPacketSizeBytes)
                    break;

                //rest
                if ((readed = ETCPSocket.TryRead(BasicSocket, ReceiveArgs, dataLength)) < 0)
                    break;

                if (readed != dataLength)
                {
                    if (!await ETCPSocket.ReadContinueAsync(BasicSocket, ReceiveArgs, readed))
                        break;
                }

                var readBytes = ReceiveArgs.GetSaveBytes(AESgcm);

                if (readBytes.Length < 37)
                    break;

                if (!PushReceivePart(readBytes))
                    break;
            }

            lock (_Lock)
                Running = false;
            ClearReceiveDataSessions();
            RunDisposeEvent?.Invoke();
        }

        private protected virtual void ClearReceiveDataSessions() { }

        bool PushReceivePart(ReadOnlyMemory<byte> buffer)
        {
            var bufferSpan = buffer.Span;
            if (bufferSpan[0] == 255 //Defensive check against obj null
                || bufferSpan[36] == 255  //Defensive check against obj.list null
                || !ESerial.Deserialize(bufferSpan, ref ReceivePartObj)
                || ReceivePartObj?.Data == null)
            {
                if (ReceivePartObj?.Data == null)
                    ReceivePartObj = new();

                RunOnPotentialSabotageEvent?.Invoke(1);
                return false;
            }

            if (ReceivePartObj.DForm == EDataForm.Special)
                return ReceiveSpecial(ReceivePartObj);

            EReceiveMsg? currentSession;
            lock (_Lock)
                ReceiveDataSessions.TryGetValue(ReceivePartObj.Session, out currentSession);

            if (currentSession == null)
            {
                if (ReceivePartObj.Session > LastSessionReceive)
                    LastSessionReceive = ReceivePartObj.Session;
                else
                    return true;

                var cell = GetReceiveCell(ReceivePartObj);
                if (cell != null)
                    RunReceiveMsg(cell, ReceivePartObj);
            }
            else
                RunReceiveMsg(currentSession, ReceivePartObj);

            return true;
        }

        internal long TryPushReceiveDTO(EReceiveData eData, PartDataDTO dto)
        {
            var result = eData.TryPushPart(dto);

            if (result == 1)
                return 1;

            if (result == 0)
            {
                var attr = eData.Cell?.AttrMethod;
                if (attr != null)
                {
                    if (attr.ChannelId == 0)
                    {
                        return PushToChannelReceive(ChannelReceiveBasic, eData);
                    }
                    else
                    {
                        if (_privateChannels.TryGetValue(attr.ChannelId, out EReceiveChannel? channelPr))
                        {
                            return PushToChannelReceive(channelPr, eData);
                        }
                        else if (EReceiveCells.TryGetShareChannel(attr.ChannelId, out EReceiveChannel? channel))
                        {
                            return PushToChannelReceive(channel, eData); ;
                        }
                        else if (EReceiveCells.TryGetPrivateChannel(attr.ChannelId, out EAttrChannel? attrChannel))
                        {
                            if (attrChannel != null)
                            {
                                var endChannel = new EReceiveChannel(true, attrChannel.ChannelTasks);
                                _privateChannels.Add(attr.ChannelId, endChannel);
                                return PushToChannelReceive(endChannel, eData); ;
                            }
                        }
                        else
                        {
                            return PushToChannelReceive(ChannelReceiveBasic, eData); ;
                        }
                    }
                }
            }
            return result;
        }

        int PushToChannelReceive(EReceiveChannel? channel, EReceiveData data)
        {
            lock (_Lock)
            {
                if (channel != null && Running)
                {
                    data.InChannel = true;
                    channel.Push(data);
                    return 0;
                }
                return 3;
            }
        }

        private protected virtual ERCell? GetReceiveCell(PartDataDTO dto) { return null; }
        private protected virtual void RunReceiveMsg(ERCell eData, PartDataDTO dto) { }
        private protected virtual void RunReceiveMsg(EReceiveData eData, PartDataDTO dto) { }
        private protected virtual bool ReceiveSpecial(PartDataDTO dto) { return true; }

        internal ValueTask<bool> RunObjMsgSend(ESendMsg msg)
        {
            try
            {
                if (msg.Session == 0) msg.Session = GetSession();
                msg.FillList(PreparedSendObj.Data);
                PreparedSendObj.TotalBytes = msg.TotalBytes;
                PreparedSendObj.Session = msg.Session;
                PreparedSendObj.DForm = EDataForm.Msg;
                PreparedSendObj.Target = msg.Target;
                PreparedSendObj.Instance = msg.Instance;
                if (ESerial.Serialize(ArrayBufferWriterSend, PreparedSendObj) == 0)
                    return ValueTask.FromResult(false);
                return ETCPSocket.Send(BasicSocket, SendArgs, AESgcm, ArrayBufferWriterSend.WrittenMemory);
            }
            finally
            {
                PreparedSendObj.Data.Clear();
                ArrayBufferWriterSend.ResetWrittenCount();
            }
        }

        readonly byte[] _memorySpecial = new byte[8];
        private protected ValueTask<bool> RunSpecialSend(ulong session, ulong typeMsg, long? msg)
        {
            try
            {
                if (msg != null)
                {
                    PreparedSendObj.Data.AddRange(_memorySpecial);
                    BinaryPrimitives.WriteInt64LittleEndian(CollectionsMarshal.AsSpan(PreparedSendObj.Data), (long)msg);
                    PreparedSendObj.TotalBytes = 8;
                }
                else
                    PreparedSendObj.TotalBytes = 0;
                PreparedSendObj.Session = session;
                PreparedSendObj.DForm = EDataForm.Special;
                PreparedSendObj.Target = typeMsg;
                PreparedSendObj.Instance = 0;
                if (ESerial.Serialize(ArrayBufferWriterSend, PreparedSendObj) == 0)
                    return ValueTask.FromResult(false);
                return ETCPSocket.Send(BasicSocket, SendArgs, AESgcm, ArrayBufferWriterSend.WrittenMemory);
            }
            finally
            {
                PreparedSendObj.Data.Clear();
                ArrayBufferWriterSend.ResetWrittenCount();
            }
        }

        internal ValueTask<bool> RunHeartbeatSend()
        {
            try
            {
                PreparedSendObj.TotalBytes = 0;
                PreparedSendObj.Session = 0;
                PreparedSendObj.DForm = EDataForm.Special;
                PreparedSendObj.Target = 0;
                PreparedSendObj.Instance = 0;
                if (ESerial.Serialize(ArrayBufferWriterSend, PreparedSendObj) == 0)
                    return ValueTask.FromResult(false);
                return ETCPSocket.Send(BasicSocket, SendArgs, AESgcm, ArrayBufferWriterSend.WrittenMemory);
            }
            finally
            {
                PreparedSendObj.Data.Clear();
                ArrayBufferWriterSend.ResetWrittenCount();
            }
        }

        ulong LastSessionToGet = (ulong)DateTime.UtcNow.Ticks;
        /// <summary>
        /// Returns a unique session on socket
        /// </summary>
        internal ulong GetSession()
        {
            return ++LastSessionToGet;
        }

        private protected async Task StartHeartbeat()
        {
            var running = true;
            while (true)
            {
                await Task.Delay(Heartbeat * (running ? 1 : 2));
                running = Running;
                if (Running)
                    await ChannelSend.TrySendHeartbeat(RunHeartbeatSend);
            }
        }

        internal long RegisterPrivateInstance(object obj)
        {
            Type t = obj.GetType();
            if (EReceiveCells.ExistInstanceCell(t, out Dictionary<ulong, ERCell>? val))
            {
                if (val == null || !val.Any(x => x.Value.SocketType == SocketType))
                    return 0;
                long id = EUserServer.GetUniqueId();
                lock (_Lock)
                {
                    _privateInstances.Add(id, (obj, val));
                    return id;
                }
            }
            return 0;
        }

        internal bool RemovePrivateInstance(long id)
        {
            lock (_Lock)
            {
                return _privateInstances.Remove(id);
            }
        }

        internal void ClearPrivateInstance()
        {
            lock (_Lock)
            {
                _privateInstances.Clear();
            }
        }

        internal bool TryGetInstance(ERCell cell, long instance, out object? obj)
        {
            if (cell.MethodInfo.IsStatic)
            {
                obj = null;
                return true;
            }

            if (instance > 0)
            {
                obj = GetPrivateInstance(instance);
                if (obj != null)
                    return true;
                else
                {
                    obj = null;
                    return false;
                }
            }

            obj = GetLocalInstance(cell.ClassType);
            return obj != null;
        }

        internal object? GetPrivateInstance(long id)
        {
            lock (_Lock)
            {
                if (_privateInstances.TryGetValue(id, out var instance))
                    return instance.Item1;
            }
            return null;
        }

        object? GetLocalInstance(Type? type)
        {
            if (type == null)
                return null;

            if (_localInstances.TryGetValue(type, out var instance))
                return instance;
            else
            {
                try
                {
                    instance = Activator.CreateInstance(type);
                    if (instance != null)
                        _localInstances?.Add(type, instance);
                }
                catch { }
            }
            return instance;
        }

        internal virtual void Dispose()
        {
            Socket? socket;
            lock (_Lock)
            {
                Running = false;
                socket = BasicSocket;
                BasicSocket = null;
                LastSessionReceive = 0;
                _privateInstances.Clear();
                _localInstances.Clear();
                DisposeReceiveDataSessions();
            }

            ETCPSocket.ShutdownAndClose(socket);
        }

        private protected virtual void DisposeReceiveDataSessions()
        {
            var listToRemove = new List<EReceiveMsg>();
            lock (_Lock)
            {
                foreach (var item in ReceiveDataSessions.Values)
                {
                    if (!item.InChannel)
                        listToRemove.Add(item);
                }
                ReceiveDataSessions.Clear();
            }

            foreach (var item in listToRemove)
                item.Dispose();
        }

        internal bool IsEmptyReceiveDataSessions()
        {
            lock (_Lock)
            {
                return ReceiveDataSessions.Count < 1;
            }
        }
    }
}
