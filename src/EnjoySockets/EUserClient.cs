// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using EnjoySockets.DTO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using static EnjoySockets.ETCPControlSendingClient;

namespace EnjoySockets
{
    public enum ESocketClientStatus
    {
        Connected, ReconnectAttempt, Disconnected
    }

    public class EUserClient : EUser<ESocketResourceClient>
    {
        /// <summary>
        /// Represents the current status of the socket client.
        /// </summary>
        /// <remarks>
        /// Possible values of <see cref="ESocketClientStatus"/>:
        /// <list type="bullet">
        ///   <item><description><c>Connected</c> - the client is actively connected to the server.</description></item>
        ///   <item><description><c>ReconnectAttempt</c> - the client is attempting to reconnect.</description></item>
        ///   <item><description><c>Disconnected</c> - the client is not connected.</description></item>
        /// </list>
        /// Regardless of the current status, the client object is reusable.
        /// Do not create new client instances unnecessarily; reuse existing ones whenever possible.
        /// </remarks>
        public ESocketClientStatus Status { get; private set; } = ESocketClientStatus.Disconnected;
        public EAddress? Address { get; set; }

        int _reconnectDelayMs = 0;
        readonly ETCPControlSendingClient _controlSending;

        /// <summary>
        /// Initializes a new instance of the <see cref="EUserClient"/> class
        /// using the specified RSA provider and default client configuration.
        /// </summary>
        /// <param name="ersa">RSA provider used for encryption and signing.</param>
        /// <remarks>
        /// The client instance is reusable regardless of its connection status.
        /// Do not create new instances unnecessarily - the same object can be
        /// reconnected and reused multiple times.
        /// </remarks>
        public EUserClient(ERSA ersa) : this(ersa, new()) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="EUserClient"/> class
        /// using the specified RSA provider and client configuration.
        /// </summary>
        /// <param name="ersa">RSA provider used for encryption and signing.</param>
        /// <param name="config">Client configuration settings.</param>
        /// <remarks>
        /// The client instance is reusable regardless of its connection status.
        /// It is recommended to reuse existing instances instead of creating
        /// new ones for each connection attempt.
        /// </remarks>
        public EUserClient(ERSA ersa, ETCPClientConfig config) : base(new ESocketResourceClient(config, ersa))
        {
            EReceiveCells.Initialize();
            if (SocketResource != null)
            {
                SocketResource.UserObj = this;
                SocketResource.RunDisposeEvent = Dispose;
            }
            _controlSending = new(SocketResource?.MessageBuffer ?? config.MessageBuffer * 1024);
            UserId = SetGuidUserId();
        }

        /// <summary>
        /// Generates a unique user ID used for client reconnection.
        /// </summary>
        /// <returns>A new <see cref="Guid"/> for the session.</returns>
        protected virtual Guid SetGuidUserId()
        {
            return Guid.NewGuid();
        }

        /// <summary>
        /// Sends a message without payload to the specified target,
        /// and awaits a guaranteed response.
        /// </summary>
        /// <inheritdoc cref="SendWithResponse{T}(long, string, T)"/>
        public ValueTask<long> SendWithResponse(string target)
        {
            return SendWithResponse(0, target);
        }

        /// <summary>
        /// Sends a message with payload to the specified target,
        /// and awaits a guaranteed response.
        /// </summary>
        /// <inheritdoc cref="SendWithResponse{T}(long, string, T)"/>
        public ValueTask<long> SendWithResponse<T>(string target, T obj)
        {
            return SendWithResponse(0, target, obj);
        }

        /// <summary>
        /// Sends a message without payload to the specified target and instance,
        /// and awaits a guaranteed response.
        /// </summary>
        /// <inheritdoc cref="SendWithResponse{T}(long, string, T)"/>
        public ValueTask<long> SendWithResponse(long instance, string target)
        {
            return SendWithResponse<object>(instance, target, null);
        }

        /// <summary>
        /// Sends a message with a payload to the specified target and instance,
        /// and awaits a guaranteed response.
        /// </summary>
        /// <remarks>
        /// This method guarantees that a response will be received even if the connection
        /// was temporarily lost and the client is in reconnection mode.
        /// <para>
        /// The returned <see langword="long"/> value encodes the outcome:
        /// <list type="bullet">
        /// <item><c>0</c> - the operation completed successfully.</item>
        /// <item><c>-1</c> - method execution failed.</item>
        /// <item><c>-2</c> - buffer full (unable to rent memory).</item>
        /// <item><c>-3</c> - session expired (response could not be retrieved).</item>
        /// <item><c>-4</c> - access denied.</item>
        /// <item><c>-5</c> - invalid payload or invalid arguments (e.g., null parameters, empty data, or deserialization failure).</item>
        /// <item>
        /// <c>&gt; 0</c> - user-defined or application-specific codes, such as instance ID or internal status.
        /// </item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <typeparam name="T">The type of the payload being sent.</typeparam>
        /// <param name="instance">The target instance ID.</param>
        /// <param name="target">The destination to which the message is sent.</param>
        /// <param name="obj">The payload object to send. May be <see langword="null"/>.</param>
        /// <returns>
        /// A <see langword="long"/> representing the outcome:
        /// <c>0</c> for success, &lt;0 for predefined errors, &gt;0 for user-defined results.
        /// </returns>
        public async ValueTask<long> SendWithResponse<T>(long instance, string target, T? obj)
        {
            if (SocketResource?.BasicSocket == null)
                return -1;

            var t = EReceiveCells.GetUlongToSend(target);
            if (t < 1) return -1;

            var segments = SocketResource.ObjToSegments(obj);

            if (segments == null && obj != null)
                return -1;

            if (segments != null && segments.WrittenBytes > SocketResource.MessageBuffer)
            {
                segments?.Clear();
                return -1;
            }

            var length = segments?.WrittenBytes ?? ETCPSocket.MinBufferSlotSizeBytes;
            length = length < ETCPSocket.MinBufferSlotSizeBytes ? ETCPSocket.MinBufferSlotSizeBytes : length;
            EControlSendingWaiter? ecsw;
            if ((ecsw = _controlSending.TryWait(length)) == null)
                ecsw = await _controlSending.Wait(length);

            if (ecsw.Cancel)
            {
                segments?.Clear();
                _controlSending.Release(ecsw);
                return -1;
            }

            ulong session = SocketResource.GetSession();
            var sender = SocketResource.MsgCache.Get(session, t, instance, segments);
            if (sender == null)
            {
                segments?.Clear();
                _controlSending.Release(ecsw);
                return -1;
            }

            var objMsg = ESendMsg.Rent();
            objMsg.RunPrepare(SocketResource.RunObjMsgSend, t, segments, instance);
            objMsg.Session = session;

            var vt = SocketResource.ChannelSend.TrySendMsgAndGetSession(objMsg);
            if (!vt.IsCompletedSuccessfully)
                await vt;

            var resultFromServ = await sender.AsValueTask();
            SocketResource.MsgCache.Remove(session);
            _controlSending.Release(ecsw);
            return resultFromServ;
        }

        public async override sealed ValueTask<bool> Send<T>(long instance, string target, T? obj) where T : default
        {
            if (SocketResource?.BasicSocket == null || Status != ESocketClientStatus.Connected)
                return false;

            var t = EReceiveCells.GetUlongToSend(target);
            if (t < 1)
                return false;

            var segments = SocketResource.ObjToSegments(obj);
            if (segments == null && obj != null)
                return false;

            if (segments != null && segments.WrittenBytes > SocketResource.MessageBuffer)
            {
                segments?.Clear();
                return false;
            }

            var length = segments?.WrittenBytes ?? ETCPSocket.MinBufferSlotSizeBytes;
            length = length < ETCPSocket.MinBufferSlotSizeBytes ? ETCPSocket.MinBufferSlotSizeBytes : length;
            EControlSendingWaiter? ecsw;
            if ((ecsw = _controlSending.TryWait(length)) == null)
                ecsw = await _controlSending.Wait(length);

            if (ecsw.Cancel)
            {
                segments?.Clear();
                _controlSending.Release(ecsw);
                return false;
            }

            var totalBytes = segments?.WrittenBytes ?? 0;
            ulong session = SocketResource.GetSession();
            var sender = SocketResource.MsgCache.Get(session, totalBytes);
            if (sender == null)
            {
                segments?.Clear();
                _controlSending.Release(ecsw);
                return false;
            }

            var objMsg = ESendMsg.Rent();
            objMsg.RunPrepare(SocketResource.RunObjMsgSend, t, segments, instance);
            objMsg.Session = session;

            var success = false;
            var vt = SocketResource.ChannelSend.TrySendMsgAndGetSession(objMsg);
            if (vt.IsCompletedSuccessfully)
                success = vt.Result > 0;
            else
                success = await vt > 0;

            segments?.Clear();

            if (!success)
            {
                SocketResource.MsgCache.Remove(session);
                _controlSending.Release(ecsw);
                return false;
            }

            _ = SendWaitOnResultFromServ(SocketResource, session, ecsw, sender);
            return true;
        }

        async ValueTask SendWaitOnResultFromServ(ESocketResourceClient sr, ulong session, EControlSendingWaiter ecsw, ESender sender)
        {
            await sender.AsValueTask();
            sr.MsgCache.Remove(session);
            _controlSending.Release(ecsw);
        }

        #region Connection

        /// <summary>
        /// Gets the user-defined authorization object to be sent during the connection handshake.
        /// <para/>
        /// The returned object is always of type <see cref="object"/> in the base class,
        /// but in the derived <c>EUserServer</c> class the user knows its actual type
        /// and should pass it directly to the corresponding method:
        /// <code>
        /// protected Task&lt;byte&gt; Authorization(actualType obj)
        /// </code>
        /// where <c>actualType</c> matches the type returned by <see cref="GetAuthorization"/>.
        /// </summary>
        /// <remarks>
        /// The serialized size of the returned object must not exceed 1250 bytes.
        /// The base implementation returns <c>null</c>, meaning no authorization.
        /// </remarks>
        protected internal virtual object? GetAuthorization() { return null; }

        /// <summary>
        /// Called after a reconnection attempt.
        /// </summary>
        /// <param name="attemptCount">The current reconnection attempt number.</param>
        /// <param name="attemptResult">
        /// The result code of the attempt. A value of <c>0</c> typically indicates success;
        /// any other value represents an error.
        /// </param>
        protected virtual void OnReconnectAttempt(int attemptCount, byte attemptResult) { }

        const int _maxReconnectDelayMs = 60000;
        const int _minReconnectDelayMs = 1000;
        /// <summary>
        /// Asynchronously connects to the server endpoint and performs handshake.
        /// </summary>
        /// <param name="eAddress">Optional server endpoint; uses <c>Address</c> if null.</param>
        /// <param name="reconnectDelayMs">
        /// Delay in milliseconds between reconnection attempts (default 5000ms).
        /// Must be between 1000 and 60000 milliseconds; if the value is outside this range,
        /// the default of 5000ms will be used.
        /// </param>
        /// <returns>
        /// A <see cref="byte"/> status code representing the result of the connection attempt:
        /// <list type="table">
        /// <item><term>0</term><description>Connection succeeded.</description></item>
        /// <item><term>1</term><description>Invalid IP address or endpoint.</description></item>
        /// <item><term>2</term><description>Connection attempt timed out.</description></item>
        /// <item><term>3</term><description>The server is full.</description></item>
        /// <item><term>4</term><description>Server verification failed.</description></item>
        /// <item><term>5</term><description>Failed to establish encryption key.</description></item>
        /// <item><term>6</term><description>General connection failure.</description></item>
        /// <item><term>7</term><description>Failed to send authorization data.</description></item>
        /// <item><term>8</term><description>Invalid authorization data received.</description></item>
        /// <item><term>9</term><description>Connection is already active or in progress.</description></item>
        /// <item><term>10</term><description>Reconnect interrupted via <c>Disconnect</c>.</description></item>
        /// </list>
        /// Values &gt; 10 may be used for custom authorization error codes if authorization is implemented.
        /// </returns>
        /// <remarks>
        /// The reconnect loop can be interrupted by calling <see cref="Disconnect"/>.
        /// This method builds on <see cref="Connect(EAddress?)"/> but adds automatic reconnection logic.
        /// </remarks>
        public Task<byte> ConnectWithAutoReconnect(EAddress? eAddress, int reconnectDelayMs = 5000)
        {
            _reconnectDelayMs = reconnectDelayMs < _minReconnectDelayMs || reconnectDelayMs > _maxReconnectDelayMs ? 5000 : reconnectDelayMs;
            return Connect(eAddress);
        }

        async Task<bool> RunConnectWithReconnect(EAddress? address, int reconnectDelayMs)
        {
            int attempt = 1;

            if (reconnectDelayMs < _minReconnectDelayMs || reconnectDelayMs > _maxReconnectDelayMs || SocketResource == null)
            {
                OnReconnectAttempt(attempt, 6);
                return false;
            }

            if (address == null || address.EndPoint == null)
            {
                OnReconnectAttempt(attempt, 1);
                return false;
            }

            Address = address;
            _reconnectDelayMs = reconnectDelayMs;

            while (true)
            {
                var time = _reconnectDelayMs;
                if (time < 1)
                {
                    OnReconnectAttempt(attempt, 10);
                    return false;
                }

                var result = await Connect(Address);
                if (result == 0 || SocketResource.Running)
                {
                    OnReconnectAttempt(attempt, result);
                    return true;
                }
                else
                {
                    OnReconnectAttempt(attempt, result);
                    await Task.Delay(time);
                    attempt++;
                }
            }
        }

        readonly byte[] BufferConDTO = new byte[512];
        readonly ConnectDTO ConnectDTOObj = new();
        bool _connecting = false;
        bool _firstConnect = true;
        /// <summary>
        /// Asynchronously connects to the server endpoint and performs handshake.
        /// </summary>
        /// <param name="eAddress">Optional server endpoint; uses <c>Address</c> if null.</param>
        /// <returns>
        /// A <see cref="byte"/> status code representing the result of the connection attempt:
        /// <list type="table">
        /// <item><term>0</term><description>Connection succeeded.</description></item>
        /// <item><term>1</term><description>Invalid IP address or endpoint.</description></item>
        /// <item><term>2</term><description>Connection attempt timed out.</description></item>
        /// <item><term>3</term><description>The server is full.</description></item>
        /// <item><term>4</term><description>Server verification failed.</description></item>
        /// <item><term>5</term><description>Failed to establish encryption key.</description></item>
        /// <item><term>6</term><description>General connection failure.</description></item>
        /// <item><term>7</term><description>Failed to send authorization data.</description></item>
        /// <item><term>8</term><description>Invalid authorization data received.</description></item>
        /// <item><term>9</term><description>Connection is already active or in progress.</description></item>
        /// </list>
        /// Values &gt; 10 may be used for custom authorization error codes if authorization is implemented.
        /// </returns>
        public async Task<byte> Connect(EAddress? eAddress)
        {
            var addr = eAddress ?? Address;
            if (addr == null || addr.EndPoint == null)
                return 1;

            Address = addr;
            try
            {
                if (SocketResource == null)
                    return 6;

                lock (_lock)
                {
                    if (_connecting)
                        return 9;

                    _connecting = true;
                }

                var nSocket = new Socket(addr.EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                if (!SocketResource.AppendSocket(nSocket))
                {
                    ETCPSocket.Close(nSocket);
                    return 9;
                }

                var connectResult = await ConnectWithTimeout(SocketResource.BasicSocket!, addr.EndPoint, TimeSpan.FromSeconds(SocketResource.ConfigClient.ConnectTimeout));
                if (connectResult != 0)
                    return Close(connectResult);

                RandomNumberGenerator.Fill(SocketResource.NewTokenToReconnect);
                FillConnectDTO();
                var conDTO = ESerial.Serialize(ConnectDTOObj);

                int countConDTO = await SocketResource.Ersa.Encrypt(conDTO, BufferConDTO);
                if (await SocketResource.SendPlainBytes(BufferConDTO.AsMemory(0, countConDTO)))
                {
                    var msg = await ETCPSocket.ReceiveWithTimeout(ETCPSocket.Receive(SocketResource.BasicSocket!, SocketResource.ReceiveArgs), SocketResource.Config.ResponseTimeout);
                    if (msg.Length < 1)
                        return Close(4);

                    var connectDTO = ESerial.Deserialize<ConnectResponseDTO>(msg.Span);
                    if (connectDTO != null)
                    {
                        if (connectDTO.Control == 3)
                            return Close(3);

                        if (connectDTO.Control == 1 || connectDTO.Control == 4)
                        {
                            //old key
                            if (_firstConnect || !SocketResource.SetSalt())
                                return Close(5);

                            if (connectDTO.Control == 1)
                                return await SendAuth();
                            else
                            {
                                Start();
                                return 0;
                            }
                        }
                        else if (connectDTO.Control == 2 || connectDTO.Control == 5)
                        {
                            var signature = SocketResource.BuildSignature(connectDTO, SocketResource.NewTokenToReconnect);
                            if (signature.Length < 1)
                                return Close(5);

                            //new key
                            if (await SocketResource.Ersa.VerifyDataRsa(signature, connectDTO.Sign))
                            {
                                if (SocketResource.SetAesGcmKey(connectDTO.PublicKey.Span, SocketResource.NewTokenToReconnect))
                                {
                                    _firstConnect = false;
                                    if (connectDTO.Control == 2)
                                        return await SendAuth();
                                    else
                                    {
                                        Start();
                                        return 0;
                                    }
                                }
                                else return Close(5);
                            }
                            else return Close(4);
                        }
                    }
                }
                else return Close(4);
            }
            catch
            {
                return Close(6);
            }
            finally
            {
                lock (_lock)
                    _connecting = false;
            }
            return Close(6);
        }

        void FillConnectDTO()
        {
            if (SocketResource == null) return;
            ConnectDTOObj.UserId = UserId;
            ConnectDTOObj.TokenToReconnect.Clear();
            ConnectDTOObj.TokenToReconnect.AddRange(SocketResource.TokenToReconnect);
            ConnectDTOObj.NewTokenToReconnect.Clear();
            ConnectDTOObj.NewTokenToReconnect.AddRange(SocketResource.NewTokenToReconnect);
            ConnectDTOObj.Key.Clear();
#if NET8_0
            ConnectDTOObj.Key.AddRange(SocketResource.PublicKey.Span);
#else
            var span = SocketResource.PublicKey.Span;
            for (int i = 0; i < span.Length; i++)
                ConnectDTOObj.Key.Add(span[i]);
#endif
        }

        async Task<byte> SendAuth()
        {
            var objAuth = GetAuthorization();
            if (objAuth == null || SocketResource == null)
                return Close(8);

            if (await SocketResource.SendBytes(objAuth))
            {
                var msg = await SocketResource.ReceiveEncryptWithTimeout();
                if (msg.Length < 1)
                    return Close(6);

                var resAuth = ESerial.Deserialize<byte>(msg.Span);
                if (resAuth == 0)
                    Start();
                return resAuth;
            }
            else return Close(7);
        }

        async Task<byte> ConnectWithTimeout(Socket socket, IPEndPoint endpoint, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await socket.ConnectAsync(endpoint).WaitAsync(cts.Token);
                return 0;
            }
            catch (OperationCanceledException)
            {
                return 2;
            }
            catch
            {
                return 6;
            }
        }

        /// <summary>
        /// Efficiently sends a heartbeat message to detect disconnection.
        /// </summary>
        /// <remarks>
        /// The message is considered sent once it is successfully handed off to the operating system. 
        /// Note that returning <see langword="true"/> does not guarantee that the connection is still active; 
        /// rather, it forces the underlying TCP socket to attempt a transmission, which triggers the 
        /// stack to update and verify the current connection status.
        /// </remarks>
        /// <returns>
        /// <see langword="true"/> if the message was successfully passed to the operating system;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public ValueTask<bool> SendHeartbeat()
        {
            if (SocketResource?.BasicSocket == null)
                return ValueTask.FromResult(false);

            return SocketResource.ChannelSend.TrySendHeartbeat(SocketResource.RunHeartbeatSend);
        }

        CancellationTokenSource? _heartbeatCts;
        public void HeartbeatStart(int cycleTime = 2000)
        {
            CancellationTokenSource cts;

            lock (_lock)
            {
                if (_heartbeatCts != null)
                    return;

                cts = new CancellationTokenSource();
                _heartbeatCts = cts;
            }

            _ = StartHandleHeartbeat(cts, cycleTime);
        }

        public void HeartbeatStop()
        {
            CancellationTokenSource? cts;

            lock (_lock)
            {
                cts = _heartbeatCts;
                _heartbeatCts = null;
            }

            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        async Task StartHandleHeartbeat(CancellationTokenSource cts, int cycleTime)
        {
            try
            {
                var ct = cts.Token;
                while (!ct.IsCancellationRequested)
                {
                    if (!await SendHeartbeat())
                        break;

                    await Task.Delay(cycleTime, ct);
                }
            }
            catch { }
        }

        internal sealed override bool Start()
        {
            if (base.Start())
            {
                ESocketClientStatus status = ESocketClientStatus.Connected;
                lock (_lock)
                {
                    status = Status;
                    Status = ESocketClientStatus.Connected;
                }
                if (status == ESocketClientStatus.Disconnected)
                    OnConnected();
                else
                    _ = StartRebuildSessions();
                return true;
            }
            return false;
        }

        async ValueTask StartRebuildSessions()
        {
            await SocketResource!.RebuildSessions();
        }

        void Dispose()
        {
            if (EReceiveArgs.IsSoftClose(SocketResource))
                Disconnect();
            else
            {
                if (_reconnectDelayMs > 0)
                {
                    lock (_lock)
                        Status = ESocketClientStatus.ReconnectAttempt;

                    _ = StartReconnect();
                }
                else
                    Disconnect();
            }
        }

        async Task StartReconnect()
        {
            if (!await RunConnectWithReconnect(Address, _reconnectDelayMs))
                Disconnect();
        }

        byte Close(byte error)
        {
            ETCPSocket.Close(SocketResource?.BasicSocket);
            return error;
        }

        /// <summary>
        /// Closes the active connection or cancel an ongoing connection attempt, then releases all associated resources to prepare for a new connection in the future.
        /// </summary>
        public void Disconnect()
        {
            ESocketClientStatus status;
            lock (_lock)
            {
                status = ESocketClientStatus.Connected;
                if (Status != ESocketClientStatus.Disconnected)
                {
                    Status = ESocketClientStatus.Disconnected;
                    _reconnectDelayMs = 0;
                    status = Status;
                    UserId = SetGuidUserId();
                    SocketResource?.Dispose();
                }
            }
            if (status == ESocketClientStatus.Disconnected)
            {
                HeartbeatStop();
                _controlSending.CancelAll();
                OnDisconnected();
            }
        }

        #endregion
    }
}
