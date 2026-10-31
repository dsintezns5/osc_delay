// DelayProxy — UDP delay proxy core. Ported from osc_delay.py (DelayProxy class).
// .NET Framework 4.x (csc from C:\Windows\Microsoft.NET\Framework64\v4.0.30319), C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace OscDelay
{
    public class Packet
    {
        public double Time;
        public byte[] Payload;
        public Packet(double time, byte[] payload) { Time = time; Payload = payload; }
    }

    public class Snapshot
    {
        public long Received;
        public long Sent;
        public long Dropped;
        public long Queued;
        public long Bytes;
        public string Error;
        public bool Running;
    }

    public class DelayProxy
    {
        const int MaxQueuePackets = 50000;
        const int MaxQueueBytes = 64 * 1024 * 1024;

        readonly object lockObj = new object();
        readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        readonly Stopwatch clock = Stopwatch.StartNew();
        Thread thread;
        IPEndPoint target;
        int delayMs = 1000;
        long generation;
        long received, sent, dropped, queued, bytes;
        string error = "";
        bool running;

        public void Start(string listenHost, int listenPort, string targetHost, int targetPort, int delayMs)
        {
            if (thread != null && thread.IsAlive)
                throw new InvalidOperationException("Прокси уже работает.");
            IPAddress targetIp = null;
            foreach (IPAddress a in Dns.GetHostAddresses(targetHost))
            {
                if (a.AddressFamily == AddressFamily.InterNetwork) { targetIp = a; break; }
            }
            if (targetIp == null)
                throw new InvalidOperationException("Не удалось найти IPv4-адрес назначения.");
            IPEndPoint target = new IPEndPoint(targetIp, targetPort);
            if (listenHost == targetIp.ToString() && listenPort == targetPort)
                throw new ArgumentException("Вход и выход совпадают: это создаст петлю.");
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                s.ReceiveBufferSize = 1024 * 1024;
                s.Bind(new IPEndPoint(IPAddress.Parse(listenHost), listenPort));
                s.Blocking = false;
            }
            catch (Exception)
            {
                s.Close();
                throw;
            }
            stopEvent.Reset();
            lock (lockObj)
            {
                this.delayMs = delayMs;
                generation += 1;
                received = 0; sent = 0; dropped = 0; queued = 0; bytes = 0;
                error = "";
                running = true;
                this.target = target;
            }
            thread = new Thread(() => Run(s, target));
            thread.IsBackground = true;
            thread.Start();
        }

        public void SetDelay(int value)
        {
            lock (lockObj) { delayMs = value; generation += 1; }
        }

        public void Clear()
        {
            lock (lockObj) { generation += 1; }
        }

        public void Stop()
        {
            stopEvent.Set();
            Thread t = thread;
            if (t != null) t.Join(2000);
            IPEndPoint tgt = target;
            if (tgt != null) SendNeutral(tgt);
        }

        static readonly string[] NeutralNames = { "JawOpen", "Viseme", "MouthOpen", "MouthX", "MouthY",
            "MouthShape0", "MouthShape1", "MouthShape2", "MouthShape3", "MouthShape4" };

        // OSC message: /avatar/parameters/<name>\0 (padded to 4), ",f\0" (padded to 4), float 0.0.
        static byte[] BuildNeutral(string name)
        {
            string addr = "/avatar/parameters/" + name;
            byte[] a = System.Text.Encoding.ASCII.GetBytes(addr);
            int na = (a.Length + 4) / 4 * 4; // address + null, padded to 4
            byte[] msg = new byte[na + 4 + 4];
            a.CopyTo(msg, 0);
            msg[a.Length] = 0; // null terminator
            byte[] t = System.Text.Encoding.ASCII.GetBytes(",f");
            t.CopyTo(msg, na);
            msg[na + t.Length] = 0; // null terminator in type
            // float 0.0 = four zero bytes, already zero
            return msg;
        }

        void SendNeutral(IPEndPoint tgt)
        {
            try
            {
                Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    foreach (string name in NeutralNames)
                        s.SendTo(BuildNeutral(name), tgt);
                }
                finally
                {
                    s.Close();
                }
            }
            catch (Exception)
            {
                // VRChat may be closed; send errors are not fatal to stopping.
            }
        }

        public Snapshot Snapshot()
        {
            lock (lockObj)
            {
                Snapshot s = new Snapshot();
                s.Received = received;
                s.Sent = sent;
                s.Dropped = dropped;
                s.Queued = queued;
                s.Bytes = bytes;
                s.Error = error;
                s.Running = running;
                return s;
            }
        }

        static bool IsBundle(byte[] p)
        {
            if (p.Length < 8) return false;
            return p[0] == (byte)'#' && p[1] == (byte)'b' && p[2] == (byte)'u' && p[3] == (byte)'n'
                && p[4] == (byte)'d' && p[5] == (byte)'l' && p[6] == (byte)'e' && p[7] == 0;
        }

        void Run(Socket s, IPEndPoint target)
        {
            Queue<Packet> queue = new Queue<Packet>();
            long totalBytes = 0;
            long gen = -1;
            byte[] buffer = new byte[65535];
            try
            {
                while (!stopEvent.WaitOne(0))
                {
                    double wait;
                    // Serialize generation checks and sends, so a changed delay never
                    // drains an old backlog. All queue operations belong to this thread.
                    lock (lockObj)
                    {
                        if (gen != generation)
                        {
                            dropped += queue.Count;
                            queue.Clear();
                            totalBytes = 0;
                            gen = generation;
                        }
                        double now = clock.Elapsed.TotalSeconds;
                        int n = Math.Min(queue.Count, 512);
                        for (int i = 0; i < n; i++)
                        {
                            if (queue.Peek().Time > now) break;
                            Packet p = queue.Dequeue();
                            totalBytes -= p.Payload.Length;
                            try
                            {
                                s.SendTo(p.Payload, target);
                                sent += 1;
                            }
                            catch (SocketException exc)
                            {
                                dropped += 1;
                                error = exc.Message;
                            }
                        }
                        queued = queue.Count;
                        bytes = totalBytes;
                        if (queue.Count > 0)
                        {
                            double head = queue.Peek().Time - clock.Elapsed.TotalSeconds;
                            if (head < 0) head = 0;
                            wait = Math.Min(0.01, head);
                        }
                        else wait = 0.01;
                    }
                    List<Socket> readList = new List<Socket>();
                    readList.Add(s);
                    Socket.Select(readList, null, null, (int)(wait * 1000000));
                    if (readList.Count > 0)
                    {
                        for (int i = 0; i < 512; i++)
                        {
                            int len;
                            try
                            {
                                len = s.Receive(buffer);
                            }
                            catch (SocketException exc)
                            {
                                if (exc.SocketErrorCode == SocketError.WouldBlock) break;
                                if (exc.SocketErrorCode == SocketError.ConnectionReset)
                                {
                                    // Windows may report an ICMP unreachable when VRChat is closed.
                                    lock (lockObj)
                                        error = "Получен UDP/ICMP отказ. Проверьте, запущен ли VRChat и верен ли порт.";
                                    break;
                                }
                                throw;
                            }
                            byte[] payload = new byte[len];
                            Array.Copy(buffer, payload, len);
                            lock (lockObj)
                            {
                                if (gen != generation)
                                {
                                    dropped += queue.Count;
                                    queue.Clear();
                                    totalBytes = 0;
                                    gen = generation;
                                }
                                received += 1;
                                // Accept OSC messages and bundles, reject arbitrary UDP traffic.
                                if (len == 0 || !(payload[0] == (byte)'/' || IsBundle(payload)))
                                {
                                    dropped += 1;
                                    continue;
                                }
                                if (queue.Count >= MaxQueuePackets || totalBytes + len > MaxQueueBytes)
                                {
                                    dropped += 1;
                                    error = "Очередь заполнена: пакет пропущен. Уменьшите задержку или поток.";
                                    continue;
                                }
                                double deadline = clock.Elapsed.TotalSeconds + delayMs / 1000.0;
                                queue.Enqueue(new Packet(deadline, payload));
                                totalBytes += len;
                                queued = queue.Count;
                                bytes = totalBytes;
                            }
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                lock (lockObj) error = exc.Message;
            }
            finally
            {
                s.Close();
                lock (lockObj)
                {
                    running = false;
                    queued = 0;
                    bytes = 0;
                }
            }
        }
    }
}
