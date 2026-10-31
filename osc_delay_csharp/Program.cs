// WinForms GUI — port of osc_delay.py App class. C# 5, .NET Framework 4 ( штатный csc).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OscDelay
{
    public class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(0x25, 0x25, 0x25);
        static readonly Color Field = Color.FromArgb(0x35, 0x35, 0x35);
        static readonly Color TextColor = Color.FromArgb(0xee, 0xee, 0xee);
        static readonly Color Accent = Color.FromArgb(0x80, 0xbf, 0xff);
        static readonly Color ErrColor = Color.FromArgb(0xff, 0xbb, 0x88);

        public DelayProxy proxy = new DelayProxy();
        TextBox listenPortBox, targetHostBox, targetPortBox, delayBox;
        TrackBar scale;
        Button startButton, stopButton;
        Label appliedLabel, statusLabel, countsLabel, errorLabel;
        TextBox helpBox;
        System.Windows.Forms.Timer debounceTimer, refreshTimer;
        int applied = 1000;
        bool scaleGuard;
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        public Icon blueIcon, grayIcon, trayIconCurrent;
        public bool trayVisible, quitting, balloonDone;
        public int balloonCount;

        public MainForm()
        {
            Text = "OSC Delay — задержка мимики для VRChat";
            ClientSize = new Size(820, 790);
            MinimumSize = new Size(700, 650);
            BackColor = Bg;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 10);
            DoubleBuffered = true;
            Application.EnableVisualStyles();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            blueIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            grayIcon = MakeGrayIcon();
            trayIconCurrent = blueIcon;
            tray = new NotifyIcon();
            tray.Icon = blueIcon;
            tray.Text = "OSC Delay — задержка мимики для VRChat";
            tray.Visible = true;
            trayVisible = true;
            trayMenu = new ContextMenuStrip();
            ToolStripItem miShow = trayMenu.Items.Add("Показать");
            miShow.Click += delegate { ShowFromTray(); };
            ToolStripItem miClear = trayMenu.Items.Add("Очистить очередь");
            miClear.Click += delegate { proxy.Clear(); };
            ToolStripItem miStop = trayMenu.Items.Add("Остановить");
            miStop.Click += delegate { proxy.Stop(); };
            ToolStripItem miExit = trayMenu.Items.Add("Выход");
            miExit.Click += delegate { ExitApp(); };
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { ShowFromTray(); };

            Dictionary<string, string> saved = Settings.Load();
            string listen = Get(saved, "listen_port", "9015");
            string host = Get(saved, "target_host", "127.0.0.1");
            string tport = Get(saved, "target_port", "9000");
            int initial = 1000;
            string d = Get(saved, "delay_ms", "1000");
            int v;
            if (int.TryParse(d, out v)) initial = Math.Max(0, Math.Min(10000, v));
            applied = initial;

            Label head = new Label();
            head.Text = "Задержка мимики → голос после войсченджера";
            head.Font = new Font("Segoe UI", 17, FontStyle.Bold);
            head.AutoSize = true;
            head.ForeColor = TextColor;
            head.Location = new Point(20, 20);
            Controls.Add(head);

            Label scheme = new Label();
            scheme.Text = "VRCFaceTracking → 127.0.0.1:9015 → этот скрипт → 127.0.0.1:9000 → VRChat";
            scheme.AutoSize = false;
            scheme.Size = new Size(780, 34);
            scheme.Location = new Point(20, 55);
            scheme.ForeColor = TextColor;
            scheme.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(scheme);

            GroupBox network = DarkGroup("Порты и адрес назначения");
            network.Location = new Point(20, 100);
            network.Size = new Size(780, 130);
            network.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(network);
            AddRow(network, 0, "Входной UDP-порт скрипта:", out listenPortBox, listen);
            AddRow(network, 1, "IP компьютера с VRChat:", out targetHostBox, host);
            AddRow(network, 2, "Входной UDP-порт VRChat:", out targetPortBox, tport);
            Label note = new Label();
            note.Text = "Скрипт принимает только локально: 127.0.0.1. Порт 9001 не занимает.";
            note.AutoSize = false;
            note.Size = new Size(756, 20);
            note.Location = new Point(12, 104);
            note.ForeColor = TextColor;
            network.Controls.Add(note);

            GroupBox delaybox = DarkGroup("Задержка — миллисекунды (1000 мс = 1 секунда)");
            delaybox.Location = new Point(20, 238);
            delaybox.Size = new Size(780, 124);
            delaybox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(delaybox);
            scale = new TrackBar();
            scale.Minimum = 0;
            scale.Maximum = 10000;
            scale.TickFrequency = 500;
            scale.Value = initial;
            scale.Size = new Size(756, 45);
            scale.Location = new Point(12, 22);
            scale.BackColor = Bg;
            scale.Scroll += OnScale;
            delaybox.Controls.Add(scale);
            delayBox = new TextBox();
            delayBox.Text = initial.ToString();
            delayBox.Width = 60;
            delayBox.Location = new Point(12, 72);
            delayBox.BackColor = Field;
            delayBox.ForeColor = Color.White;
            delayBox.KeyUp += OnKeyUp;
            delayBox.KeyDown += OnKeyDown;
            delayBox.Leave += OnLeave;
            delaybox.Controls.Add(delayBox);
            Button apply = new Button();
            apply.Text = "Применить";
            apply.Location = new Point(80, 71);
            apply.Size = new Size(90, 26);
            apply.Click += delegate { ApplyDelay(false); };
            delaybox.Controls.Add(apply);
            appliedLabel = new Label();
            appliedLabel.Text = "Применено: " + initial + " мс";
            appliedLabel.AutoSize = true;
            appliedLabel.ForeColor = TextColor;
            appliedLabel.Location = new Point(180, 75);
            delaybox.Controls.Add(appliedLabel);
            Label hint = new Label();
            hint.Text = "При изменении задержки очередь очищается. Во время подбора возможна пауза; это нормально.";
            hint.AutoSize = false;
            hint.Size = new Size(756, 20);
            hint.Location = new Point(12, 98);
            hint.ForeColor = TextColor;
            delaybox.Controls.Add(hint);

            startButton = new Button();
            startButton.Text = "▶ Запустить";
            startButton.Location = new Point(20, 370);
            startButton.Size = new Size(110, 28);
            startButton.Click += delegate { Start(); };
            Controls.Add(startButton);
            stopButton = new Button();
            stopButton.Text = "■ Остановить";
            stopButton.Location = new Point(138, 370);
            stopButton.Size = new Size(110, 28);
            stopButton.Enabled = false;
            stopButton.Click += delegate { proxy.Stop(); SetRunning(false); Save(); };
            Controls.Add(stopButton);
            Button clear = new Button();
            clear.Text = "Очистить очередь";
            clear.Location = new Point(256, 370);
            clear.Size = new Size(140, 28);
            clear.Click += delegate { proxy.Clear(); };
            Controls.Add(clear);

            statusLabel = new Label();
            statusLabel.Text = "Остановлено. Настройте отправку VRCFaceTracking на порт 9015.";
            statusLabel.AutoSize = false;
            statusLabel.Size = new Size(780, 40);
            statusLabel.Location = new Point(20, 408);
            statusLabel.ForeColor = TextColor;
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(statusLabel);

            countsLabel = new Label();
            countsLabel.AutoSize = false;
            countsLabel.Size = new Size(780, 24);
            countsLabel.Location = new Point(20, 450);
            countsLabel.ForeColor = TextColor;
            countsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(countsLabel);

            errorLabel = new Label();
            errorLabel.AutoSize = false;
            errorLabel.Size = new Size(780, 40);
            errorLabel.Location = new Point(20, 476);
            errorLabel.ForeColor = ErrColor;
            errorLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(errorLabel);

            helpBox = new TextBox();
            helpBox.Multiline = true;
            helpBox.ReadOnly = true;
            helpBox.BackColor = Color.FromArgb(0x1d, 0x1d, 0x1d);
            helpBox.ForeColor = Color.FromArgb(0xdd, 0xdd, 0xdd);
            helpBox.ScrollBars = ScrollBars.Vertical;
            helpBox.Location = new Point(20, 522);
            helpBox.Size = new Size(780, 248);
            helpBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            helpBox.Text = ("ИНСТРУКЦИЯ\n"
                + "1. Включите OSC в VRChat. При стандартной конфигурации VRChat принимает на 9000.\n"
                + "2. В VRCFaceTracking укажите отправку: IP 127.0.0.1, порт 9015.\n"
                + "3. Отключите другие прямые отправки тех же выражений на 9000. Если реальный отправитель — VRC Direct внутри Cymple, перенаправляйте именно его, а не добавляйте второй параллельный маршрут.\n"
                + "4. Выберите задержку и нажмите «Запустить». Начните, например, с 500 мс и подбирайте по записи аватара с обработанным голосом.\n"
                + "5. Крестик окна сворачивает прокси в трей — он продолжает работать. Выход — через меню трея («Выход»). Повторный запуск открывает существующее окно.\n"
                + "6. При «Остановить» и «Выход» очередь отбрасывается и в VRChat отправляются нейтральные значения — лицо возвращается в нейтраль.\n\n"
                + "ВАЖНО\n"
                + "Скрипт задерживает ВСЕ OSC-пакеты, поступившие на его вход, а не только рот. Направляйте сюда только поток выражений: не голову, контроллеры, чат и служебные команды.\n"
                + "Он не убирает конфликт Viseme и фейстрекинга и не меняет задержку голоса. Сначала проверьте двойное управление ртом.\n"
                + "OSCQuery/mDNS не реализованы: настройте фиксированную отправку на 9015; автоматическое обнаружение может обходить скрипт. Если счётчик приёма равен нулю — проверьте маршрут.\n"
                + "Задержка приблизительная: точность зависит от Windows, нагрузки и UDP. Содержимое пакетов не меняется, включая временные метки OSC bundle. Подробности и ограничения — в README_RU.md.").Replace("\n", "\r\n");
            Controls.Add(helpBox);

            debounceTimer = new System.Windows.Forms.Timer();
            debounceTimer.Interval = 300;
            debounceTimer.Tick += delegate { debounceTimer.Stop(); ApplyDelay(true); };

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 250;
            refreshTimer.Tick += delegate { RefreshUi(); };
            refreshTimer.Start();

            FormClosing += delegate (object sender, FormClosingEventArgs e)
            {
                if (quitting)
                {
                    proxy.Stop();
                    Save();
                }
                else
                {
                    e.Cancel = true;
                    CloseX();
                }
            };
        }

        static string Get(Dictionary<string, string> d, string key, string def)
        {
            return d.ContainsKey(key) ? d[key] : def;
        }

        // 16x16 dark square with a gray clock: stopped-state tray icon.
        static Icon MakeGrayIcon()
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillRectangle(new SolidBrush(Bg), 0, 0, 16, 16);
                Color gray = Color.FromArgb(128, 128, 128);
                Pen pen = new Pen(gray, 1.5f);
                g.DrawEllipse(pen, 3, 3, 10, 10);
                g.DrawLine(pen, 8, 8, 8, 5);
                g.DrawLine(pen, 8, 8, 11, 9);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public void CloseX()
        {
            if (!balloonDone)
            {
                tray.ShowBalloonTip(4000, "OSC Delay",
                    "Прокси работает в трее; выход — через меню трея", ToolTipIcon.Info);
                balloonDone = true;
                balloonCount++;
            }
            trayVisible = true;
            Hide();
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
        }

        public void ExitApp()
        {
            quitting = true;
            proxy.Stop();
            Save();
            tray.Visible = false;
            Close();
        }

        GroupBox DarkGroup(string title)
        {
            GroupBox g = new GroupBox();
            g.Text = title;
            g.BackColor = Bg;
            g.ForeColor = Accent;
            return g;
        }

        void AddRow(GroupBox g, int row, string label, out TextBox box, string value)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.ForeColor = TextColor;
            l.Location = new Point(12, 26 + row * 26);
            g.Controls.Add(l);
            box = new TextBox();
            box.Text = value;
            box.Width = 160;
            box.Location = new Point(212, 24 + row * 26);
            box.BackColor = Field;
            box.ForeColor = Color.White;
            g.Controls.Add(box);
        }

        void OnScale(object sender, EventArgs e)
        {
            // Setting TrackBar.Value in code fires Scroll too; ignore it, so
            // ApplyDelay -> Scroll -> ApplyDelay never loops and repaints.
            if (scaleGuard) return;
            delayBox.Text = ((int)scale.Value).ToString();
            ScheduleApply();
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
            ScheduleApply();
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return) ApplyDelay(false);
        }

        void OnLeave(object sender, EventArgs e)
        {
            ApplyDelay(false);
        }

        void ScheduleApply()
        {
            debounceTimer.Stop();
            debounceTimer.Start();
        }

        bool ApplyDelay(bool quiet)
        {
            int value;
            if (!int.TryParse(delayBox.Text, out value) || value < 0 || value > 10000)
            {
                if (!quiet)
                    MessageBox.Show("Введите целое число от 0 до 10000 мс.", "Задержка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            scale.Value = value;
            scaleGuard = true;
            scale.Value = value;
            scaleGuard = false;
            if (value != applied)
            {
                proxy.SetDelay(value);
                applied = value;
            }
            appliedLabel.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Применено: {0} мс ({1:F3} с)", value, value / 1000.0);
            return true;
        }

        void Start()
        {
            if (!ApplyDelay(false)) return;
            int lp, tp;
            bool okL = int.TryParse(listenPortBox.Text, out lp);
            bool okT = int.TryParse(targetPortBox.Text, out tp);
            string msg = null;
            if (!okL || !okT)
                msg = "Порты должны быть от 1 до 65535.";
            else if (!(lp >= 1 && lp <= 65535 && tp >= 1 && tp <= 65535))
                msg = "Порты должны быть от 1 до 65535.";
            else if (lp == 9001)
                msg = "Не занимайте порт 9001: он обычно нужен для обратных сообщений VRChat.";
            string host = targetHostBox.Text.Trim();
            if (msg == null && host == "")
                msg = "Укажите адрес назначения.";
            if (msg != null)
            {
                StartFail(msg);
                return;
            }
            try
            {
                proxy.Start("127.0.0.1", lp, host, tp, applied);
                Save();
            }
            catch (Exception exc)
            {
                StartFail(exc.Message);
                return;
            }
            SetRunning(true);
        }

        void StartFail(string msg)
        {
            MessageBox.Show(msg + "\n\nПроверьте порты. Возможно, входной порт занят другим приложением.",
                "Не удалось запустить", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        void SetRunning(bool running)
        {
            // Assign only on change: re-assigning Enabled every timer tick repaints
            // the controls and shows as flicker.
            if (startButton.Enabled == !running) return;
            startButton.Enabled = !running;
            stopButton.Enabled = running;
            listenPortBox.Enabled = !running;
            targetHostBox.Enabled = !running;
            targetPortBox.Enabled = !running;
        }

        void SetText(Label label, string value)
        {
            // Assign only on change; unconditional re-assignment every tick repaints.
            if (label.Text != value) label.Text = value;
        }

        public void RefreshUi()
        {
            Snapshot s = proxy.Snapshot();
            SetRunning(s.Running);
            Icon wanted = s.Running ? blueIcon : grayIcon;
            if (trayIconCurrent != wanted)
            {
                tray.Icon = wanted;
                trayIconCurrent = wanted;
            }
            if (s.Running)
                SetText(statusLabel, "Работает. " + (s.Received == 0
                    ? "Ожидание OSC-пакетов: проверьте отправку на входной порт."
                    : "Пакеты приняты; отправка UDP не подтверждает, что VRChat применил выражения."));
            else
                SetText(statusLabel, "Остановлено. Очередь не воспроизводится.");
            SetText(countsLabel, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Принято: {0}   Отправлено: {1}   В очереди: {2}   Пропущено/очищено: {3}   Память: {4:F1} МБ",
                s.Received, s.Sent, s.Queued, s.Dropped, s.Bytes / 1048576.0));
            SetText(errorLabel, s.Error);
        }

        void Save()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d["listen_port"] = listenPortBox.Text;
            d["target_host"] = targetHostBox.Text;
            d["target_port"] = targetPortBox.Text;
            d["delay_ms"] = applied.ToString();
            Settings.Save(d);
        }

        [STAThread]
        static void Main()
        {
            bool created;
            Mutex single = new Mutex(true, "OSC_Delay_RU_Single_Instance", out created);
            if (!created)
            {
                // Second launch: show the existing window (restore if minimized),
                // then exit 0. The first instance keeps the tray and the proxy.
                IntPtr h = FindWindowW(null, "OSC Delay — задержка мимики для VRChat");
                if (h != IntPtr.Zero)
                {
                    ShowWindow(h, 9); // SW_RESTORE
                    // Windows blocks SetForegroundWindow for a process that does not
                    // own the foreground (foreground lock). Attach to the foreground
                    // window's thread, then the restore call is accepted.
                    IntPtr fg = GetForegroundWindow();
                    uint fgTid = GetWindowThreadProcessId(fg, IntPtr.Zero);
                    uint myTid = GetCurrentThreadId();
                    bool attached = AttachThreadInput(myTid, fgTid, true);
                    BringWindowToTop(h);
                    SetForegroundWindow(h);
                    if (attached) AttachThreadInput(myTid, fgTid, false);
                }
                return;
            }
            GC.KeepAlive(single); // keep the mutex for the process lifetime
            Application.Run(new MainForm());
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr FindWindowW(string cls, string name);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr h, int cmd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr procId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint a, uint b, bool attach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool BringWindowToTop(IntPtr h);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr h);
    }

    static class Settings
    {
        static string FilePath()
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrEmpty(local))
                local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(Path.Combine(local, "OSC_Delay_RU"), "settings.json");
        }

        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            try
            {
                string text = File.ReadAllText(FilePath(), Encoding.UTF8);
                string[] keys = { "listen_port", "target_host", "target_port", "delay_ms" };
                foreach (string key in keys)
                {
                    Match m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*(\"([^\"]*)\"|(-?[0-9]+))");
                    if (m.Success)
                        d[key] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                }
            }
            catch (Exception)
            {
            }
            return d;
        }

        public static void Save(Dictionary<string, string> d)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath()));
                StringBuilder sb = new StringBuilder();
                sb.Append("{\n  \"listen_port\": \"");
                sb.Append(Escape(d["listen_port"]));
                sb.Append("\",\n  \"target_host\": \"");
                sb.Append(Escape(d["target_host"]));
                sb.Append("\",\n  \"target_port\": \"");
                sb.Append(Escape(d["target_port"]));
                sb.Append("\",\n  \"delay_ms\": ");
                sb.Append(d["delay_ms"]);
                sb.Append("}");
                File.WriteAllText(FilePath(), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
