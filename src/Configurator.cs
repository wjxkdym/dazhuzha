using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;

[assembly: System.Reflection.AssemblyTitle("大猪扎配置器")]
[assembly: System.Reflection.AssemblyProduct("大猪扎")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace PigModSuite
{
    internal sealed class Server
    {
        internal readonly string Game, Root, ConfigFile, Url, SessionFile;
        private readonly string token, authority;
        private readonly TcpListener listener;
        private readonly Dictionary<string, bool> ids = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly HashSet<string> previews = new HashSet<string>(StringComparer.Ordinal);
        private readonly object gate = new object();
        private readonly string[] modules = { "cards", "heroes", "events", "monsters", "storage", "mainmenu" };
        private volatile bool stopped;
        internal event Action Quit;
        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024, RecursionLimit = 30 }; }
        internal static Dictionary<string, object> Parse(string s) { return Json().Deserialize<Dictionary<string, object>>(s); }
        internal static string Encode(object o) { return Json().Serialize(o); }
        internal static string Hash(byte[] b) { using (SHA256 h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
        internal Server(string root, string game, string sessionFile)
        {
            Root = root; Game = Path.GetFullPath(game); SessionFile = sessionFile;
            if (!File.Exists(Path.Combine(Game, "TheBazaar.exe"))) throw new IOException("请选择包含 TheBazaar.exe 的游戏目录。");
            ConfigFile = Path.Combine(Game, "BepInEx", "config", "PigModSuite", "settings.json");
            Dictionary<string, object> catalog = Parse(File.ReadAllText(Path.Combine(Root, "catalog.json")));
            foreach (Dictionary<string, object> item in (IEnumerable)catalog["items"])
            {
                ids.Add((string)item["id"], (bool)item["available"]);
                foreach (Dictionary<string, object> part in (IEnumerable)item["parts"])
                    foreach (string field in new[] { "original", "pig" })
                        if (part[field] is string) previews.Add((string)part[field]);
            }
            byte[] secret = new byte[24]; using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
            token = BitConverter.ToString(secret).Replace("-", "").ToLowerInvariant();
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(32);
            authority = "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            Url = "http://" + authority + "/" + token + "/";
        }
        internal void Start()
        {
            File.WriteAllText(SessionFile, Encode(new { url = Url, pid = Process.GetCurrentProcess().Id, game = Game }), new UTF8Encoding(false));
            Thread thread = new Thread(delegate() {
                while (!stopped)
                {
                    try { TcpClient client = listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(delegate { Serve(client); }); }
                    catch (SocketException) { if (!stopped) throw; }
                }
            }); thread.IsBackground = true; thread.Start();
        }
        internal void Stop() { stopped = true; listener.Stop(); try { File.Delete(SessionFile); } catch { } }
        private string Revision() { return File.Exists(ConfigFile) ? Hash(File.ReadAllBytes(ConfigFile)) : "missing"; }
        private bool Legacy(string filename, string key)
        {
            string file = Path.Combine(Game, "BepInEx", "config", filename);
            if (!File.Exists(file)) return true;
            bool general = false;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) general = line == "[General]";
                if (!general || !line.StartsWith(key + " ", StringComparison.Ordinal) && !line.StartsWith(key + "=", StringComparison.Ordinal)) continue;
                int equal = line.IndexOf('='); bool value;
                if (equal >= 0 && bool.TryParse(line.Substring(equal + 1).Trim(), out value)) return value;
            }
            return true;
        }
        private Dictionary<string, object> Defaults(bool migrate)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            foreach (string module in modules) m[module] = true;
            if (migrate)
            {
                m["cards"] = Legacy("local.pigcardart.cfg", "Enabled");
                m["heroes"] = Legacy("local.pigportraitart.cfg", "HeroBaseSkins");
                m["events"] = m["monsters"] = Legacy("local.pigencounterart.cfg", "Enabled");
                m["storage"] = Legacy("local.pigstorageart.cfg", "Enabled");
                m["mainmenu"] = Legacy("local.pigmainmenuart.cfg", "Enabled");
            }
            return new Dictionary<string, object> { { "schemaVersion", 1 }, { "modules", m }, { "items", new Dictionary<string, object>() } };
        }
        private Dictionary<string, object> Normalize(Dictionary<string, object> data)
        {
            if (data == null || !data.ContainsKey("schemaVersion") || !(data["schemaVersion"] is int) || (int)data["schemaVersion"] != 1) throw new ArgumentException("配置版本不支持。");
            object raw;
            Dictionary<string, object> m = data.TryGetValue("modules", out raw) ? raw as Dictionary<string, object> : null;
            Dictionary<string, object> i = data.TryGetValue("items", out raw) ? raw as Dictionary<string, object> : null;
            if (m == null || i == null || m.Count != modules.Length) throw new ArgumentException("配置缺少模块或单项设置。");
            Dictionary<string, object> clean = new Dictionary<string, object>();
            foreach (string module in modules)
                if (!m.ContainsKey(module) || !(m[module] is bool)) throw new ArgumentException("模块设置必须是布尔值：" + module);
            foreach (KeyValuePair<string, object> pair in i)
            {
                if (!ids.ContainsKey(pair.Key)) throw new ArgumentException("配置包含未知项目：" + pair.Key);
                if (!(pair.Value is bool)) throw new ArgumentException("项目设置必须是布尔值：" + pair.Key);
                if (!ids[pair.Key] && (bool)pair.Value) throw new ArgumentException("无法启用未开放的概念卡：" + pair.Key);
                // Only exceptions are stored, keeping profiles small and forward compatible.
                if (ids[pair.Key] && !(bool)pair.Value) clean[pair.Key] = false;
            }
            return new Dictionary<string, object> { { "schemaVersion", 1 }, { "modules", m }, { "items", clean } };
        }
        private object State()
        {
            lock (gate)
            {
                string warning = ""; Dictionary<string, object> current;
                try { current = File.Exists(ConfigFile) ? Normalize(Parse(File.ReadAllText(ConfigFile))) : Defaults(true); }
                catch (Exception ex) { current = Defaults(true); warning = "现有配置无法读取，游戏会保留原图。请重新选择并保存；损坏文件会先备份。" + ex.Message; }
                return new { settings = current, defaults = Defaults(false), revision = Revision(), gamePath = Game, configPath = ConfigFile,
                    gameRunning = Process.GetProcessesByName("TheBazaar").Length > 0, warning = warning, version = "1.1.0" };
            }
        }
        private object Save(Dictionary<string, object> request)
        {
            Dictionary<string, object> clean = Normalize(request.ContainsKey("settings") ? request["settings"] as Dictionary<string, object> : null);
            lock (gate)
            {
                if (!request.ContainsKey("revision") || !string.Equals(request["revision"] as string, Revision(), StringComparison.Ordinal)) throw new ConflictException();
                clean["updatedAt"] = DateTimeOffset.Now.ToString("o");
                string folder = Path.GetDirectoryName(ConfigFile); Directory.CreateDirectory(folder);
                string temp = Path.Combine(folder, "settings." + Guid.NewGuid().ToString("N") + ".tmp");
                string backup = null;
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(Encode(clean));
                    using (FileStream file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                    if (File.Exists(ConfigFile))
                    {
                        string backups = Path.Combine(folder, "backups"); Directory.CreateDirectory(backups);
                        backup = Path.Combine(backups, "settings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
                        File.Replace(temp, ConfigFile, backup);
                    }
                    else File.Move(temp, ConfigFile);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                return new { state = State(), backup = backup };
            }
        }
        private sealed class ConflictException : Exception { }
        private static void Reply(NetworkStream stream, int code, string mime, byte[] bytes)
        {
            string header = "HTTP/1.1 " + code + " " + (code == 200 ? "OK" : "Error") + "\r\nContent-Type: " + mime + "\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; img-src 'self' blob:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'\r\n\r\n";
            byte[] h = Encoding.ASCII.GetBytes(header); stream.Write(h, 0, h.Length); stream.Write(bytes, 0, bytes.Length);
        }
        private static void JsonReply(NetworkStream s, int code, object value) { Reply(s, code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(Encode(value))); }
        private void Serve(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 8000; client.SendTimeout = 15000;
                using (NetworkStream stream = client.GetStream())
                {
                    try
                    {
                        List<byte> head = new List<byte>();
                        while (head.Count < 16384)
                        {
                            int b = stream.ReadByte(); if (b < 0) return; head.Add((byte)b); int n = head.Count;
                            if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break;
                        }
                        if (head.Count >= 16384) throw new ArgumentException("请求头过长。");
                        string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                        string[] first = lines[0].Split(' '); if (first.Length != 3) throw new ArgumentException("无效请求。");
                        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int n = 1; n < lines.Length; n++) { int colon = lines[n].IndexOf(':'); if (colon > 0) headers.Add(lines[n].Substring(0, colon), lines[n].Substring(colon + 1).Trim()); }
                        if (!headers.ContainsKey("Host") || headers["Host"] != authority || !first[1].StartsWith("/" + token + "/", StringComparison.Ordinal)) { JsonReply(stream, 403, new { error = "访问被拒绝。请从配置器启动入口打开。" }); return; }
                        string route = first[1].Substring(token.Length + 2);
                        if (first[0] == "GET")
                        {
                            if (route == "api/state") { JsonReply(stream, 200, State()); return; }
                            string file = null, mime = "application/octet-stream";
                            if (route == "" || route == "index.html") { file = Path.Combine(Root, "web", "index.html"); mime = "text/html; charset=utf-8"; }
                            if (route == "app.js") { file = Path.Combine(Root, "web", "app.js"); mime = "text/javascript; charset=utf-8"; }
                            if (route == "style.css") { file = Path.Combine(Root, "web", "style.css"); mime = "text/css; charset=utf-8"; }
                            if (route == "dazhuzha-logo.png" || route == "dazhuzha-icon.png") { file = Path.Combine(Root, "web", route); mime = "image/png"; }
                            if (route == "catalog.json") { file = Path.Combine(Root, "catalog.json"); mime = "application/json; charset=utf-8"; }
                            if (route.StartsWith("previews/", StringComparison.Ordinal) && previews.Contains(route.Substring(9))) { file = Path.Combine(Root, "previews", route.Substring(9)); mime = "image/webp"; }
                            if (file != null && File.Exists(file)) { Reply(stream, 200, mime, File.ReadAllBytes(file)); return; }
                        }
                        else if (first[0] == "POST")
                        {
                            int length;
                            if (headers.ContainsKey("Transfer-Encoding") || !headers.ContainsKey("Content-Length") || !int.TryParse(headers["Content-Length"], out length) || length < 0 || length > 2 * 1024 * 1024) throw new ArgumentException("配置文件过大或请求格式无效。");
                            byte[] body = new byte[length]; int offset = 0;
                            while (offset < length) { int n = stream.Read(body, offset, length - offset); if (n == 0) throw new IOException("请求中断。"); offset += n; }
                            if (!headers.ContainsKey("X-Pig-Token") || headers["X-Pig-Token"] != token || (headers.ContainsKey("Origin") && headers["Origin"] != "http://" + authority)) { JsonReply(stream, 403, new { error = "请求来源不匹配。" }); return; }
                            Dictionary<string, object> request = Parse(Encoding.UTF8.GetString(body));
                            if (route == "api/save") { JsonReply(stream, 200, Save(request)); return; }
                            if (route == "api/validate") { JsonReply(stream, 200, Normalize(request)); return; }
                            if (route == "api/folder") { Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)); Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(ConfigFile) + "\""); JsonReply(stream, 200, new { ok = true }); return; }
                            if (route == "api/quit") { JsonReply(stream, 200, new { ok = true }); if (Quit != null) Quit(); return; }
                        }
                        JsonReply(stream, 404, new { error = "未找到。" });
                    }
                    catch (ConflictException) { JsonReply(stream, 409, new { error = "配置已被另一个窗口或程序修改。请先重新载入，再保存。" }); }
                    catch (Exception ex) { try { JsonReply(stream, 400, new { error = ex.Message }); } catch { } }
                }
            }
        }
    }
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Server server = null; NotifyIcon tray = null; Mutex mutex = null;
            try
            {
                string root = AppDomain.CurrentDomain.BaseDirectory, game = null; bool headless = false, noBrowser = false;
                for (int i = 0; i < args.Length; i++) { if (args[i] == "--game" && i + 1 < args.Length) game = args[++i]; if (args[i] == "--headless") headless = true; if (args[i] == "--no-browser") noBrowser = true; }
                string install = Path.Combine(root, "installation.json");
                if (game == null && File.Exists(install)) game = (string)Server.Parse(File.ReadAllText(install))["gamePath"];
                if (game == null && File.Exists(Path.Combine(root, "..", "TheBazaar.exe"))) game = Path.GetFullPath(Path.Combine(root, ".."));
                if (game == null)
                {
                    using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择包含 TheBazaar.exe 的游戏目录" })
                    { if (dialog.ShowDialog() != DialogResult.OK) return; game = dialog.SelectedPath; }
                }
                string key = Server.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(game).ToLowerInvariant())).Substring(0, 16);
                string sessions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PigModSuite"); Directory.CreateDirectory(sessions);
                string session = Path.Combine(sessions, key + ".json"); bool fresh;
                mutex = new Mutex(true, "Local\\PigModSuite-" + key, out fresh);
                if (!fresh) { if (!noBrowser && File.Exists(session)) Process.Start((string)Server.Parse(File.ReadAllText(session))["url"]); return; }
                server = new Server(root, game, session);
                using (Form dispatch = new Form())
                {
                    IntPtr handle = dispatch.Handle;
                    server.Quit += delegate { dispatch.BeginInvoke((Action)delegate { Application.ExitThread(); }); };
                    server.Start();
                    if (!headless)
                    {
                        tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application, Text = "大猪扎配置器", Visible = true };
                        ContextMenuStrip menu = new ContextMenuStrip();
                        menu.Items.Add("打开配置器", null, delegate { Process.Start(server.Url); });
                        menu.Items.Add("退出配置器", null, delegate { Application.ExitThread(); }); tray.ContextMenuStrip = menu;
                        tray.DoubleClick += delegate { Process.Start(server.Url); };
                    }
                    if (!noBrowser) Process.Start(server.Url);
                    Application.Run();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "大猪扎配置器", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (tray != null) tray.Dispose(); if (server != null) server.Stop(); if (mutex != null) mutex.Dispose(); }
        }
    }
}
