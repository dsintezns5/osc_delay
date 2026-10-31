// Console test for tray / X-to-tray / balloon / exit wiring. Build:
// csc /target:exe /main:OscDelay.UiTest /out:ui_test.exe delay_proxy.cs Program.cs ui_test.cs
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OscDelay
{
    static class UiTest
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

        static byte[] Pad4(byte[] a)
        {
            int n = (a.Length + 3) / 4 * 4;
            byte[] r = new byte[n];
            a.CopyTo(r, 0);
            return r;
        }

        static string[] NeutralNames = { "JawOpen", "Viseme", "MouthOpen", "MouthX", "MouthY",
            "MouthShape0", "MouthShape1", "MouthShape2", "MouthShape3", "MouthShape4" };

        static byte[] ExpectedNeutral(int i)
        {
            byte[] addr = Cat(Encoding.ASCII.GetBytes("/avatar/parameters/" + NeutralNames[i]), new byte[] { 0 });
            byte[] type = Cat(Encoding.ASCII.GetBytes(",f"), new byte[] { 0 });
            return Cat(Pad4(addr), Pad4(type), new byte[] { 0, 0, 0, 0 });
        }

        static bool Eq(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        [STAThread]
        static void Main()
        {
            string ip = null;
            foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a.ToString(); break; }
            if (ip == null) { Console.WriteLine("FAIL: no IPv4 address"); return; }

            Socket rx = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            rx.Bind(new IPEndPoint(IPAddress.Parse(ip), 0));
            int rxPort = ((IPEndPoint)rx.LocalEndPoint).Port;

            MainForm f = new MainForm();
            f.Visible = true;
            int lp = FreePort();
            f.proxy.Start(ip, lp, ip, rxPort, 150);
            f.RefreshUi();

            // 1. X: hide to tray, proxy keeps running, first balloon fires.
            f.CloseX();
            if (f.Visible) throw new Exception("window visible after CloseX");
            if (!f.trayVisible) throw new Exception("tray not visible after CloseX");
            if (!f.proxy.Snapshot().Running) throw new Exception("proxy stopped after CloseX");
            if (f.balloonCount != 1) throw new Exception("balloon count " + f.balloonCount);
            if (f.trayIconCurrent != f.blueIcon) throw new Exception("tray icon not blue while running");
            Console.WriteLine("PASS: X-to-tray keeps proxy running and shows balloon once");

            // 2. Double-click tray: window back.
            f.ShowFromTray();
            if (!f.Visible) throw new Exception("window not visible after ShowFromTray");
            Console.WriteLine("PASS: ShowFromTray restores window");

            // 3. Second X: no second balloon.
            f.CloseX();
            if (f.balloonCount != 1) throw new Exception("balloon fired twice: " + f.balloonCount);
            Console.WriteLine("PASS: second CloseX no second balloon");

            // 4. Exit: proxy stops, 10 neutrals arrive on rx.
            f.ExitApp();
            if (f.proxy.Snapshot().Running) throw new Exception("proxy still running after ExitApp");
            rx.ReceiveTimeout = 2000;
            byte[] buf = new byte[65535];
            for (int i = 0; i < 10; i++)
            {
                int len = rx.Receive(buf);
                byte[] got = new byte[len];
                Array.Copy(buf, got, len);
                if (!Eq(got, ExpectedNeutral(i))) throw new Exception("neutral wrong at " + i);
            }
            Console.WriteLine("PASS: ExitApp stops proxy and sends neutrals");

            // 5. Stopped-state icon swap.
            f.RefreshUi();
            if (f.trayIconCurrent == f.blueIcon) throw new Exception("tray icon stayed blue after exit");
            Console.WriteLine("PASS: tray icon swaps to gray on stop");

            rx.Close();
        }
    }
}
