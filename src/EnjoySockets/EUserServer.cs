using System.Net.Sockets;
using System.Reflection;

namespace EnjoySockets
{
    public enum ESocketServerStatus
    {
        Alive, ReconnectAttempt, BypassToReconnect, Dead
    }

    public class EUserServer : EUser<ESocketResourceServer>
    {
        /// <summary>
        /// Represents the current status of the socket server.
        /// </summary>
        /// <remarks>
        /// Possible values of <see cref="ESocketServerStatus"/>:
        /// <list type="bullet">
        ///   <item><description><c>Alive</c> – the client is active and operational.</description></item>
        ///   <item><description><c>ReconnectAttempt</c> – the client is trying to reconnect.</description></item>
        ///   <item><description><c>BypassToReconnect</c> – the client is bypassed temporarily to perform reconnection logic.</description></item>
        ///   <item><description><c>Dead</c> – the client object is no longer usable; it should not be accessed,
        ///   allowing the garbage collector to collect it safely.</description></item>
        /// </list>
        /// </remarks>
        public ESocketServerStatus Status { get; private set; } = ESocketServerStatus.Alive;
        internal MethodInfo? AuthorizationMethod { get; set; }
        internal Action<EUserServer, ESocketResourceServer?>? ReleaseEvent { get; set; }

        public EUserServer(ESocketResourceServer eSocketResource) : base(eSocketResource)
        {
            eSocketResource.CheckAccessEvent = OnCheckAccess;
            eSocketResource.RunOnPotentialSabotageEvent = RunOnPotentialSabotage;
            eSocketResource.RunDisposeEvent = Dispose;
        }

        internal bool AppendSocket(Socket socket)
        {
            lock (_lock)
            {
                if (Status == ESocketServerStatus.ReconnectAttempt || Status == ESocketServerStatus.Dead)
                    return false;

                Status = ESocketServerStatus.ReconnectAttempt;
            }

            if (SocketResource != null)
            {
                SocketResource.FirstConnect = false;
                return SocketResource.AppendSocket(socket);
            }
            return false;
        }

        internal void TrySetBypass()
        {
            lock (_lock)
            {
                if (Status == ESocketServerStatus.ReconnectAttempt)
                    Status = ESocketServerStatus.BypassToReconnect;
            }
        }

        internal sealed override bool Start()
        {
            ESocketServerStatus status;
            lock (_lock)
                status = Status;

            if (status != ESocketServerStatus.Dead)
            {
                if (base.Start())
                {
                    if (status == ESocketServerStatus.Alive)
                    {
                        OnConnected();
                    }
                    else
                    {
                        lock (_lock)
                        {
                            if (Status == ESocketServerStatus.Dead)
                                return false;
                            Status = ESocketServerStatus.Alive;
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Closes the active connection, then releases all associated resources.
        /// After calling this method, any further reconnection attempts will be rejected.
        /// </summary>
        public void Disconnect()
        {
            _ = DisposeRun();
        }

        internal void Dispose()
        {
            bool dead;
            lock (_lock)
            {
                dead = EReceiveArgs.IsSoftClose(SocketResource);
                if (!dead)
                {
                    if (Status != ESocketServerStatus.ReconnectAttempt)
                        Status = ESocketServerStatus.BypassToReconnect;
                }
            }

            if (dead)
                _ = DisposeRun();
            else
                _ = StartKeepAliveRun();
        }

        public override sealed ValueTask<bool> Send<T>(long instance, string target, T? obj) where T : default
        {
            if (Status != ESocketServerStatus.Alive)
                return new ValueTask<bool>(false);

            return base.Send(instance, target, obj);
        }

        /// <summary>
        /// Sends a serialized message to the specified target and instance.
        /// </summary>
        /// <remarks>
        /// The message is considered sent once it is successfully handed off to the operating system.
        /// <para>
        /// This does not guarantee delivery to the receiving client.
        /// </para>
        /// <para>
        /// The <paramref name="obj"/> parameter should contain bytes serialized by the <c>MemoryPack</c> library.
        /// </para>
        /// </remarks>
        /// <param name="target">The destination to which the message is sent.</param>
        /// <param name="obj">The payload as a serialized byte array. May be empty.</param>
        /// <param name="instance">The target instance ID. Defaults to <c>0</c>.</param>
        /// <returns>
        /// <see langword="true"/> if the message was successfully passed to the operating system;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public ValueTask<bool> Send(string target, ReadOnlyMemory<byte> obj, long instance = 0)
        {
            if (Status != ESocketServerStatus.Alive)
                return ValueTask.FromResult(false);

            var t = EReceiveCells.GetUlongToSend(target);
            if (t < 1)
                return ValueTask.FromResult(false);

            var rentBytesToBuffer = obj.Length < ETCPSocket.MinBufferSlotSizeBytes ? ETCPSocket.MinBufferSlotSizeBytes : obj.Length;
            if (!BufferToSendMsg.TryRent(rentBytesToBuffer))
                return ValueTask.FromResult(false);

            if (SocketResource == null)
            {
                BufferToSendMsg.Return(rentBytesToBuffer);
                return ValueTask.FromResult(false);
            }

            var vt = SocketResource.ChannelSend.TrySendMsgAndGetSession(SocketResource.RunObjMsgSend, t, obj, instance);
            if (vt.IsCompletedSuccessfully)
            {
                BufferToSendMsg.Return(rentBytesToBuffer);
                return ValueTask.FromResult(vt.Result != 0);
            }
            return AwaitSendMemAsync(vt, rentBytesToBuffer);
        }

        async ValueTask<bool> AwaitSendMemAsync(ValueTask<ulong> vt, int buffer)
        {
            try
            {
                return (await vt) != 0;
            }
            finally
            {
                BufferToSendMsg.Return(buffer);
            }
        }

        internal async Task<byte> CheckAuthorization(object? obj)
        {
            try
            {
                if (AuthorizationMethod == null)
                    return await Task.FromResult((byte)1);

                var result = AuthorizationMethod.Invoke(this, [obj]);
                return await (Task<byte>)result!;
            }
            catch
            {
                return await Task.FromResult((byte)1);
            }
        }

        /// <summary>
        /// Checks whether access to a method is allowed based on the <see cref="EAttr.Access"/> value.
        /// </summary>
        /// <param name="accessType">
        /// The access type specified in the <see cref="EAttr"/> attribute of the method.
        /// </param>
        /// <returns>
        /// <c>true</c> if access is allowed; <c>false</c> if access should be denied.
        /// </returns>
        /// <remarks>
        /// Override this method in a derived class to implement custom access control logic.
        /// This method is called automatically for server-type methods that have <see cref="EAttr.Access"/> set.
        /// By default, access is always allowed (<c>true</c>).
        /// </remarks>
        protected virtual bool OnCheckAccess(long accessType) { return true; }

        /// <summary>
        /// Called when any suspicious or abnormal activity is detected from the client side.
        /// <param/>
        /// Note: the client has already been disconnected by the system before this method is called.
        /// </summary>
        /// <param name="msg">
        /// The code indicating the type of suspicious activity:
        /// <list type="table">
        /// <item><term>1</term><description>The main object of the packet is corrupted.</description></item>
        /// <item><term>2</term><description>The client is not receiving data while continuously sending.</description></item>
        /// </list>
        /// </param>
        /// <remarks>
        /// Override this method in a derived class to implement custom handling,
        /// such as logging, alerting or recording metrics.
        /// </remarks>
        protected virtual Task OnPotentialSabotage(int msg) { return Task.CompletedTask; }

        private protected void RunOnPotentialSabotage(int msg)
        {
            Disconnect();
            _ = OnPotentialSabotage(msg);
        }

        /// <summary>
        /// Releases user resources and resets the object state to its defaults. 
        /// Close connect with socket and break client reconnect.
        /// Object can't use again.
        /// </summary>
        async Task DisposeRun()
        {
            ESocketResourceServer? esr = null;
            lock (_lock)
            {
                Status = ESocketServerStatus.Dead;
                if (SocketResource != null)
                {
                    SocketResource.RunDisposeEvent = null;
                    SocketResource.RunOnPotentialSabotageEvent = null;
                    SocketResource.CheckAccessEvent = null;
                    esr = SocketResource;
                }
                SocketResource = null;
            }

            if (esr != null)
            {
                esr.Dispose();
                while (true) //Wait for all sessions in channels to be released (protection against memory exhaustion attacks)
                {
                    if (esr.IsEmptyReceiveDataSessions())
                    {
                        esr.ClearResponseCache();
                        ReleaseEvent?.Invoke(this, esr);
                        ReleaseEvent = null;
                        OnDisconnected();
                        break;
                    }
                    await Task.Delay(1000);
                }
            }
        }

        int _idRunKeepAlive = 0;
        async Task StartKeepAliveRun()
        {
            int id = 0;
            lock (_lock)
            {
                if (Status == ESocketServerStatus.Dead)
                    return;
                _idRunKeepAlive++;
                id = _idRunKeepAlive;
            }

            var time = SocketResource?.KeepAlive ?? 0;
            if (time > 0)
                await Task.Delay(time);
            else return;

            lock (_lock)
            {
                if ((Status == ESocketServerStatus.BypassToReconnect || Status == ESocketServerStatus.ReconnectAttempt) && _idRunKeepAlive == id)
                    Status = ESocketServerStatus.Dead;
                else
                    return;
            }
            _ = DisposeRun();
        }
    }
}
