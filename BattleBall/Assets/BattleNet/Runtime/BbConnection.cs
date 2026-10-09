using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using BattleBall.Proto;
using Google.Protobuf;

namespace BattleBall.Net
{
    /// <summary>
    /// One TLS connection to BattleBallServer (docs/protocol/BattleBall.md section 11): frames are a u32 little-endian length followed by one
    /// serialized <see cref="Envelope"/>. A reader thread parses frames; answers to a request (same request_id) complete its Task, everything else
    /// (events pushed by the server, request_id 0) is queued for <see cref="TryDequeueEvent"/> so the game can read it on the main thread.
    /// Works without any Unity API, so it also runs in batch-mode verification.
    /// </summary>
    public sealed class BbConnection : IDisposable
    {
        public const int ProtocolVersion = 1;
        const int MaxFrameBytes = 1 << 20;

        readonly object _writeLock = new object();
        readonly ConcurrentDictionary<uint, TaskCompletionSource<Envelope>> _pending = new ConcurrentDictionary<uint, TaskCompletionSource<Envelope>>();
        readonly ConcurrentQueue<Envelope> _events = new ConcurrentQueue<Envelope>();
        TcpClient _tcp;
        SslStream _ssl;
        Thread _reader;
        Timer _heartbeat;
        int _nextRequestId;
        volatile bool _closed;

        public HelloAck ServerInfo { get; private set; }
        public bool IsConnected { get { return !_closed && _tcp != null && _tcp.Connected; } }
        /// <summary>Raised once, on a background thread, when the connection ends (reason is the server notice, or null for a plain drop).</summary>
        public event Action<DisconnectNotice> Disconnected;

        /// <summary>
        /// Development only: accept any server certificate (the dev keystore is self-signed). Production builds must leave this false so that
        /// the platform validates the certificate chain.
        /// </summary>
        public bool AcceptAnyCertificate { get; set; }

        public async Task ConnectAsync(string host, int port, string clientVersion, string platform, int timeoutMs = 8000, Action<float> progress = null)
        {
            progress?.Invoke(0.1f);
            _tcp = new TcpClient { NoDelay = true };
            var connect = _tcp.ConnectAsync(host, port);
            if (await Task.WhenAny(connect, Task.Delay(timeoutMs)) != connect)
            {
                _tcp.Close();
                throw new TimeoutException("connect timed out");
            }
            await connect;
            progress?.Invoke(0.4f);
            RemoteCertificateValidationCallback validate = (s, cert, chain, errors) => AcceptAnyCertificate || errors == SslPolicyErrors.None;
            _ssl = new SslStream(_tcp.GetStream(), false, validate);
            await _ssl.AuthenticateAsClientAsync(host, null, SslProtocols.Tls12, false);
            progress?.Invoke(0.7f);

            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "bb-net-read" };
            _reader.Start();

            var ack = await RequestAsync(new Envelope { Hello = new Hello { ProtocolVersion = ProtocolVersion, ClientVersion = clientVersion, Platform = platform } }, timeoutMs);
            if (ack.PayloadCase != Envelope.PayloadOneofCase.HelloAck)
            {
                Close();
                throw new InvalidOperationException("handshake refused: " + Describe(ack));
            }
            ServerInfo = ack.HelloAck;
            progress?.Invoke(1f);
            uint every = Math.Max(1000u, ServerInfo.HeartbeatIntervalMs);
            _heartbeat = new Timer(_ => SendPing(), null, (int)every, (int)every);
        }

        /// <summary>Sends a request and waits for the answer carrying the same request_id.</summary>
        public Task<Envelope> RequestAsync(Envelope request, int timeoutMs = 10000)
        {
            uint id = (uint)Interlocked.Increment(ref _nextRequestId);
            request.RequestId = id;
            var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            Send(request);
            return WithTimeout(id, tcs, timeoutMs);
        }

        async Task<Envelope> WithTimeout(uint id, TaskCompletionSource<Envelope> tcs, int timeoutMs)
        {
            if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)) != tcs.Task)
            {
                _pending.TryRemove(id, out _);
                throw new TimeoutException("no answer to request " + id);
            }
            return await tcs.Task;
        }

        /// <summary>Fire and forget (no answer expected), e.g. PlayerStateReport during a match.</summary>
        public void Send(Envelope message)
        {
            var body = message.ToByteArray();
            var frame = new byte[4 + body.Length];
            frame[0] = (byte)body.Length;
            frame[1] = (byte)(body.Length >> 8);
            frame[2] = (byte)(body.Length >> 16);
            frame[3] = (byte)(body.Length >> 24);
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            lock (_writeLock)
            {
                if (_closed) throw new IOException("connection closed");
                _ssl.Write(frame, 0, frame.Length);
                _ssl.Flush();
            }
        }

        public bool TryDequeueEvent(out Envelope e) { return _events.TryDequeue(out e); }

        void SendPing()
        {
            try { Send(new Envelope { Ping = new Ping { ClientTimeMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } }); }
            catch (Exception) { /* the read loop notices a dead connection */ }
        }

        void ReadLoop()
        {
            DisconnectNotice notice = null;
            try
            {
                var head = new byte[4];
                while (!_closed)
                {
                    ReadFully(head, 4);
                    int len = head[0] | (head[1] << 8) | (head[2] << 16) | (head[3] << 24);
                    if (len < 0 || len > MaxFrameBytes) throw new InvalidDataException("bad frame length " + len);
                    var body = new byte[len];
                    ReadFully(body, len);
                    var env = Envelope.Parser.ParseFrom(body);
                    if (env.PayloadCase == Envelope.PayloadOneofCase.DisconnectNotice) notice = env.DisconnectNotice;
                    if (env.RequestId != 0 && _pending.TryRemove(env.RequestId, out var tcs)) tcs.TrySetResult(env);
                    else if (env.PayloadCase != Envelope.PayloadOneofCase.Pong) _events.Enqueue(env);
                }
            }
            catch (Exception) { /* closed or broken: handled below */ }
            _closed = true;
            foreach (var kv in _pending)
                if (_pending.TryRemove(kv.Key, out var t)) t.TrySetException(new IOException("connection closed"));
            Disconnected?.Invoke(notice);
        }

        void ReadFully(byte[] buffer, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = _ssl.Read(buffer, got, count - got);
                if (n <= 0) throw new EndOfStreamException();
                got += n;
            }
        }

        public static string Describe(Envelope e)
        {
            switch (e.PayloadCase)
            {
                case Envelope.PayloadOneofCase.Error: return e.Error.Code + ": " + e.Error.Message;
                case Envelope.PayloadOneofCase.DisconnectNotice: return e.DisconnectNotice.Reason + ": " + e.DisconnectNotice.Message;
                default: return e.PayloadCase.ToString();
            }
        }

        public void Close()
        {
            _closed = true;
            try { _heartbeat?.Dispose(); } catch (Exception) { }
            try { _ssl?.Close(); } catch (Exception) { }
            try { _tcp?.Close(); } catch (Exception) { }
        }

        public void Dispose() { Close(); }
    }
}
