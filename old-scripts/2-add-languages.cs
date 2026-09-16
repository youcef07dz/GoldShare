// ============================================================================
//  GoldShare.cs — Premium Gold/White File Sharing Server (ShareIt-style)
//  + QR code share/scan (pure C# QR encoder, no external libraries)
//  + Multi-language GUI: English / Français / العربية (with RTL)
//  + Persistent settings (GoldShare.ini: port + language)
//  + Single-instance guard (mutex), fixed-size GUI, Explorer-safe folder open
//  Target: .NET Framework 2.0 — Windows XP compatible
//  Compile: C:\WINDOWS\Microsoft.NET\Framework\v2.0.50727\csc.exe
//           /target:winexe /optimize+ /out:GoldShare.exe GoldShare.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GoldShare
{
    // ========================================================================
    //  PERSISTENT SETTINGS  (GoldShare.ini next to the EXE)
    // ========================================================================
    public static class Settings
    {
        public static int Port = 8080;
        public static string Language = "en";

        public static string IniPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GoldShare.ini"); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(IniPath)) return;
                string[] lines = File.ReadAllLines(IniPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("[") ||
                        line.StartsWith(";") || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLower();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "port")
                    {
                        int p;
                        if (int.TryParse(val, out p) && p >= 1 && p <= 65535) Port = p;
                    }
                    else if (key == "language")
                    {
                        if (val == "en" || val == "fr" || val == "ar") Language = val;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[Settings]");
                sb.AppendLine("Port=" + Port);
                sb.AppendLine("Language=" + Language);
                File.WriteAllText(IniPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ========================================================================
    //  LOCALIZATION  (en / fr / ar) — L.T("key") everywhere
    // ========================================================================
    public static class L
    {
        private static string lang = "en";
        private static Dictionary<string, string> dict;

        private static readonly Dictionary<string, string> English = MakeEn();
        private static readonly Dictionary<string, string> French = MakeFr();
        private static readonly Dictionary<string, string> Arabic = MakeAr();

        public static string Lang { get { return lang; } }
        public static bool IsArabic { get { return lang == "ar"; } }
        public static bool IsFrench { get { return lang == "fr"; } }

        public static void Set(string code)
        {
            lang = (code == "fr") ? "fr" : (code == "ar") ? "ar" : "en";
            if (lang == "fr") dict = French;
            else if (lang == "ar") dict = Arabic;
            else dict = English;
        }

        public static string T(string key)
        {
            string s;
            if (dict != null && dict.TryGetValue(key, out s)) return s;
            if (English.TryGetValue(key, out s)) return s;
            return key;
        }

        // ------------------------------------------------------------------
        private static Dictionary<string, string> MakeEn()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("title", "GoldShare - Premium File Sharing");
            d.Add("hdr_sub", "Premium File Sharing  \u2014  Upload, Download & QR Connect");
            d.Add("start", "START SERVER");
            d.Add("stop", "STOP SERVER");
            d.Add("open_browser", "OPEN IN BROWSER");
            d.Add("open_folder", "OPEN FOLDER");
            d.Add("copy_url", "COPY URL");
            d.Add("qr_scan", "QR / SCAN");
            d.Add("port", "Port:");
            d.Add("folder", "Folder:");
            d.Add("addresses", "Server addresses - select one, then COPY URL or QR / SCAN:");
            d.Add("log", "Activity log:");
            d.Add("language", "Language:");
            d.Add("msg_already", "GoldShare is already running.\r\n\r\nCheck the taskbar for the existing window.");
            d.Add("msg_admin", "Administrator privileges are recommended.\r\n\r\nContinue without them anyway?");
            d.Add("msg_start_first", "Start the server first.");
            d.Add("msg_bad_port", "Enter a valid port (1-65535).");
            d.Add("msg_start_fail", "Could not start server:\r\n{0}\r\n\r\nTry another port.");
            d.Add("msg_open_fail", "Could not open folder:\r\n{0}");
            d.Add("msg_copied", "Copied: {0}");
            d.Add("msg_started", "Server started on port {0}");
            d.Add("msg_tip", "Tip: click 'QR / SCAN' and point a phone camera at the code.");
            d.Add("msg_stopped", "Server stopped.");
            d.Add("msg_qr_preview", "QR preview uses port {0} (server not running)");
            d.Add("msg_upload_done", "Upload finished: {0} file(s) received");
            d.Add("msg_uploaded", "Uploaded: {0} ({1})");
            d.Add("msg_files_added", "{0} file(s) added to Downloads (drag & drop)");
            d.Add("msg_copy_failed", "Copy failed: {0}");
            d.Add("msg_qr_error", "QR error: {0}");
            d.Add("msg_url_copied", "URL copied to clipboard.");
            d.Add("msg_saved", "QR image saved:\r\n{0}");
            d.Add("qr_header", "\u25C6  SCAN WITH PHONE CAMERA  \u25C6");
            d.Add("qr_hint", "Open the camera on your phone / tablet and point it at this code:");
            d.Add("qr_for_addr", "QR for address:");
            d.Add("save_png", "SAVE PNG");
            d.Add("close", "CLOSE");
            d.Add("page_title", "GoldShare - File Sharing");
            d.Add("page_sub", "Premium File Sharing \u2014 Upload & Download");
            d.Add("page_dl", "\u2B07 DOWNLOAD FILES");
            d.Add("page_up", "\u2B06 UPLOAD TO THIS DEVICE");
            d.Add("page_received", "\uD83D\uDCE6 RECEIVED FILES");
            d.Add("page_no_files", "No files yet.");
            d.Add("page_upload_btn", "UPLOAD");
            return d;
        }

        private static Dictionary<string, string> MakeFr()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("title", "GoldShare - Partage de fichiers Premium");
            d.Add("hdr_sub", "Partage de fichiers Premium \u2014 Envoi, T\u00E9l\u00E9chargement & QR");
            d.Add("start", "D\u00C9MARRER LE SERVEUR");
            d.Add("stop", "ARR\u00CATER LE SERVEUR");
            d.Add("open_browser", "OUVRIR LE NAVIGATEUR");
            d.Add("open_folder", "OUVRIR LE DOSSIER");
            d.Add("copy_url", "COPIER L'URL");
            d.Add("qr_scan", "QR / SCAN");
            d.Add("port", "Port :");
            d.Add("folder", "Dossier :");
            d.Add("addresses", "Adresses du serveur - s\u00E9lectionnez, puis COPIER L'URL ou QR / SCAN :");
            d.Add("log", "Journal d'activit\u00E9 :");
            d.Add("language", "Langue :");
            d.Add("msg_already", "GoldShare est d\u00E9j\u00E0 en cours d'ex\u00E9cution.\r\n\r\nConsultez la barre des t\u00E2ches.");
            d.Add("msg_admin", "Les privil\u00E8ges administrateur sont recommand\u00E9s.\r\n\r\nContinuer quand m\u00EAme sans ?");
            d.Add("msg_start_first", "D\u00E9marrez d'abord le serveur.");
            d.Add("msg_bad_port", "Entrez un port valide (1-65535).");
            d.Add("msg_start_fail", "Impossible de d\u00E9marrer le serveur :\r\n{0}\r\n\r\nEssayez un autre port.");
            d.Add("msg_open_fail", "Impossible d'ouvrir le dossier :\r\n{0}");
            d.Add("msg_copied", "Copi\u00E9 : {0}");
            d.Add("msg_started", "Serveur d\u00E9marr\u00E9 sur le port {0}");
            d.Add("msg_tip", "Astuce : cliquez sur \u00AB QR / SCAN \u00BB et visez avec la cam\u00E9ra du t\u00E9l\u00E9phone.");
            d.Add("msg_stopped", "Serveur arr\u00EAt\u00E9.");
            d.Add("msg_qr_preview", "Aper\u00E7u QR utilise le port {0} (serveur arr\u00EAt\u00E9)");
            d.Add("msg_upload_done", "T\u00E9l\u00E9versement termin\u00E9 : {0} fichier(s) re\u00E7u(s)");
            d.Add("msg_uploaded", "Re\u00E7u : {0} ({1})");
            d.Add("msg_files_added", "{0} fichier(s) ajout\u00E9(s) aux t\u00E9l\u00E9chargements (glisser-d\u00E9poser)");
            d.Add("msg_copy_failed", "\u00C9chec de la copie : {0}");
            d.Add("msg_qr_error", "Erreur QR : {0}");
            d.Add("msg_url_copied", "URL copi\u00E9e dans le presse-papiers.");
            d.Add("msg_saved", "Image QR enregistr\u00E9e :\r\n{0}");
            d.Add("qr_header", "\u25C6  SCANNEZ AVEC LA CAM\u00C9RA DU T\u00C9L\u00C9PHONE  \u25C6");
            d.Add("qr_hint", "Ouvrez la cam\u00E9ra de votre t\u00E9l\u00E9phone / tablette et visez ce code :");
            d.Add("qr_for_addr", "QR pour l'adresse :");
            d.Add("save_png", "ENREGISTRER PNG");
            d.Add("close", "FERMER");
            d.Add("page_title", "GoldShare - Partage de fichiers");
            d.Add("page_sub", "Partage Premium \u2014 Envoi & T\u00E9l\u00E9chargement");
            d.Add("page_dl", "\u2B07 T\u00C9L\u00C9CHARGER LES FICHIERS");
            d.Add("page_up", "\u2B06 ENVOYER VERS CET APPAREIL");
            d.Add("page_received", "\uD83D\uDCE6 FICHIERS RE\u00C7US");
            d.Add("page_no_files", "Aucun fichier pour le moment.");
            d.Add("page_upload_btn", "ENVOYER");
            return d;
        }

        private static Dictionary<string, string> MakeAr()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("title", "GoldShare - \u0645\u0634\u0627\u0631\u0643\u0629 \u0627\u0644\u0645\u0644\u0641\u0627\u062A \u0627\u0644\u0645\u0645\u064A\u0632\u0629");
            d.Add("hdr_sub", "\u0645\u0634\u0627\u0631\u0643\u0629 \u0645\u0644\u0641\u0627\u062A \u0645\u0645\u064A\u0632\u0629 \u2014 \u0631\u0641\u0639 \u0648\u062A\u0646\u0632\u064A\u0644 \u0648\u0631\u0628\u0637 QR");
            d.Add("start", "\u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645");
            d.Add("stop", "\u0625\u064A\u0642\u0627\u0641 \u0627\u0644\u062E\u0627\u062F\u0645");
            d.Add("open_browser", "\u0641\u062A\u062D \u0641\u064A \u0627\u0644\u0645\u062A\u0635\u0641\u062D");
            d.Add("open_folder", "\u0641\u062A\u062D \u0627\u0644\u0645\u062C\u0644\u062F");
            d.Add("copy_url", "\u0646\u0633\u062E \u0627\u0644\u0631\u0627\u0628\u0637");
            d.Add("qr_scan", "\u0631\u0645\u0632 QR");
            d.Add("port", "\u0627\u0644\u0645\u0646\u0641\u0630:");
            d.Add("folder", "\u0627\u0644\u0645\u062C\u0644\u062F:");
            d.Add("addresses", "\u0639\u0646\u0627\u0648\u064A\u0646 \u0627\u0644\u062E\u0627\u062F\u0645 - \u062D\u062F\u062F \u0639\u0646\u0648\u0627\u0646\u0627\u064B \u062B\u0645 \u0646\u0633\u062E \u0627\u0644\u0631\u0627\u0628\u0637 \u0623\u0648 QR:");
            d.Add("log", "\u0633\u062C\u0644 \u0627\u0644\u0646\u0634\u0627\u0637:");
            d.Add("language", "\u0627\u0644\u0644\u063A\u0629:");
            d.Add("msg_already", "GoldShare \u064A\u0639\u0645\u0644 \u0628\u0627\u0644\u0641\u0639\u0644.\r\n\r\n\u062A\u062D\u0642\u0642 \u0645\u0646 \u0634\u0631\u064A\u0637 \u0627\u0644\u0645\u0647\u0627\u0645.");
            d.Add("msg_admin", "\u064A\u064F\u0646\u0635\u062D \u0628\u0627\u0645\u062A\u064A\u0627\u0632\u0627\u062A \u0627\u0644\u0645\u0633\u0624\u0648\u0644.\r\n\r\n\u0627\u0644\u0645\u062A\u0627\u0628\u0639\u0629 \u0628\u062F\u0648\u0646\u0647\u0627\u061F");
            d.Add("msg_start_first", "\u0634\u063A\u0651\u0644 \u0627\u0644\u062E\u0627\u062F\u0645 \u0623\u0648\u0644\u0627\u064B.");
            d.Add("msg_bad_port", "\u0623\u062F\u062E\u0644 \u0645\u0646\u0641\u0630\u0627\u064B \u0635\u062D\u064A\u062D\u0627\u064B (1-65535).");
            d.Add("msg_start_fail", "\u062A\u0639\u0630\u0631 \u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645:\r\n{0}\r\n\r\n\u062C\u0631\u0651\u0628 \u0645\u0646\u0641\u0630\u0627\u064B \u0622\u062E\u0631.");
            d.Add("msg_open_fail", "\u062A\u0639\u0630\u0631 \u0641\u062A\u062D \u0627\u0644\u0645\u062C\u0644\u062F:\r\n{0}");
            d.Add("msg_copied", "\u062A\u0645 \u0627\u0644\u0646\u0633\u062E: {0}");
            d.Add("msg_started", "\u062A\u0645 \u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645 \u0639\u0644\u0649 \u0627\u0644\u0645\u0646\u0641\u0630 {0}");
            d.Add("msg_tip", "\u0627\u0636\u063A\u0637 '\u0631\u0645\u0632 QR' \u0648\u0648\u062C\u0651\u0647 \u0643\u0627\u0645\u064A\u0631\u0627 \u0627\u0644\u0647\u0627\u062A\u0641 \u0646\u062D\u0648 \u0627\u0644\u0631\u0645\u0632.");
            d.Add("msg_stopped", "\u062A\u0645 \u0625\u064A\u0642\u0627\u0641 \u0627\u0644\u062E\u0627\u062F\u0645.");
            d.Add("msg_qr_preview", "\u0645\u0639\u0627\u064A\u0646\u0629 QR \u062A\u0633\u062A\u062E\u062F\u0645 \u0627\u0644\u0645\u0646\u0641\u0630 {0} (\u0627\u0644\u062E\u0627\u062F\u0645 \u063A\u064A\u0631 \u0645\u0634\u063A\u0651\u0644)");
            d.Add("msg_upload_done", "\u0627\u0646\u062A\u0647\u0649 \u0627\u0644\u0631\u0641\u0639: {0} \u0645\u0644\u0641");
            d.Add("msg_uploaded", "\u062A\u0645 \u0631\u0641\u0639: {0} ({1})");
            d.Add("msg_files_added", "\u062A\u0645\u062A \u0625\u0636\u0627\u0641\u0629 {0} \u0645\u0644\u0641/\u0645\u0644\u0641\u0627\u062A \u0625\u0644\u0649 \u0627\u0644\u062A\u0646\u0632\u064A\u0644\u0627\u062A (\u0633\u062D\u0628 \u0648\u0625\u0641\u0644\u0627\u062A)");
            d.Add("msg_copy_failed", "\u0641\u0634\u0644 \u0627\u0644\u0646\u0633\u062E: {0}");
            d.Add("msg_qr_error", "\u062E\u0637\u0623 QR: {0}");
            d.Add("msg_url_copied", "\u062A\u0645 \u0646\u0633\u062E \u0627\u0644\u0631\u0627\u0628\u0637.");
            d.Add("msg_saved", "\u062A\u0645 \u062D\u0641\u0638 \u0635\u0648\u0631\u0629 QR:\r\n{0}");
            d.Add("qr_header", "\u25C6  \u0627\u0645\u0633\u062D \u0628\u0643\u0627\u0645\u064A\u0631\u0627 \u0627\u0644\u0647\u0627\u062A\u0641  \u25C6");
            d.Add("qr_hint", "\u0627\u0641\u062A\u062D \u0627\u0644\u0643\u0627\u0645\u064A\u0631\u0627 \u0641\u064A \u0647\u0627\u062A\u0641\u0643 \u0623\u0648 \u062C\u0647\u0627\u0632\u0643 \u0627\u0644\u0644\u0648\u062D\u064A \u0648\u0648\u062C\u0651\u0647\u0647\u0627 \u0646\u062D\u0648 \u0647\u0630\u0627 \u0627\u0644\u0631\u0645\u0632:");
            d.Add("qr_for_addr", "QR \u0644\u0644\u0639\u0646\u0648\u0627\u0646:");
            d.Add("save_png", "\u062D\u0641\u0638 PNG");
            d.Add("close", "\u0625\u063A\u0644\u0627\u0642");
            d.Add("page_title", "GoldShare - \u0645\u0634\u0627\u0631\u0643\u0629 \u0627\u0644\u0645\u0644\u0641\u0627\u062A");
            d.Add("page_sub", "\u0645\u0634\u0627\u0631\u0643\u0629 \u0645\u0644\u0641\u0627\u062A \u0645\u0645\u064A\u0632\u0629 \u2014 \u0631\u0641\u0639 \u0648\u062A\u0646\u0632\u064A\u0644");
            d.Add("page_dl", "\u2B07 \u062A\u0646\u0632\u064A\u0644 \u0627\u0644\u0645\u0644\u0641\u0627\u062A");
            d.Add("page_up", "\u2B06 \u0631\u0641\u0639 \u0625\u0644\u0649 \u0647\u0630\u0627 \u0627\u0644\u062C\u0647\u0627\u0632");
            d.Add("page_received", "\uD83D\uDCE6 \u0627\u0644\u0645\u0644\u0641\u0627\u062A \u0627\u0644\u0645\u0633\u062A\u0644\u0645\u0629");
            d.Add("page_no_files", "\u0644\u0627 \u062A\u0648\u062C\u062F \u0645\u0644\u0641\u0627\u062A \u0628\u0639\u062F.");
            d.Add("page_upload_btn", "\u0631\u0641\u0639");
            return d;
        }
    }

    // ========================================================================
    //  ENTRY POINT (settings load + single instance + self-elevating admin)
    // ========================================================================
    static class Program
    {
        private static Mutex instanceMutex;

        [STAThread]
        static void Main()
        {
            Settings.Load();               // load saved port + language FIRST
            L.Set(Settings.Language);

            // ---- enforce single instance ----
            bool createdNew;
            instanceMutex = new Mutex(true, "Global\\GoldShare_Premium_Server_Mutex", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(L.T("msg_already"), "GoldShare",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // ---- request admin privileges ----
            if (!IsRunningAsAdmin())
            {
                if (Elevate())
                {
                    instanceMutex.ReleaseMutex();
                    return;              // relaunched elevated -> exit old copy
                }
                // user cancelled -> continue unelevated
            }

            Application.EnableVisualStyles();
            Application.Run(new MainForm());

            instanceMutex.ReleaseMutex();
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static bool Elevate()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = "elevated";
                psi.UseShellExecute = true;
                psi.Verb = "runas";   // UAC prompt on Vista+ / Run-As dialog on XP
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception)
            {
                DialogResult r = MessageBox.Show(L.T("msg_admin"), "GoldShare",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                return (r != DialogResult.Yes);
            }
        }
    }

    // ========================================================================
    //  MAIN FORM (gold/white premium UI, fixed size, multilingual + RTL)
    // ========================================================================
    public class MainForm : Form
    {
        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool running;
        private int port = 8080;

        private string baseFolder;
        private string downloadsFolder;
        private string uploadsFolder;

        private const int MaxUploadBytes = 512 * 1024 * 1024; // 512 MB cap
        private static readonly byte[] CRLFCRLF = new byte[] { 13, 10, 13, 10 };

        private Panel headerPanel;
        private TextBox txtPort;
        private TextBox txtPath;
        private GoldButton btnStart, btnStop, btnOpenPage, btnOpenFolder, btnCopy, btnQr;
        private ListBox lstUrls, lstLog;
        private Label lblPort, lblFolder, lblAddresses, lblLog, lblLang;
        private ComboBox cboLang;
        private bool langReady;   // guard combo events during init

        public MainForm()
        {
            SetupFolders();
            InitUI();
            ApplyLanguage();
            langReady = true;
            Log("GoldShare.ini  ->  " + Settings.IniPath);
            Log(L.T("msg_started").Replace("{0}", port.ToString()) + " ?");
            Log(L.T("msg_tip"));
        }

        // --------------------------------------------------------------------
        //  FOLDERS:  \GoldShare\Downloads  and  \GoldShare\Uploads
        // --------------------------------------------------------------------
        private void SetupFolders()
        {
            baseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GoldShare");
            downloadsFolder = Path.Combine(baseFolder, "Downloads");
            uploadsFolder = Path.Combine(baseFolder, "Uploads");
            if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);
            if (!Directory.Exists(downloadsFolder)) Directory.CreateDirectory(downloadsFolder);
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
        }

        // --------------------------------------------------------------------
        //  UI  (fixed-size window — no resize, no maximize)
        // --------------------------------------------------------------------
        private void InitUI()
        {
            this.Text = "GoldShare";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(640, 640);
            this.BackColor = Color.FromArgb(253, 250, 243);
            try { this.Font = new Font("Segoe UI", 9f); } catch { this.Font = new Font("Tahoma", 8.25f); }

            // ----- gold gradient header -----
            headerPanel = new Panel();
            headerPanel.Location = new Point(0, 0);
            headerPanel.Size = new Size(640, 88);
            headerPanel.Paint += new PaintEventHandler(HeaderPaint);
            this.Controls.Add(headerPanel);

            // ----- row 1 : port + start/stop/open -----
            lblPort = MakeLabel("", 24, 108, 38);
            this.Controls.Add(lblPort);

            txtPort = new TextBox();
            txtPort.Text = Settings.Port.ToString();      // saved port
            txtPort.Location = new Point(62, 104);
            txtPort.Size = new Size(58, 24);
            this.Controls.Add(txtPort);

            btnStart = new GoldButton();
            btnStart.Location = new Point(134, 100); btnStart.Size = new Size(142, 34);
            btnStart.Click += new EventHandler(delegate { StartServer(); });
            this.Controls.Add(btnStart);

            btnStop = new GoldButton();
            btnStop.Location = new Point(286, 100); btnStop.Size = new Size(140, 34);
            btnStop.Enabled = false;
            btnStop.Click += new EventHandler(delegate { StopServer(); });
            this.Controls.Add(btnStop);

            btnOpenPage = new GoldButton();
            btnOpenPage.Location = new Point(436, 100); btnOpenPage.Size = new Size(180, 34);
            btnOpenPage.Click += new EventHandler(delegate
            {
                if (running) Process.Start("http://localhost:" + port + "/");
                else MessageBox.Show(L.T("msg_start_first"), "GoldShare");
            });
            this.Controls.Add(btnOpenPage);

            // ----- row 2 : folder -----
            lblFolder = MakeLabel("", 24, 154, 46);
            this.Controls.Add(lblFolder);

            txtPath = new TextBox();
            txtPath.Text = baseFolder;
            txtPath.ReadOnly = true;
            txtPath.Location = new Point(70, 150);
            txtPath.Size = new Size(356, 24);
            txtPath.BackColor = Color.FromArgb(255, 253, 244);
            this.Controls.Add(txtPath);

            btnOpenFolder = new GoldButton();
            btnOpenFolder.Location = new Point(436, 146); btnOpenFolder.Size = new Size(180, 34);
            btnOpenFolder.Click += new EventHandler(delegate { OpenSharedFolder(); });
            this.Controls.Add(btnOpenFolder);

            // ----- row 3 : addresses + QR -----
            lblAddresses = MakeLabel("", 24, 188, 520);
            this.Controls.Add(lblAddresses);

            lstUrls = new ListBox();
            lstUrls.Location = new Point(24, 208);
            lstUrls.Size = new Size(470, 56);
            lstUrls.IntegralHeight = false;
            lstUrls.BorderStyle = BorderStyle.FixedSingle;
            lstUrls.BackColor = Color.FromArgb(255, 253, 244);
            lstUrls.ForeColor = Color.FromArgb(122, 86, 6);
            this.Controls.Add(lstUrls);

            btnCopy = new GoldButton();
            btnCopy.Location = new Point(506, 208); btnCopy.Size = new Size(110, 30);
            btnCopy.Click += new EventHandler(delegate
            {
                string s = null;
                if (lstUrls.SelectedItem != null) s = lstUrls.SelectedItem.ToString();
                else if (lstUrls.Items.Count > 0) s = lstUrls.Items[0].ToString();
                if (s != null)
                {
                    Clipboard.SetText(s);
                    Log(string.Format(L.T("msg_copied"), s));
                }
            });
            this.Controls.Add(btnCopy);

            btnQr = new GoldButton();
            btnQr.Location = new Point(506, 244); btnQr.Size = new Size(110, 30);
            btnQr.Click += new EventHandler(delegate { ShowQr(); });
            this.Controls.Add(btnQr);

            // ----- row 4 : language + log -----
            lblLog = MakeLabel("", 24, 284, 200);
            this.Controls.Add(lblLog);

            lblLang = MakeLabel("", 380, 284, 66);
            lblLang.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(lblLang);

            cboLang = new ComboBox();
            cboLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cboLang.Location = new Point(446, 280);
            cboLang.Size = new Size(170, 24);
            cboLang.BackColor = Color.FromArgb(255, 253, 244);
            cboLang.ForeColor = Color.FromArgb(122, 86, 6);
            cboLang.Font = new Font("Verdana", 8.25f, FontStyle.Bold);
            cboLang.Items.Add("English");        // index 0 -> en
            cboLang.Items.Add("Fran\u00E7ais");  // index 1 -> fr
            cboLang.Items.Add("\u0627\u0644\u0639\u0631\u0628\u064A\u0629"); // index 2 -> ar
            cboLang.SelectedIndex = (Settings.Language == "fr") ? 1 :
                                    (Settings.Language == "ar") ? 2 : 0;
            cboLang.SelectedIndexChanged += new EventHandler(delegate(object s, EventArgs e)
            {
                if (!langReady) return;
                string code = (cboLang.SelectedIndex == 1) ? "fr" :
                              (cboLang.SelectedIndex == 2) ? "ar" : "en";
                if (code == L.Lang) return;
                L.Set(code);
                Settings.Language = code;
                Settings.Save();
                ApplyLanguage();
                Log("\u2713  " + cboLang.Text);
            });
            this.Controls.Add(cboLang);

            lstLog = new ListBox();
            lstLog.Location = new Point(24, 304);
            lstLog.Size = new Size(592, 310);
            lstLog.IntegralHeight = false;
            lstLog.BorderStyle = BorderStyle.FixedSingle;
            lstLog.BackColor = Color.FromArgb(255, 253, 244);
            lstLog.ForeColor = Color.FromArgb(90, 66, 26);
            this.Controls.Add(lstLog);

            // ----- drag & drop files onto window -> copies into Downloads -----
            this.AllowDrop = true;
            this.DragEnter += new DragEventHandler(FormDragEnter);
            this.DragDrop += new DragEventHandler(FormDragDrop);
            this.FormClosing += new FormClosingEventHandler(delegate(object s, FormClosingEventArgs e)
            {
                int p;
                if (int.TryParse(txtPort.Text.Trim(), out p) && p >= 1 && p <= 65535)
                { Settings.Port = p; Settings.Save(); }
                if (running) StopServer();
            });
        }

        private Label MakeLabel(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, 20);
            l.ForeColor = Color.FromArgb(90, 66, 26);
            return l;
        }

        // --------------------------------------------------------------------
        //  APPLY LANGUAGE (re-text everything + RTL for Arabic)
        // --------------------------------------------------------------------
        private void ApplyLanguage()
        {
            this.Text = L.T("title");
            this.RightToLeft = L.IsArabic ? RightToLeft.Yes : RightToLeft.No;
            this.RightToLeftLayout = L.IsArabic;

            lblPort.Text = L.T("port");
            lblFolder.Text = L.T("folder");
            lblAddresses.Text = L.T("addresses");
            lblLog.Text = L.T("log");
            lblLang.Text = L.T("language");

            btnStart.Text = L.T("start");
            btnStop.Text = L.T("stop");
            btnOpenPage.Text = L.T("open_browser");
            btnOpenFolder.Text = L.T("open_folder");
            btnCopy.Text = L.T("copy_url");
            btnQr.Text = L.T("qr_scan");

            headerPanel.Invalidate();   // repaint translated subtitle
        }

        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = new Rectangle(0, 0, headerPanel.Width, headerPanel.Height);
            using (LinearGradientBrush b = new LinearGradientBrush(r,
                Color.FromArgb(112, 79, 5), Color.FromArgb(212, 175, 55), LinearGradientMode.Horizontal))
                g.FillRectangle(b, r);
            using (Pen p = new Pen(Color.FromArgb(246, 228, 168), 3))
                g.DrawLine(p, 0, headerPanel.Height - 2, headerPanel.Width, headerPanel.Height - 2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font f = new Font("Georgia", 20f, FontStyle.Bold))
            {
                string t = "\u25C6  G O L D S H A R E  \u25C6";
                SizeF sz = g.MeasureString(t, f);
                float x = (headerPanel.Width - sz.Width) / 2f;
                using (SolidBrush sh = new SolidBrush(Color.FromArgb(90, 40, 28, 4)))
                    g.DrawString(t, f, sh, x + 2f, 14f + 2f);
                using (SolidBrush sb2 = new SolidBrush(Color.FromArgb(255, 248, 224)))
                    g.DrawString(t, f, sb2, x, 14f);
            }
            using (Font f2 = new Font("Tahoma", 9f, FontStyle.Italic | FontStyle.Bold))
            {
                string s2 = L.T("hdr_sub");
                SizeF sz2 = g.MeasureString(s2, f2);
                using (SolidBrush b2 = new SolidBrush(Color.FromArgb(255, 238, 196)))
                    g.DrawString(s2, f2, b2, (headerPanel.Width - sz2.Width) / 2f, 52f);
            }
        }

        // --------------------------------------------------------------------
        //  OPEN FOLDER — explicitly via explorer.exe (fixes app-relaunch bug)
        // --------------------------------------------------------------------
        private void OpenSharedFolder()
        {
            try
            {
                Process.Start("explorer.exe", "\"" + baseFolder + "\"");
                Log("Explorer: " + baseFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(L.T("msg_open_fail"), ex.Message), "GoldShare");
            }
        }

        private void FormDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void FormDragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;
            int count = 0;
            foreach (string f in files)
            {
                try
                {
                    if (File.Exists(f))
                    {
                        File.Copy(f, Path.Combine(downloadsFolder, Path.GetFileName(f)), true);
                        count++;
                    }
                }
                catch (Exception ex) { Log(string.Format(L.T("msg_copy_failed"), ex.Message)); }
            }
            if (count > 0) Log(string.Format(L.T("msg_files_added"), count));
        }

        // --------------------------------------------------------------------
        //  QR DIALOG  (passes ALL server URLs; preselects the highlighted one)
        // --------------------------------------------------------------------
        private void ShowQr()
        {
            List<string> urls = new List<string>();
            int selectedIndex = 0;

            if (running && lstUrls.Items.Count > 0)
            {
                foreach (object o in lstUrls.Items) urls.Add(o.ToString());
                if (lstUrls.SelectedIndex >= 0) selectedIndex = lstUrls.SelectedIndex;
            }
            else
            {
                int p;
                if (!int.TryParse(txtPort.Text.Trim(), out p) || p < 1 || p > 65535) p = 8080;
                List<string> ips = GetLocalIPs();
                foreach (string ip in ips) urls.Add("http://" + ip + ":" + p + "/");
                if (!running) Log(string.Format(L.T("msg_qr_preview"), p));
            }

            try
            {
                using (QrForm f = new QrForm(urls, selectedIndex))
                    f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(L.T("msg_qr_error"), ex.Message), "GoldShare");
            }
        }

        // --------------------------------------------------------------------
        //  SERVER START / STOP
        // --------------------------------------------------------------------
        private void StartServer()
        {
            if (running) return;
            int p;
            if (!int.TryParse(txtPort.Text.Trim(), out p) || p < 1 || p > 65535)
            {
                MessageBox.Show(L.T("msg_bad_port"), "GoldShare");
                return;
            }
            port = p;
            Settings.Port = p;
            Settings.Save();
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://+:" + port + "/");
                listener.Start();
            }
            catch
            {
                try
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add("http://*:" + port + "/");
                    listener.Start();
                }
                catch (Exception ex2)
                {
                    MessageBox.Show(string.Format(L.T("msg_start_fail"), ex2.Message), "GoldShare");
                    return;
                }
            }
            running = true;
            listenerThread = new Thread(new ThreadStart(ListenerLoop));
            listenerThread.IsBackground = true;
            listenerThread.Start();

            btnStart.Enabled = false;
            btnStop.Enabled = true;
            txtPort.Enabled = false;
            RefreshUrls();
            Log(string.Format(L.T("msg_started"), port));
            Log(L.T("msg_tip"));
        }

        private void StopServer()
        {
            running = false;
            try { listener.Stop(); } catch { }
            try { listener.Abort(); } catch { }
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            txtPort.Enabled = true;
            Log(L.T("msg_stopped"));
        }

        private List<string> GetLocalIPs()
        {
            List<string> result = new List<string>();
            try
            {
                IPHostEntry entry = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in entry.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !result.Contains(ip.ToString()))
                        result.Add(ip.ToString());
                }
            }
            catch { }
            if (result.Count == 0) result.Add("127.0.0.1");
            return result;
        }

        private void RefreshUrls()
        {
            lstUrls.Items.Clear();
            List<string> ips = GetLocalIPs();
            foreach (string ip in ips)
                lstUrls.Items.Add("http://" + ip + ":" + port + "/");
            if (lstUrls.Items.Count > 0) lstUrls.SelectedIndex = 0;
        }

        private void Log(string msg)
        {
            if (lstLog.InvokeRequired)
            {
                try { lstLog.BeginInvoke(new MethodInvoker(delegate { Log(msg); })); } catch { }
                return;
            }
            lstLog.Items.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + msg);
            while (lstLog.Items.Count > 500) lstLog.Items.RemoveAt(0);
            lstLog.TopIndex = lstLog.Items.Count - 1;
        }

        // --------------------------------------------------------------------
        //  HTTP LISTENER LOOP
        // --------------------------------------------------------------------
        private void ListenerLoop()
        {
            while (running)
            {
                try
                {
                    HttpListenerContext ctx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(new WaitCallback(HandleRequest), ctx);
                }
                catch
                {
                    if (!running) break;
                }
            }
        }

        private void HandleRequest(object state)
        {
            HttpListenerContext ctx = (HttpListenerContext)state;
            string rawPath = ctx.Request.Url.AbsolutePath;
            string method = ctx.Request.HttpMethod;
            string lower = rawPath.ToLower();
            try
            {
                if (lower == "/" || lower == "/index.html")
                {
                    if (method == "GET" || method == "HEAD")
                        WriteText(ctx, BuildIndexHtml(), 200, "text/html; charset=utf-8");
                    else WriteText(ctx, "Method not allowed", 405, "text/plain");
                }
                else if (lower == "/upload")
                {
                    if (method == "POST") HandleUpload(ctx);
                    else WriteText(ctx, "Use POST", 405, "text/plain");
                }
                else if (lower.StartsWith("/downloads/"))
                    ServeFile(ctx, downloadsFolder, rawPath.Substring("/Downloads/".Length));
                else if (lower.StartsWith("/uploads/"))
                    ServeFile(ctx, uploadsFolder, rawPath.Substring("/Uploads/".Length));
                else if (lower == "/favicon.ico")
                    WriteText(ctx, "", 404, "text/plain");
                else
                    WriteText(ctx, "<h2>404 - Not found</h2><a href=\"/\">GoldShare</a>", 404, "text/html; charset=utf-8");
            }
            catch
            {
                try { WriteText(ctx, "Server error", 500, "text/plain"); } catch { }
            }
            finally
            {
                Log(method + " " + rawPath + "  ->  " + ctx.Response.StatusCode);
            }
        }

        // --------------------------------------------------------------------
        //  INDEX PAGE (gold/white premium HTML — localized, RTL for Arabic)
        // --------------------------------------------------------------------
        private string BuildIndexHtml()
        {
            List<string> ips = GetLocalIPs();
            string primary = "http://" + ips[0] + ":" + port + "/";
            string dir = L.IsArabic ? " dir=\"rtl\" lang=\"ar\"" :
                         (L.IsFrench ? " lang=\"fr\"" : " lang=\"en\"");

            StringBuilder sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html").Append(dir).Append("><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>").Append(HtmlEncode(L.T("page_title"))).Append("</title><style>");
            sb.Append("*{box-sizing:border-box}");
            sb.Append("body{margin:0;background:#fbf7ee;font-family:Georgia,'Times New Roman',Tahoma,serif;color:#43331a}");
            sb.Append(".hero{background:linear-gradient(120deg,#7a5606,#b8860b 45%,#e9d78a);padding:34px 16px 26px;text-align:center;box-shadow:0 5px 24px rgba(122,86,6,.35)}");
            sb.Append(".hero h1{margin:0;font-size:34px;color:#fff8e2;letter-spacing:5px;text-shadow:0 2px 8px rgba(60,42,2,.6)}");
            sb.Append(".hero .sub{margin-top:6px;color:#fdf0c8;font-style:italic;font-size:15px}");
            sb.Append(".badge{display:inline-block;margin-top:12px;background:rgba(255,255,255,.92);color:#8a6408;border:1px solid #e3c766;border-radius:20px;padding:4px 16px;font-size:13px;font-family:Verdana,sans-serif}");
            sb.Append(".wrap{max-width:840px;margin:26px auto 40px;padding:0 14px}");
            sb.Append(".card{background:#fff;border:1px solid #ecd9a0;border-radius:14px;box-shadow:0 8px 26px rgba(184,134,11,.13);padding:22px;margin-bottom:24px}");
            sb.Append(".card h2{margin:0 0 14px;font-size:17px;color:#8a6408;letter-spacing:2px;border-bottom:2px solid #f3e6bd;padding-bottom:9px}");
            sb.Append(".file{display:flex;align-items:center;gap:11px;padding:10px 12px;margin:2px 0;border-radius:9px;text-decoration:none;color:#43331a;border:1px solid transparent}");
            sb.Append(".file:hover{background:#fdf4da;border-color:#e8d084}");
            sb.Append(".fname{word-break:break-all}");
            sb.Append(".fsize{margin-left:auto;color:#b08d2f;font-size:12px;white-space:nowrap;font-family:Verdana,sans-serif}");
            sb.Append(".empty{color:#b9a469;font-style:italic;padding:8px 4px}");
            sb.Append("input[type=file]{font-family:Verdana,sans-serif;font-size:14px;margin-bottom:14px;max-width:100%}");
            sb.Append(".btn{display:inline-block;background:linear-gradient(180deg,#e7c96c,#c29b21);border:1px solid #a67c00;color:#fff8e2;font-weight:bold;font-size:15px;letter-spacing:1px;padding:11px 30px;border-radius:9px;cursor:pointer;box-shadow:0 4px 12px rgba(166,124,0,.35);font-family:inherit}");
            sb.Append(".btn:hover{background:linear-gradient(180deg,#d9b64a,#a67c00)}");
            sb.Append(".bar{height:14px;background:#f3e6bd;border-radius:8px;overflow:hidden;display:none;margin-top:14px}");
            sb.Append(".bar>div{height:100%;width:0;background:linear-gradient(90deg,#c9a227,#e7c96c)}");
            sb.Append("#ptxt{font-size:12px;color:#8a6408;font-family:Verdana,sans-serif;margin-top:5px;display:none}");
            sb.Append("footer{text-align:center;color:#c3ab6a;font-size:12px;padding:16px;font-family:Verdana,sans-serif}");
            sb.Append("</style></head><body>");
            sb.Append("<div class=\"hero\"><h1>&#9670; GOLDSHARE &#9670;</h1>");
            sb.Append("<div class=\"sub\">").Append(HtmlEncode(L.T("page_sub"))).Append("</div>");
            sb.Append("<span class=\"badge\">").Append(HtmlEncode(primary)).Append("</span></div><div class=\"wrap\">");

            // --- downloads card ---
            sb.Append("<div class=\"card\"><h2>").Append(L.T("page_dl")).Append("</h2>");
            AppendFiles(sb, downloadsFolder, "Downloads");
            sb.Append("</div>");

            // --- upload card ---
            sb.Append("<div class=\"card\"><h2>").Append(L.T("page_up")).Append("</h2>");
            sb.Append("<form id=\"upform\" method=\"post\" action=\"/upload\" enctype=\"multipart/form-data\">");
            sb.Append("<input type=\"file\" name=\"file\" id=\"fileinput\" multiple> ");
            sb.Append("<button class=\"btn\" type=\"submit\">").Append(L.T("page_upload_btn")).Append("</button></form>");
            sb.Append("<div class=\"bar\" id=\"bar\"><div id=\"barfill\"></div></div><div id=\"ptxt\">0 %</div>");
            sb.Append("<script>(function(){var f=document.getElementById('upform');if(!f||!f.addEventListener)return;");
            sb.Append("f.addEventListener('submit',function(e){var fi=document.getElementById('fileinput');");
            sb.Append("if(!window.FormData||!fi.files||!fi.files.length)return;e.preventDefault();");
            sb.Append("var fd=new FormData(f),x=new XMLHttpRequest();x.open('POST','/upload');");
            sb.Append("var bar=document.getElementById('bar'),fill=document.getElementById('barfill'),pt=document.getElementById('ptxt');");
            sb.Append("bar.style.display='block';pt.style.display='block';");
            sb.Append("x.upload.onprogress=function(ev){if(ev.lengthComputable){var p=Math.round(ev.loaded*100/ev.total);fill.style.width=p+'%';pt.innerHTML=p+' %';}};");
            sb.Append("x.onload=function(){location.reload();};x.onerror=function(){f.submit();};x.send(fd);});})();</script>");
            sb.Append("<h2 style=\"margin-top:22px\">").Append(L.T("page_received")).Append("</h2>");
            AppendFiles(sb, uploadsFolder, "Uploads");
            sb.Append("</div>");

            sb.Append("</div><footer>&#9670; GoldShare Server &#9670;</footer></body></html>");
            return sb.ToString();
        }

        private void AppendFiles(StringBuilder sb, string folder, string urlRoot)
        {
            try
            {
                FileInfo[] files = new DirectoryInfo(folder).GetFiles();
                Array.Sort(files, delegate(FileInfo a, FileInfo b)
                { return string.Compare(a.Name, b.Name, true); });
                if (files.Length == 0)
                {
                    sb.Append("<div class=\"empty\">").Append(L.T("page_no_files")).Append("</div>");
                    return;
                }
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo fi = files[i];
                    sb.Append("<a class=\"file\" href=\"/" + urlRoot + "/" + Uri.EscapeDataString(fi.Name) + "\">");
                    sb.Append("<span>").Append(IconFor(fi.Name)).Append("</span>");
                    sb.Append("<span class=\"fname\">").Append(HtmlEncode(fi.Name)).Append("</span>");
                    sb.Append("<span class=\"fsize\">").Append(FormatSize(fi.Length)).Append("</span></a>");
                }
            }
            catch { sb.Append("<div class=\"empty\">Folder not available.</div>"); }
        }

        // --------------------------------------------------------------------
        //  FILE DOWNLOAD
        // --------------------------------------------------------------------
        private void ServeFile(HttpListenerContext ctx, string folder, string rawName)
        {
            string name = UrlDecode(rawName);
            if (name == null || name.Length == 0 || name.IndexOf("..") >= 0 ||
                name.IndexOf("/") >= 0 || name.IndexOf("\\") >= 0)
            {
                WriteText(ctx, "Forbidden", 403, "text/plain");
                return;
            }
            string full = Path.Combine(folder, name);
            if (!File.Exists(full))
            {
                WriteText(ctx, "404 - File not found", 404, "text/plain");
                return;
            }
            string ext = Path.GetExtension(name).ToLower();
            bool inline = (ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                           ext == ".gif" || ext == ".bmp" || ext == ".pdf" || ext == ".txt");
            string disp = inline ? "inline" : "attachment";
            if (IsAscii(name))
                ctx.Response.AddHeader("Content-Disposition", disp + "; filename=\"" + name + "\"");
            else
                ctx.Response.AddHeader("Content-Disposition", disp + "; filename=\"file" + ext +
                    "\"; filename*=UTF-8''" + Uri.EscapeDataString(name));

            ctx.Response.ContentType = MimeFor(name);
            using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ctx.Response.ContentLength64 = fs.Length;
                byte[] buf = new byte[65536];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0 && running)
                    ctx.Response.OutputStream.Write(buf, 0, n);
            }
            ctx.Response.OutputStream.Close();
        }

        // --------------------------------------------------------------------
        //  UPLOAD (multipart/form-data parser, .NET 2.0 safe)
        // --------------------------------------------------------------------
        private void HandleUpload(HttpListenerContext ctx)
        {
            string ctype = ctx.Request.ContentType == null ? "" : ctx.Request.ContentType;
            int bi = ctype.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            if (bi < 0) { WriteText(ctx, "Bad request", 400, "text/plain"); return; }
            string boundary = ctype.Substring(bi + 9).Trim();
            if (boundary.StartsWith("\"") && boundary.EndsWith("\"") && boundary.Length >= 2)
                boundary = boundary.Substring(1, boundary.Length - 2);
            byte[] bB = Encoding.UTF8.GetBytes("--" + boundary);

            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[32768];
            int n; long total = 0;
            while ((n = ctx.Request.InputStream.Read(buf, 0, buf.Length)) > 0)
            {
                ms.Write(buf, 0, n);
                total += n;
                if (total > MaxUploadBytes)
                {
                    ms.Dispose();
                    WriteText(ctx, "File too large (limit 512 MB)", 413, "text/plain");
                    return;
                }
            }
            byte[] body = ms.ToArray();
            ms.Dispose();

            int saved = 0;
            int pos = IndexOf(body, bB, 0);
            while (pos >= 0)
            {
                int next = IndexOf(body, bB, pos + bB.Length);
                if (next < 0) break;
                int segStart = pos + bB.Length;
                if (segStart + 1 < body.Length && body[segStart] == (byte)'-' && body[segStart + 1] == (byte)'-')
                    break; // final boundary
                if (segStart + 1 < body.Length && body[segStart] == 13 && body[segStart + 1] == 10)
                    segStart += 2;
                int dataEnd = next - 2; // strip CRLF before boundary
                if (dataEnd > segStart)
                {
                    int hdrEnd = IndexOf(body, CRLFCRLF, segStart);
                    if (hdrEnd >= 0 && hdrEnd + 4 <= dataEnd)
                    {
                        string headers = Encoding.UTF8.GetString(body, segStart, hdrEnd - segStart);
                        string fname = ExtractFilename(headers);
                        if (fname != null && fname.Length > 0)
                        {
                            int di = fname.LastIndexOf('\\'); if (di >= 0) fname = fname.Substring(di + 1);
                            di = fname.LastIndexOf('/'); if (di >= 0) fname = fname.Substring(di + 1);
                            int len = dataEnd - (hdrEnd + 4);
                            if (len > 0) { SaveUploaded(fname, body, hdrEnd + 4, len); saved++; }
                        }
                    }
                }
                pos = next;
            }
            Log(string.Format(L.T("msg_upload_done"), saved));
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = "/";
            ctx.Response.OutputStream.Close();
        }

        private void SaveUploaded(string fname, byte[] data, int offset, int len)
        {
            string safe = SanitizeFileName(fname);
            if (safe.Length == 0) safe = "upload.bin";
            string dest = Path.Combine(uploadsFolder, safe);
            int c = 1;
            while (File.Exists(dest))
            {
                string ext = Path.GetExtension(safe);
                dest = Path.Combine(uploadsFolder,
                    Path.GetFileNameWithoutExtension(safe) + " (" + c + ")" + ext);
                c++;
            }
            using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                fs.Write(data, offset, len);
            Log(string.Format(L.T("msg_uploaded"), Path.GetFileName(dest), FormatSize(len)));
        }

        // --------------------------------------------------------------------
        //  HELPERS
        // --------------------------------------------------------------------
        private static int IndexOf(byte[] hay, byte[] needle, int start)
        {
            if (needle.Length == 0) return start;
            int limit = hay.Length - needle.Length;
            byte first = needle[0];
            for (int i = start; i <= limit; i++)
            {
                if (hay[i] != first) continue;
                bool ok = true;
                for (int j = 1; j < needle.Length; j++)
                    if (hay[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        private static string ExtractFilename(string headers)
        {
            string lower = headers.ToLower();
            int i = lower.IndexOf("filename=");
            if (i < 0) return null;
            string rest = headers.Substring(i + 9).Trim();
            if (rest.StartsWith("\""))
            {
                int end = rest.IndexOf('"', 1);
                return end < 0 ? null : rest.Substring(1, end - 1);
            }
            int sp = rest.IndexOf(';');
            return sp < 0 ? rest.Trim() : rest.Substring(0, sp).Trim();
        }

        private static string SanitizeFileName(string name)
        {
            name = name.Replace("/", "_").Replace("\\", "_").Replace("..", "_");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length > 150)
            {
                string ext = Path.GetExtension(name);
                name = name.Substring(0, 150 - ext.Length) + ext;
            }
            return name.Trim();
        }

        private static string UrlDecode(string s)
        {
            try { return Uri.UnescapeDataString(s.Replace("+", "%20")); }
            catch { return s; }
        }

        private static string HtmlEncode(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;")
                    .Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static bool IsAscii(string s)
        {
            foreach (char c in s) if (c < 32 || c > 126) return false;
            return true;
        }

        private static string FormatSize(long len)
        {
            double d = len;
            if (d < 1024) return len + " B";
            d /= 1024; if (d < 1024) return d.ToString("0.0") + " KB";
            d /= 1024; if (d < 1024) return d.ToString("0.0") + " MB";
            d /= 1024; return d.ToString("0.00") + " GB";
        }

        private static string MimeFor(string name)
        {
            switch (Path.GetExtension(name).ToLower())
            {
                case ".html": case ".htm": return "text/html";
                case ".css": return "text/css";
                case ".js": return "application/javascript";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".txt": case ".log": case ".ini": case ".cs": return "text/plain";
                case ".pdf": return "application/pdf";
                case ".mp3": return "audio/mpeg";
                case ".wav": return "audio/wav";
                case ".mp4": return "video/mp4";
                case ".zip": return "application/zip";
                default: return "application/octet-stream";
            }
        }

        private static string IconFor(string name)
        {
            switch (Path.GetExtension(name).ToLower())
            {
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp":
                    return "\uD83D\uDDBC";
                case ".mp3": case ".wav": case ".wma": case ".mid":
                    return "\uD83C\uDFB5";
                case ".mp4": case ".avi": case ".wmv": case ".mkv": case ".mov":
                    return "\uD83C\uDFAC";
                case ".zip": case ".rar": case ".7z":
                    return "\uD83D\uDDDC";
                case ".pdf": return "\uD83D\uDCD5";
                case ".txt": case ".log": case ".ini": return "\uD83D\uDCDD";
                case ".cs": case ".cpp": case ".h": case ".js": case ".css":
                    return "\uD83D\uDCBB";
                case ".doc": case ".docx": case ".xls": case ".xlsx":
                    return "\uD83D\uDCCA";
                default: return "\uD83D\uDCC4";
            }
        }

        private static void WriteText(HttpListenerContext ctx, string content, int status, string contentType)
        {
            byte[] data = Encoding.UTF8.GetBytes(content);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = data.Length;
            if (ctx.Request.HttpMethod != "HEAD")
                ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        }
    }

    // ========================================================================
    //  QR CODE ENCODER (pure C# 2.0 — byte mode, EC level M, versions 1-10)
    //  Full ISO/IEC 18004 implementation: Reed-Solomon EC, block interleaving,
    //  alignment/version patterns, 8 mask trials with penalty scoring.
    // ========================================================================
    public class QrCode
    {
        public readonly int Size;
        public readonly bool[,] Modules;

        private int version;
        private bool[,] isFunction;

        private QrCode(int size)
        {
            Size = size;
            Modules = new bool[size, size];
            isFunction = new bool[size, size];
        }

        // ---- capacity/block tables for EC level M, versions 1..10 ----
        private static readonly int[] ByteCap = { 14, 26, 42, 62, 84, 106, 122, 152, 180, 213 };
        private static readonly int[] EcPerBlock = { 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        private static readonly int[] G1Blocks = { 1, 1, 1, 2, 2, 4, 4, 2, 3, 4 };
        private static readonly int[] G1Data = { 16, 28, 44, 32, 43, 27, 31, 38, 36, 43 };
        private static readonly int[] G2Blocks = { 0, 0, 0, 0, 0, 0, 0, 2, 2, 1 };
        private static readonly int[] G2Data = { 0, 0, 0, 0, 0, 0, 0, 39, 37, 44 };
        private static readonly int[][] AlignPos = new int[][] {
            new int[] {},
            new int[] {6, 18},
            new int[] {6, 22},
            new int[] {6, 26},
            new int[] {6, 30},
            new int[] {6, 34},
            new int[] {6, 22, 38},
            new int[] {6, 24, 42},
            new int[] {6, 26, 46},
            new int[] {6, 28, 50}
        };
        private static readonly bool[] FinderSeq =
            new bool[] { true, false, true, true, true, false, true };

        // ---- GF(256) arithmetic ----
        private static readonly byte[] GfExp = new byte[512];
        private static readonly byte[] GfLog = new byte[256];

        static QrCode()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                GfExp[i] = (byte)x;
                GfLog[x] = (byte)i;
                x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) GfExp[i] = GfExp[i - 255];
        }

        private static byte GfMul(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return GfExp[GfLog[a] + GfLog[b]];
        }

        // --------------------------------------------------------------------
        //  PUBLIC ENCODE
        // --------------------------------------------------------------------
        public static QrCode Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("QR data is empty");
            int version = 0;
            for (int v = 1; v <= 10; v++)
                if (data.Length <= ByteCap[v - 1]) { version = v; break; }
            if (version == 0)
                throw new ArgumentException("QR data too long (max 213 bytes)");

            // ---- build bit stream (byte mode, EC level M) ----
            List<bool> bits = new List<bool>();
            AppendBits(bits, 4, 4);                                  // mode: byte
            AppendBits(bits, data.Length, version <= 9 ? 8 : 16);    // char count
            for (int i = 0; i < data.Length; i++) AppendBits(bits, data[i], 8);

            int nBlocks = G1Blocks[version - 1] + G2Blocks[version - 1];
            int capBits = (TotalFor(version) - EcPerBlock[version - 1] * nBlocks) * 8;
            int term = capBits - bits.Count; if (term > 4) term = 4; if (term < 0) term = 0;
            AppendBits(bits, 0, term);                               // terminator
            while (bits.Count % 8 != 0) bits.Add(false);             // pad to byte
            byte[] pads = new byte[] { 0xEC, 0x11 };
            int p = 0;
            while (bits.Count < capBits) { AppendBits(bits, pads[p % 2], 8); p++; }

            byte[] dataCw = BitsToBytes(bits);

            // ---- split into blocks, compute EC, interleave ----
            int ecLen = EcPerBlock[version - 1];
            byte[][] blocks = new byte[nBlocks][];
            byte[][] ecs = new byte[nBlocks][];
            int idx = 0;
            for (int b = 0; b < G1Blocks[version - 1]; b++)
            {
                blocks[b] = new byte[G1Data[version - 1]];
                Array.Copy(dataCw, idx, blocks[b], 0, G1Data[version - 1]);
                idx += G1Data[version - 1];
                ecs[b] = ReedSolomon(blocks[b], ecLen);
            }
            for (int b = 0; b < G2Blocks[version - 1]; b++)
            {
                int bi = G1Blocks[version - 1] + b;
                blocks[bi] = new byte[G2Data[version - 1]];
                Array.Copy(dataCw, idx, blocks[bi], 0, G2Data[version - 1]);
                idx += G2Data[version - 1];
                ecs[bi] = ReedSolomon(blocks[bi], ecLen);
            }
            List<byte> final = new List<byte>();
            int maxLen = 0;
            for (int b = 0; b < nBlocks; b++)
                if (blocks[b].Length > maxLen) maxLen = blocks[b].Length;
            for (int i = 0; i < maxLen; i++)
                for (int b = 0; b < nBlocks; b++)
                    if (i < blocks[b].Length) final.Add(blocks[b][i]);
            for (int i = 0; i < ecLen; i++)
                for (int b = 0; b < nBlocks; b++)
                    final.Add(ecs[b][i]);

            List<bool> allBits = BytesToBits(final.ToArray());

            // ---- try all 8 masks, keep lowest-penalty result ----
            QrCode best = null;
            int bestPen = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                QrCode qr = new QrCode(version * 4 + 17);
                qr.version = version;
                qr.DrawFunctionPatterns();
                qr.PlaceData(allBits);
                qr.ApplyMask(mask);
                qr.DrawFormat(mask);
                int pen = qr.Penalty();
                if (pen < bestPen) { bestPen = pen; best = qr; }
            }
            return best;
        }

        private static int TotalFor(int version)
        {
            switch (version)
            {
                case 1: return 26; case 2: return 44; case 3: return 70;
                case 4: return 100; case 5: return 134; case 6: return 172;
                case 7: return 196; case 8: return 242; case 9: return 292;
                default: return 346;
            }
        }

        // --------------------------------------------------------------------
        //  BIT HELPERS
        // --------------------------------------------------------------------
        private static void AppendBits(List<bool> bits, int val, int len)
        {
            for (int i = len - 1; i >= 0; i--)
                bits.Add(((val >> i) & 1) != 0);
        }

        private static byte[] BitsToBytes(List<bool> bits)
        {
            byte[] outB = new byte[bits.Count / 8];
            for (int i = 0; i < outB.Length; i++)
            {
                int v = 0;
                for (int b = 0; b < 8; b++)
                    if (bits[i * 8 + b]) v |= 1 << (7 - b);
                outB[i] = (byte)v;
            }
            return outB;
        }

        private static List<bool> BytesToBits(byte[] bytes)
        {
            List<bool> bits = new List<bool>(bytes.Length * 8);
            for (int i = 0; i < bytes.Length; i++)
                for (int b = 7; b >= 0; b--)
                    bits.Add(((bytes[i] >> b) & 1) != 0);
            return bits;
        }

        private static bool GetBit(int x, int i) { return ((x >> i) & 1) != 0; }

        // --------------------------------------------------------------------
        //  REED-SOLOMON ERROR CORRECTION
        // --------------------------------------------------------------------
        private static byte[] ReedSolomon(byte[] block, int ecLen)
        {
            byte[] gen = BuildGenerator(ecLen);
            byte[] rem = new byte[ecLen];
            for (int i = 0; i < block.Length; i++)
            {
                byte factor = (byte)(block[i] ^ rem[0]);
                Array.Copy(rem, 1, rem, 0, ecLen - 1);
                rem[ecLen - 1] = 0;
                if (factor != 0)
                    for (int j = 0; j < ecLen; j++)
                        rem[j] ^= GfMul(gen[j + 1], factor);
            }
            return rem;
        }

        private static byte[] BuildGenerator(int n)
        {
            List<byte> poly = new List<byte>();
            poly.Add(1);
            for (int i = 0; i < n; i++)
            {
                List<byte> np = new List<byte>();
                byte alpha = GfExp[i];
                for (int j = 0; j <= poly.Count; j++)
                {
                    byte v = 0;
                    if (j < poly.Count) v ^= poly[j];
                    if (j > 0) v ^= GfMul(poly[j - 1], alpha);
                    np.Add(v);
                }
                poly = np;
            }
            return poly.ToArray();
        }

        // --------------------------------------------------------------------
        //  MATRIX CONSTRUCTION
        // --------------------------------------------------------------------
        private void SetFunction(int row, int col, bool dark)
        {
            Modules[row, col] = dark;
            isFunction[row, col] = true;
        }

        private void DrawFunctionPatterns()
        {
            // finder patterns + separators
            DrawFinder(0, 0);
            DrawFinder(Size - 7, 0);
            DrawFinder(0, Size - 7);

            // timing patterns
            for (int i = 8; i < Size - 8; i++)
            {
                bool dark = (i % 2) == 0;
                SetFunction(6, i, dark);
                SetFunction(i, 6, dark);
            }

            // alignment patterns
            int[] pos = AlignPos[version - 1];
            for (int a = 0; a < pos.Length; a++)
                for (int b = 0; b < pos.Length; b++)
                {
                    int r = pos[a], c = pos[b];
                    if ((r == 6 && c == 6) || (r == 6 && c == Size - 7) ||
                        (r == Size - 7 && c == 6)) continue;
                    DrawAlignment(r, c);
                }

            // version information (version 7+)
            if (version >= 7)
            {
                int rem = version;
                for (int i = 0; i < 12; i++)
                    rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int vbits = (version << 12) | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = (vbits & 1) != 0;
                    vbits >>= 1;
                    int ra = Size - 11 + i % 3;
                    int rb = i / 3;
                    SetFunction(rb, ra, bit);
                    SetFunction(ra, rb, bit);
                }
            }
        }

        private void DrawFinder(int row, int col)
        {
            for (int r = -1; r <= 7; r++)
            {
                int rr = row + r;
                if (rr < 0 || rr >= Size) continue;
                for (int c = -1; c <= 7; c++)
                {
                    int cc = col + c;
                    if (cc < 0 || cc >= Size) continue;
                    bool dark =
                        (r >= 0 && r <= 6 && (c == 0 || c == 6)) ||
                        (c >= 0 && c <= 6 && (r == 0 || r == 6)) ||
                        (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                    SetFunction(rr, cc, dark);
                }
            }
        }

        private void DrawAlignment(int row, int col)
        {
            for (int r = -2; r <= 2; r++)
                for (int c = -2; c <= 2; c++)
                {
                    int dist = Math.Abs(r);
                    if (Math.Abs(c) > dist) dist = Math.Abs(c);
                    SetFunction(row + r, col + c, dist != 1);
                }
        }

        private void DrawFormat(int mask)
        {
            int data = mask; // EC level M = 00, so data = mask
            int rem = data << 10;
            for (int i = 4; i >= 0; i--)
                if (((rem >> (10 + i)) & 1) != 0) rem ^= 0x537 << i;
            int fbits = ((data << 10) | rem) ^ 0x5412;

            // first copy (around top-left finder)
            for (int i = 0; i <= 5; i++) SetFunction(i, 8, GetBit(fbits, i));
            SetFunction(7, 8, GetBit(fbits, 6));
            SetFunction(8, 8, GetBit(fbits, 7));
            SetFunction(8, 7, GetBit(fbits, 8));
            for (int i = 9; i < 15; i++) SetFunction(8, 14 - i, GetBit(fbits, i));
            // second copy (top-right + bottom-left strips)
            for (int i = 0; i < 8; i++) SetFunction(8, Size - 1 - i, GetBit(fbits, i));
            for (int i = 8; i < 15; i++) SetFunction(Size - 15 + i, 8, GetBit(fbits, i));
            SetFunction(8, Size - 8, true); // always-dark module
        }

        private void PlaceData(List<bool> bits)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5; // skip timing column
                for (int vert = 0; vert < Size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int col = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int row = upward ? (Size - 1 - vert) : vert;
                        if (!isFunction[row, col])
                        {
                            bool dark = false;
                            if (i < bits.Count) { dark = bits[i]; i++; }
                            Modules[row, col] = dark;
                        }
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int row = 0; row < Size; row++)
                for (int col = 0; col < Size; col++)
                    if (!isFunction[row, col] && MaskBit(mask, row, col))
                        Modules[row, col] = !Modules[row, col];
        }

        private static bool MaskBit(int mask, int row, int col)
        {
            switch (mask)
            {
                case 0: return (row + col) % 2 == 0;
                case 1: return row % 2 == 0;
                case 2: return col % 3 == 0;
                case 3: return (row + col) % 3 == 0;
                case 4: return (row / 2 + col / 3) % 2 == 0;
                case 5: return (row * col) % 2 + (row * col) % 3 == 0;
                case 6: return ((row * col) % 2 + (row * col) % 3) % 2 == 0;
                default: return ((row + col) % 2 + (row * col) % 3) % 2 == 0;
            }
        }

        // --------------------------------------------------------------------
        //  PENALTY SCORING (ISO 18004 rules N1-N4)
        // --------------------------------------------------------------------
        private int Penalty()
        {
            int n = Size, result = 0, x, y, k, run;
            bool color;

            // N1: runs of 5+ same-colour modules
            for (y = 0; y < n; y++)
            {
                color = Modules[y, 0]; run = 1;
                for (x = 1; x < n; x++)
                {
                    if (Modules[y, x] == color) { run++; if (run == 6) result += 3; else if (run > 6) result++; }
                    else { color = Modules[y, x]; run = 1; }
                }
            }
            for (x = 0; x < n; x++)
            {
                color = Modules[0, x]; run = 1;
                for (y = 1; y < n; y++)
                {
                    if (Modules[y, x] == color) { run++; if (run == 6) result += 3; else if (run > 6) result++; }
                    else { color = Modules[y, x]; run = 1; }
                }
            }

            // N2: 2x2 blocks of same colour
            for (y = 0; y < n - 1; y++)
                for (x = 0; x < n - 1; x++)
                    if (Modules[y, x] == Modules[y, x + 1] &&
                        Modules[y, x] == Modules[y + 1, x] &&
                        Modules[y, x] == Modules[y + 1, x + 1])
                        result += 3;

            // N3: finder-like patterns
            for (y = 0; y < n; y++)
                for (x = 0; x <= n - 7; x++)
                {
                    bool match = true;
                    for (k = 0; k < 7; k++)
                        if (Modules[y, x + k] != FinderSeq[k]) { match = false; break; }
                    if (!match) continue;
                    bool before = (x >= 4);
                    if (before) for (k = 1; k <= 4; k++) if (Modules[y, x - k]) { before = false; break; }
                    bool after = (x + 10 < n);
                    if (after) for (k = 7; k <= 10; k++) if (Modules[y, x + k]) { after = false; break; }
                    if (before || after) result += 40;
                }
            for (x = 0; x < n; x++)
                for (y = 0; y <= n - 7; y++)
                {
                    bool match = true;
                    for (k = 0; k < 7; k++)
                        if (Modules[y + k, x] != FinderSeq[k]) { match = false; break; }
                    if (!match) continue;
                    bool before = (y >= 4);
                    if (before) for (k = 1; k <= 4; k++) if (Modules[y - k, x]) { before = false; break; }
                    bool after = (y + 10 < n);
                    if (after) for (k = 7; k <= 10; k++) if (Modules[y + k, x]) { after = false; break; }
                    if (before || after) result += 40;
                }

            // N4: dark/light balance
            int darkCount = 0;
            for (y = 0; y < n; y++) for (x = 0; x < n; x++) if (Modules[y, x]) darkCount++;
            int total = n * n;
            int kk = (Math.Abs(darkCount * 20 - total * 10) + total - 1) / total - 1;
            result += kk * 10;

            return result;
        }

        // --------------------------------------------------------------------
        //  RENDER TO BITMAP
        // --------------------------------------------------------------------
        public Bitmap ToBitmap(int scale)
        {
            int quiet = 4;
            int dim = (Size + quiet * 2) * scale;
            Bitmap bmp = new Bitmap(dim, dim, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(30, 22, 0)))
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                            if (Modules[y, x])
                                g.FillRectangle(br, (x + quiet) * scale, (y + quiet) * scale, scale, scale);
            }
            return bmp;
        }
    }

    // ========================================================================
    //  QR DIALOG — phone scan / IP selector / save PNG / copy URL (localized)
    // ========================================================================
    public class QrForm : Form
    {
        private List<string> urls;
        private int currentIndex;
        private Bitmap qrBmp;
        private PictureBox pb;
        private Label urlLabel;

        public QrForm(List<string> urlList, int initialIndex)
        {
            urls = urlList;
            currentIndex = (initialIndex >= 0 && initialIndex < urls.Count) ? initialIndex : 0;

            this.Text = "GoldShare - QR";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(253, 250, 243);
            this.RightToLeft = L.IsArabic ? RightToLeft.Yes : RightToLeft.No;
            try { this.Font = new Font("Segoe UI", 9f); } catch { this.Font = new Font("Tahoma", 8.25f); }

            // gold header
            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 56;
            head.Paint += new PaintEventHandler(delegate(object s, PaintEventArgs e)
            {
                Rectangle r = new Rectangle(0, 0, head.Width, head.Height);
                using (LinearGradientBrush b = new LinearGradientBrush(r,
                    Color.FromArgb(112, 79, 5), Color.FromArgb(212, 175, 55), LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(b, r);
                using (Pen p = new Pen(Color.FromArgb(246, 228, 168), 3))
                    e.Graphics.DrawLine(p, 0, head.Height - 2, head.Width, head.Height - 2);
                TextRenderer.DrawText(e.Graphics, L.T("qr_header"),
                    new Font("Tahoma", 12f, FontStyle.Bold), head.ClientRectangle,
                    Color.FromArgb(255, 248, 224),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            });
            this.Controls.Add(head);

            Label hint = new Label();
            hint.Text = L.T("qr_hint");
            hint.Location = new Point(20, 64);
            hint.Size = new Size(520, 18);
            hint.ForeColor = Color.FromArgb(90, 66, 26);
            this.Controls.Add(hint);

            int y = 86;

            // ----- IP selector (only when more than one address exists) -----
            if (urls.Count > 1)
            {
                Label lblSel = new Label();
                lblSel.Text = L.T("qr_for_addr");
                lblSel.Location = new Point(20, y + 3);
                lblSel.Size = new Size(100, 18);
                lblSel.ForeColor = Color.FromArgb(90, 66, 26);
                this.Controls.Add(lblSel);

                ComboBox cbo = new ComboBox();
                cbo.DropDownStyle = ComboBoxStyle.DropDownList;
                cbo.Location = new Point(124, y);
                cbo.Size = new Size(416, 24);
                cbo.BackColor = Color.FromArgb(255, 253, 244);
                cbo.ForeColor = Color.FromArgb(122, 86, 6);
                cbo.Font = new Font("Verdana", 8.25f, FontStyle.Bold);
                foreach (string u in urls) cbo.Items.Add(u);
                cbo.SelectedIndex = currentIndex;
                cbo.SelectedIndexChanged += new EventHandler(delegate(object s, EventArgs e)
                {
                    currentIndex = cbo.SelectedIndex;
                    RegenerateQr();
                });
                this.Controls.Add(cbo);

                y += 30;
            }

            // ----- QR picture box (fixed 352x352, centered) -----
            pb = new PictureBox();
            pb.BorderStyle = BorderStyle.FixedSingle;
            pb.BackColor = Color.White;
            pb.Size = new Size(352, 352);
            pb.SizeMode = PictureBoxSizeMode.CenterImage;
            pb.Location = new Point(104, y);
            this.Controls.Add(pb);

            y += 352 + 10;

            // ----- url label -----
            urlLabel = new Label();
            urlLabel.Location = new Point(20, y);
            urlLabel.Size = new Size(520, 20);
            urlLabel.TextAlign = ContentAlignment.MiddleCenter;
            urlLabel.ForeColor = Color.FromArgb(122, 86, 6);
            urlLabel.Font = new Font("Verdana", 9f, FontStyle.Bold);
            this.Controls.Add(urlLabel);

            y += 28;

            // ----- buttons -----
            GoldButton btnSave = new GoldButton();
            btnSave.Text = L.T("save_png");
            btnSave.Location = new Point(20, y);
            btnSave.Size = new Size(160, 38);
            btnSave.Click += new EventHandler(delegate
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "PNG Image|*.png";
                    sfd.FileName = "GoldShare-QR.png";
                    if (sfd.ShowDialog(this) == DialogResult.OK)
                    {
                        qrBmp.Save(sfd.FileName, ImageFormat.Png);
                        MessageBox.Show(string.Format(L.T("msg_saved"), sfd.FileName),
                            "GoldShare", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            });
            this.Controls.Add(btnSave);

            GoldButton btnCopy = new GoldButton();
            btnCopy.Text = L.T("copy_url");
            btnCopy.Location = new Point(200, y);
            btnCopy.Size = new Size(160, 38);
            btnCopy.Click += new EventHandler(delegate
            {
                try
                {
                    Clipboard.SetText(urls[currentIndex]);
                    MessageBox.Show(L.T("msg_url_copied"), "GoldShare");
                }
                catch { }
            });
            this.Controls.Add(btnCopy);

            GoldButton btnClose = new GoldButton();
            btnClose.Text = L.T("close");
            btnClose.Location = new Point(380, y);
            btnClose.Size = new Size(160, 38);
            btnClose.Click += new EventHandler(delegate { this.Close(); });
            this.Controls.Add(btnClose);

            this.ClientSize = new Size(560, y + 38 + 14);

            // initial QR render
            RegenerateQr();

            this.FormClosed += new FormClosedEventHandler(delegate(object s, FormClosedEventArgs e)
            {
                if (qrBmp != null) { pb.Image = null; qrBmp.Dispose(); }
            });
        }

        // ----- rebuild the QR for the currently selected URL -----
        private void RegenerateQr()
        {
            try
            {
                Bitmap old = qrBmp;
                QrCode qr = QrCode.Encode(Encoding.UTF8.GetBytes(urls[currentIndex]));
                int scale = 320 / (qr.Size + 8);
                if (scale < 4) scale = 4;
                if (scale > 10) scale = 10;
                qrBmp = qr.ToBitmap(scale);
                pb.Image = qrBmp;
                urlLabel.Text = urls[currentIndex];
                if (old != null) old.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(L.T("msg_qr_error"), ex.Message), "GoldShare");
            }
        }
    }

    // ========================================================================
    //  GOLD BUTTON (rounded, gradient, hover/press states)
    // ========================================================================
    public class GoldButton : Control
    {
        private bool hovered;
        private bool pressed;

        public GoldButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Tahoma", 8.5f, FontStyle.Bold);   // Tahoma: AR/FR-safe on XP
            Cursor = Cursors.Hand;
            Size = new Size(120, 34);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            GraphicsPath gp = RoundPath(r, 10);
            Color top, bottom, border;
            if (!Enabled)
            {
                top = Color.FromArgb(226, 220, 205); bottom = Color.FromArgb(206, 199, 182);
                border = Color.FromArgb(190, 183, 166);
            }
            else if (pressed)
            {
                top = Color.FromArgb(168, 124, 10); bottom = Color.FromArgb(196, 156, 28);
                border = Color.FromArgb(140, 102, 6);
            }
            else if (hovered)
            {
                top = Color.FromArgb(233, 205, 105); bottom = Color.FromArgb(196, 156, 28);
                border = Color.FromArgb(160, 120, 8);
            }
            else
            {
                top = Color.FromArgb(228, 196, 88); bottom = Color.FromArgb(200, 160, 36);
                border = Color.FromArgb(158, 118, 10);
            }
            using (LinearGradientBrush br = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical))
                g.FillPath(br, gp);
            using (Pen p = new Pen(border)) g.DrawPath(p, gp);
            Color txt = Enabled ? Color.FromArgb(255, 252, 240) : Color.FromArgb(140, 132, 116);
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(r.X, r.Y - 1, r.Width, r.Height), txt,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.RightToLeft);
            gp.Dispose();
        }

        private static GraphicsPath RoundPath(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}