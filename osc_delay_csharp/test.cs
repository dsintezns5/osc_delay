// Синтетический транспортный тест; VRChat не нужен. Запуск: test_proxy.exe
// Эквивалент test_proxy.py. В этой песочнице UDP loopback 127.0.0.1 блокируется,
// поэтому тест использует реальный NIC-адрес (работает и на 127.0.0.1 дома).
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OscDelay
{
    static class Test
    {
        static int FreePort()
        {
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Bind(new IPEndPoint(IPAddress.Any, 0));
            int p = ((IPEndPoint)s.LocalEndPoint).Port;
            s.Close();
            return p;
        }

        static byte[] Cat(params byte[][] parts)
        {
            int n = 0;
            foreach (byte[] p in parts) n += p.Length;
            byte[] r = new byte[n];
            int k = 0;
            foreach (byte[] p in parts) { p.CopyTo(r, k); k += p.Length; }
            return r;
        }

        static byte[] Be(int v)
        {
            return new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
        }

        static bool Eq(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static byte[] CopyLen(byte[] buf, int len)
        {
            byte[] r = new byte[len];
            Array.Copy(buf, r, len);
            return r;
        }

        // Pad a byte[] with nulls to a multiple of 4 (OSC addresses/types).
        static byte[] Pad4(byte[] a)
        {
            int n = (a.Length + 3) / 4 * 4;
            byte[] r = new byte[n];
            a.CopyTo(r, 0);
            return r;
        }

        static string[] NeutralNames = { "JawOpen", "Viseme", "MouthOpen", "MouthX", "MouthY",
            "MouthShape0", "MouthShape1", "MouthShape2", "MouthShape3", "MouthShape4" };

        // Expected neutral OSC message: /avatar/parameters/<name>\0 (pad 4), ",f\0" (pad 4), float 0.0.
        static byte[] ExpectedNeutral(int i)
        {
            byte[] addr = Cat(Encoding.ASCII.GetBytes("/avatar/parameters/" + NeutralNames[i]), new byte[] { 0 });
            byte[] type = Cat(Encoding.ASCII.GetBytes(",f"), new byte[] { 0 });
            return Cat(Pad4(addr), Pad4(type), new byte[] { 0, 0, 0, 0 });
        }


        static void Main()
        {
            string ip = null;
            foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a.ToString(); break; }
            if (ip == null) { Console.WriteLine("FAIL: no IPv4 address"); return; }
            IPEndPoint ipEP = new IPEndPoint(IPAddress.Parse(ip), 0);
            Socket rx = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            rx.Bind(ipEP);
            rx.ReceiveTimeout = 2000;
            Socket tx = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            int lp = FreePort();
            int rxPort = ((IPEndPoint)rx.LocalEndPoint).Port;
            DelayProxy proxy = new DelayProxy();
            byte[] payload = Cat(Encoding.ASCII.GetBytes("/avatar/parameters/JawOpen"),
                new byte[] { 0, 0, 0 }, Encoding.ASCII.GetBytes(",f"),
                new byte[] { 0, 0, 0x3f, 0x80, 0, 0 });
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                proxy.Start(ip, lp, ip, rxPort, 150);
                tx.SendTo(payload, new IPEndPoint(IPAddress.Parse(ip), lp));
                byte[] buf = new byte[65535];
                int len = rx.Receive(buf);
                double elapsed = sw.Elapsed.TotalSeconds;
                byte[] result = new byte[len];
                Array.Copy(buf, result, len);
                if (!Eq(result, payload)) throw new Exception("payload changed");
                if (!(elapsed >= 0.13 && elapsed < 0.8)) throw new Exception("delay out of band: " + elapsed);
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "PASS: payload not changed, delay {0:F1} ms", elapsed * 1000));

                proxy.SetDelay(400);
                tx.SendTo(payload, new IPEndPoint(IPAddress.Parse(ip), lp));
                Thread.Sleep(80);
                proxy.SetDelay(0);
                byte[] second = Cat(Encoding.ASCII.GetBytes("/test"), new byte[] { 0, 0, 0 },
                    Encoding.ASCII.GetBytes(",i"), new byte[] { 0, 0, 0, 0, 0, 2 });
                tx.SendTo(second, new IPEndPoint(IPAddress.Parse(ip), lp));
                len = rx.Receive(buf);
                result = new byte[len];
                Array.Copy(buf, result, len);
                if (!Eq(result, second)) throw new Exception("second payload changed");
                rx.ReceiveTimeout = 550;
                bool leaked = false;
                try
                {
                    rx.Receive(buf);
                    leaked = true;
                }
                catch (SocketException exc)
                {
                    if (exc.SocketErrorCode != SocketError.TimedOut) throw;
                }
                if (leaked) throw new Exception("old backlog leaked");
                Console.WriteLine("PASS: changing delay clears old queue");

                byte[] bundle = Cat(Encoding.ASCII.GetBytes("#bundle"), new byte[] { 0 },
                    new byte[] { 0, 0, 0, 0, 0, 0, 0 }, new byte[] { 1 }, Be(second.Length), second);
                tx.SendTo(bundle, new IPEndPoint(IPAddress.Parse(ip), lp));
                rx.ReceiveTimeout = 1000;
                len = rx.Receive(buf);
                result = new byte[len];
                Array.Copy(buf, result, len);
                if (!Eq(result, bundle)) throw new Exception("bundle changed");
                Console.WriteLine("PASS: OSC bundle preserved");

                for (int i = 0; i < 20; i++)
                {
                    byte[] order = Cat(Encoding.ASCII.GetBytes("/order"), new byte[] { 0, 0 },
                        Encoding.ASCII.GetBytes(",i"), new byte[] { 0, 0 }, Be(i));
                    tx.SendTo(order, new IPEndPoint(IPAddress.Parse(ip), lp));
                }
                rx.ReceiveTimeout = 2000;
                for (int i = 0; i < 20; i++)
                {
                    len = rx.Receive(buf);
                    int got = (buf[len - 4] << 24) | (buf[len - 3] << 16) | (buf[len - 2] << 8) | buf[len - 1];
                    if (got != i) throw new Exception("packet order broken at " + i + " got " + got);
                }
                Console.WriteLine("PASS: packet ordering");
            }
            finally
            {
                proxy.Stop();
                tx.Close();
                rx.Close();
            }
            if (proxy.Snapshot().Running) throw new Exception("proxy still running");
            // Socket is released and same port can be reused.
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Bind(new IPEndPoint(IPAddress.Parse(ip), lp));
            s.Close();
            Console.WriteLine("PASS: clean stop and input port released");

            // Neutral face: Start a fresh proxy, stop with an empty queue,
            // Stop() must send 10 neutral OSC messages to the target, in order,
            // and nothing after them.
            Socket rx2 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            rx2.Bind(ipEP);
            rx2.ReceiveTimeout = 2000;
            int lp2 = FreePort();
            int rx2Port = ((IPEndPoint)rx2.LocalEndPoint).Port;
            DelayProxy neutralProxy = new DelayProxy();
            neutralProxy.Start(ip, lp2, ip, rx2Port, 150);
            neutralProxy.Stop();
            byte[] buf2 = new byte[65535];
            for (int i = 0; i < 10; i++)
            {
                int len2 = rx2.Receive(buf2);
                byte[] got = CopyLen(buf2, len2);
                if (!Eq(got, ExpectedNeutral(i)))
                    throw new Exception("neutral face wrong at " + i);
            }
            rx2.ReceiveTimeout = 500;
            bool extra = false;
            try
            {
                rx2.Receive(buf2);
                extra = true;
            }
            catch (SocketException exc)
            {
                if (exc.SocketErrorCode != SocketError.TimedOut) throw;
            }
            if (extra) throw new Exception("extra message after neutral face");
            Console.WriteLine("PASS: neutral face sent on stop");

            // VRChat closed: target has no listener. Stop() must swallow send errors
            // and reach Running == false.
            int closed = FreePort();
            DelayProxy closedProxy = new DelayProxy();
            closedProxy.Start(ip, lp2, ip, closed, 150);
            closedProxy.Stop();
            if (closedProxy.Snapshot().Running) throw new Exception("closed-VRChat proxy still running");
            Console.WriteLine("PASS: neutral send tolerates closed VRChat");
            rx2.Close();
        }
    }
}
