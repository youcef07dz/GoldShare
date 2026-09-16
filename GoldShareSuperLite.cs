// ============================================================================
//  GoldShareSuperLite.cs — RAM-frugal edition of GoldShare (ShareIt-style)
//  Same feature set, engineered to idle under ~10 MB working set on XP-era
//  low-RAM boxes: lazy localization (only the chosen language loads), per-thread
//  reusable IO buffers (no per-request byte[] churn), and an idle RAM governor
//  that force-collects and trims the working set when the server goes quiet.
//  Target: .NET Framework 2.0 — Windows XP
//  Compile: C:\WINDOWS\Microsoft.NET\Framework\v2.0.50727\csc.exe
//           /target:winexe /optimize+ /out:GoldShareSuperLite.exe GoldShareSuperLite.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GoldShare
{
    // ========================================================================
    //  THEME — palettes drive GUI AND hosted HTML
    // ========================================================================
    public class Theme
    {
        public string Code;
        public Color Dark, Mid, Light, Soft, Bg, Text, FieldBg;
        public bool IsDark;

        public Theme(string code, Color dark, Color mid, Color light,
                     Color soft, Color bg, Color text, Color fieldBg, bool isDark)
        { Code=code; Dark=dark; Mid=mid; Light=light; Soft=soft; Bg=bg; Text=text; FieldBg=fieldBg; IsDark=isDark; }

        private static Theme current;
        public static Theme Current { get { if(current==null)current=Themes.Get("gold"); return current; } set { current=value; } }

        public static Color Mix(Color a, Color b, double t)
        {
            int r=a.R+(int)((b.R-a.R)*t+0.5), g=a.G+(int)((b.G-a.G)*t+0.5), b2=a.B+(int)((b.B-a.B)*t+0.5);
            if(r<0)r=0; if(r>255)r=255; if(g<0)g=0; if(g>255)g=255; if(b2<0)b2=0; if(b2>255)b2=255;
            return Color.FromArgb(r,g,b2);
        }
        public static string Hex(Color c){ return "#"+c.R.ToString("x2")+c.G.ToString("x2")+c.B.ToString("x2"); }
        public static string Rgba(Color c, double a)
        { return "rgba("+c.R+","+c.G+","+c.B+","+a.ToString("0.00",CultureInfo.InvariantCulture)+")"; }
    }

    public static class Themes
    {
        public static readonly Theme[] All = new Theme[]
        {
            new Theme("gold",   Color.FromArgb(112,79,5),   Color.FromArgb(184,134,11), Color.FromArgb(233,215,138), Color.FromArgb(236,217,160), Color.FromArgb(253,250,243), Color.FromArgb(90,66,26),  Color.FromArgb(255,253,244), false),
            new Theme("sea",    Color.FromArgb(5,68,94),    Color.FromArgb(24,154,180), Color.FromArgb(117,230,218), Color.FromArgb(190,230,238), Color.FromArgb(240,250,252), Color.FromArgb(15,80,105), Color.FromArgb(244,252,254), false),
            new Theme("nature", Color.FromArgb(25,70,28),   Color.FromArgb(62,137,72),  Color.FromArgb(163,217,119), Color.FromArgb(205,235,190), Color.FromArgb(244,252,242), Color.FromArgb(35,85,40),  Color.FromArgb(247,253,245), false),
            new Theme("fruits", Color.FromArgb(192,57,43),  Color.FromArgb(230,126,34), Color.FromArgb(249,199,79),  Color.FromArgb(250,220,180), Color.FromArgb(255,250,243), Color.FromArgb(150,60,25), Color.FromArgb(255,250,244), false),
            new Theme("space",  Color.FromArgb(26,16,64),   Color.FromArgb(75,46,131),  Color.FromArgb(157,141,241), Color.FromArgb(205,198,240), Color.FromArgb(244,243,252), Color.FromArgb(45,35,90),  Color.FromArgb(247,246,254), false),
            new Theme("fire",   Color.FromArgb(127,29,13),  Color.FromArgb(220,75,26),  Color.FromArgb(249,160,63),  Color.FromArgb(250,205,180), Color.FromArgb(253,245,241), Color.FromArgb(140,40,18), Color.FromArgb(255,247,243), false),
            new Theme("moon",   Color.FromArgb(45,55,72),   Color.FromArgb(100,116,139),Color.FromArgb(203,213,225), Color.FromArgb(215,222,232), Color.FromArgb(246,248,251), Color.FromArgb(55,65,80),  Color.FromArgb(249,251,253), false),
            new Theme("midnight",Color.FromArgb(8,12,28),   Color.FromArgb(30,41,82),   Color.FromArgb(96,120,200),  Color.FromArgb(38,50,92),    Color.FromArgb(15,20,40),   Color.FromArgb(215,224,248),Color.FromArgb(24,32,62),   true)
        };
        public static Theme Get(string code){ for(int i=0;i<All.Length;i++) if(All[i].Code==code) return All[i]; return All[0]; }
        public static int IndexOf(string code){ for(int i=0;i<All.Length;i++) if(All[i].Code==code) return i; return 0; }
    }

    // ========================================================================
    //  PERSISTENT SETTINGS (GoldShare.ini)
    // ========================================================================
    public static class Settings
    {
        public static int Port=8080, UploadMB=512, SpeedKB=0, FontSize=1;
        public static string Language="en", Theme="gold", PasswordHash="";
        public static bool AutoStart=false, AllowDelete=false, Tray=true,
                           SavePos=false, Beep=true, LogFile=true;
        public static int PosX=-1, PosY=-1;

        public static string IniPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GoldShare.ini"); } }

        public static void Load()
        {
            try
            {
                if(!File.Exists(IniPath)) return;
                string[] lines=File.ReadAllLines(IniPath,Encoding.UTF8);
                for(int i=0;i<lines.Length;i++)
                {
                    string ln=lines[i].Trim();
                    if(ln.Length==0||ln.StartsWith("[")||ln.StartsWith(";")||ln.StartsWith("#")) continue;
                    int eq=ln.IndexOf('='); if(eq<=0) continue;
                    string k=ln.Substring(0,eq).Trim().ToLower(), v=ln.Substring(eq+1).Trim();
                    int n;
                    switch(k)
                    {
                        case "port": if(int.TryParse(v,out n)&&n>=1&&n<=65535) Port=n; break;
                        case "uploadmb": if(int.TryParse(v,out n)&&n>=1&&n<=4096) UploadMB=n; break;
                        case "speedkb": if(int.TryParse(v,out n)&&n>=0&&n<=1048576) SpeedKB=n; break;
                        case "fontsize": if(int.TryParse(v,out n)&&n>=0&&n<=2) FontSize=n; break;
                        case "posx": if(int.TryParse(v,out n)) PosX=n; break;
                        case "posy": if(int.TryParse(v,out n)) PosY=n; break;
                        case "language": if(L.Langs.Contains(v)) Language=v; break;
                        case "theme": if(Themes.Get(v).Code==v) Theme=v; break;
                        case "passwordhash": PasswordHash=v; break;
                        case "autostart": AutoStart=(v=="true"); break;
                        case "allowdelete": AllowDelete=(v=="true"); break;
                        case "tray": Tray=(v=="true"); break;
                        case "savepos": SavePos=(v=="true"); break;
                        case "beep": Beep=(v=="true"); break;
                        case "logfile": LogFile=(v=="true"); break;
                    }
                }
            } catch { }
        }

        public static void Save()
        {
            try
            {
                StringBuilder sb=new StringBuilder();
                sb.AppendLine("[Settings]");
                sb.AppendLine("Port="+Port);
                sb.AppendLine("UploadMB="+UploadMB);
                sb.AppendLine("SpeedKB="+SpeedKB);
                sb.AppendLine("FontSize="+FontSize);
                sb.AppendLine("Language="+Language);
                sb.AppendLine("Theme="+Theme);
                sb.AppendLine("PasswordHash="+PasswordHash);
                sb.AppendLine("AutoStart="+(AutoStart?"true":"false"));
                sb.AppendLine("AllowDelete="+(AllowDelete?"true":"false"));
                sb.AppendLine("Tray="+(Tray?"true":"false"));
                sb.AppendLine("SavePos="+(SavePos?"true":"false"));
                sb.AppendLine("Beep="+(Beep?"true":"false"));
                sb.AppendLine("LogFile="+(LogFile?"true":"false"));
                sb.AppendLine("PosX="+PosX);
                sb.AppendLine("PosY="+PosY);
                File.WriteAllText(IniPath,sb.ToString(),Encoding.UTF8);
            } catch { }
        }

        public static string Hash(string s)
        {
            uint h=2166136261;
            foreach(char c in s) h=(h^(uint)c)*16777619;
            return h.ToString("x8");
        }
    }

    // ========================================================================
    //  LOG FILE (with 1 MB rotation)
    // ========================================================================
    public static class LogF
    {
        private static object lk=new object();
        public static void W(string s)
        {
            if(!Settings.LogFile) return;
            try
            {
                string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GoldShare.log");
                lock(lk)
                {
                    FileInfo fi=new FileInfo(p);
                    if(fi.Exists&&fi.Length>1048576)
                    {
                        string old=p+".old";
                        if(File.Exists(old)) File.Delete(old);
                        File.Move(p,old);
                    }
                    File.AppendAllText(p,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"  "+s+"\r\n",Encoding.UTF8);
                }
            } catch { }
        }
    }

    // ========================================================================
    //  LOCALIZATION (en fr ar es de tr ru)
    // ========================================================================
    public static class L
    {
        private static string lang="en";
        private static Dictionary<string,string> dict;
        public static readonly List<string> Langs=new List<string>(
            new string[]{"en","fr","ar","es","de","tr","ru"});

        private static readonly Dictionary<string,string> En=MakeEn(); // fallback, always present

        public static string Lang { get { return lang; } }
        public static bool IsArabic { get { return lang=="ar"; } }

        // SuperLite: only the selected language (plus English) is materialized when
        // the app starts — the other five dictionaries are never built unless chosen,
        // saving ~150 KB of cold-start heap on a 256 MB box.
        public static void Set(string code)
        {
            lang=Langs.Contains(code)?code:"en";
            dict=Build(lang);
        }
        private static Dictionary<string,string> Build(string code)
        {
            if(code=="fr") return MakeFr();
            if(code=="ar") return MakeAr();
            if(code=="es") return MakeEs();
            if(code=="de") return MakeDe();
            if(code=="tr") return MakeTr();
            if(code=="ru") return MakeRu();
            return En;
        }
        public static string T(string key)
        {
            string s;
            if(dict!=null&&dict.TryGetValue(key,out s)) return s;
            if(En.TryGetValue(key,out s)) return s;
            return key;
        }

        private static void A(Dictionary<string,string> d, string k, string v){ d.Add(k,v); }

        private static Dictionary<string,string> MakeEn()
        {
            Dictionary<string,string> d=new Dictionary<string,string>();
            A(d,"title","GoldShare - Premium File Sharing");
            A(d,"hdr_sub","Premium File Sharing \u2014 Upload, Download & QR Connect");
            A(d,"start","START SERVER"); A(d,"stop","STOP SERVER");
            A(d,"open_browser","OPEN IN BROWSER"); A(d,"open_folder","OPEN FOLDER");
            A(d,"copy_url","COPY URL"); A(d,"qr_scan","QR / SCAN");
            A(d,"gear","\u2699 SETTINGS"); A(d,"port","Port:"); A(d,"folder","Folder:");
            A(d,"addresses","Server addresses - select one, then COPY URL or QR / SCAN:");
            A(d,"log","Activity log:"); A(d,"language","Language:"); A(d,"theme_lbl","Theme:");
            A(d,"theme_gold","Gold"); A(d,"theme_sea","Sea"); A(d,"theme_nature","Nature");
            A(d,"theme_fruits","Fruits"); A(d,"theme_space","Space"); A(d,"theme_fire","Fire");
            A(d,"theme_moon","Moon"); A(d,"theme_midnight","Midnight");
            A(d,"msg_ready","Ready. Press 'START SERVER'.");
            A(d,"msg_already","GoldShare is already running.\r\n\r\nCheck the taskbar / tray.");
            A(d,"msg_admin","Administrator privileges are recommended.\r\n\r\nContinue without them anyway?");
            A(d,"msg_start_first","Start the server first.");
            A(d,"msg_bad_port","Enter a valid port (1-65535).");
            A(d,"msg_start_fail","Could not start server:\r\n{0}\r\n\r\nTry another port.");
            A(d,"msg_open_fail","Could not open folder:\r\n{0}");
            A(d,"msg_copied","Copied: {0}");
            A(d,"msg_started","Server started on port {0}");
            A(d,"msg_tip","Tip: click 'QR / SCAN' and point a phone camera at the code.");
            A(d,"msg_stopped","Server stopped.");
            A(d,"msg_qr_preview","QR preview uses port {0} (server not running)");
            A(d,"msg_upload_done","Upload finished: {0} file(s) received");
            A(d,"msg_uploaded","Uploaded: {0} ({1})");
            A(d,"msg_files_added","{0} file(s) added to Downloads (drag & drop)");
            A(d,"msg_copy_failed","Copy failed: {0}");
            A(d,"msg_qr_error","QR error: {0}");
            A(d,"msg_url_copied","URL copied to clipboard.");
            A(d,"msg_saved","QR image saved:\r\n{0}");
            A(d,"msg_port_restart","Port changed. Restart the server to apply.");
            A(d,"msg_font_restart","Font size applies after restart.");
            A(d,"msg_tray_min","Still running here in the tray.");
            A(d,"msg_zip_none","No files selected.");
            A(d,"msg_zip_ok","ZIP sent: {0} file(s), {1}");
            A(d,"msg_deleted","Deleted: {0}");
            A(d,"msg_del_deny","Delete denied (wrong PIN or disabled).");
            A(d,"msg_note_in","Note received: {0}");
            A(d,"status_run","\u25CF RUNNING :{0}"); A(d,"status_stop","\u25CB STOPPED");
            A(d,"conns_fmt","\u2022 {0} conns");
            A(d,"tray_open","Open"); A(d,"tray_exit","Exit");
            A(d,"set_title","GoldShare - Settings");
            A(d,"set_port","Port:"); A(d,"set_uplim","Upload limit (MB):");
            A(d,"set_speed","Download speed limit (KB/s, 0 = unlimited):");
            A(d,"set_autostart","Start server automatically on launch");
            A(d,"set_pass","Access PIN (blank = none):");
            A(d,"set_pass_hint","Visitors must enter this PIN before opening the page.");
            A(d,"set_allowdel","Allow deleting files from the web page (needs PIN)");
            A(d,"set_tray","Minimize to system tray");
            A(d,"set_savepos","Remember window position");
            A(d,"set_beep","Sound when a file is received");
            A(d,"set_log","Write activity to GoldShare.log");
            A(d,"set_font","Font size:"); A(d,"font_s","Small"); A(d,"font_m","Medium"); A(d,"font_l","Large");
            A(d,"save","SAVE"); A(d,"cancel","CANCEL");
            A(d,"tip_start","Start / stop the HTTP server (F5)");
            A(d,"tip_browser","Open the share page locally (in your browser)");
            A(d,"tip_folder","Open the shared folder in Explorer (Ctrl+O)");
            A(d,"tip_copy","Copy the selected URL (Ctrl+U)");
            A(d,"tip_qr","Show QR code to scan with a phone (Ctrl+Q)");
            A(d,"tip_gear","Open settings (Ctrl+G)");
            A(d,"tip_port","Server port (1-65535)");
            A(d,"tip_theme","Change color theme (GUI + web page)");
            A(d,"tip_lang","Interface language");
            A(d,"tip_log","Server activity");
            A(d,"qr_header","\u25C6  SCAN WITH PHONE CAMERA  \u25C6");
            A(d,"qr_hint","Open the camera on your phone / tablet and point it at this code:");
            A(d,"qr_for_addr","QR for address:");
            A(d,"save_png","SAVE PNG"); A(d,"close","CLOSE");
            A(d,"page_title","GoldShare - File Sharing");
            A(d,"page_sub","Premium File Sharing \u2014 Upload & Download");
            A(d,"page_dl","\u2B07 DOWNLOAD FILES"); A(d,"page_up","\u2B06 UPLOAD TO THIS DEVICE");
            A(d,"page_received","\uD83D\uDCE6 RECEIVED FILES"); A(d,"page_no_files","No files yet.");
            A(d,"page_upload_btn","UPLOAD"); A(d,"page_cam","\uD83D\uDCF7 Photo");
            A(d,"page_search_ph","\uD83D\uDD0D Search files...");
            A(d,"sort_name","A-Z"); A(d,"sort_size","Size"); A(d,"sort_new","New");
            A(d,"view_grid","\u25A6 Grid"); A(d,"view_list","\u2630 List");
            A(d,"btn_zip","\u2B07 ZIP SELECTED");
            A(d,"page_storage_f","\uD83D\uDCBE {0} free of {1}");
            A(d,"page_note_h","\uD83D\uDCDD NOTE TO PC");
            A(d,"page_note_ph","Type a message... it appears on the PC & its clipboard");
            A(d,"page_note_send","SEND \u27A4"); A(d,"page_note_ok","\u2713 Sent to PC");
            A(d,"page_del_ask","PIN to delete:"); A(d,"page_wrong_pin","Wrong PIN or delete disabled.");
            A(d,"page_login_h","\uD83D\uDD12 GoldShare"); A(d,"page_login_btn","ENTER");
            A(d,"page_bad_pass","Wrong PIN. Try again.");
            return d;
        }

        private static Dictionary<string,string> MakeFr()
        {
            Dictionary<string,string> d=new Dictionary<string,string>();
            A(d,"title","GoldShare - Partage de fichiers Premium");
            A(d,"hdr_sub","Partage Premium \u2014 Envoi, T\u00E9l\u00E9chargement & QR");
            A(d,"start","D\u00C9MARRER LE SERVEUR"); A(d,"stop","ARR\u00CATER LE SERVEUR");
            A(d,"open_browser","OUVRIR LE NAVIGATEUR"); A(d,"open_folder","OUVRIR LE DOSSIER");
            A(d,"copy_url","COPIER L'URL"); A(d,"qr_scan","QR / SCAN");
            A(d,"gear","\u2699 R\u00C9GLAGES"); A(d,"port","Port :"); A(d,"folder","Dossier :");
            A(d,"addresses","Adresses du serveur - s\u00E9lectionnez, puis COPIER ou QR / SCAN :");
            A(d,"log","Journal :"); A(d,"language","Langue :"); A(d,"theme_lbl","Th\u00E8me :");
            A(d,"theme_gold","Or"); A(d,"theme_sea","Mer"); A(d,"theme_nature","Nature");
            A(d,"theme_fruits","Fruits"); A(d,"theme_space","Espace"); A(d,"theme_fire","Feu");
            A(d,"theme_moon","Lune"); A(d,"theme_midnight","Minuit");
            A(d,"msg_ready","Pr\u00EAt. Cliquez sur \u00AB D\u00C9MARRER \u00BB.");
            A(d,"msg_already","GoldShare est d\u00E9j\u00E0 en cours.\r\n\r\nVoir la barre des t\u00E2ches / tray.");
            A(d,"msg_admin","Privil\u00E8ges administrateur recommand\u00E9s.\r\n\r\nContinuer sans ?");
            A(d,"msg_start_first","D\u00E9marrez d'abord le serveur.");
            A(d,"msg_bad_port","Entrez un port valide (1-65535).");
            A(d,"msg_start_fail","Impossible de d\u00E9marrer :\r\n{0}\r\n\r\nEssayez un autre port.");
            A(d,"msg_open_fail","Impossible d'ouvrir le dossier :\r\n{0}");
            A(d,"msg_copied","Copi\u00E9 : {0}");
            A(d,"msg_started","Serveur d\u00E9marr\u00E9 sur le port {0}");
            A(d,"msg_tip","Astuce : cliquez \u00AB QR / SCAN \u00BB et visez avec le t\u00E9l\u00E9phone.");
            A(d,"msg_stopped","Serveur arr\u00EAt\u00E9.");
            A(d,"msg_qr_preview","Aper\u00E7u QR port {0} (serveur arr\u00EAt\u00E9)");
            A(d,"msg_upload_done","Envoi termin\u00E9 : {0} fichier(s) re\u00E7u(s)");
            A(d,"msg_uploaded","Re\u00E7u : {0} ({1})");
            A(d,"msg_files_added","{0} fichier(s) ajout\u00E9(s) (glisser-d\u00E9poser)");
            A(d,"msg_copy_failed","\u00C9chec de copie : {0}");
            A(d,"msg_qr_error","Erreur QR : {0}");
            A(d,"msg_url_copied","URL copi\u00E9e.");
            A(d,"msg_saved","Image QR enregistr\u00E9e :\r\n{0}");
            A(d,"msg_port_restart","Port modifi\u00E9. Red\u00E9marrez le serveur.");
            A(d,"msg_font_restart","Taille de police appliqu\u00E9e apr\u00E8s red\u00E9marrage.");
            A(d,"msg_tray_min","Le serveur tourne dans la zone de notification.");
            A(d,"msg_zip_none","Aucun fichier s\u00E9lectionn\u00E9.");
            A(d,"msg_zip_ok","ZIP envoy\u00E9 : {0} fichier(s), {1}");
            A(d,"msg_deleted","Supprim\u00E9 : {0}");
            A(d,"msg_del_deny","Suppression refus\u00E9e (PIN faux ou d\u00E9sactiv\u00E9e).");
            A(d,"msg_note_in","Note re\u00E7ue : {0}");
            A(d,"status_run","\u25CF EN MARCHE :{0}"); A(d,"status_stop","\u25CB ARR\u00CAT\u00C9");
            A(d,"conns_fmt","\u2022 {0} conn.");
            A(d,"tray_open","Ouvrir"); A(d,"tray_exit","Quitter");
            A(d,"set_title","GoldShare - R\u00E9glages");
            A(d,"set_port","Port :"); A(d,"set_uplim","Limite d'envoi (Mo) :");
            A(d,"set_speed","Limite de t\u00E9l\u00E9chargement (Ko/s, 0 = illimit\u00E9) :");
            A(d,"set_autostart","D\u00E9marrer le serveur au lancement");
            A(d,"set_pass","PIN d'acc\u00E8s (vide = aucun) :");
            A(d,"set_pass_hint","Les visiteurs devront saisir ce PIN.");
            A(d,"set_allowdel","Autoriser la suppression via la page (PIN requis)");
            A(d,"set_tray","R\u00E9duire dans la zone de notification");
            A(d,"set_savepos","M\u00E9moriser la position de la fen\u00EAtre");
            A(d,"set_beep","Son quand un fichier est re\u00E7u");
            A(d,"set_log","\u00C9crire le journal dans GoldShare.log");
            A(d,"set_font","Taille de police :"); A(d,"font_s","Petite"); A(d,"font_m","Moyenne"); A(d,"font_l","Grande");
            A(d,"save","ENREGISTRER"); A(d,"cancel","ANNULER");
            A(d,"tip_start","D\u00E9marrer / arr\u00EAter le serveur (F5)");
            A(d,"tip_browser","Ouvrir la page localement");
            A(d,"tip_folder","Ouvrir le dossier (Ctrl+O)");
            A(d,"tip_copy","Copier l'URL s\u00E9lectionn\u00E9e (Ctrl+U)");
            A(d,"tip_qr","Afficher le QR (Ctrl+Q)");
            A(d,"tip_gear","R\u00E9glages (Ctrl+G)");
            A(d,"tip_port","Port du serveur"); A(d,"tip_theme","Th\u00E8me de couleurs");
            A(d,"tip_lang","Langue de l'interface"); A(d,"tip_log","Activit\u00E9 du serveur");
            A(d,"qr_header","\u25C6  SCANNEZ AVEC LA CAM\u00C9RA  \u25C6");
            A(d,"qr_hint","Ouvrez la cam\u00E9ra du t\u00E9l\u00E9phone et visez ce code :");
            A(d,"qr_for_addr","QR pour l'adresse :");
            A(d,"save_png","ENREGISTRER PNG"); A(d,"close","FERMER");
            A(d,"page_title","GoldShare - Partage");
            A(d,"page_sub","Partage Premium \u2014 Envoi & T\u00E9l\u00E9chargement");
            A(d,"page_dl","\u2B07 T\u00C9L\u00C9CHARGER"); A(d,"page_up","\u2B06 ENVOYER VERS CE PC");
            A(d,"page_received","\uD83D\uDCE6 FICHIERS RE\u00C7US"); A(d,"page_no_files","Aucun fichier.");
            A(d,"page_upload_btn","ENVOYER"); A(d,"page_cam","\uD83D\uDCF7 Photo");
            A(d,"page_search_ph","\uD83D\uDD0D Rechercher...");
            A(d,"sort_name","A-Z"); A(d,"sort_size","Taille"); A(d,"sort_new","Nouveau");
            A(d,"view_grid","\u25A6 Grille"); A(d,"view_list","\u2630 Liste");
            A(d,"btn_zip","\u2B07 ZIP S\u00C9LECTIONN\u00C9");
            A(d,"page_storage_f","\uD83D\uDCBE {0} libres sur {1}");
            A(d,"page_note_h","\uD83D\uDCDD NOTE VERS PC");
            A(d,"page_note_ph","\u00C9crivez un message... affich\u00E9 sur le PC + presse-papiers");
            A(d,"page_note_send","ENVOYER \u27A4"); A(d,"page_note_ok","\u2713 Envoy\u00E9 au PC");
            A(d,"page_del_ask","PIN pour supprimer :"); A(d,"page_wrong_pin","PIN faux ou suppression d\u00E9sactiv\u00E9e.");
            A(d,"page_login_h","\uD83D\uDD12 GoldShare"); A(d,"page_login_btn","ENTRER");
            A(d,"page_bad_pass","PIN incorrect.");
            return d;
        }

        private static Dictionary<string,string> MakeAr()
        {
            Dictionary<string,string> d=new Dictionary<string,string>();
            A(d,"title","GoldShare - \u0645\u0634\u0627\u0631\u0643\u0629 \u0627\u0644\u0645\u0644\u0641\u0627\u062A \u0627\u0644\u0645\u0645\u064A\u0632\u0629");
            A(d,"hdr_sub","\u0645\u0634\u0627\u0631\u0643\u0629 \u0645\u0644\u0641\u0627\u062A \u0645\u0645\u064A\u0632\u0629 \u2014 \u0631\u0641\u0639 \u0648\u062A\u0646\u0632\u064A\u0644 \u0648QR");
            A(d,"start","\u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645"); A(d,"stop","\u0625\u064A\u0642\u0627\u0641 \u0627\u0644\u062E\u0627\u062F\u0645");
            A(d,"open_browser","\u0641\u062A\u062D \u0641\u064A \u0627\u0644\u0645\u062A\u0635\u0641\u062D"); A(d,"open_folder","\u0641\u062A\u062D \u0627\u0644\u0645\u062C\u0644\u062F");
            A(d,"copy_url","\u0646\u0633\u062E \u0627\u0644\u0631\u0627\u0628\u0637"); A(d,"qr_scan","\u0631\u0645\u0632 QR");
            A(d,"gear","\u2699 \u0627\u0644\u0625\u0639\u062F\u0627\u062F\u0627\u062A"); A(d,"port","\u0627\u0644\u0645\u0646\u0641\u0630:"); A(d,"folder","\u0627\u0644\u0645\u062C\u0644\u062F:");
            A(d,"addresses","\u0639\u0646\u0627\u0648\u064A\u0646 \u0627\u0644\u062E\u0627\u062F\u0645 - \u062D\u062F\u062F \u0639\u0646\u0648\u0627\u0646\u0627\u064B \u062B\u0645 \u0646\u0633\u062E \u0623\u0648 QR:");
            A(d,"log","\u0627\u0644\u0633\u062C\u0644:"); A(d,"language","\u0627\u0644\u0644\u063A\u0629:"); A(d,"theme_lbl","\u0627\u0644\u0633\u0645\u0629:");
            A(d,"theme_gold","\u0630\u0647\u0628\u064A"); A(d,"theme_sea","\u0628\u062D\u0631"); A(d,"theme_nature","\u0637\u0628\u064A\u0639\u0629");
            A(d,"theme_fruits","\u0641\u0648\u0627\u0643\u0647"); A(d,"theme_space","\u0641\u0636\u0627\u0621"); A(d,"theme_fire","\u0646\u0627\u0631");
            A(d,"theme_moon","\u0642\u0645\u0631"); A(d,"theme_midnight","\u0645\u0646\u062A\u0635\u0641 \u0627\u0644\u0644\u064A\u0644");
            A(d,"msg_ready","\u062C\u0627\u0647\u0632. \u0627\u0636\u063A\u0637 '\u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645'.");
            A(d,"msg_already","GoldShare \u064A\u0639\u0645\u0644 \u0628\u0627\u0644\u0641\u0639\u0644.\r\n\r\n\u062A\u062D\u0642\u0642 \u0645\u0646 \u0634\u0631\u064A\u0637 \u0627\u0644\u0645\u0647\u0627\u0645.");
            A(d,"msg_admin","\u064A\u064F\u0646\u0635\u062D \u0628\u0627\u0645\u062A\u064A\u0627\u0632\u0627\u062A \u0627\u0644\u0645\u0633\u0624\u0648\u0644.\r\n\r\n\u0627\u0644\u0645\u062A\u0627\u0628\u0639\u0629 \u0628\u062F\u0648\u0646\u0647\u0627\u061F");
            A(d,"msg_start_first","\u0634\u063A\u0651\u0644 \u0627\u0644\u062E\u0627\u062F\u0645 \u0623\u0648\u0644\u0627\u064B.");
            A(d,"msg_bad_port","\u0623\u062F\u062E\u0644 \u0645\u0646\u0641\u0630\u0627\u064B \u0635\u062D\u064A\u062D\u0627\u064B (1-65535).");
            A(d,"msg_start_fail","\u062A\u0639\u0630\u0651\u0631 \u0627\u0644\u062A\u0634\u063A\u064A\u0644:\r\n{0}\r\n\r\n\u062C\u0631\u0651\u0628 \u0645\u0646\u0641\u0630\u0627\u064B \u0622\u062E\u0631.");
            A(d,"msg_open_fail","\u062A\u0639\u0630\u0651\u0631 \u0641\u062A\u062D \u0627\u0644\u0645\u062C\u0644\u062F:\r\n{0}");
            A(d,"msg_copied","\u062A\u0645 \u0627\u0644\u0646\u0633\u062E: {0}");
            A(d,"msg_started","\u062A\u0645 \u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645 \u0639\u0644\u0649 \u0627\u0644\u0645\u0646\u0641\u0630 {0}");
            A(d,"msg_tip","\u0627\u0636\u063A\u0637 '\u0631\u0645\u0632 QR' \u0648\u0648\u062C\u0651\u0647 \u0643\u0627\u0645\u064A\u0631\u0627 \u0627\u0644\u0647\u0627\u062A\u0641.");
            A(d,"msg_stopped","\u062A\u0645 \u0625\u064A\u0642\u0627\u0641 \u0627\u0644\u062E\u0627\u062F\u0645.");
            A(d,"msg_qr_preview","\u0645\u0639\u0627\u064A\u0646\u0629 QR \u0627\u0644\u0645\u0646\u0641\u0630 {0} (\u0627\u0644\u062E\u0627\u062F\u0645 \u0645\u0648\u0642\u0641)");
            A(d,"msg_upload_done","\u0627\u0646\u062A\u0647\u0649 \u0627\u0644\u0631\u0641\u0639: {0} \u0645\u0644\u0641");
            A(d,"msg_uploaded","\u0631\u0641\u0639: {0} ({1})");
            A(d,"msg_files_added","\u0623\u064F\u0636\u064A\u0641 {0} \u0645\u0644\u0641 (\u0633\u062D\u0628 \u0648\u0625\u0641\u0644\u0627\u062A)");
            A(d,"msg_copy_failed","\u0641\u0634\u0644 \u0627\u0644\u0646\u0633\u062E: {0}");
            A(d,"msg_qr_error","\u062E\u0637\u0623 QR: {0}");
            A(d,"msg_url_copied","\u062A\u0645 \u0646\u0633\u062E \u0627\u0644\u0631\u0627\u0628\u0637.");
            A(d,"msg_saved","\u062D\u064F\u0641\u0638\u062A \u0635\u0648\u0631\u0629 QR:\r\n{0}");
            A(d,"msg_port_restart","\u062A\u063A\u064A\u0651\u0631 \u0627\u0644\u0645\u0646\u0641\u0630. \u0623\u0639\u062F \u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645.");
            A(d,"msg_font_restart","\u062D\u062C\u0645 \u0627\u0644\u062E\u0637 \u064A\u064F\u0637\u0628\u0642 \u0628\u0639\u062F \u0625\u0639\u0627\u062F\u0629 \u0627\u0644\u062A\u0634\u063A\u064A\u0644.");
            A(d,"msg_tray_min","\u0627\u0644\u062E\u0627\u062F\u0645 \u064A\u0639\u0645\u0644 \u0641\u064A \u0639\u062F\u0629 \u0627\u0644\u0646\u0638\u0627\u0645.");
            A(d,"msg_zip_none","\u0644\u0627 \u0645\u0644\u0641\u0627\u062A \u0645\u062D\u062F\u062F\u0629.");
            A(d,"msg_zip_ok","\u062A\u0645 \u0625\u0631\u0633\u0627\u0644 ZIP: {0} \u0645\u0644\u0641\u060C {1}");
            A(d,"msg_deleted","\u062D\u064F\u0630\u0641: {0}");
            A(d,"msg_del_deny","\u0631\u064F\u0641\u0636 \u0627\u0644\u062D\u0630\u0641 (\u0631\u0642\u0645 \u062E\u0627\u0637\u0623 \u0623\u0648 \u0645\u0639\u0637\u0651\u0644).");
            A(d,"msg_note_in","\u0645\u0644\u0627\u062D\u0638\u0629 \u0648\u0627\u0635\u0644\u0629: {0}");
            A(d,"status_run","\u25CF \u064A\u0639\u0645\u0644 :{0}"); A(d,"status_stop","\u25CB \u0645\u0648\u0642\u0641");
            A(d,"conns_fmt","\u2022 {0} \u0627\u062A\u0635\u0627\u0644");
            A(d,"tray_open","\u0641\u062A\u062D"); A(d,"tray_exit","\u062E\u0631\u0648\u062C");
            A(d,"set_title","GoldShare - \u0627\u0644\u0625\u0639\u062F\u0627\u062F\u0627\u062A");
            A(d,"set_port","\u0627\u0644\u0645\u0646\u0641\u0630:"); A(d,"set_uplim","\u062D\u062F \u0627\u0644\u0631\u0641\u0639 (\u0645\u0628):");
            A(d,"set_speed","\u062D\u062F \u0633\u0631\u0639\u0629 \u0627\u0644\u062A\u0646\u0632\u064A\u0644 (\u0643\u0628/\u062B\u060C 0=\u0628\u0644\u0627 \u062D\u062F):");
            A(d,"set_autostart","\u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645 \u062A\u0644\u0642\u0627\u0626\u064A\u0627\u064B");
            A(d,"set_pass","\u0631\u0642\u0645 \u0627\u0644\u062F\u062E\u0648\u0644 (\u0641\u0627\u0631\u063A=\u0628\u0644\u0627):");
            A(d,"set_pass_hint","\u064A\u0637\u0644\u0628 \u0645\u0646 \u0627\u0644\u0632\u0648\u0627\u0631 \u0625\u062F\u062E\u0627\u0644 \u0647\u0630\u0627 \u0627\u0644\u0631\u0642\u0645.");
            A(d,"set_allowdel","\u0627\u0644\u0633\u0645\u0627\u062D \u0628\u0627\u0644\u062D\u0630\u0641 \u0645\u0646 \u0627\u0644\u0635\u0641\u062D\u0629 (\u064A\u0644\u0632\u0645 \u0631\u0642\u0645)");
            A(d,"set_tray","\u062A\u0635\u063A\u064A\u0631 \u0625\u0644\u0649 \u0639\u062F\u0629 \u0627\u0644\u0646\u0638\u0627\u0645");
            A(d,"set_savepos","\u062A\u0630\u0643\u0651\u0631 \u0645\u0648\u0636\u0639 \u0627\u0644\u0646\u0627\u0641\u0630\u0629");
            A(d,"set_beep","\u0635\u0648\u062A \u0639\u0646\u062F \u0627\u0633\u062A\u0644\u0627\u0645 \u0645\u0644\u0641");
            A(d,"set_log","\u062A\u0633\u062C\u064A\u0644 \u0627\u0644\u0646\u0634\u0627\u0637 \u0641\u064A GoldShare.log");
            A(d,"set_font","\u062D\u062C\u0645 \u0627\u0644\u062E\u0637:"); A(d,"font_s","\u0635\u063A\u064A\u0631"); A(d,"font_m","\u0645\u062A\u0648\u0633\u0637"); A(d,"font_l","\u0643\u0628\u064A\u0631");
            A(d,"save","\u062D\u0641\u0638"); A(d,"cancel","\u0625\u0644\u063A\u0627\u0621");
            A(d,"tip_start","\u062A\u0634\u063A\u064A\u0644/\u0625\u064A\u0642\u0627\u0641 \u0627\u0644\u062E\u0627\u062F\u0645 (F5)");
            A(d,"tip_browser","\u0641\u062A\u062D \u0627\u0644\u0635\u0641\u062D\u0629 \u0645\u062D\u0644\u064A\u0627\u064B");
            A(d,"tip_folder","\u0641\u062A\u062D \u0627\u0644\u0645\u062C\u0644\u062F (Ctrl+O)");
            A(d,"tip_copy","\u0646\u0633\u062E \u0627\u0644\u0631\u0627\u0628\u0637 (Ctrl+U)");
            A(d,"tip_qr","\u0639\u0631\u0636 QR (Ctrl+Q)");
            A(d,"tip_gear","\u0627\u0644\u0625\u0639\u062F\u0627\u062F\u0627\u062A (Ctrl+G)");
            A(d,"tip_port","\u0645\u0646\u0641\u0630 \u0627\u0644\u062E\u0627\u062F\u0645"); A(d,"tip_theme","\u0633\u0645\u0629 \u0627\u0644\u0623\u0644\u0648\u0627\u0646");
            A(d,"tip_lang","\u0644\u063A\u0629 \u0627\u0644\u0648\u0627\u062C\u0647\u0629"); A(d,"tip_log","\u0646\u0634\u0627\u0637 \u0627\u0644\u062E\u0627\u062F\u0645");
            A(d,"qr_header","\u25C6  \u0627\u0645\u0633\u062D \u0628\u0643\u0627\u0645\u064A\u0631\u0627 \u0627\u0644\u0647\u0627\u062A\u0641  \u25C6");
            A(d,"qr_hint","\u0627\u0641\u062A\u062D \u0627\u0644\u0643\u0627\u0645\u064A\u0631\u0627 \u0648\u0648\u062C\u0651\u0647\u0647\u0627 \u0646\u062D\u0648 \u0627\u0644\u0631\u0645\u0632:");
            A(d,"qr_for_addr","QR \u0644\u0644\u0639\u0646\u0648\u0627\u0646:");
            A(d,"save_png","\u062D\u0641\u0638 PNG"); A(d,"close","\u0625\u063A\u0644\u0627\u0642");
            A(d,"page_title","GoldShare - \u0645\u0634\u0627\u0631\u0643\u0629 \u0627\u0644\u0645\u0644\u0641\u0627\u062A");
            A(d,"page_sub","\u0645\u0634\u0627\u0631\u0643\u0629 \u0645\u0645\u064A\u0632\u0629 \u2014 \u0631\u0641\u0639 \u0648\u062A\u0646\u0632\u064A\u0644");
            A(d,"page_dl","\u2B07 \u062A\u0646\u0632\u064A\u0644 \u0627\u0644\u0645\u0644\u0641\u0627\u062A"); A(d,"page_up","\u2B06 \u0631\u0641\u0639 \u0625\u0644\u0649 \u0647\u0630\u0627 \u0627\u0644\u062C\u0647\u0627\u0632");
            A(d,"page_received","\uD83D\uDCE6 \u0627\u0644\u0645\u0644\u0641\u0627\u062A \u0627\u0644\u0645\u0633\u062A\u0644\u0645\u0629"); A(d,"page_no_files","\u0644\u0627 \u0645\u0644\u0641\u0627\u062A \u0628\u0639\u062F.");
            A(d,"page_upload_btn","\u0631\u0641\u0639"); A(d,"page_cam","\uD83D\uDCF7 \u0635\u0648\u0631\u0629");
            A(d,"page_search_ph","\uD83D\uDD0D \u0628\u062D\u062B...");
            A(d,"sort_name","\u0623-\u064A"); A(d,"sort_size","\u0627\u0644\u062D\u062C\u0645"); A(d,"sort_new","\u0627\u0644\u0623\u062D\u062F\u062B");
            A(d,"view_grid","\u25A6 \u0634\u0628\u0643\u0629"); A(d,"view_list","\u2630 \u0642\u0627\u0626\u0645\u0629");
            A(d,"btn_zip","\u2B07 \u0636\u063A\u0637 \u0627\u0644\u0645\u062D\u062F\u062F");
            A(d,"page_storage_f","\uD83D\uDCBE {0} \u0645\u062A\u0627\u062D \u0645\u0646 {1}");
            A(d,"page_note_h","\uD83D\uDCDD \u0645\u0644\u0627\u062D\u0638\u0629 \u0625\u0644\u0649 \u0627\u0644\u0643\u0645\u0628\u064A\u0648\u062A\u0631");
            A(d,"page_note_ph","\u0627\u0643\u062A\u0628 \u0631\u0633\u0627\u0644\u0629... \u0633\u062A\u0638\u0647\u0631 \u0639\u0644\u0649 \u0627\u0644\u0643\u0645\u0628\u064A\u0648\u062A\u0631 \u0648\u0627\u0644\u062D\u0627\u0641\u0638\u0629");
            A(d,"page_note_send","\u0625\u0631\u0633\u0627\u0644 \u27A4"); A(d,"page_note_ok","\u2713 \u0623\u0631\u0633\u0644\u062A \u0625\u0644\u0649 \u0627\u0644\u0643\u0645\u0628\u064A\u0648\u062A\u0631");
            A(d,"page_del_ask","\u0631\u0642\u0645 \u0627\u0644\u062D\u0630\u0641:"); A(d,"page_wrong_pin","\u0631\u0642\u0645 \u062E\u0627\u0637\u0623 \u0623\u0648 \u0627\u0644\u062D\u0630\u0641 \u0645\u0639\u0637\u0651\u0644.");
            A(d,"page_login_h","\uD83D\uDD12 GoldShare"); A(d,"page_login_btn","\u062F\u062E\u0648\u0644");
            A(d,"page_bad_pass","\u0631\u0642\u0645 \u062E\u0627\u0637\u0623.");
            return d;
        }

        private static Dictionary<string,string> MakeEs()
        {
            Dictionary<string,string> d=new Dictionary<string,string>();
            A(d,"title","GoldShare - Compartici\u00F3n de archivos Premium");
            A(d,"hdr_sub","Compartici\u00F3n Premium \u2014 Subir, Descargar y QR");
            A(d,"start","INICIAR SERVIDOR"); A(d,"stop","DETENER SERVIDOR");
            A(d,"open_browser","ABRIR EN NAVEGADOR"); A(d,"open_folder","ABRIR CARPETA");
            A(d,"copy_url","COPIAR URL"); A(d,"qr_scan","QR / ESCANEAR");
            A(d,"gear","\u2699 AJUSTES"); A(d,"port","Puerto:"); A(d,"folder","Carpeta:");
            A(d,"addresses","Direcciones - seleccione y COPIAR URL o QR / ESCANEAR:");
            A(d,"log","Registro:"); A(d,"language","Idioma:"); A(d,"theme_lbl","Tema:");
            A(d,"theme_gold","Oro"); A(d,"theme_sea","Mar"); A(d,"theme_nature","Naturaleza");
            A(d,"theme_fruits","Frutas"); A(d,"theme_space","Espacio"); A(d,"theme_fire","Fuego");
            A(d,"theme_moon","Luna"); A(d,"theme_midnight","Medianoche");
            A(d,"msg_ready","Listo. Pulse 'INICIAR SERVIDOR'.");
            A(d,"msg_already","GoldShare ya est\u00E1 en ejecuci\u00F3n.\r\n\r\nVea la barra de tareas.");
            A(d,"msg_admin","Se recomiendan privilegios de administrador.\r\n\r\n\u00BFContinuar sin ellos?");
            A(d,"msg_start_first","Inicie el servidor primero.");
            A(d,"msg_bad_port","Introduzca un puerto v\u00E1lido (1-65535).");
            A(d,"msg_start_fail","No se pudo iniciar:\r\n{0}\r\n\r\nPruebe otro puerto.");
            A(d,"msg_open_fail","No se pudo abrir la carpeta:\r\n{0}");
            A(d,"msg_copied","Copiado: {0}");
            A(d,"msg_started","Servidor iniciado en el puerto {0}");
            A(d,"msg_tip","Pulse 'QR / ESCANEAR' y apunte la c\u00E1mara del m\u00F3vil.");
            A(d,"msg_stopped","Servidor detenido.");
            A(d,"msg_qr_preview","Vista previa QR puerto {0} (servidor parado)");
            A(d,"msg_upload_done","Subida completada: {0} archivo(s)");
            A(d,"msg_uploaded","Recibido: {0} ({1})");
            A(d,"msg_files_added","{0} archivo(s) a\u00F1adido(s) (arrastrar y soltar)");
            A(d,"msg_copy_failed","Error al copiar: {0}");
            A(d,"msg_qr_error","Error QR: {0}");
            A(d,"msg_url_copied","URL copiada.");
            A(d,"msg_saved","Imagen QR guardada:\r\n{0}");
            A(d,"msg_port_restart","Puerto cambiado. Reinicie el servidor.");
            A(d,"msg_font_restart","El tama\u00F1o de fuente se aplica tras reiniciar.");
            A(d,"msg_tray_min","Sigue funcionando en la bandeja.");
            A(d,"msg_zip_none","No hay archivos seleccionados.");
            A(d,"msg_zip_ok","ZIP enviado: {0} archivo(s), {1}");
            A(d,"msg_deleted","Eliminado: {0}");
            A(d,"msg_del_deny","Eliminaci\u00F3n denegada (PIN falso o desactivada).");
            A(d,"msg_note_in","Nota recibida: {0}");
            A(d,"status_run","\u25CF ACTIVO :{0}"); A(d,"status_stop","\u25CB DETENIDO");
            A(d,"conns_fmt","\u2022 {0} conex.");
            A(d,"tray_open","Abrir"); A(d,"tray_exit","Salir");
            A(d,"set_title","GoldShare - Ajustes");
            A(d,"set_port","Puerto:"); A(d,"set_uplim","L\u00EDmite de subida (MB):");
            A(d,"set_speed","L\u00EDmite de descarga (KB/s, 0 = sin l\u00EDmite):");
            A(d,"set_autostart","Iniciar servidor autom\u00E1ticamente");
            A(d,"set_pass","PIN de acceso (vac\u00EDo = ninguno):");
            A(d,"set_pass_hint","Los visitantes deber\u00E1n introducir este PIN.");
            A(d,"set_allowdel","Permitir borrar desde la p\u00E1gina (requiere PIN)");
            A(d,"set_tray","Minimizar a la bandeja");
            A(d,"set_savepos","Recordar posici\u00F3n de la ventana");
            A(d,"set_beep","Sonido al recibir archivo");
            A(d,"set_log","Escribir registro en GoldShare.log");
            A(d,"set_font","Tama\u00F1o de fuente:"); A(d,"font_s","Peque\u00F1o"); A(d,"font_m","Medio"); A(d,"font_l","Grande");
            A(d,"save","GUARDAR"); A(d,"cancel","CANCELAR");
            A(d,"tip_start","Iniciar/detener servidor (F5)"); A(d,"tip_browser","Abrir p\u00E1gina local");
            A(d,"tip_folder","Abrir carpeta (Ctrl+O)"); A(d,"tip_copy","Copiar URL (Ctrl+U)");
            A(d,"tip_qr","Mostrar QR (Ctrl+Q)"); A(d,"tip_gear","Ajustes (Ctrl+G)");
            A(d,"tip_port","Puerto del servidor"); A(d,"tip_theme","Tema de color");
            A(d,"tip_lang","Idioma"); A(d,"tip_log","Actividad");
            A(d,"qr_header","\u25C6  ESCANEA CON LA C\u00C1MARA  \u25C6");
            A(d,"qr_hint","Abre la c\u00E1mara del m\u00F3vil y ap\u00FAtala a este c\u00F3digo:");
            A(d,"qr_for_addr","QR para direcci\u00F3n:");
            A(d,"save_png","GUARDAR PNG"); A(d,"close","CERRAR");
            A(d,"page_title","GoldShare - Archivos");
            A(d,"page_sub","Compartici\u00F3n Premium \u2014 Subir y Descargar");
            A(d,"page_dl","\u2B07 DESCARGAR"); A(d,"page_up","\u2B06 SUBIR A ESTE EQUIPO");
            A(d,"page_received","\uD83D\uDCE6 RECIBIDOS"); A(d,"page_no_files","Sin archivos.");
            A(d,"page_upload_btn","SUBIR"); A(d,"page_cam","\uD83D\uDCF7 Foto");
            A(d,"page_search_ph","\uD83D\uDD0D Buscar...");
            A(d,"sort_name","A-Z"); A(d,"sort_size","Tama\u00F1o"); A(d,"sort_new","Nuevo");
            A(d,"view_grid","\u25A6 Cuadr\u00EDcula"); A(d,"view_list","\u2630 Lista");
            A(d,"btn_zip","\u2B07 ZIP SELECC.");
            A(d,"page_storage_f","\uD83D\uDCBE {0} libres de {1}");
            A(d,"page_note_h","\uD83D\uDCDD NOTA AL PC");
            A(d,"page_note_ph","Escribe un mensaje... llega al PC y su portapapeles");
            A(d,"page_note_send","ENVIAR \u27A4"); A(d,"page_note_ok","\u2713 Enviado al PC");
            A(d,"page_del_ask","PIN para borrar:"); A(d,"page_wrong_pin","PIN incorrecto o borrado desactivado.");
            A(d,"page_login_h","\uD83D\uDD12 GoldShare"); A(d,"page_login_btn","ENTRAR");
            A(d,"page_bad_pass","PIN incorrecto.");
            return d;
        }

        private static Dictionary<string,string> MakeDe()
        {
            Dictionary<string,string> d=new Dictionary<string,string>();
            A(d,"title","GoldShare - Premium-Dateifreigabe");
            A(d,"hdr_sub","Premium-Freigabe \u2014 Hochladen, Herunterladen & QR");
            A(d,"start","SERVER STARTEN"); A(d,"stop","SERVER STOPPEN");
            A(d,"open_browser","IM BROWSER \u00D6FFNEN"); A(d,"open_folder","ORDNER \u00D6FFNEN");
            A(d,"copy_url","URL KOPIEREN"); A(d,"qr_scan","QR / SCAN");
            A(d,"gear","\u2699 EINSTELLUNGEN"); A(d,"port","Port:"); A(d,"folder","Ordner:");
            A(d,"addresses","Adressen - w\u00E4hlen, dann URL KOPIEREN oder QR / SCAN:");
            A(d,"log","Protokoll:"); A(d,"language","Sprache:"); A(d,"theme_lbl","Thema:");
            A(d,"theme_gold","Gold"); A(d,"theme_sea","Meer"); A(d,"theme_nature","Natur");
            A(d,"theme_fruits","Fr\u00FCchte"); A(d,"theme_space","Weltraum"); A(d,"theme_fire","Feuer");
            A(d,"theme_moon","Mond"); A(d,"theme_midnight","Mitternacht");
            A(d,"msg_ready","Bereit. 'SERVER STARTEN' dr\u00FCcken.");
            A(d,"msg_already","GoldShare l\u00E4uft bereits.\r\n\r\nSiehe Taskleiste.");
            A(d,"msg_admin","Administratorrechte empfohlen.\r\n\r\nTrotzdem fortfahren?");
            A(d,"msg_start_first","Erst den Server starten.");
            A(d,"msg_bad_port","G\u00FCltigen Port eingeben (1-65535).");
            A(d,"msg_start_fail","Start fehlgeschlagen:\r\n{0}\r\n\r\nAnderen Port versuchen.");
            A(d,"msg_open_fail","Ordner konnte nicht ge\u00F6ffnet werden:\r\n{0}");
            A(d,"msg_copied","Kopiert: {0}");
            A(d,"msg_started","Server gestartet auf Port {0}");
            A(d,"msg_tip","Tipp: 'QR / SCAN' klicken und mit dem Handy draufzeigen.");
            A(d,"msg_stopped","Server gestoppt.");
            A(d,"msg_qr_preview","QR-Vorschau Port {0} (Server aus)");
            A(d,"msg_upload_done","Upload fertig: {0} Datei(en)");
            A(d,"msg_uploaded","Empfangen: {0} ({1})");
            A(d,"msg_files_added","{0} Datei(en) hinzugef\u00FCgt (Drag & Drop)");
            A(d,"msg_copy_failed","Kopieren fehlgeschlagen: {0}");
            A(d,"msg_qr_error","QR-Fehler: {0}");
            A(d,"msg_url_copied","URL kopiert.");
            A(d,"msg_saved","QR-Bild gespeichert:\r\n{0}");
            A(d,"msg_port_restart","Port ge\u00E4ndert. Server neu starten.");
            A(d,"msg_font_restart","Schriftgr\u00F6\u00DFe gilt nach Neustart.");
            A(d,"msg_tray_min","L\u00E4uft weiter im Tray.");
            A(d,"msg_zip_none","Keine Dateien ausgew\u00E4hlt.");
            A(d,"msg_zip_ok","ZIP gesendet: {0} Datei(en), {1}");
            A(d,"msg_deleted","Gel\u00F6scht: {0}");
            A(d,"msg_del_deny","L\u00F6schen verweigert (falsche PIN oder deaktiviert).");
            A(d,"msg_note_in","Notiz erhalten: {0}");
            A(d,"status_run","\u25CF L\u00C4UFT :{0}"); A(d,"status_stop","\u25CB GESTOPPT");
            A(d,"conns_fmt","\u2022 {0} Verb.");
            A(d,"tray_open","\u00D6ffnen"); A(d,"tray_exit","Beenden");
            A(d,"set_title","GoldShare - Einstellungen");
            A(d,"set_port","Port:"); A(d,"set_uplim","Upload-Limit (MB):");
            A(d,"set_speed","Download-Limit (KB/s, 0 = unbegrenzt):");
            A(d,"set_autostart","Server automatisch starten");
            A(d,"set_pass","Zugangs-PIN (leer = keine):");
            A(d,"set_pass_hint","Besucher m\u00FCssen diese PIN eingeben.");
            A(d,"set_allowdel","L\u00F6schen \u00FCber die Seite erlauben (PIN n\u00F6tig)");
            A(d,"set_tray","In den Tray minimieren");
            A(d,"set_savepos","Fensterposition merken");
            A(d,"set_beep","Ton bei Dateieingang");
            A(d,"set_log","Protokoll in GoldShare.log schreiben");
            A(d,"set_font","Schriftgr\u00F6\u00DFe:"); A(d,"font_s","Klein"); A(d,"font_m","Mittel"); A(d,"font_l","Gro\u00DF");
            A(d,"save","SPEICHERN"); A(d,"cancel","ABBRECHEN");
            A(d,"tip_start","Server starten/stoppen (F5)"); A(d,"tip_browser","Seite lokal \u00F6ffnen");
            A(d,"tip_folder","Ordner \u00F6ffnen (Ctrl+O)"); A(d,"tip_copy","URL kopieren (Ctrl+U)");
            A(d,"tip_qr","QR zeigen (Ctrl+Q)"); A(d,"tip_gear","Einstellungen (Ctrl+G)");
            A(d,"tip_port","Server-Port"); A(d,"tip_theme","Farbschema");
            A(d,"tip_lang","Sprache"); A(d,"tip_log","Aktivit\u00E4t");
            A(d,"qr_header","\u25C6  MIT HANDY-KAMERA SCANNEN  \u25C6");
            A(d,"qr_hint","Handykamera \u00F6ffnen und auf diesen Code richten:");
            A(d,"qr_for_addr","QR f\u00FCr Adresse:");
            A(d,"save_png","PNG SPEICHERN"); A(d,"close","SCHLIESSEN");
            A(d,"page_title","GoldShare - Dateien");
            A(d,"page_sub","Premium-Freigabe \u2014 Upload & Download");
            A(d,"page_dl","\u2B07 HERUNTERLADEN"); A(d,"page_up","\u2B06 ZU DIESEM PC HOCHLADEN");
            A(d,"page_received","\uD83D\uDCE6 EMPFANGEN"); A(d,"page_no_files","Keine Dateien.");
            A(d,"page_upload_btn","HOCHLADEN"); A(d,"page_cam","\uD83D\uDCF7 Foto");
            A(d,"page_search_ph","\uD83D\uDD0D Suchen...");
            A(d,"sort_name","A-Z"); A(d,"sort_size","Gr\u00F6\u00DFe"); A(d,"sort_new","Neu");
            A(d,"view_grid","\u25A6 Raster"); A(d,"view_list","\u2630 Liste");
            A(d,"btn_zip","\u2B07 ZIP AUSWAHL");
            A(d,"page_storage_f","\uD83D\uDCBE {0} frei von {1}");
            A(d,"page_note_h","\uD83D\uDCDD NOTIZ AN PC");
            A(d,"page_note_ph","Nachricht schreiben... erscheint auf dem PC + Zwischenablage");
            A(d,"page_note_send","SENDEN \u27A4"); A(d,"page_note_ok","\u2713 An PC gesendet");
            A(d,"page_del_ask","PIN zum L\u00F6schen:"); A(d,"page_wrong_pin","Falsche PIN oder L\u00F6schen deaktiviert.");
            A(d,"page_login_h","\uD83D\uDD12 GoldShare"); A(d,"page_login_btn","OFFEN");
            A(d,"page_bad_pass","Falsche PIN.");
            return d;
        }

        private static Dictionary<string,string> MakeTr()
        {
            Dictionary<string,string> d=new Dictionary<string,string>();
            A(d,"title","GoldShare - Premium Dosya Payla\u015F\u0131m\u0131");
            A(d,"hdr_sub","Premium Payla\u015F\u0131m \u2014 Y\u00FCkle, \u0130ndir & QR");
            A(d,"start","SUNUCUYU BA\u015ELAT"); A(d,"stop","SUNUCUYU DURDUR");
            A(d,"open_browser","TARAYICIDA A\u00C7"); A(d,"open_folder","KLAS\u00D6R\u00DC A\u00C7");
            A(d,"copy_url","URL KOPYALA"); A(d,"qr_scan","QR / TARA");
            A(d,"gear","\u2699 AYARLAR"); A(d,"port","Port:"); A(d,"folder","Klas\u00F6r:");
            A(d,"addresses","Adresler - se\u00E7in, sonra URL KOPYALA veya QR / TARA:");
            A(d,"log","G\u00FCnl\u00FCk:"); A(d,"language","Dil:"); A(d,"theme_lbl","Tema:");
            A(d,"theme_gold","Alt\u0131n"); A(d,"theme_sea","Deniz"); A(d,"theme_nature","Do\u011Fa");
            A(d,"theme_fruits","Meyveler"); A(d,"theme_space","Uzay"); A(d,"theme_fire","Ate\u015F");
            A(d,"theme_moon","Ay"); A(d,"theme_midnight","Gece Yar\u0131s\u0131");
            A(d,"msg_ready","Haz\u0131r. 'SUNUCUYU BA\u015ELAT'a bas\u0131n.");
            A(d,"msg_already","GoldShare zaten \u00E7al\u0131\u015F\u0131yor.\r\n\r\nG\u00F6rev \u00E7ubu\u011Funa bak\u0131n.");
            A(d,"msg_admin","Y\u00F6netici haklar\u0131 \u00F6nerilir.\r\n\r\nYine de devam edilsin mi?");
            A(d,"msg_start_first","\u00D6nce sunucuyu ba\u015Flat\u0131n.");
            A(d,"msg_bad_port","Ge\u00E7erli bir port girin (1-65535).");
            A(d,"msg_start_fail","Ba\u015Flat\u0131lamad\u0131:\r\n{0}\r\n\r\nBa\u015Fka port deneyin.");
            A(d,"msg_open_fail","Klas\u00F6r a\u00E7\u0131lamad\u0131:\r\n{0}");
            A(d,"msg_copied","Kopyaland\u0131: {0}");
            A(d,"msg_started","Sunucu {0} portunda ba\u015Flad\u0131");
            A(d,"msg_tip","\u0130pucu: 'QR / TARA'ya bas\u0131n, telefon kameras\u0131n\u0131 tutun.");
            A(d,"msg_stopped","Sunucu durduruldu.");
            A(d,"msg_qr_preview","QR \u00F6nizleme port {0} (sunucu kapal\u0131)");
            A(d,"msg_upload_done","Y\u00FCkleme bitti: {0} dosya");
            A(d,"msg_uploaded","Al\u0131nd\u0131: {0} ({1})");
            A(d,"msg_files_added","{0} dosya eklendi (s\u00FCr\u00FCkle-b\u0131rak)");
            A(d,"msg_copy_failed","Kopyalama hatas\u0131: {0}");
            A(d,"msg_qr_error","QR hatas\u0131: {0}");
            A(d,"msg_url_copied","URL kopyaland\u0131.");
            A(d,"msg_saved","QR g\u00F6rseli kaydedildi:\r\n{0}");
            A(d,"msg_port_restart","Port de\u011Fi\u015Fti. Sunucuyu yeniden ba\u015Flat\u0131n.");
            A(d,"msg_font_restart","Yaz\u0131 boyutu yeniden ba\u015Flat\u0131nca uygulan\u0131r.");
            A(d,"msg_tray_min","Sunucu sistem tepsisinde \u00E7al\u0131\u015F\u0131yor.");
            A(d,"msg_zip_none","Dosya se\u00E7ilmedi.");
            A(d,"msg_zip_ok","ZIP g\u00F6nderildi: {0} dosya, {1}");
            A(d,"msg_deleted","Silindi: {0}");
            A(d,"msg_del_deny","Silme reddedildi (PIN yanl\u0131\u015F veya kapal\u0131).");
            A(d,"msg_note_in","Not al\u0131nd\u0131: {0}");
            A(d,"status_run","\u25CF \u00C7ALI\u015EIYOR :{0}"); A(d,"status_stop","\u25CB DURDU");
            A(d,"conns_fmt","\u2022 {0} ba\u011Fl.");
            A(d,"tray_open","A\u00E7"); A(d,"tray_exit","\u00C7\u0131k\u0131\u015F");
            A(d,"set_title","GoldShare - Ayarlar");
            A(d,"set_port","Port:"); A(d,"set_uplim","Y\u00FCkleme limiti (MB):");
            A(d,"set_speed","\u0130ndirme h\u0131z limiti (KB/s, 0 = s\u0131n\u0131rs\u0131z):");
            A(d,"set_autostart","Sunucuyu a\u00E7\u0131l\u0131\u015Fta otomatik ba\u015Flat");
            A(d,"set_pass","Eri\u015Fim PIN'i (bo\u015F = yok):");
            A(d,"set_pass_hint","Ziyaret\u00E7iler bu PIN'i girecek.");
            A(d,"set_allowdel","Sayfadan silmeye izin ver (PIN gerekli)");
            A(d,"set_tray","Sistem tepsisine k\u00FC\u00E7\u00FCl");
            A(d,"set_savepos","Pencere konumunu hat\u0131rla");
            A(d,"set_beep","Dosya gelince ses \u00E7al");
            A(d,"set_log","GoldShare.log'a g\u00FCnl\u00FCk yaz");
            A(d,"set_font","Yaz\u0131 boyutu:"); A(d,"font_s","K\u00FC\u00E7\u00FCk"); A(d,"font_m","Orta"); A(d,"font_l","B\u00FCy\u00FCk");
            A(d,"save","KAYDET"); A(d,"cancel","\u0130PTAL");
            A(d,"tip_start","Sunucu ba\u015Flat/durdur (F5)"); A(d,"tip_browser","Sayfay\u0131 yerel a\u00E7");
            A(d,"tip_folder","Klas\u00F6r a\u00E7 (Ctrl+O)"); A(d,"tip_copy","URL kopyala (Ctrl+U)");
            A(d,"tip_qr","QR g\u00F6ster (Ctrl+Q)"); A(d,"tip_gear","Ayarlar (Ctrl+G)");
            A(d,"tip_port","Sunucu portu"); A(d,"tip_theme","Renk temas\u0131");
            A(d,"tip_lang","Aray\u00FCz dili"); A(d,"tip_log","Etkinlik");
            A(d,"qr_header","\u25C6  TELEFON KAMERASIYLA TARA  \u25C6");
            A(d,"qr_hint","Telefon kameras\u0131n\u0131 a\u00E7\u0131p bu koda y\u00F6neltin:");
            A(d,"qr_for_addr","Adres i\u00E7in QR:");
            A(d,"save_png","PNG KAYDET"); A(d,"close","KAPAT");
            A(d,"page_title","GoldShare - Dosyalar");
            A(d,"page_sub","Premium Payla\u015F\u0131m \u2014 Y\u00FCkle & \u0130ndir");
            A(d,"page_dl","\u2B07 \u0130ND\u0130R"); A(d,"page_up","\u2B06 BU C\u0130HAZA Y\u00DCKLE");
            A(d,"page_received","\uD83D\uDCE6 ALINANLAR"); A(d,"page_no_files","Dosya yok.");
            A(d,"page_upload_btn","Y\u00DCKLE"); A(d,"page_cam","\uD83D\uDCF7 Foto\u011Fraf");
            A(d,"page_search_ph","\uD83D\uDD0D Ara...");
            A(d,"sort_name","A-Z"); A(d,"sort_size","Boyut"); A(d,"sort_new","Yeni");
            A(d,"view_grid","\u25A6 Izgara"); A(d,"view_list","\u2630 Liste");
            A(d,"btn_zip","\u2B07 ZIP SE\u00C7\u0130LEN");
            A(d,"page_storage_f","\uD83D\uDCBE {1} i\u00E7inde {0} bo\u015F");
            A(d,"page_note_h","\uD83D\uDCDD PC'YE NOT");
            A(d,"page_note_ph","Mesaj yaz\u0131n... PC'de ve panosunda g\u00F6r\u00FCn\u00FCr");
            A(d,"page_note_send","G\u00D6NDER \u27A4"); A(d,"page_note_ok","\u2713 PC'ye g\u00F6nderildi");
            A(d,"page_del_ask","Silmek i\u00E7in PIN:"); A(d,"page_wrong_pin","PIN yanl\u0131\u015F veya silme kapal\u0131.");
            A(d,"page_login_h","\uD83D\uDD12 GoldShare"); A(d,"page_login_btn","G\u0130R");
            A(d,"page_bad_pass","PIN yanl\u0131\u015F.");
            return d;
        }

        private static Dictionary<string,string> MakeRu()
        {
            Dictionary<string,string> d=new Dictionary<string,string>();
            A(d,"title","GoldShare - \u041F\u0440\u0435\u043C\u0438\u0443\u043C \u043E\u0431\u043C\u0435\u043D \u0444\u0430\u0439\u043B\u0430\u043C\u0438");
            A(d,"hdr_sub","\u041F\u0440\u0435\u043C\u0438\u0443\u043C \u043E\u0431\u043C\u0435\u043D \u2014 \u0437\u0430\u0433\u0440\u0443\u0437\u043A\u0430, \u0441\u043A\u0430\u0447\u0438\u0432\u0430\u043D\u0438\u0435 \u0438 QR");
            A(d,"start","\u0417\u0410\u041F\u0423\u0421\u0422\u0418\u0422\u042C \u0421\u0415\u0420\u0412\u0415\u0420"); A(d,"stop","\u041E\u0421\u0422\u0410\u041D\u041E\u0412\u0418\u0422\u042C");
            A(d,"open_browser","\u041E\u0422\u041A\u0420\u042B\u0422\u042C \u0412 \u0411\u0420\u0410\u0423\u0417\u0415\u0420\u0415"); A(d,"open_folder","\u041E\u0422\u041A\u0420\u042B\u0422\u042C \u041F\u0410\u041F\u041A\u0423");
            A(d,"copy_url","\u041A\u041E\u041F\u0418\u0420\u041E\u0412\u0410\u0422\u042C URL"); A(d,"qr_scan","QR / \u0421\u041A\u0410\u041D");
            A(d,"gear","\u2699 \u041D\u0410\u0421\u0422\u0420\u041E\u0419\u041A\u0418"); A(d,"port","\u041F\u043E\u0440\u0442:"); A(d,"folder","\u041F\u0430\u043F\u043A\u0430:");
            A(d,"addresses","\u0410\u0434\u0440\u0435\u0441\u0430 - \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435, \u0437\u0430\u0442\u0435\u043C \u041A\u041E\u041F\u0418\u042F URL \u0438\u043B\u0438 QR:");
            A(d,"log","\u0416\u0443\u0440\u043D\u0430\u043B:"); A(d,"language","\u042F\u0437\u044B\u043A:"); A(d,"theme_lbl","\u0422\u0435\u043C\u0430:");
            A(d,"theme_gold","\u0417\u043E\u043B\u043E\u0442\u043E"); A(d,"theme_sea","\u041C\u043E\u0440\u0435"); A(d,"theme_nature","\u041F\u0440\u0438\u0440\u043E\u0434\u0430");
            A(d,"theme_fruits","\u0424\u0440\u0443\u043A\u0442\u044B"); A(d,"theme_space","\u041A\u043E\u0441\u043C\u043E\u0441"); A(d,"theme_fire","\u041E\u0433\u043E\u043D\u044C");
            A(d,"theme_moon","\u041B\u0443\u043D\u0430"); A(d,"theme_midnight","\u041F\u043E\u043B\u043D\u043E\u0447\u044C");
            A(d,"msg_ready","\u0413\u043E\u0442\u043E\u0432. \u041D\u0430\u0436\u043C\u0438\u0442\u0435 '\u0417\u0410\u041F\u0423\u0421\u0422\u0418\u0422\u042C'.");
            A(d,"msg_already","GoldShare \u0443\u0436\u0435 \u0437\u0430\u043F\u0443\u0449\u0435\u043D.\r\n\r\n\u0421\u043C. \u043F\u0430\u043D\u0435\u043B\u044C \u0437\u0430\u0434\u0430\u0447.");
            A(d,"msg_admin","\u0420\u0435\u043A\u043E\u043C\u0435\u043D\u0434\u0443\u044E\u0442\u0441\u044F \u043F\u0440\u0430\u0432\u0430 \u0430\u0434\u043C\u0438\u043D\u0438\u0441\u0442\u0440\u0430\u0442\u043E\u0440\u0430.\r\n\r\n\u041F\u0440\u043E\u0434\u043E\u043B\u0436\u0438\u0442\u044C \u0431\u0435\u0437 \u043D\u0438\u0445?");
            A(d,"msg_start_first","\u0421\u043D\u0430\u0447\u0430\u043B\u0430 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0441\u0435\u0440\u0432\u0435\u0440.");
            A(d,"msg_bad_port","\u0412\u0432\u0435\u0434\u0438\u0442\u0435 \u043F\u043E\u0440\u0442 1-65535.");
            A(d,"msg_start_fail","\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u044C:\r\n{0}\r\n\r\n\u041F\u043E\u043F\u0440\u043E\u0431\u0443\u0439\u0442\u0435 \u0434\u0440\u0443\u0433\u043E\u0439 \u043F\u043E\u0440\u0442.");
            A(d,"msg_open_fail","\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043E\u0442\u043A\u0440\u044B\u0442\u044C \u043F\u0430\u043F\u043A\u0443:\r\n{0}");
            A(d,"msg_copied","\u0421\u043A\u043E\u043F\u0438\u0440\u043E\u0432\u0430\u043D\u043E: {0}");
            A(d,"msg_started","\u0421\u0435\u0440\u0432\u0435\u0440 \u0437\u0430\u043F\u0443\u0449\u0435\u043D \u043D\u0430 \u043F\u043E\u0440\u0442\u0443 {0}");
            A(d,"msg_tip","\u041D\u0430\u0436\u043C\u0438\u0442\u0435 'QR / \u0421\u041A\u0410\u041D' \u0438 \u043D\u0430\u0432\u0435\u0434\u0438\u0442\u0435 \u043A\u0430\u043C\u0435\u0440\u0443 \u0442\u0435\u043B\u0435\u0444\u043E\u043D\u0430.");
            A(d,"msg_stopped","\u0421\u0435\u0440\u0432\u0435\u0440 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D.");
            A(d,"msg_qr_preview","QR-\u043F\u0440\u0435\u0434\u043F\u0440\u043E\u0441\u043C\u043E\u0442\u0440, \u043F\u043E\u0440\u0442 {0} (\u0441\u0435\u0440\u0432\u0435\u0440 \u0432\u044B\u043A\u043B.)");
            A(d,"msg_upload_done","\u0417\u0430\u0433\u0440\u0443\u0437\u043A\u0430 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0430: {0} \u0444\u0430\u0439\u043B(\u043E\u0432)");
            A(d,"msg_uploaded","\u041F\u043E\u043B\u0443\u0447\u0435\u043D\u043E: {0} ({1})");
            A(d,"msg_files_added","\u0414\u043E\u0431\u0430\u0432\u043B\u0435\u043D\u043E \u0444\u0430\u0439\u043B\u043E\u0432: {0} (\u043F\u0435\u0440\u0435\u0442\u0430\u0441\u043A\u0438\u0432\u0430\u043D\u0438\u0435)");
            A(d,"msg_copy_failed","\u041E\u0448\u0438\u0431\u043A\u0430 \u043A\u043E\u043F\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u044F: {0}");
            A(d,"msg_qr_error","\u041E\u0448\u0438\u0431\u043A\u0430 QR: {0}");
            A(d,"msg_url_copied","URL \u0441\u043A\u043E\u043F\u0438\u0440\u043E\u0432\u0430\u043D.");
            A(d,"msg_saved","QR \u0441\u043E\u0445\u0440\u0430\u043D\u0451\u043D:\r\n{0}");
            A(d,"msg_port_restart","\u041F\u043E\u0440\u0442 \u0438\u0437\u043C\u0435\u043D\u0451\u043D. \u041F\u0435\u0440\u0435\u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0441\u0435\u0440\u0432\u0435\u0440.");
            A(d,"msg_font_restart","\u0420\u0430\u0437\u043C\u0435\u0440 \u0448\u0440\u0438\u0444\u0442\u0430 \u043F\u0440\u0438\u043C\u0435\u043D\u0438\u0442\u0441\u044F \u043F\u043E\u0441\u043B\u0435 \u043F\u0435\u0440\u0435\u0437\u0430\u043F\u0443\u0441\u043A\u0430.");
            A(d,"msg_tray_min","\u0421\u0435\u0440\u0432\u0435\u0440 \u0440\u0430\u0431\u043E\u0442\u0430\u0435\u0442 \u0432 \u0442\u0440\u0435\u0435.");
            A(d,"msg_zip_none","\u0424\u0430\u0439\u043B\u044B \u043D\u0435 \u0432\u044B\u0431\u0440\u0430\u043D\u044B.");
            A(d,"msg_zip_ok","ZIP \u043E\u0442\u043F\u0440\u0430\u0432\u043B\u0435\u043D: {0} \u0444\u0430\u0439\u043B(\u043E\u0432), {1}");
            A(d,"msg_deleted","\u0423\u0434\u0430\u043B\u0435\u043D\u043E: {0}");
            A(d,"msg_del_deny","\u0423\u0434\u0430\u043B\u0435\u043D\u0438\u0435 \u043E\u0442\u043A\u043B\u043E\u043D\u0435\u043D\u043E (\u043D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 PIN \u0438\u043B\u0438 \u043E\u0442\u043A\u043B\u044E\u0447\u0435\u043D\u043E).");
            A(d,"msg_note_in","\u0417\u0430\u043C\u0435\u0442\u043A\u0430 \u043F\u043E\u043B\u0443\u0447\u0435\u043D\u0430: {0}");
            A(d,"status_run","\u25CF \u0420\u0410\u0411\u041E\u0422\u0410\u0415\u0422 :{0}"); A(d,"status_stop","\u25CB \u041E\u0421\u0422\u0410\u041D\u041E\u0412\u041B\u0415\u041D");
            A(d,"conns_fmt","\u2022 {0} \u0441\u043E\u0435\u0434.");
            A(d,"tray_open","\u041E\u0442\u043A\u0440\u044B\u0442\u044C"); A(d,"tray_exit","\u0412\u044B\u0445\u043E\u0434");
            A(d,"set_title","GoldShare - \u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438");
            A(d,"set_port","\u041F\u043E\u0440\u0442:"); A(d,"set_uplim","\u041B\u0438\u043C\u0438\u0442 \u0437\u0430\u0433\u0440\u0443\u0437\u043A\u0438 (\u041C\u0411):");
            A(d,"set_speed","\u041B\u0438\u043C\u0438\u0442 \u0441\u043A\u043E\u0440\u043E\u0441\u0442\u0438 (\u041A\u0411/\u0441, 0 = \u0431\u0435\u0437):");
            A(d,"set_autostart","\u0417\u0430\u043F\u0443\u0441\u043A\u0430\u0442\u044C \u0441\u0435\u0440\u0432\u0435\u0440 \u0430\u0432\u0442\u043E\u043C\u0430\u0442\u0438\u0447\u0435\u0441\u043A\u0438");
            A(d,"set_pass","PIN \u0434\u043E\u0441\u0442\u0443\u043F\u0430 (\u043F\u0443\u0441\u0442\u043E = \u043D\u0435\u0442):");
            A(d,"set_pass_hint","\u041F\u043E\u0441\u0435\u0442\u0438\u0442\u0435\u043B\u0438 \u0434\u043E\u043B\u0436\u043D\u044B \u0432\u0432\u0435\u0441\u0442\u0438 \u044D\u0442\u043E\u0442 PIN.");
            A(d,"set_allowdel","\u0420\u0430\u0437\u0440\u0435\u0448\u0438\u0442\u044C \u0443\u0434\u0430\u043B\u0435\u043D\u0438\u0435 \u0441\u043E \u0441\u0442\u0440\u0430\u043D\u0438\u0446\u044B (\u043D\u0443\u0436\u0435\u043D PIN)");
            A(d,"set_tray","\u0421\u0432\u043E\u0440\u0430\u0447\u0438\u0432\u0430\u0442\u044C \u0432 \u0442\u0440\u0435\u0439");
            A(d,"set_savepos","\u041F\u043E\u043C\u043D\u0438\u0442\u044C \u043F\u043E\u043B\u043E\u0436\u0435\u043D\u0438\u0435 \u043E\u043A\u043D\u0430");
            A(d,"set_beep","\u0417\u0432\u0443\u043A \u043F\u0440\u0438 \u043F\u043E\u043B\u0443\u0447\u0435\u043D\u0438\u0438 \u0444\u0430\u0439\u043B\u0430");
            A(d,"set_log","\u041F\u0438\u0441\u0430\u0442\u044C \u0436\u0443\u0440\u043D\u0430\u043B \u0432 GoldShare.log");
            A(d,"set_font","\u0420\u0430\u0437\u043C\u0435\u0440 \u0448\u0440\u0438\u0444\u0442\u0430:"); A(d,"font_s","\u041C\u0430\u043B\u044B\u0439"); A(d,"font_m","\u0421\u0440\u0435\u0434\u043D\u0438\u0439"); A(d,"font_l","\u0411\u043E\u043B\u044C\u0448\u043E\u0439");
            A(d,"save","\u0421\u041E\u0425\u0420\u0410\u041D\u0418\u0422\u042C"); A(d,"cancel","\u041E\u0422\u041C\u0415\u041D\u0410");
            A(d,"tip_start","\u041F\u0443\u0441\u043A/\u0441\u0442\u043E\u043F \u0441\u0435\u0440\u0432\u0435\u0440\u0430 (F5)"); A(d,"tip_browser","\u041E\u0442\u043A\u0440\u044B\u0442\u044C \u0441\u0442\u0440\u0430\u043D\u0438\u0446\u0443");
            A(d,"tip_folder","\u041E\u0442\u043A\u0440\u044B\u0442\u044C \u043F\u0430\u043F\u043A\u0443 (Ctrl+O)"); A(d,"tip_copy","\u041A\u043E\u043F\u0438\u0440\u043E\u0432\u0430\u0442\u044C URL (Ctrl+U)");
            A(d,"tip_qr","\u041F\u043E\u043A\u0430\u0437\u0430\u0442\u044C QR (Ctrl+Q)"); A(d,"tip_gear","\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438 (Ctrl+G)");
            A(d,"tip_port","\u041F\u043E\u0440\u0442 \u0441\u0435\u0440\u0432\u0435\u0440\u0430"); A(d,"tip_theme","\u0426\u0432\u0435\u0442\u043E\u0432\u0430\u044F \u0442\u0435\u043C\u0430");
            A(d,"tip_lang","\u042F\u0437\u044B\u043A"); A(d,"tip_log","\u0410\u043A\u0442\u0438\u0432\u043D\u043E\u0441\u0442\u044C");
            A(d,"qr_header","\u25C6  \u0421\u041A\u0410\u041D\u0418\u0420\u0423\u0419\u0422\u0415 \u041A\u0410\u041C\u0415\u0420\u041E\u0419  \u25C6");
            A(d,"qr_hint","\u041E\u0442\u043A\u0440\u043E\u0439\u0442\u0435 \u043A\u0430\u043C\u0435\u0440\u0443 \u0442\u0435\u043B\u0435\u0444\u043E\u043D\u0430 \u0438 \u043D\u0430\u0432\u0435\u0434\u0438\u0442\u0435 \u043D\u0430 \u043A\u043E\u0434:");
            A(d,"qr_for_addr","QR \u0434\u043B\u044F \u0430\u0434\u0440\u0435\u0441\u0430:");
            A(d,"save_png","\u0421\u041E\u0425\u0420\u0410\u041D\u0418\u0422\u042C PNG"); A(d,"close","\u0417\u0410\u041A\u0420\u042B\u0422\u042C");
            A(d,"page_title","GoldShare - \u0424\u0430\u0439\u043B\u044B");
            A(d,"page_sub","\u041F\u0440\u0435\u043C\u0438\u0443\u043C \u043E\u0431\u043C\u0435\u043D \u2014 \u0437\u0430\u0433\u0440\u0443\u0437\u043A\u0430 & \u0441\u043A\u0430\u0447\u0438\u0432\u0430\u043D\u0438\u0435");
            A(d,"page_dl","\u2B07 \u0421\u041A\u0410\u0427\u0410\u0422\u042C"); A(d,"page_up","\u2B06 \u0417\u0410\u0413\u0420\u0423\u0417\u0418\u0422\u042C \u0421\u042E\u0414\u0410");
            A(d,"page_received","\uD83D\uDCE6 \u041F\u041E\u041B\u0423\u0427\u0415\u041D\u041E"); A(d,"page_no_files","\u0424\u0430\u0439\u043B\u043E\u0432 \u043D\u0435\u0442.");
            A(d,"page_upload_btn","\u0417\u0410\u0413\u0420\u0423\u0417\u0418\u0422\u042C"); A(d,"page_cam","\uD83D\uDCF7 \u0424\u043E\u0442\u043E");
            A(d,"page_search_ph","\uD83D\uDD0D \u041F\u043E\u0438\u0441\u043A...");
            A(d,"sort_name","A-\u042F"); A(d,"sort_size","\u0420\u0430\u0437\u043C\u0435\u0440"); A(d,"sort_new","\u041D\u043E\u0432\u044B\u0435");
            A(d,"view_grid","\u25A6 \u041F\u043B\u0438\u0442\u043A\u0438"); A(d,"view_list","\u2630 \u0421\u043F\u0438\u0441\u043E\u043A");
            A(d,"btn_zip","\u2B07 ZIP \u0412\u042B\u0411\u0420\u0410\u041D\u041D\u041E\u0415");
            A(d,"page_storage_f","\uD83D\uDCBE {0} \u0441\u0432\u043E\u0431\u043E\u0434\u043D\u043E \u0438\u0437 {1}");
            A(d,"page_note_h","\uD83D\uDCDD \u0417\u0410\u041C\u0415\u0422\u041A\u0410 \u041D\u0410 \u041F\u041A");
            A(d,"page_note_ph","\u041D\u0430\u043F\u0438\u0448\u0438\u0442\u0435 \u0441\u043E\u043E\u0431\u0449\u0435\u043D\u0438\u0435... \u043F\u043E\u044F\u0432\u0438\u0442\u0441\u044F \u043D\u0430 \u041F\u041A \u0438 \u0432 \u0431\u0443\u0444\u0435\u0440\u0435");
            A(d,"page_note_send","\u041E\u0422\u041F\u0420\u0410\u0412\u0418\u0422\u042C \u27A4"); A(d,"page_note_ok","\u2713 \u041E\u0442\u043F\u0440\u0430\u0432\u043B\u0435\u043D\u043E \u043D\u0430 \u041F\u041A");
            A(d,"page_del_ask","PIN \u0434\u043B\u044F \u0443\u0434\u0430\u043B\u0435\u043D\u0438\u044F:"); A(d,"page_wrong_pin","\u041D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 PIN \u0438\u043B\u0438 \u0443\u0434\u0430\u043B\u0435\u043D\u0438\u0435 \u043E\u0442\u043A\u043B\u044E\u0447\u0435\u043D\u043E.");
            A(d,"page_login_h","\uD83D\uDD12 GoldShare"); A(d,"page_login_btn","\u0412\u041E\u0419\u0422\u0418");
            A(d,"page_bad_pass","\u041D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 PIN.");
            return d;
        }
    }

    // ========================================================================
    //  ENTRY POINT
    // ========================================================================
    static class Program
    {
        private static Mutex instanceMutex;

        [STAThread]
        static void Main(string[] args)
        {
            bool lite=args.Length>0&&args[0]=="--lite";
            MainForm.Silent=lite;
            Settings.Load();
            L.Set(Settings.Language);
            Theme.Current=Themes.Get(Settings.Theme);

            bool createdNew;
            instanceMutex=new Mutex(true,"Global\\GoldShare_Premium_Server_Mutex",out createdNew);
            if(!createdNew)
            {
                MessageBox.Show(L.T("msg_already"),"GoldShare",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }

            if(!lite&&!IsRunningAsAdmin())
            {
                if(Elevate()){ instanceMutex.ReleaseMutex(); return; }
            }

            Application.EnableVisualStyles();
            Application.Run(new MainForm(lite));
            instanceMutex.ReleaseMutex();
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                WindowsPrincipal p=new WindowsPrincipal(WindowsIdentity.GetCurrent());
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            } catch { return false; }
        }

        private static bool Elevate()
        {
            try
            {
                ProcessStartInfo psi=new ProcessStartInfo();
                psi.FileName=Application.ExecutablePath;
                psi.Arguments="elevated";
                psi.UseShellExecute=true;
                psi.Verb="runas";
                Process.Start(psi);
                return true;
            }
            catch(Win32Exception)
            {
                DialogResult r=MessageBox.Show(L.T("msg_admin"),"GoldShare",
                    MessageBoxButtons.YesNo,MessageBoxIcon.Question);
                return (r!=DialogResult.Yes);
            }
        }
    }

    // ========================================================================
    //  MAIN FORM
    // ========================================================================
    public class MainForm : Form
    {
        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool running;
        private int port=8080;

        private string baseFolder, downloadsFolder, uploadsFolder;
        private static readonly byte[] CRLFCRLF=new byte[]{13,10,13,10};

        private Panel headerPanel, statusPanel;
        private TextBox txtPort, txtPath;
        private GoldButton btnStart,btnStop,btnOpenPage,btnOpenFolder,btnCopy,btnQr,btnGear;
        private ListBox lstUrls, lstLog;
        private Label lblPort,lblFolder,lblAddresses,lblLog,lblLang,lblTheme;
        private ComboBox cboLang, cboTheme;
        private bool langReady;
        private ToolTip tips;
        private NotifyIcon tray;
        private ContextMenu trayMenu;
        private MenuItem miToggle;
        private System.Windows.Forms.Timer tick;

        // traffic / progress shared state
        private static long statSent, statRecv, lastSent, lastRecv;
        private static int statConns, upActive;
        private static long upSent, upTotal;
        private static string trafficText="";

        // cached drawing objects (never re-alloc per paint — kills GC churn)
        private Font fStatusBold, fStatus, fStatusSmall, fHeaderTitle, fHeaderSub;
        private int lastConns, lastPhase=-1;

        // SuperLite RAM governor: tick marks of last request activity and last trim,
        // plus a periodic timer that, once the server has been quiet for QUIET_MS,
        // drops the whole app into DEEP IDLE (all timers stopped, working set
        // trimmed) until the next HTTP request wakes it. A truly idle background
        // server therefore resides at a few MB, not tens.
        private static long lastReqTick, lastTrimTick;
        private System.Windows.Forms.Timer trimTimer;
        private const int QUIET_MS=10000;
        private bool deepIdle;

        // measurement/embedding hook: --lite suppresses blocking UI (elevation, error
        // boxes) so the app can be run, measured and driven headless.
        internal static bool Silent;

        public MainForm() : this(false) { }

        public MainForm(bool lite)
        {
            SetupFolders();
            InitUI();
            ApplyLanguage();
            ApplyTheme();
            langReady=true;
            Log("GoldShare.ini  ->  "+Settings.IniPath);
            Log(L.T("msg_ready"));
            Log(L.T("msg_tip"));
            if(Settings.AutoStart||lite){ try{ StartServer(); } catch(Exception sx){ Log("start: "+sx.Message); } }
            if(lite)
            {
                // headless server mode: run from the tray, exactly like the normal
                // minimize-to-tray path, so a background server idles tiny.
                try { Hide(); } catch { }
            }
        }

        private void SetupFolders()
        {
            baseFolder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GoldShare");
            downloadsFolder=Path.Combine(baseFolder,"Downloads");
            uploadsFolder=Path.Combine(baseFolder,"Uploads");
            if(!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);
            if(!Directory.Exists(downloadsFolder)) Directory.CreateDirectory(downloadsFolder);
            if(!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
        }

        private void InitUI()
        {
            this.Text="GoldShare";
            this.FormBorderStyle=FormBorderStyle.FixedSingle;
            this.MaximizeBox=false; this.MinimizeBox=true;
            this.StartPosition=FormStartPosition.CenterScreen;
            this.ClientSize=new Size(700,640);

            float fs=(Settings.FontSize==0)?0.9f:(Settings.FontSize==2)?1.18f:1.0f;
            try{ this.Font=new Font("Segoe UI",9f*fs); } catch { this.Font=new Font("Tahoma",8.25f*fs); }
            GoldButton.BaseFontSize=8.5f*fs;

            // build paint fonts ONCE, reuse for the life of the form
            fStatusBold=new Font("Verdana",8f*fs,FontStyle.Bold);
            fStatus=new Font("Verdana",8f*fs);
            fStatusSmall=new Font("Verdana",7.5f*fs,FontStyle.Bold);
            fHeaderTitle=new Font("Georgia",20f*fs,FontStyle.Bold);
            fHeaderSub=new Font("Tahoma",9f*fs,FontStyle.Italic|FontStyle.Bold);

            tips=new ToolTip();

            if(Settings.SavePos&&Settings.PosX>-4000)
            {
                Point p=new Point(Settings.PosX,Settings.PosY);
                bool onScreen=false;
                foreach(Screen s in Screen.AllScreens) if(s.Bounds.Contains(p)) onScreen=true;
                if(onScreen){ this.StartPosition=FormStartPosition.Manual; this.Location=p; }
            }

            headerPanel=new Panel();
            headerPanel.Location=new Point(0,0);
            headerPanel.Size=new Size(700,88);
            headerPanel.Paint+=new PaintEventHandler(HeaderPaint);
            this.Controls.Add(headerPanel);

            statusPanel=new Panel();
            statusPanel.Location=new Point(0,88);
            statusPanel.Size=new Size(700,28);
            statusPanel.Paint+=new PaintEventHandler(StatusPaint);
            this.Controls.Add(statusPanel);

            lblPort=MakeLabel("",24,128,40); this.Controls.Add(lblPort);
            txtPort=new TextBox();
            txtPort.Text=Settings.Port.ToString();
            txtPort.Location=new Point(64,124); txtPort.Size=new Size(52,24);
            this.Controls.Add(txtPort);
            tips.SetToolTip(txtPort,L.T("tip_port"));

            btnStart=new GoldButton(); btnStart.Location=new Point(122,120); btnStart.Size=new Size(150,34);
            btnStart.Click+=new EventHandler(delegate { StartServer(); });
            this.Controls.Add(btnStart);

            btnStop=new GoldButton(); btnStop.Location=new Point(276,120); btnStop.Size=new Size(140,34);
            btnStop.Enabled=false;
            btnStop.Click+=new EventHandler(delegate { StopServer(); });
            this.Controls.Add(btnStop);

            btnOpenPage=new GoldButton(); btnOpenPage.Location=new Point(420,120); btnOpenPage.Size=new Size(158,34);
            btnOpenPage.Click+=new EventHandler(delegate
            {
                if(running) Process.Start("http://localhost:"+port+"/");
                else MessageBox.Show(L.T("msg_start_first"),"GoldShare");
            });
            this.Controls.Add(btnOpenPage);

            btnGear=new GoldButton(); btnGear.Location=new Point(582,120); btnGear.Size=new Size(94,34);
            btnGear.Click+=new EventHandler(delegate { ShowSettings(); });
            this.Controls.Add(btnGear);

            lblFolder=MakeLabel("",24,162,46); this.Controls.Add(lblFolder);
            txtPath=new TextBox();
            txtPath.Text=baseFolder; txtPath.ReadOnly=true;
            txtPath.Location=new Point(70,158); txtPath.Size=new Size(342,24);
            this.Controls.Add(txtPath);

            btnOpenFolder=new GoldButton(); btnOpenFolder.Location=new Point(418,154); btnOpenFolder.Size=new Size(160,34);
            btnOpenFolder.Click+=new EventHandler(delegate { OpenSharedFolder(); });
            this.Controls.Add(btnOpenFolder);

            lblAddresses=MakeLabel("",24,196,600); this.Controls.Add(lblAddresses);

            lstUrls=new ListBox();
            lstUrls.Location=new Point(24,216); lstUrls.Size=new Size(530,56);
            lstUrls.IntegralHeight=false; lstUrls.BorderStyle=BorderStyle.FixedSingle;
            this.Controls.Add(lstUrls);

            btnCopy=new GoldButton(); btnCopy.Location=new Point(566,216); btnCopy.Size=new Size(110,26);
            btnCopy.Click+=new EventHandler(delegate { CopyUrl(); });
            this.Controls.Add(btnCopy);

            btnQr=new GoldButton(); btnQr.Location=new Point(566,246); btnQr.Size=new Size(110,26);
            btnQr.Click+=new EventHandler(delegate { ShowQr(); });
            this.Controls.Add(btnQr);

            lblLog=MakeLabel("",24,284,120); this.Controls.Add(lblLog);
            lblTheme=MakeLabel("",160,284,50); this.Controls.Add(lblTheme);

            cboTheme=new ComboBox();
            cboTheme.DropDownStyle=ComboBoxStyle.DropDownList;
            cboTheme.DrawMode=DrawMode.OwnerDrawFixed;
            cboTheme.ItemHeight=18;
            cboTheme.Location=new Point(212,280); cboTheme.Size=new Size(122,24);
            cboTheme.Font=new Font("Verdana",8f*fs,FontStyle.Bold);
            foreach(Theme t in Themes.All) cboTheme.Items.Add(L.T("theme_"+t.Code));
            cboTheme.SelectedIndex=Themes.IndexOf(Settings.Theme);
            cboTheme.DrawItem+=new DrawItemEventHandler(ThemeDrawItem);
            cboTheme.SelectedIndexChanged+=new EventHandler(delegate(object s,EventArgs e)
            {
                if(!langReady) return;
                int i=cboTheme.SelectedIndex;
                if(i<0||i>=Themes.All.Length) return;
                string code=Themes.All[i].Code;
                if(code==Settings.Theme) return;
                Settings.Theme=code; Theme.Current=Themes.Get(code); Settings.Save();
                ApplyTheme();
                Log("\u2713 "+L.T("theme_"+code));
            });
            this.Controls.Add(cboTheme);

            lblLang=MakeLabel("",346,284,60); this.Controls.Add(lblLang);
            cboLang=new ComboBox();
            cboLang.DropDownStyle=ComboBoxStyle.DropDownList;
            cboLang.Location=new Point(408,280); cboLang.Size=new Size(120,24);
            cboLang.Font=new Font("Verdana",8f*fs,FontStyle.Bold);
            cboLang.Items.Add("English"); cboLang.Items.Add("Fran\u00E7ais");
            cboLang.Items.Add("\u0627\u0644\u0639\u0631\u0628\u064A\u0629"); cboLang.Items.Add("Espa\u00F1ol");
            cboLang.Items.Add("Deutsch"); cboLang.Items.Add("T\u00FCrk\u00E7e");
            cboLang.Items.Add("\u0420\u0443\u0441\u0441\u043A\u0438\u0439");
            int li=L.Langs.IndexOf(Settings.Language); cboLang.SelectedIndex=(li<0)?0:li;
            cboLang.SelectedIndexChanged+=new EventHandler(delegate(object s,EventArgs e)
            {
                if(!langReady) return;
                int i=cboLang.SelectedIndex;
                if(i<0||i>=L.Langs.Count) return;
                string code=L.Langs[i];
                if(code==L.Lang) return;
                L.Set(code); Settings.Language=code; Settings.Save();
                ApplyLanguage();
                Log("\u2713 "+cboLang.Text);
            });
            this.Controls.Add(cboLang);

            lstLog=new ListBox();
            lstLog.Location=new Point(24,308); lstLog.Size=new Size(652,306);
            lstLog.IntegralHeight=false; lstLog.BorderStyle=BorderStyle.FixedSingle;
            this.Controls.Add(lstLog);

            // kill flicker on the busy log/url lists
            DoubleBuffer(lstUrls); DoubleBuffer(lstLog); DoubleBuffer(txtPath);

            // tray icon (custom-drawn) — indices: 0 Open, 1 Toggle, 2 QR, 3 Folder, 4 Settings, 5 sep, 6 Exit
            trayMenu=new ContextMenu();
            trayMenu.MenuItems.Add(new MenuItem(L.T("tray_open"),new EventHandler(delegate { RestoreFromTray(); })));
            miToggle=new MenuItem(L.T("start"),new EventHandler(delegate { ToggleServer(); }));
            trayMenu.MenuItems.Add(miToggle);
            trayMenu.MenuItems.Add(new MenuItem(L.T("qr_scan"),new EventHandler(delegate { ShowQr(); })));
            trayMenu.MenuItems.Add(new MenuItem(L.T("open_folder"),new EventHandler(delegate { OpenSharedFolder(); })));
            trayMenu.MenuItems.Add(new MenuItem(L.T("gear"),new EventHandler(delegate { ShowSettings(); })));
            trayMenu.MenuItems.Add(new MenuItem("-"));
            trayMenu.MenuItems.Add(new MenuItem(L.T("tray_exit"),new EventHandler(delegate
            { tray.Visible=false; this.Close(); Application.Exit(); })));

            tray=new NotifyIcon();
            tray.Icon=MakeAppIcon();
            tray.Text="GoldShare";
            tray.ContextMenu=trayMenu;
            tray.Visible=true;
            tray.DoubleClick+=new EventHandler(delegate { RestoreFromTray(); });

            this.Resize+=new EventHandler(delegate(object s,EventArgs e)
            {
                if(WindowState==FormWindowState.Minimized&&Settings.Tray)
                {
                    Hide();
                    try{ tick.Stop(); } catch { }
                    TrimWorkingSet();
                    tray.BalloonTipTitle="GoldShare";
                    tray.BalloonTipText=L.T("msg_tray_min");
                    try{ tray.ShowBalloonTip(1500); } catch { }
                }
            });

            // status refresh timer (traffic + LED pulse + upload bar)
            tick=new System.Windows.Forms.Timer();
            tick.Interval=500;
            tick.Tick+=new EventHandler(delegate(object s,EventArgs e)
            {
                long sentNow=Interlocked.Read(ref statSent);
                long recvNow=Interlocked.Read(ref statRecv);
                int c=statConns;
                double kbOut=(sentNow-lastSent)/512.0, kbIn=(recvNow-lastRecv)/512.0;
                lastSent=sentNow; lastRecv=recvNow;
                bool dirty=(kbIn>0.05||kbOut>0.05||c!=lastConns);
                lastConns=c;
                if(dirty)
                    trafficText=FormatRate(kbIn)+" \u25BC   "+FormatRate(kbOut)+" \u25B2   "
                        +string.Format(L.T("conns_fmt"),c);
                // LED pulse only needs a repaint while running; idle = no work.
                // Throttled to 2s so a visible-but-quiet form isn't churning pages.
                bool needPaint=dirty;
                if(running)
                {
                    int phase=Environment.TickCount/2000;
                    if(phase!=lastPhase){ needPaint=true; lastPhase=phase; }
                }
                if(needPaint) statusPanel.Invalidate();
            });
            tick.Start();

            // SuperLite: reclaim RAM right after the form settles, then watch for a
            // quiet period; once the server has been request-free, drop into DEEP
            // IDLE (timers stopped, pages trimmed) until somebody hits it again.
            EventHandler idleH=null;
            idleH=delegate { Application.Idle-=idleH; CollectAndTrim(); };
            Application.Idle+=idleH;
            trimTimer=new System.Windows.Forms.Timer();
            trimTimer.Interval=15000;
            trimTimer.Tick+=new EventHandler(delegate
            {
                if(deepIdle) return;
                if(Environment.TickCount-lastReqTick>QUIET_MS) EnterDeepIdle();
            });
            trimTimer.Start();

            this.AllowDrop=true;
            this.DragEnter+=new DragEventHandler(FormDragEnter);
            this.DragDrop+=new DragEventHandler(FormDragDrop);
            this.FormClosing+=new FormClosingEventHandler(delegate(object s,FormClosingEventArgs e)
            {
                int p;
                if(int.TryParse(txtPort.Text.Trim(),out p)&&p>=1&&p<=65535)
                { Settings.Port=p; }
                if(Settings.SavePos){ Settings.PosX=Location.X; Settings.PosY=Location.Y; }
                Settings.Save();
                if(tray!=null) tray.Visible=false;
                if(running) StopServer();
            });
        }

        private Label MakeLabel(string text,int x,int y,int w)
        {
            Label l=new Label();
            l.Text=text; l.Location=new Point(x,y); l.Size=new Size(w,20);
            return l;
        }

        // ---- custom app icon (drawn, no .ico file) ----
        private Icon MakeAppIcon()
        {
            using(Bitmap b=new Bitmap(32,32))
            {
                using(Graphics g=Graphics.FromImage(b))
                {
                    g.SmoothingMode=SmoothingMode.AntiAlias;
                    using(LinearGradientBrush br=new LinearGradientBrush(new Rectangle(0,0,32,32),
                        Color.FromArgb(212,175,55),Color.FromArgb(112,79,5),LinearGradientMode.ForwardDiagonal))
                    {
                        Point[] pts=new Point[]{ new Point(16,1), new Point(31,16), new Point(16,31), new Point(1,16) };
                        g.FillPolygon(br,pts);
                    }
                    using(Pen p=new Pen(Color.FromArgb(255,244,200),2))
                        g.DrawPolygon(p,new Point[]{ new Point(16,3), new Point(29,16), new Point(16,29), new Point(3,16) });
                    using(Font f=new Font("Georgia",13f,FontStyle.Bold))
                    using(SolidBrush sb=new SolidBrush(Color.FromArgb(60,42,2)))
                        g.DrawString("G",f,sb,8,5);
                }
                return Icon.FromHandle(b.GetHicon());
            }
        }

        private void RestoreFromTray()
        {
            EnsureActive();
            try{ if(!tick.Enabled) tick.Start(); } catch { }
            Show(); WindowState=FormWindowState.Normal; Activate();
        }

        // ---- release cached RAM right away on XP-era low-memory boxes ----
        // (HANDLE)-1 is GetCurrentProcess()' pseudo-handle: always valid, never
        // finalized/closed like a cached Process.Handle would be.
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc,IntPtr min,IntPtr max);
        private static void TrimWorkingSet()
        {
            try
            {
                SetProcessWorkingSetSize(new IntPtr(-1),new IntPtr(-1),new IntPtr(-1));
            } catch { }
        }

        // super-lite: force a full collect, then trim the working set to its real
        // footprint. Running after idle means a quiet GUI server holds almost
        // nothing in RAM; running after a transfer burst flushes the buffers.
        private static void CollectAndTrim()
        {
            int now=Environment.TickCount;
            if(now-lastTrimTick<10000) return; // throttle: never thrash
            lastTrimTick=now;
            try{ GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); } catch { }
            TrimWorkingSet();
        }

        // ---- DEEP IDLE: the server is request-free, so stop every timer, flush
        // everything we can, and go to sleep until the next HTTP request. ----
        private void EnterDeepIdle()
        {
            if(deepIdle) return;
            deepIdle=true;
            try{ tick.Stop(); } catch { }
            // trimTimer deliberately keeps ticking: it is the only thing that can
            // re-enter deep idle after a wake, and a 4/min self-gating wake is
            // trivial. Stopping it here created a restart race after traffic.
            lastReqTick=Environment.TickCount; // re-arm the quiet meter for next wake
            CollectAndTrim();
        }

        private void EnsureActive()
        {
            if(!deepIdle) return;
            deepIdle=false;
            try{ tick.Start(); } catch { }
        }

        // ---- double-buffered ListBox => smooth, flicker-free log ----
        private static void DoubleBuffer(Control c)
        {
            try
            {
                System.Reflection.PropertyInfo p=typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                p.SetValue(c,true,null);
            } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                if(tick!=null) tick.Dispose();
                if(trimTimer!=null) trimTimer.Dispose();
                if(tray!=null) tray.Dispose();
                if(fStatusBold!=null)fStatusBold.Dispose();
                if(fStatus!=null)fStatus.Dispose();
                if(fStatusSmall!=null)fStatusSmall.Dispose();
                if(fHeaderTitle!=null)fHeaderTitle.Dispose();
                if(fHeaderSub!=null)fHeaderSub.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ToggleServer(){ if(running) StopServer(); else StartServer(); }

        private void CopyUrl()
        {
            string s=null;
            if(lstUrls.SelectedItem!=null) s=lstUrls.SelectedItem.ToString();
            else if(lstUrls.Items.Count>0) s=lstUrls.Items[0].ToString();
            if(s!=null){ Clipboard.SetText(s); Log(string.Format(L.T("msg_copied"),s)); }
        }

        private void ThemeDrawItem(object sender,DrawItemEventArgs e)
        {
            e.DrawBackground();
            if(e.Index>=0&&e.Index<Themes.All.Length)
            {
                Theme th=Themes.All[e.Index];
                Rectangle rb=new Rectangle(e.Bounds.X+3,e.Bounds.Y+3,16,e.Bounds.Height-6);
                using(LinearGradientBrush b=new LinearGradientBrush(rb,th.Dark,th.Light,45f))
                    e.Graphics.FillRectangle(b,rb);
                using(Pen p=new Pen(Color.FromArgb(120,120,120))) e.Graphics.DrawRectangle(p,rb);
                TextRenderer.DrawText(e.Graphics,cboTheme.Items[e.Index].ToString(),e.Font,
                    new Rectangle(e.Bounds.X+24,e.Bounds.Y,e.Bounds.Width-24,e.Bounds.Height),
                    Theme.Current.Text,
                    TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
            }
            e.DrawFocusRectangle();
        }

        // --------------------------------------------------------------------
        private void ApplyLanguage()
        {
            this.Text=L.T("title");
            this.RightToLeft=L.IsArabic?RightToLeft.Yes:RightToLeft.No;
            this.RightToLeftLayout=L.IsArabic;

            lblPort.Text=L.T("port"); lblFolder.Text=L.T("folder");
            lblAddresses.Text=L.T("addresses"); lblLog.Text=L.T("log");
            lblLang.Text=L.T("language"); lblTheme.Text=L.T("theme_lbl");

            btnStart.Text=L.T("start"); btnStop.Text=L.T("stop");
            btnOpenPage.Text=L.T("open_browser"); btnOpenFolder.Text=L.T("open_folder");
            btnCopy.Text=L.T("copy_url"); btnQr.Text=L.T("qr_scan"); btnGear.Text=L.T("gear");

            miToggle.Text=running?L.T("stop"):L.T("start");
            tray.Text="GoldShare"+(running?"  \u25CF":"");
            trayMenu.MenuItems[0].Text=L.T("tray_open");
            trayMenu.MenuItems[2].Text=L.T("qr_scan");
            trayMenu.MenuItems[3].Text=L.T("open_folder");
            trayMenu.MenuItems[4].Text=L.T("gear");
            trayMenu.MenuItems[6].Text=L.T("tray_exit");

            tips.SetToolTip(btnStart,L.T("tip_start"));
            tips.SetToolTip(btnStop,L.T("tip_start"));
            tips.SetToolTip(btnOpenPage,L.T("tip_browser"));
            tips.SetToolTip(btnOpenFolder,L.T("tip_folder"));
            tips.SetToolTip(btnCopy,L.T("tip_copy"));
            tips.SetToolTip(btnQr,L.T("tip_qr"));
            tips.SetToolTip(btnGear,L.T("tip_gear"));
            tips.SetToolTip(txtPort,L.T("tip_port"));
            tips.SetToolTip(cboTheme,L.T("tip_theme"));
            tips.SetToolTip(cboLang,L.T("tip_lang"));
            tips.SetToolTip(lstLog,L.T("tip_log"));

            int sel=cboTheme.SelectedIndex; if(sel<0) sel=Themes.IndexOf(Settings.Theme);
            cboTheme.Items.Clear();
            foreach(Theme t in Themes.All) cboTheme.Items.Add(L.T("theme_"+t.Code));
            cboTheme.SelectedIndex=sel;

            headerPanel.Invalidate(); statusPanel.Invalidate();
        }

        private void ApplyTheme()
        {
            Theme t=Theme.Current;
            BackColor=t.Bg;
            foreach(Control c in Controls)
            {
                if(c is Label) c.ForeColor=t.Text;
            }
            txtPath.BackColor=t.FieldBg; txtPath.ForeColor=t.Text;
            lstUrls.BackColor=t.FieldBg; lstUrls.ForeColor=t.Text;
            lstLog.BackColor=t.FieldBg; lstLog.ForeColor=t.Text;
            cboTheme.BackColor=t.FieldBg; cboTheme.ForeColor=t.Text;
            cboLang.BackColor=t.FieldBg; cboLang.ForeColor=t.Text;
            headerPanel.Invalidate(); statusPanel.Invalidate();
            foreach(Control c in Controls) if(c is GoldButton) c.Invalidate();
        }

        private void HeaderPaint(object sender,PaintEventArgs e)
        {
            Theme t=Theme.Current;
            Graphics g=e.Graphics;
            Rectangle r=new Rectangle(0,0,headerPanel.Width,headerPanel.Height);
            using(LinearGradientBrush b=new LinearGradientBrush(r,t.Dark,t.Mid,LinearGradientMode.Horizontal))
                g.FillRectangle(b,r);
            using(Pen p=new Pen(t.Light,3))
                g.DrawLine(p,0,headerPanel.Height-2,headerPanel.Width,headerPanel.Height-2);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            {
                string title="\u25C6  G O L D S H A R E  \u25C6";
                SizeF sz=g.MeasureString(title,fHeaderTitle);
                float x=(headerPanel.Width-sz.Width)/2f;
                using(SolidBrush sh=new SolidBrush(Color.FromArgb(90,0,0,0)))
                    g.DrawString(title,fHeaderTitle,sh,x+2f,14f+2f);
                using(SolidBrush sb=new SolidBrush(Theme.Mix(t.Light,Color.White,0.88)))
                    g.DrawString(title,fHeaderTitle,sb,x,14f);
            }
            {
                string s2=L.T("hdr_sub");
                SizeF sz2=g.MeasureString(s2,fHeaderSub);
                using(SolidBrush b2=new SolidBrush(Theme.Mix(t.Light,Color.White,0.68)))
                    g.DrawString(s2,fHeaderSub,b2,(headerPanel.Width-sz2.Width)/2f,52f);
            }
        }

        private void StatusPaint(object sender,PaintEventArgs e)
        {
            Theme t=Theme.Current;
            Graphics g=e.Graphics;
            g.SmoothingMode=SmoothingMode.AntiAlias;

            // LED pill
            bool on=running;
            int a=on?(150+(int)(90*Math.Sin(Environment.TickCount/250.0))):120;
            using(SolidBrush glow=new SolidBrush(Color.FromArgb(Math.Max(30,a),on?Color.FromArgb(40,200,90):Color.FromArgb(200,60,50))))
                g.FillEllipse(glow,8,7,16,16);
            using(SolidBrush led=new SolidBrush(on?Color.FromArgb(50,210,100):Color.FromArgb(190,70,60)))
                g.FillEllipse(led,11,10,10,10);

            string st=on?string.Format(L.T("status_run"),port):L.T("status_stop");
            TextRenderer.DrawText(g,st,fStatusBold,
                new Rectangle(30,0,150,28),on?Color.FromArgb(30,120,50):t.Text,
                TextFormatFlags.VerticalCenter|TextFormatFlags.Left);

            TextRenderer.DrawText(g,trafficText,fStatus,
                new Rectangle(185,0,280,28),Theme.Mix(t.Text,Color.Gray,0.25),
                TextFormatFlags.VerticalCenter|TextFormatFlags.Left);

            // upload progress bar (right side)
            if(upActive>0&&upTotal>0)
            {
                int pct=(int)(upSent*100/upTotal); if(pct>100)pct=100;
                Rectangle bar=new Rectangle(480,8,190,13);
                using(SolidBrush bg=new SolidBrush(t.Soft)) g.FillRectangle(bg,bar);
                Rectangle fill=new Rectangle(bar.X,bar.Y,bar.Width*pct/100,bar.Height);
                if(fill.Width>0)
                using(LinearGradientBrush fb=new LinearGradientBrush(bar,t.Mid,t.Light,0f))
                    g.FillRectangle(fb,fill);
                using(Pen p=new Pen(Theme.Mix(t.Dark,t.Mid,0.4))) g.DrawRectangle(p,bar);
                string txt="\u2B06 "+pct+"%  "+FormatSize(upSent)+" / "+FormatSize(upTotal);
                TextRenderer.DrawText(g,txt,fStatusSmall,
                    new Rectangle(bar.X,bar.Bottom,bar.Width+30,14),t.Text,
                    TextFormatFlags.Left);
            }
        }

        private static string FormatRate(double kb)
        {
            if(kb<1024) return kb.ToString("0")+" KB/s";
            return (kb/1024).ToString("0.0")+" MB/s";
        }

        // --------------------------------------------------------------------
        private void OpenSharedFolder()
        {
            try
            {
                Process.Start("explorer.exe","\""+baseFolder+"\"");
                Log("Explorer: "+baseFolder);
            }
            catch(Exception ex)
            { MessageBox.Show(string.Format(L.T("msg_open_fail"),ex.Message),"GoldShare"); }
        }

        private void FormDragEnter(object sender,DragEventArgs e)
        { if(e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect=DragDropEffects.Copy; }

        private void FormDragDrop(object sender,DragEventArgs e)
        {
            string[] files=(string[])e.Data.GetData(DataFormats.FileDrop);
            if(files==null) return;
            int count=0;
            foreach(string f in files)
            {
                try
                {
                    if(File.Exists(f))
                    { File.Copy(f,Path.Combine(downloadsFolder,Path.GetFileName(f)),true); count++; }
                }
                catch(Exception ex){ Log(string.Format(L.T("msg_copy_failed"),ex.Message)); }
            }
            if(count>0) Log(string.Format(L.T("msg_files_added"),count));
        }

        // --------------------------------------------------------------------
        private void ShowQr()
        {
            List<string> urls=new List<string>();
            int selectedIndex=0;
            if(running&&lstUrls.Items.Count>0)
            {
                foreach(object o in lstUrls.Items) urls.Add(o.ToString());
                if(lstUrls.SelectedIndex>=0) selectedIndex=lstUrls.SelectedIndex;
            }
            else
            {
                int p;
                if(!int.TryParse(txtPort.Text.Trim(),out p)||p<1||p>65535) p=8080;
                foreach(string ip in GetLocalIPs()) urls.Add("http://"+ip+":"+p+"/");
                if(!running) Log(string.Format(L.T("msg_qr_preview"),p));
            }
            try
            {
                using(QrForm f=new QrForm(urls,selectedIndex)) f.ShowDialog(this);
            }
            catch(Exception ex)
            { MessageBox.Show(string.Format(L.T("msg_qr_error"),ex.Message),"GoldShare"); }
        }

        // --------------------------------------------------------------------
        private void ShowSettings()
        {
            using(SettingsForm sf=new SettingsForm())
            {
                if(sf.ShowDialog(this)==DialogResult.OK)
                {
                    if(running&&port!=Settings.Port) Log(L.T("msg_port_restart"));
                    if(sf.FontNeedsRestart) Log(L.T("msg_font_restart"));
                }
            }
        }

        // --------------------------------------------------------------------
        private void StartServer()
        {
            if(running) return;
            int p;
            if(!int.TryParse(txtPort.Text.Trim(),out p)||p<1||p>65535)
            { MessageBox.Show(L.T("msg_bad_port"),"GoldShare"); return; }
            port=p; Settings.Port=p; Settings.Save();
            try
            {
                listener=new HttpListener();
                listener.Prefixes.Add("http://+:"+port+"/");
                listener.Start();
            }
            catch
            {
                try
                {
                    listener=new HttpListener();
                    listener.Prefixes.Add("http://*:"+port+"/");
                    listener.Start();
                }
                catch(Exception ex2)
                {
                    if(!Silent) MessageBox.Show(string.Format(L.T("msg_start_fail"),ex2.Message),"GoldShare");
                    else Log("start fail: "+ex2.Message);
                    return;
                }
            }
            running=true;
            listenerThread=new Thread(new ThreadStart(ListenerLoop));
            listenerThread.IsBackground=true;
            listenerThread.Start();

            btnStart.Enabled=false; btnStop.Enabled=true; txtPort.Enabled=false;
            miToggle.Text=L.T("stop");
            RefreshUrls();
            Log(string.Format(L.T("msg_started"),port));
            Log(L.T("msg_tip"));
        }

        private void StopServer()
        {
            running=false;
            try{ listener.Stop(); } catch { }
            try{ listener.Abort(); } catch { }
            btnStart.Enabled=true; btnStop.Enabled=false; txtPort.Enabled=true;
            miToggle.Text=L.T("start");
            statusPanel.Invalidate();
            Log(L.T("msg_stopped"));
        }

        private List<string> GetLocalIPs()
        {
            List<string> result=new List<string>();
            try
            {
                IPHostEntry entry=Dns.GetHostEntry(Dns.GetHostName());
                foreach(IPAddress ip in entry.AddressList)
                    if(ip.AddressFamily==AddressFamily.InterNetwork&&!result.Contains(ip.ToString()))
                        result.Add(ip.ToString());
            } catch { }
            if(result.Count==0) result.Add("127.0.0.1");
            return result;
        }

        private void RefreshUrls()
        {
            lstUrls.Items.Clear();
            foreach(string ip in GetLocalIPs())
                lstUrls.Items.Add("http://"+ip+":"+port+"/");
            if(lstUrls.Items.Count>0) lstUrls.SelectedIndex=0;
        }

        private void Log(string msg)
        {
            if(lstLog.InvokeRequired)
            { try{ BeginInvoke(new MethodInvoker(delegate { Log(msg); })); } catch { } return; }
            lstLog.Items.Add(DateTime.Now.ToString("HH:mm:ss")+"  "+msg);
            while(lstLog.Items.Count>500) lstLog.Items.RemoveAt(0);
            lstLog.TopIndex=lstLog.Items.Count-1;
            LogF.W(msg);
        }

        // --------------------------------------------------------------------
        private void ListenerLoop()
        {
            while(running)
            {
                try
                {
                    HttpListenerContext ctx=listener.GetContext();
                    ThreadPool.QueueUserWorkItem(new WaitCallback(HandleRequest),ctx);
                }
                catch { if(!running) break; }
            }
        }

        private void HandleRequest(object state)
        {
            HttpListenerContext ctx=(HttpListenerContext)state;
            Interlocked.Increment(ref statConns);
            lastReqTick=Environment.TickCount;
            if(deepIdle) { try { EnsureActive(); } catch { } }
            string rawPath=ctx.Request.Url.AbsolutePath;
            string method=ctx.Request.HttpMethod;
            string lower=rawPath.ToLower();
            try
            {
                // ---- access PIN gate (cookie auth) ----
                if(Settings.PasswordHash.Length>0&&lower!="/login")
                {
                    Cookie ck=ctx.Request.Cookies["gs_auth"];
                    if(ck==null||ck.Value!=Settings.PasswordHash)
                    {
                        ShowLoginPage(ctx);
                        return;
                    }
                }

                if(lower=="/"||lower=="/index.html")
                {
                    if(method=="GET"||method=="HEAD")
                        WriteText(ctx,BuildIndexHtml(),200,"text/html; charset=utf-8");
                    else WriteText(ctx,"Method not allowed",405,"text/plain");
                }
                else if(lower=="/login")
                {
                    if(method=="POST")
                    {
                        string body=ReadBody(ctx,4096);
                        string pin=FormVal(body,"pin");
                        if(Settings.Hash(pin)==Settings.PasswordHash)
                        {
                            Cookie hc=new Cookie("gs_auth",Settings.PasswordHash);
                            hc.Path="/";
                            ctx.Response.AppendCookie(hc);
                            ctx.Response.StatusCode=302;
                            ctx.Response.RedirectLocation="/";
                            ctx.Response.OutputStream.Close();
                        }
                        else
                        {
                            WriteText(ctx,BuildLoginPage(true),200,"text/html; charset=utf-8");
                        }
                    }
                    else WriteText(ctx,BuildLoginPage(false),200,"text/html; charset=utf-8");
                }
                else if(lower=="/upload")
                {
                    if(method=="POST") HandleUpload(ctx);
                    else WriteText(ctx,"Use POST",405,"text/plain");
                }
                else if(lower=="/note")
                {
                    if(method=="POST")
                    {
                        string body=ReadBody(ctx,65536);
                        string text=FormVal(body,"text");
                        if(text.Length>0)
                        {
                            string line="["+DateTime.Now.ToString("yyyy-MM-dd HH:mm")+"] "+text;
                            File.AppendAllText(Path.Combine(baseFolder,"Notes.txt"),
                                line+"\r\n",Encoding.UTF8);
                            Log(string.Format(L.T("msg_note_in"),
                                text.Length>60?text.Substring(0,60)+"\u2026":text));
                            try
                            {
                                BeginInvoke(new MethodInvoker(delegate
                                { try{ Clipboard.SetText(text); } catch { } }));
                            } catch { }
                        }
                        WriteText(ctx,"OK",200,"text/plain");
                    }
                    else WriteText(ctx,"Use POST",405,"text/plain");
                }
                else if(lower=="/delete")
                {
                    if(method=="POST"&&Settings.AllowDelete&&Settings.PasswordHash.Length>0)
                    {
                        string body=ReadBody(ctx,4096);
                        string root=FormVal(body,"root"), name=FormVal(body,"name"), pin=FormVal(body,"pin");
                        string folder=(root=="Downloads")?downloadsFolder:
                                      (root=="Uploads")?uploadsFolder:null;
                        if(Settings.Hash(pin)==Settings.PasswordHash&&folder!=null&&
                           SafeName(name)&&File.Exists(Path.Combine(folder,name)))
                        {
                            File.Delete(Path.Combine(folder,name));
                            Log(string.Format(L.T("msg_deleted"),root+"/"+name));
                            WriteText(ctx,"OK",200,"text/plain");
                        }
                        else WriteText(ctx,"DENY",403,"text/plain");
                    }
                    else WriteText(ctx,"DENY",403,"text/plain");
                }
                else if(lower=="/zip")
                {
                    HandleZip(ctx);
                }
                else if(lower=="/qr.png")
                {
                    string u=ctx.Request.QueryString["u"];
                    if(u==null||u.Length==0||u.Length>213) u="http://"+ctx.Request.Url.Authority+"/";
                    QrCode qr=QrCode.Encode(Encoding.UTF8.GetBytes(u));
                    using(Bitmap bmp=qr.ToBitmap(6))
                    {
                        MemoryStream ms=new MemoryStream();
                        bmp.Save(ms,ImageFormat.Png);
                        WriteBytes(ctx,ms.ToArray(),200,"image/png");
                    }
                }
                else if(lower=="/poll")
                {
                    string cs=ctx.Request.QueryString["sig"];
                    string ns=CalcSig();
                    for(int i=0;i<20&&ns==cs&&running;i++)
                    { Thread.Sleep(1000); ns=CalcSig(); }
                    WriteText(ctx,ns,200,"text/plain");
                }
                else if(lower=="/fragment")
                {
                    WriteText(ctx,BuildFragment(),200,"text/html; charset=utf-8");
                }
                else if(lower.StartsWith("/thumb/"))
                {
                    HandleThumb(ctx,rawPath.Substring(7));
                }
                else if(lower.StartsWith("/downloads/"))
                    ServeFile(ctx,downloadsFolder,rawPath.Substring(11));
                else if(lower.StartsWith("/uploads/"))
                    ServeFile(ctx,uploadsFolder,rawPath.Substring(9));
                else if(lower=="/favicon.ico")
                    WriteText(ctx,"",404,"text/plain");
                else
                    WriteText(ctx,"<h2>404</h2><a href=\"/\">GoldShare</a>",404,"text/html; charset=utf-8");
            }
            catch
            {
                try{ WriteText(ctx,"Server error",500,"text/plain"); } catch { }
            }
            finally
            {
                Interlocked.Decrement(ref statConns);
                Log(method+" "+rawPath+"  ->  "+ctx.Response.StatusCode);
            }
        }

        private static string ReadBody(HttpListenerContext ctx,int max)
        {
            using(MemoryStream ms=new MemoryStream())
            {
                byte[] buf=new byte[8192]; int n;
                while((n=ctx.Request.InputStream.Read(buf,0,buf.Length))>0)
                { ms.Write(buf,0,n); if(ms.Length>max) break; }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static string FormVal(string body,string key)
        {
            if(body==null) return "";
            string[] parts=body.Split('&');
            for(int i=0;i<parts.Length;i++)
            {
                int eq=parts[i].IndexOf('=');
                if(eq<=0) continue;
                if(parts[i].Substring(0,eq)==key)
                    try{ return Uri.UnescapeDataString(parts[i].Substring(eq+1).Replace("+"," ")); }
                    catch { return parts[i].Substring(eq+1); }
            }
            return "";
        }

        private static bool SafeName(string n)
        {
            return n!=null&&n.Length>0&&n.IndexOf("..")<0&&n.IndexOf('/')<0&&n.IndexOf('\\')<0;
        }

        private string CalcSig()
        {
            try
            {
                long h=unchecked((long)14695981039346656037UL);
                string[] dirs=new string[]{downloadsFolder,uploadsFolder};
                foreach(string dir in dirs)
                {
                    DirectoryInfo di=new DirectoryInfo(dir);
                    if(!di.Exists) continue;
                    foreach(FileInfo f in di.GetFiles())
                    {
                        string s=f.Name+":"+f.Length+":"+f.LastWriteTimeUtc.Ticks+";";
                        foreach(char c in s) h=(h^(long)c)*1099511628211L;
                    }
                }
                return h.ToString("x16");
            } catch { return "err"; }
        }

        // ---- login page ----
        private string BuildLoginPage(bool bad)
        {
            Theme t=Theme.Current;
            StringBuilder sb=new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>GoldShare</title><style>");
            sb.Append("body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:").Append(Theme.Hex(t.Bg)).Append(";font-family:Georgia,serif}");
            sb.Append(".card{background:").Append(Theme.Hex(t.FieldBg)).Append(";border:1px solid ").Append(Theme.Hex(t.Soft));
            sb.Append(";border-radius:16px;padding:34px 30px;box-shadow:0 10px 40px ").Append(Theme.Rgba(t.Dark,0.35)).Append(";text-align:center;width:300px}");
            sb.Append("h1{color:").Append(Theme.Hex(t.Mid)).Append(";letter-spacing:2px}");
            sb.Append("input{width:100%;box-sizing:border-box;padding:11px;font-size:18px;text-align:center;border:1px solid ").Append(Theme.Hex(t.Soft)).Append(";border-radius:9px;background:#fff}");
            sb.Append(".btn{margin-top:14px;width:100%;background:linear-gradient(180deg,").Append(Theme.Hex(Theme.Mix(t.Mid,t.Light,0.35))).Append(",").Append(Theme.Hex(t.Mid));
            sb.Append(");border:1px solid ").Append(Theme.Hex(t.Dark)).Append(";color:#fff;font-weight:bold;font-size:16px;padding:12px;border-radius:9px;cursor:pointer}");
            sb.Append(".err{color:#c0392b;font-size:14px;min-height:18px;margin-top:8px}</style></head><body>");
            sb.Append("<form class=\"card\" method=\"post\" action=\"/login\">");
            sb.Append("<h1>").Append(L.T("page_login_h")).Append("</h1>");
            sb.Append("<input type=\"password\" name=\"pin\" placeholder=\"PIN\" autofocus>");
            sb.Append("<button class=\"btn\" type=\"submit\">").Append(L.T("page_login_btn")).Append("</button>");
            sb.Append("<div class=\"err\">").Append(bad?L.T("page_bad_pass"):"").Append("</div>");
            sb.Append("</form></body></html>");
            return sb.ToString();
        }

        private void ShowLoginPage(HttpListenerContext ctx)
        {
            WriteText(ctx,BuildLoginPage(false),200,"text/html; charset=utf-8");
        }

        // ========================================================================
        //  WEB PAGE
        // ========================================================================
        private string BuildIndexHtml()
        {
            Theme t=Theme.Current;
            List<string> ips=GetLocalIPs();
            string primary="http://"+ips[0]+":"+port+"/";
            string dir=L.IsArabic?" dir=\"rtl\" lang=\"ar\"":"";
            string pageFont=(Settings.FontSize==0)?"14px":(Settings.FontSize==2)?"18px":"16px";

            StringBuilder sb=new StringBuilder();
            sb.Append("<!DOCTYPE html><html").Append(dir).Append("><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>").Append(HtmlEncode(L.T("page_title"))).Append("</title>");
            sb.Append("<style>").Append(BuildCss(t,pageFont));
            if(!t.IsDark)
            {
                Theme dk=new Theme(t.Code,t.Dark,t.Mid,t.Light,
                    Theme.Mix(t.Soft,Color.Black,0.72),
                    Theme.Mix(t.Bg,Color.Black,0.86),
                    Theme.Mix(t.Text,Color.White,0.8),
                    Theme.Mix(t.FieldBg,Color.Black,0.8),true);
                sb.Append("@media (prefers-color-scheme:dark){").Append(BuildCss(dk,pageFont)).Append("}");
            }
            sb.Append("</style></head><body>");
            sb.Append("<div class=\"hero\"><h1>&#9670; GOLDSHARE &#9670;</h1>");
            sb.Append("<div class=\"sub\">").Append(HtmlEncode(L.T("page_sub"))).Append("</div>");
            sb.Append("<span class=\"badge\">").Append(HtmlEncode(primary)).Append("</span></div>");
            sb.Append("<div class=\"wrap\">");

            string sig=CalcSig();

            // toolbar
            sb.Append("<div class=\"toolbar\">");
            sb.Append("<input id=\"search\" type=\"search\" placeholder=\"").Append(HtmlEncode(L.T("page_search_ph"))).Append("\">");
            sb.Append("<button class=\"chip\" data-sort=\"n\">"+L.T("sort_name")+"</button>");
            sb.Append("<button class=\"chip\" data-sort=\"s\">"+L.T("sort_size")+"</button>");
            sb.Append("<button class=\"chip\" data-sort=\"t\">"+L.T("sort_new")+"</button>");
            sb.Append("<button class=\"chip\" id=\"vgrid\">"+L.T("view_grid")+"</button>");
            sb.Append("<button class=\"chip\" id=\"vlist\">"+L.T("view_list")+"</button>");
            sb.Append("<button class=\"chip gold\" id=\"zipbtn\">"+L.T("btn_zip")+"</button>");
            sb.Append("</div>");
            sb.Append("<div class=\"storage\">"+StorageText()+"</div>");

            // downloads card
            sb.Append("<div class=\"card\"><h2>").Append(L.T("page_dl")).Append("</h2>");
            sb.Append("<div id=\"dl\">");
            BuildFileList(sb,downloadsFolder,"Downloads");
            sb.Append("</div></div>");

            // upload card
            sb.Append("<div class=\"card\"><h2>").Append(L.T("page_up")).Append("</h2>");
            sb.Append("<form id=\"upform\" method=\"post\" action=\"/upload\" enctype=\"multipart/form-data\">");
            sb.Append("<input type=\"file\" name=\"f1\" id=\"fileinput\" multiple> ");
            sb.Append("<input type=\"file\" name=\"f2\" accept=\"image/*\" capture=\"environment\" class=\"cam\"> ");
            sb.Append("<button class=\"btn\" type=\"submit\">").Append(L.T("page_upload_btn")).Append("</button> ");
            sb.Append("<button class=\"btn cambtn\" type=\"submit\">").Append(L.T("page_cam")).Append("</button></form>");
            sb.Append("<div class=\"bar\" id=\"bar\"><div id=\"barfill\"></div></div><div id=\"ptxt\">0 %</div>");
            sb.Append("<h2 style=\"margin-top:22px\">").Append(L.T("page_received")).Append("</h2>");
            sb.Append("<div id=\"up\">");
            BuildFileList(sb,uploadsFolder,"Uploads");
            sb.Append("</div>");

            // note box
            sb.Append("<h2 style=\"margin-top:22px\">").Append(L.T("page_note_h")).Append("</h2>");
            sb.Append("<textarea id=\"note\" rows=\"3\" placeholder=\"").Append(HtmlEncode(L.T("page_note_ph"))).Append("\"></textarea>");
            sb.Append("<button class=\"btn\" id=\"notesend\" style=\"margin-top:8px\">").Append(L.T("page_note_send")).Append("</button>");
            sb.Append("<div id=\"noteok\"></div>");
            sb.Append("</div>"); // card

            sb.Append("</div><footer>&#9670; GoldShare Server &#9670;</footer>");

            // ---- client JS ----
            sb.Append("<script>");
            sb.Append("var SIG='").Append(sig).Append("';");
            sb.Append("var HOST=location.protocol+'//'+location.host;");
            sb.Append("function $(i){return document.getElementById(i);}");
            // search filter
            sb.Append("$('search').addEventListener('input',function(){var q=this.value.toLowerCase();");
            sb.Append("var fl=document.querySelectorAll('.file');for(var i=0;i<fl.length;i++){");
            sb.Append("var n=(fl[i].getAttribute('data-n')||'').toLowerCase();");
            sb.Append("fl[i].style.display=(n.indexOf(q)>=0)?'':'none';}});");
            // sort
            sb.Append("function sortBy(k){['dl','up'].forEach(function(id){var c=$(id);");
            sb.Append("var items=[];var fl=c.querySelectorAll('.file');for(var i=0;i<fl.length;i++)items.push(fl[i]);");
            sb.Append("items.sort(function(a,b){");
            sb.Append("if(k=='n')return (a.getAttribute('data-n')||'').localeCompare(b.getAttribute('data-n')||'');");
            sb.Append("if(k=='s')return (parseInt(b.getAttribute('data-s'))||0)-(parseInt(a.getAttribute('data-s'))||0);");
            sb.Append("return (parseInt(b.getAttribute('data-t'))||0)-(parseInt(a.getAttribute('data-t'))||0);});");
            sb.Append("for(var i=0;i<items.length;i++)c.appendChild(items[i]);});}");
            sb.Append("var chips=document.querySelectorAll('.chip[data-sort]');");
            sb.Append("for(var i=0;i<chips.length;i++)chips[i].addEventListener('click',function(){sortBy(this.getAttribute('data-sort'));});");
            // view toggle
            sb.Append("function setView(g){document.body.classList.toggle('grid',g);}");
            sb.Append("$('vgrid').addEventListener('click',function(){setView(true);});");
            sb.Append("$('vlist').addEventListener('click',function(){setView(false);});");
            // zip
            sb.Append("$('zipbtn').addEventListener('click',function(){var sel=[];");
            sb.Append("var cb=document.querySelectorAll('.pick');for(var i=0;i<cb.length;i++)if(cb[i].checked)sel.push(cb[i].value);");
            sb.Append("if(!sel.length){alert('").Append(HtmlEncode(L.T("msg_zip_none"))).Append("');return;}");
            sb.Append("location.href='/zip?files='+encodeURIComponent(sel.join('|'));});");
            // copy-link + per-file qr + delete (event delegation)
            sb.Append("function copyText(s){var ta=document.createElement('textarea');ta.value=s;");
            sb.Append("document.body.appendChild(ta);ta.select();try{document.execCommand('copy');}catch(e){}");
            sb.Append("document.body.removeChild(ta);}");
            sb.Append("document.body.addEventListener('click',function(e){var b=e.target;");
            sb.Append("if(!b.classList||!b.classList.contains('mini'))return;");
            sb.Append("e.preventDefault();e.stopPropagation();var act=b.getAttribute('data-act');");
            sb.Append("if(act=='copy'){copyText(HOST+b.getAttribute('data-u'));b.textContent='\\u2713';setTimeout(function(){b.textContent='\\uD83D\\uDCCB';},900);}");
            sb.Append("else if(act=='qr'){window.open('/qr.png?u='+encodeURIComponent(HOST+b.getAttribute('data-u')),'_blank');}");
            sb.Append("else if(act=='del'){var pin=prompt('").Append(HtmlEncode(L.T("page_del_ask"))).Append("');");
            sb.Append("if(pin==null)return;var x=new XMLHttpRequest();x.open('POST','/delete');");
            sb.Append("x.setRequestHeader('Content-Type','application/x-www-form-urlencoded');");
            sb.Append("x.onload=function(){if(x.responseText=='OK'){location.reload();}else{alert('")
              .Append(HtmlEncode(L.T("page_wrong_pin"))).Append("');}};");
            sb.Append("x.send('root='+encodeURIComponent(b.getAttribute('data-r'))+'&name='+encodeURIComponent(b.getAttribute('data-n'))+'&pin='+encodeURIComponent(pin));}");
            sb.Append("},true);");
            sb.Append("document.body.addEventListener('change',function(e){if(e.target.classList&&e.target.classList.contains('pick'))e.stopPropagation();},true);");
            // upload progress (with speed)
            sb.Append("(function(){var f=$('upform');if(!f||!f.addEventListener)return;");
            sb.Append("f.addEventListener('submit',function(e){var fi=$('fileinput');");
            sb.Append("if(!window.FormData||!fi.files||!fi.files.length)return;e.preventDefault();");
            sb.Append("var fd=new FormData(f),x=new XMLHttpRequest();x.open('POST','/upload');");
            sb.Append("var bar=$('bar'),fill=$('barfill'),pt=$('ptxt'),t0=Date.now();");
            sb.Append("bar.style.display='block';");
            sb.Append("x.upload.onprogress=function(ev){if(ev.lengthComputable){var p=Math.round(ev.loaded*100/ev.total);");
            sb.Append("fill.style.width=p+'%';var sp=(ev.loaded/1024)/((Date.now()-t0)/1000+0.001);");
            sb.Append("pt.innerHTML=p+' % \\u2022 '+(sp>1024?(sp/1024).toFixed(1)+' MB/s':sp.toFixed(0)+' KB/s');}};");
            sb.Append("x.onload=function(){location.reload();};x.onerror=function(){f.submit();};x.send(fd);});})();");
            // note send
            sb.Append("$('notesend').addEventListener('click',function(){var n=$('note');");
            sb.Append("if(!n.value)return;var x=new XMLHttpRequest();x.open('POST','/note');");
            sb.Append("x.setRequestHeader('Content-Type','application/x-www-form-urlencoded');");
            sb.Append("x.onload=function(){$('noteok').textContent='").Append(HtmlEncode(L.T("page_note_ok"))).Append("';n.value='';setTimeout(function(){$('noteok').textContent='';},2500);};");
            sb.Append("x.send('text='+encodeURIComponent(n.value));});");
            // auto-refresh long-poll
            sb.Append("function poll(){var x=new XMLHttpRequest();x.open('GET','/poll?sig='+SIG,true);");
            sb.Append("x.onreadystatechange=function(){if(x.readyState!=4)return;");
            sb.Append("if(x.responseText&&x.responseText!='same'&&x.responseText.length<40){SIG=x.responseText;");
            sb.Append("var y=new XMLHttpRequest();y.open('GET','/fragment',true);");
            sb.Append("y.onreadystatechange=function(){if(y.readyState==4&&y.responseText){");
            sb.Append("var tmp=document.createElement('div');tmp.innerHTML=y.responseText;");
            sb.Append("var a=tmp.querySelector('#a'),b=tmp.querySelector('#b');");
            sb.Append("if(a)$('dl').innerHTML=a.innerHTML;if(b)$('up').innerHTML=b.innerHTML;}};");
            sb.Append("y.send(null);}");
            sb.Append("setTimeout(poll,1500);};x.send(null);}");
            sb.Append("poll();");
            sb.Append("</script></body></html>");
            return sb.ToString();
        }

        private string BuildCss(Theme t,string pageFont)
        {
            string cDark=Theme.Hex(t.Dark), cMid=Theme.Hex(t.Mid), cLight=Theme.Hex(t.Light);
            string cSoft=Theme.Hex(t.Soft), cBg=Theme.Hex(t.Bg), cTxt=Theme.Hex(t.Text);
            string cCard=Theme.Hex(t.FieldBg);
            string cHead=Theme.Hex(Theme.Mix(t.Dark,t.Mid,0.45));
            string cEmpty=Theme.Hex(Theme.Mix(t.Text,t.Bg,0.45));
            string cSize=Theme.Hex(Theme.Mix(t.Text,t.Bg,0.3));
            string cBtnTop=Theme.Hex(Theme.Mix(t.Mid,t.Light,0.35));
            string cBtnTxt=Theme.Hex(Theme.Mix(t.Light,Color.White,0.92));
            string cTitle=Theme.Hex(Theme.Mix(t.Light,Color.White,0.88));
            string cSub=Theme.Hex(Theme.Mix(t.Light,Color.White,0.7));
            StringBuilder s=new StringBuilder();
            s.Append("*{box-sizing:border-box}");
            s.Append("body{margin:0;background:").Append(cBg).Append(";color:").Append(cTxt)
              .Append(";font-size:").Append(pageFont)
              .Append(";font-family:Georgia,'Times New Roman',Tahoma,serif}");
            s.Append(".hero{background:linear-gradient(120deg,").Append(cDark).Append(",").Append(cMid)
              .Append(" 45%,").Append(cLight).Append(");padding:30px 16px 22px;text-align:center}");
            s.Append(".hero h1{margin:0;font-size:32px;color:").Append(cTitle).Append(";letter-spacing:5px;text-shadow:0 2px 8px ").Append(Theme.Rgba(Color.Black,0.5)).Append("}");
            s.Append(".hero .sub{margin-top:5px;color:").Append(cSub).Append(";font-style:italic;font-size:14px}");
            s.Append(".badge{display:inline-block;margin-top:10px;background:").Append(cCard).Append(";color:").Append(cDark)
              .Append(";border:1px solid ").Append(cSoft).Append(";border-radius:20px;padding:4px 16px;font-size:13px;font-family:Verdana}");
            s.Append(".wrap{max-width:840px;margin:22px auto 40px;padding:0 12px}");
            s.Append(".toolbar{display:flex;flex-wrap:wrap;gap:6px;margin-bottom:8px}");
            s.Append("#search{flex:1;min-width:160px;padding:9px 12px;border:1px solid ").Append(cSoft).Append(";border-radius:20px;font-size:15px;background:").Append(cCard).Append(";color:").Append(cTxt).Append("}");
            s.Append(".chip{background:").Append(cCard).Append(";border:1px solid ").Append(cSoft).Append(";color:").Append(cHead)
              .Append(";border-radius:16px;padding:8px 13px;font-size:13px;cursor:pointer;font-family:Verdana}");
            s.Append(".chip.gold{background:linear-gradient(180deg,").Append(cBtnTop).Append(",").Append(cMid).Append(");color:").Append(cBtnTxt).Append(";border-color:").Append(cDark).Append("}");
            s.Append(".storage{font-size:12px;color:").Append(cSize).Append(";margin:4px 2px 14px;font-family:Verdana}");
            s.Append(".card{background:").Append(cCard).Append(";border:1px solid ").Append(cSoft)
              .Append(";border-radius:14px;box-shadow:0 8px 26px ").Append(Theme.Rgba(t.Dark,0.18)).Append(";padding:20px;margin-bottom:22px}");
            s.Append(".card h2{margin:0 0 13px;font-size:16px;color:").Append(cHead).Append(";letter-spacing:2px;border-bottom:2px solid ").Append(cSoft).Append(";padding-bottom:8px}");
            s.Append(".file{display:flex;align-items:center;gap:9px;padding:9px 10px;margin:2px 0;border-radius:9px;text-decoration:none;color:").Append(cTxt).Append(";border:1px solid transparent}");
            s.Append(".file:hover{background:").Append(cSoft).Append("}");
            s.Append(".pick{width:17px;height:17px}");
            s.Append(".fname{word-break:break-all}");
            s.Append(".mini{background:none;border:none;cursor:pointer;font-size:15px;padding:2px 4px}");
            s.Append(".fsize{margin-left:auto;color:").Append(cSize).Append(";font-size:12px;white-space:nowrap;font-family:Verdana}");
            s.Append(".empty{color:").Append(cEmpty).Append(";font-style:italic;padding:8px 4px}");
            s.Append(".thumb{width:44px;height:44px;object-fit:cover;border-radius:7px;border:1px solid ").Append(cSoft).Append("}");
            s.Append("body.grid .file{display:inline-flex;flex-direction:column;width:30.5%;vertical-align:top;margin:4px 0.4%;text-align:center}");
            s.Append("body.grid .thumb{width:100%;height:110px;object-fit:cover}");
            s.Append("body.grid .fname{font-size:13px}");
            s.Append("input[type=file]{font-family:Verdana;font-size:13px;margin-bottom:12px;max-width:100%;color:").Append(cTxt).Append("}");
            s.Append(".btn{display:inline-block;background:linear-gradient(180deg,").Append(cBtnTop).Append(",").Append(cMid)
              .Append(");border:1px solid ").Append(cDark).Append(";color:").Append(cBtnTxt)
              .Append(";font-weight:bold;font-size:15px;padding:11px 26px;border-radius:9px;cursor:pointer;font-family:inherit}");
            s.Append(".bar{height:14px;background:").Append(cSoft).Append(";border-radius:8px;overflow:hidden;display:none;margin-top:12px}");
            s.Append(".bar>div{height:100%;width:0;background:linear-gradient(90deg,").Append(cMid).Append(",").Append(cLight).Append(")}");
            s.Append("#ptxt{font-size:12px;color:").Append(cHead).Append(";font-family:Verdana;margin-top:4px}");
            s.Append("textarea{width:100%;padding:10px;border:1px solid ").Append(cSoft).Append(";border-radius:9px;font-family:inherit;font-size:15px;background:").Append(cCard).Append(";color:").Append(cTxt).Append("}");
            s.Append("#noteok{color:").Append(cHead).Append(";font-size:13px;min-height:16px;margin-top:6px;font-family:Verdana}");
            s.Append("footer{text-align:center;color:").Append(cEmpty).Append(";font-size:12px;padding:14px;font-family:Verdana}");
            return s.ToString();
        }

        private string StorageText()
        {
            try
            {
                DriveInfo d=new DriveInfo(Path.GetPathRoot(baseFolder));
                return string.Format(L.T("page_storage_f"),FormatSize(d.AvailableFreeSpace),FormatSize(d.TotalSize));
            } catch { return ""; }
        }

        private void BuildFileList(StringBuilder sb,string folder,string root)
        {
            bool canDel=Settings.AllowDelete&&Settings.PasswordHash.Length>0;
            try
            {
                DirectoryInfo di=new DirectoryInfo(folder);
                FileInfo[] files=di.GetFiles();
                Array.Sort(files,delegate(FileInfo a,FileInfo b)
                { return string.Compare(a.Name,b.Name,true); });
                if(files.Length==0)
                { sb.Append("<div class=\"empty\">").Append(L.T("page_no_files")).Append("</div>"); return; }
                for(int i=0;i<files.Length;i++)
                {
                    FileInfo fi=files[i];
                    string enc=Uri.EscapeDataString(fi.Name);
                    string rel="/"+root+"/"+enc;
                    bool isImg=IsImage(fi.Name);
                    sb.Append("<a class=\"file\" href=\"").Append(rel).Append("\"");
                    sb.Append(" data-n=\"").Append(HtmlEncode(fi.Name)).Append("\"");
                    sb.Append(" data-s=\"").Append(fi.Length).Append("\"");
                    sb.Append(" data-t=\"").Append(fi.LastWriteTimeUtc.Ticks).Append("\">");
                    sb.Append("<input type=\"checkbox\" class=\"pick\" value=\"").Append(root+"|"+HtmlEncode(fi.Name)).Append("\">");
                    if(isImg)
                        sb.Append("<img class=\"thumb\" loading=\"lazy\" src=\"/thumb/").Append(root.ToLower()).Append("/").Append(enc).Append("\">");
                    else
                        sb.Append("<span>").Append(IconFor(fi.Name)).Append("</span>");
                    sb.Append("<span class=\"fname\">").Append(HtmlEncode(fi.Name)).Append("</span>");
                    sb.Append("<button class=\"mini\" data-act=\"copy\" data-u=\"").Append(rel).Append("\" title=\"Copy link\">\uD83D\uDCCB</button>");
                    sb.Append("<button class=\"mini\" data-act=\"qr\" data-u=\"").Append(rel).Append("\" title=\"QR\">\u25A3</button>");
                    if(canDel)
                        sb.Append("<button class=\"mini\" data-act=\"del\" data-r=\"").Append(root)
                          .Append("\" data-n=\"").Append(HtmlEncode(fi.Name)).Append("\" title=\"Delete\">\uD83D\uDDD1</button>");
                    sb.Append("<span class=\"fsize\">").Append(FormatSize(fi.Length)).Append("</span></a>");
                }
            }
            catch { sb.Append("<div class=\"empty\">Folder not available.</div>"); }
        }

        private static bool IsImage(string name)
        {
            string e=Path.GetExtension(name).ToLower();
            return e==".png"||e==".jpg"||e==".jpeg"||e==".gif"||e==".bmp";
        }

        private string BuildFragment()
        {
            StringBuilder sb=new StringBuilder();
            sb.Append("<div id=\"a\">"); BuildFileList(sb,downloadsFolder,"Downloads"); sb.Append("</div>");
            sb.Append("<div id=\"b\">"); BuildFileList(sb,uploadsFolder,"Uploads"); sb.Append("</div>");
            return sb.ToString();
        }

        // --------------------------------------------------------------------
        //  THUMBNAILS
        // --------------------------------------------------------------------
        private void HandleThumb(HttpListenerContext ctx,string rest)
        {
            try
            {
                int sl=rest.IndexOf('/');
                if(sl<=0) { WriteText(ctx,"404",404,"text/plain"); return; }
                string root=rest.Substring(0,sl).ToLower();
                string name=UrlDecode(rest.Substring(sl+1));
                if(root!="downloads"&&root!="uploads") { WriteText(ctx,"404",404,"text/plain"); return; }
                if(!SafeName(name)||!IsImage(name)) { WriteText(ctx,"404",404,"text/plain"); return; }
                string full=Path.Combine(root=="downloads"?downloadsFolder:uploadsFolder,name);
                if(!File.Exists(full)) { WriteText(ctx,"404",404,"text/plain"); return; }
                using(FileStream fs=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(Image img=Image.FromStream(fs))
                {
                    int w=180, h=(int)((long)img.Height*w/img.Height);
                    if(h<1)h=1;
                    Image.GetThumbnailImageAbort cb=delegate { return false; };
                    using(Image tn=img.GetThumbnailImage(w,h,cb,IntPtr.Zero))
                    {
                        MemoryStream ms=new MemoryStream();
                        tn.Save(ms,ImageFormat.Png);
                        ctx.Response.AddHeader("Cache-Control","max-age=300");
                        WriteBytes(ctx,ms.ToArray(),200,"image/png");
                    }
                }
            }
            catch { try{ WriteText(ctx,"404",404,"text/plain"); } catch { } }
        }

        // --------------------------------------------------------------------
        //  ZIP BUILDER (STORE method, pure C#)
        // --------------------------------------------------------------------
        private static uint[] zipTbl;
        static MainForm()
        {
            zipTbl=new uint[256];
            for(int i=0;i<256;i++)
            {
                uint c=(uint)i;
                for(int k=0;k<8;k++) c=((c&1)!=0)?(0xEDB88320^(c>>1)):(c>>1);
                zipTbl[i]=c;
            }
        }
        private static uint Crc32Cont(uint c,byte[] b,int off,int len)
        {
            for(int i=0;i<len;i++) c=zipTbl[(c^b[off+i])&0xFF]^(c>>8);
            return c;
        }

        private void HandleZip(HttpListenerContext ctx)
        {
            string files=ctx.Request.QueryString["files"];
            if(files==null||files.Length==0){ WriteText(ctx,L.T("msg_zip_none"),400,"text/plain"); return; }
            List<string[]> items=new List<string[]>();
            long total=0;
            string[] parts=files.Split('|');
            for(int i=0;i<parts.Length;i++)
            {
                string p;
                try{ p=UrlDecode(parts[i]); } catch { continue; }
                int sl=p.IndexOf('|');
                string root,name;
                if(sl>0){ root=p.Substring(0,sl); name=p.Substring(sl+1); }
                else { int s2=p.IndexOf('/'); if(s2<=0) continue; root=p.Substring(0,s2); name=UrlDecode(p.Substring(s2+1)); }
                if(root!="Downloads"&&root!="Uploads") continue;
                if(!SafeName(name)) continue;
                string full=Path.Combine(root=="Downloads"?downloadsFolder:uploadsFolder,name);
                if(!File.Exists(full)) continue;
                total+=new FileInfo(full).Length;
                if(total>1024L*1024*1024) break; // 1 GB cap
                items.Add(new string[]{root,name,full});
            }
            if(items.Count==0){ WriteText(ctx,L.T("msg_zip_none"),400,"text/plain"); return; }

            // ---- STREAMING ZIP: local headers + file data fly straight out to the
            // client; CRC/sizes land in a post-data descriptor (bit 3), so RAM stays
            // flat even for large selections. Exact Content-Length pre-computed once. ----
            byte[][] names=new byte[items.Count][];
            long[] lens=new long[items.Count];
            long cdStart=0, centralSize=0;
            for(int i=0;i<items.Count;i++)
            {
                names[i]=Encoding.UTF8.GetBytes(items[i][0]+"/"+items[i][1]);
                lens[i]=new FileInfo(items[i][2]).Length;
                cdStart+=30+names[i].Length+lens[i]+16;      // local header + name + data + descriptor
                centralSize+=46+names[i].Length;
            }
            long totalOut=cdStart+centralSize+22;

            ctx.Response.StatusCode=200;
            ctx.Response.ContentType="application/zip";
            ctx.Response.AddHeader("Content-Disposition","attachment; filename=\"GoldShare-Files.zip\"");
            ctx.Response.ContentLength64=totalOut;

            BinaryWriter bw=new BinaryWriter(ctx.Response.OutputStream);
            uint[] crcs=new uint[items.Count];
            long[] offs=new long[items.Count];
            byte[] buf=IoBuf();
            long pos=0;
            for(int i=0;i<items.Count;i++)
            {
                offs[i]=pos;
                bw.Write((int)0x04034b50); bw.Write((short)20);      // version
                bw.Write((short)0x0008);                            // bit 3: sizes via descriptor
                bw.Write((short)0);                                 // method 0 = STORE
                bw.Write((short)0); bw.Write((short)0);             // time/date
                bw.Write((int)0); bw.Write((int)0); bw.Write((int)0); // crc/sizes in descriptor
                bw.Write((short)names[i].Length); bw.Write((short)0);
                bw.Write(names[i]);
                pos+=30+names[i].Length;

                uint c=0xFFFFFFFF;
                using(FileStream fs=new FileStream(items[i][2],FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    int n;
                    while((n=fs.Read(buf,0,buf.Length))>0)
                    { c=Crc32Cont(c,buf,0,n); bw.Write(buf,0,n); pos+=n; }
                }
                crcs[i]=c^0xFFFFFFFF;

                bw.Write((int)0x08074b50);                         // data descriptor
                bw.Write((uint)crcs[i]); bw.Write((int)lens[i]); bw.Write((int)lens[i]);
                pos+=16;
            }

            for(int i=0;i<items.Count;i++)
            {
                bw.Write((int)0x02014b50); bw.Write((short)20); bw.Write((short)20);
                bw.Write((short)0x0008);
                bw.Write((short)0); bw.Write((short)0); bw.Write((short)0);
                bw.Write((uint)crcs[i]); bw.Write((int)lens[i]); bw.Write((int)lens[i]);
                bw.Write((short)names[i].Length);
                bw.Write((short)0); bw.Write((short)0); bw.Write((short)0); bw.Write((short)0);
                bw.Write((int)0); bw.Write((int)offs[i]); bw.Write(names[i]);
            }
            bw.Write((int)0x06054b50); bw.Write((short)0); bw.Write((short)0);
            bw.Write((short)items.Count); bw.Write((short)items.Count);
            bw.Write((int)centralSize); bw.Write((int)cdStart); bw.Write((short)0);
            bw.Flush();

            Log(string.Format(L.T("msg_zip_ok"),items.Count,FormatSize(totalOut)));
            try{ ctx.Response.OutputStream.Close(); } catch { }
            CollectAndTrim(); // flush the streamed zip buffers out of RAM
        }

        // --------------------------------------------------------------------
        //  FILE DOWNLOAD (with Range resume + speed limit)
        // --------------------------------------------------------------------
        private void ServeFile(HttpListenerContext ctx,string folder,string rawName)
        {
            string name=UrlDecode(rawName);
            if(name==null||name.Length==0||name.IndexOf("..")>=0||
               name.IndexOf("/")>=0||name.IndexOf("\\")>=0)
            { WriteText(ctx,"Forbidden",403,"text/plain"); return; }
            string full=Path.Combine(folder,name);
            if(!File.Exists(full)){ WriteText(ctx,"404 - File not found",404,"text/plain"); return; }

            long total=new FileInfo(full).Length;
            long start=0,end=total-1;
            string rng=ctx.Request.Headers["Range"];
            bool partial=false;
            if(rng!=null&&rng.StartsWith("bytes="))
            {
                string spec=rng.Substring(6);
                int dash=spec.IndexOf('-');
                if(dash==0){ long n; if(long.TryParse(spec.Substring(1),out n)) { start=total-n; if(start<0)start=0; partial=true; } }
                else if(dash>0)
                {
                    long s;
                    if(long.TryParse(spec.Substring(0,dash),out s)&&s<total)
                    {
                        start=s; partial=true;
                        string eStr=spec.Substring(dash+1);
                        if(eStr.Length>0){ long e2; if(long.TryParse(eStr,out e2)&&e2<total) end=e2; }
                    }
                }
            }
            if(start>end||start>=total){ WriteText(ctx,"Range error",416,"text/plain"); return; }

            string ext=Path.GetExtension(name).ToLower();
            bool inline=(ext==".png"||ext==".jpg"||ext==".jpeg"||ext==".gif"||ext==".bmp"||ext==".pdf"||ext==".txt");
            string disp=inline?"inline":"attachment";
            if(IsAscii(name))
                ctx.Response.AddHeader("Content-Disposition",disp+"; filename=\""+name+"\"");
            else
                ctx.Response.AddHeader("Content-Disposition",disp+"; filename=\"file"+ext+
                    "\"; filename*=UTF-8''"+Uri.EscapeDataString(name));
            if(partial)
                ctx.Response.AddHeader("Content-Range","bytes "+start+"-"+end+"/"+total);

            ctx.Response.StatusCode=partial?206:200;
            ctx.Response.ContentType=MimeFor(name);
            long sendLen=end-start+1;
            ctx.Response.ContentLength64=sendLen;

            Stopwatch sw=new Stopwatch(); sw.Start();
            long sent=0;
            using(FileStream fs=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                fs.Position=start;
                byte[] buf=IoBuf();
                while(sent<sendLen&&running)
                {
                    int want=(int)Math.Min((long)buf.Length,sendLen-sent);
                    int n=fs.Read(buf,0,want);
                    if(n<=0) break;
                    ctx.Response.OutputStream.Write(buf,0,n);
                    sent+=n;
                    Interlocked.Add(ref statSent,(long)n);
                    if(Settings.SpeedKB>0)
                    {
                        double wantSec=sent/(Settings.SpeedKB*1024.0);
                        double el=sw.Elapsed.TotalSeconds;
                        if(wantSec>el+0.002)
                            Thread.Sleep((int)((wantSec-el)*1000));
                    }
                }
            }
            ctx.Response.OutputStream.Close();
        }

        // --------------------------------------------------------------------
        //  UPLOAD (STREAMING multipart parser — constant RAM, no full-body buffer)
        //  Body lands on a temp file on disk; parts are copied out as boundaries
        //  are hit. A 500 MB upload stays flat in memory on a 256 MB XP box.
        // --------------------------------------------------------------------
        private static int upSeq;

        private void HandleUpload(HttpListenerContext ctx)
        {
            Interlocked.Increment(ref upActive);
            upTotal=ctx.Request.ContentLength64;
            upSent=0;
            string tempFile=null;
            try
            {
                string ctype=ctx.Request.ContentType==null?"":ctx.Request.ContentType;
                int bi=ctype.IndexOf("boundary=",StringComparison.OrdinalIgnoreCase);
                if(bi<0){ WriteText(ctx,"Bad request",400,"text/plain"); return; }
                string boundary=ctype.Substring(bi+9).Trim();
                if(boundary.StartsWith("\"")&&boundary.EndsWith("\"")&&boundary.Length>=2)
                    boundary=boundary.Substring(1,boundary.Length-2);
                byte[] bB=Encoding.UTF8.GetBytes("--"+boundary);

                long cap=(long)Settings.UploadMB*1024*1024;
                tempFile=Path.Combine(baseFolder,"up_"+DateTime.Now.Ticks+"_"
                    +Interlocked.Increment(ref upSeq)+".tmp");

                // pass 1: drain the network to disk, enforcing the size cap
                long total=0;
                using(FileStream tmp=new FileStream(tempFile,FileMode.Create,FileAccess.Write))
                {
                    byte[] buf=IoBuf(); int n;
                    while((n=ctx.Request.InputStream.Read(buf,0,buf.Length))>0)
                    {
                        tmp.Write(buf,0,n);
                        total+=n;
                        Interlocked.Add(ref statRecv,(long)n);
                        upSent=total;
                        if(total>cap) break;
                    }
                }
                if(total>cap)
                {
                    WriteText(ctx,"File too large (limit "+Settings.UploadMB+" MB)",413,"text/plain");
                    return;
                }

                // pass 2: scan boundaries in 64 KB windows (flat memory)
                // a real part separator must sit at a line start (preceded by CRLF)
                // AND be followed by CRLF (or '--' for the closing one) per RFC 2046 —
                // the random sequence may otherwise appear inside a file's bytes.
                int saved=0;
                using(FileStream tmp=new FileStream(tempFile,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    List<long> B=new List<long>();
                    FindBoundaries(tmp,bB,B);
                    List<long> valid=new List<long>();
                    for(int v=0;v<B.Count;v++)
                    {
                        long p=B[v];
                        if(p>0)
                        {
                            tmp.Position=p-2;
                            if(tmp.ReadByte()!=13||tmp.ReadByte()!=10) continue;
                        }
                        long tail=p+bB.Length;
                        if(tail+1>=tmp.Length) continue;
                        tmp.Position=tail;
                        int b1=tmp.ReadByte(), b2=tmp.ReadByte();
                        if(!((b1==13&&b2==10)||(b1==45&&b2==45))) continue;
                        valid.Add(p);
                    }
                    for(int k=0;k<valid.Count-1;k++)
                    {
                        long bp=valid[k], np=valid[k+1];
                        long after=bp+bB.Length;
                        byte[] m2=new byte[2];
                        tmp.Position=after;
                        if(tmp.Read(m2,0,2)==2&&m2[0]==(byte)'-'&&m2[1]==(byte)'-') break; // closing boundary
                        long seg=after+2; // skip CRLF
                        byte[] hdr=IoBuf();
                        int hdrN=ReadUpTo(tmp,seg,hdr);
                        int hdrEnd=IndexOf(hdr,CRLFCRLF,0);
                        if(hdrEnd<0||hdrEnd+4>hdrN||seg+hdrEnd+4>=np) continue; // malformed part, keep going
                        string headers=Encoding.UTF8.GetString(hdr,0,hdrEnd);
                        string fname=ExtractFilename(headers);
                        int bi2=fname==null?-1:fname.LastIndexOf('\\'); if(bi2>=0)fname=fname.Substring(bi2+1);
                        bi2=fname==null?-1:fname.LastIndexOf('/'); if(bi2>=0)fname=fname.Substring(bi2+1);
                        long dataStart=seg+hdrEnd+4;
                        long dataEnd=np-2; // strip CRLF ahead of next boundary
                        if(fname!=null&&fname.Length>0&&dataEnd>dataStart)
                        {
                            string dest=UniqueDest(fname);
                            CopyRange(tmp,dataStart,dataEnd-dataStart,dest);
                            saved++;
                            Log(string.Format(L.T("msg_uploaded"),Path.GetFileName(dest),FormatSize(dataEnd-dataStart)));
                        }
                    }
                }
                Log(string.Format(L.T("msg_upload_done"),saved));
                if(Settings.Beep&&saved>0)
                { try{ Console.Beep(700,120); Console.Beep(1000,160); } catch { } }
                ctx.Response.StatusCode=302;
                ctx.Response.RedirectLocation="/";
                ctx.Response.OutputStream.Close();
                CollectAndTrim(); // flush the transfer buffers out of RAM
            }
            finally
            {
                if(tempFile!=null) try{ File.Delete(tempFile); } catch { }
                Interlocked.Decrement(ref upActive);
                try
                {
                    statusPanel.BeginInvoke(new MethodInvoker(delegate { statusPanel.Invalidate(); }));
                } catch { }
            }
        }

        private string UniqueDest(string fname)
        {
            string safe=SanitizeFileName(fname);
            if(safe.Length==0) safe="upload.bin";
            string dest=Path.Combine(uploadsFolder,safe);
            int c=1;
            while(File.Exists(dest))
            {
                string ext=Path.GetExtension(safe);
                dest=Path.Combine(uploadsFolder,Path.GetFileNameWithoutExtension(safe)+" ("+c+")"+ext);
                c++;
            }
            return dest;
        }

        // ---- streaming scan for a byte pattern across a whole file ----
        private static void FindBoundaries(FileStream fs,byte[] needle,List<long> res)
        {
            int nl=needle.Length;
            if(nl==0) return;
            byte[] win=new byte[65536+nl];
            int wl=0; long baseOff=0;
            while(true)
            {
                int n=fs.Read(win,wl,win.Length-wl);
                if(n<=0) break;
                wl+=n;
                int lim=wl-nl;
                for(int i=0;i<=lim;i++)
                {
                    if(win[i]!=needle[0]) continue;
                    bool ok=true;
                    for(int j=1;j<nl;j++) if(win[i+j]!=needle[j]){ ok=false; break; }
                    if(ok) res.Add(baseOff+i);
                }
                int tail=nl-1;
                if(wl>tail)
                { Buffer.BlockCopy(win,wl-tail,win,0,tail); baseOff+=wl-tail; wl=tail; }
                else { baseOff+=wl; wl=0; }
            }
        }

        private static int ReadUpTo(FileStream fs,long off,byte[] buf)
        { fs.Position=off; return fs.Read(buf,0,buf.Length); }

        private static void CopyRange(FileStream src,long off,long len,string dest)
        {
            src.Position=off;
            byte[] buf=IoBuf();
            using(FileStream o=new FileStream(dest,FileMode.Create,FileAccess.Write))
            {
                long left=len;
                while(left>0)
                {
                    int want=(int)Math.Min((long)buf.Length,left);
                    int n=src.Read(buf,0,want);
                    if(n<=0) break;
                    o.Write(buf,0,n);
                    left-=n;
                }
            }
        }

        // --------------------------------------------------------------------
        //  HELPERS
        // --------------------------------------------------------------------
        // SuperLite: one 64 KB buffer per worker thread instead of a fresh beat[] on
        // every request — the heap stays flat, so a long-lived server holds nothing.
        [ThreadStatic] private static byte[] tIoBuf;
        private static byte[] IoBuf()
        {
            byte[] b=tIoBuf;
            if(b==null){ b=new byte[65536]; tIoBuf=b; }
            return b;
        }

        private static int IndexOf(byte[] hay,byte[] needle,int start)
        {
            if(needle.Length==0) return start;
            int limit=hay.Length-needle.Length;
            byte first=needle[0];
            for(int i=start;i<=limit;i++)
            {
                if(hay[i]!=first) continue;
                bool ok=true;
                for(int j=1;j<needle.Length;j++)
                    if(hay[i+j]!=needle[j]){ ok=false; break; }
                if(ok) return i;
            }
            return -1;
        }

        private static string ExtractFilename(string headers)
        {
            string lower=headers.ToLower();
            int i=lower.IndexOf("filename=");
            if(i<0) return null;
            string rest=headers.Substring(i+9).Trim();
            if(rest.StartsWith("\""))
            {
                int end=rest.IndexOf('"',1);
                return end<0?null:rest.Substring(1,end-1);
            }
            int sp=rest.IndexOf(';');
            return sp<0?rest.Trim():rest.Substring(0,sp).Trim();
        }

        private static string SanitizeFileName(string name)
        {
            name=name.Replace("/","_").Replace("\\","_").Replace("..","_");
            foreach(char c in Path.GetInvalidFileNameChars()) name=name.Replace(c,'_');
            if(name.Length>150)
            {
                string ext=Path.GetExtension(name);
                name=name.Substring(0,150-ext.Length)+ext;
            }
            return name.Trim();
        }

        private static string UrlDecode(string s)
        {
            try{ return Uri.UnescapeDataString(s.Replace("+","%20")); }
            catch { return s; }
        }

        private static string HtmlEncode(string s)
        {
            return s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;");
        }

        private static bool IsAscii(string s)
        { foreach(char c in s) if(c<32||c>126) return false; return true; }

        private static string FormatSize(long len)
        {
            double d=len;
            if(d<1024) return len+" B";
            d/=1024; if(d<1024) return d.ToString("0.0")+" KB";
            d/=1024; if(d<1024) return d.ToString("0.0")+" MB";
            d/=1024; return d.ToString("0.00")+" GB";
        }

        private static string MimeFor(string name)
        {
            switch(Path.GetExtension(name).ToLower())
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
            switch(Path.GetExtension(name).ToLower())
            {
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": return "\uD83D\uDDBC";
                case ".mp3": case ".wav": case ".wma": case ".mid": return "\uD83C\uDFB5";
                case ".mp4": case ".avi": case ".wmv": case ".mkv": case ".mov": return "\uD83C\uDFAC";
                case ".zip": case ".rar": case ".7z": return "\uD83D\uDDDC";
                case ".pdf": return "\uD83D\uDCD5";
                case ".txt": case ".log": case ".ini": return "\uD83D\uDCDD";
                case ".cs": case ".cpp": case ".h": case ".js": case ".css": return "\uD83D\uDCBB";
                case ".doc": case ".docx": case ".xls": case ".xlsx": return "\uD83D\uDCCA";
                default: return "\uD83D\uDCC4";
            }
        }

        private static void WriteText(HttpListenerContext ctx,string content,int status,string contentType)
        { WriteBytes(ctx,Encoding.UTF8.GetBytes(content),status,contentType); }

        private static void WriteBytes(HttpListenerContext ctx,byte[] data,int status,string contentType)
        {
            ctx.Response.StatusCode=status;
            ctx.Response.ContentType=contentType;
            ctx.Response.ContentLength64=data.Length;
            if(ctx.Request.HttpMethod!="HEAD")
                ctx.Response.OutputStream.Write(data,0,data.Length);
            ctx.Response.OutputStream.Close();
        }

        // ---- keyboard shortcuts ----
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {
            if(keyData==Keys.F5){ ToggleServer(); return true; }
            if(keyData==(Keys.Control|Keys.Q)){ ShowQr(); return true; }
            if(keyData==(Keys.Control|Keys.U)){ CopyUrl(); return true; }
            if(keyData==(Keys.Control|Keys.O)){ OpenSharedFolder(); return true; }
            if(keyData==(Keys.Control|Keys.G)){ ShowSettings(); return true; }
            return base.ProcessCmdKey(ref msg,keyData);
        }
    }

    // ========================================================================
    //  SETTINGS DIALOG
    // ========================================================================
    public class SettingsForm : Form
    {
        public bool FontNeedsRestart;   // renamed: 'FontChanged' clashed with Control.FontChanged

        private TextBox txtPort, txtUp, txtSpeed, txtPass;
        private CheckBox chkAuto, chkDel, chkTray, chkPos, chkBeep, chkLog;
        private ComboBox cboFont;
        private int oldFont;

        public SettingsForm()
        {
            Text=L.T("set_title");
            FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=false; MinimizeBox=false;
            ShowInTaskbar=false;
            StartPosition=FormStartPosition.CenterParent;
            ClientSize=new Size(430,470);
            BackColor=Theme.Current.Bg;
            RightToLeft=L.IsArabic?RightToLeft.Yes:RightToLeft.No;

            Theme t=Theme.Current;
            int y=20;
            txtPort=Field(L.T("set_port"),Settings.Port.ToString(),ref y);
            txtUp=Field(L.T("set_uplim"),Settings.UploadMB.ToString(),ref y);
            txtSpeed=Field(L.T("set_speed"),Settings.SpeedKB.ToString(),ref y);

            chkAuto=Check(L.T("set_autostart"),Settings.AutoStart,ref y);
            chkDel=Check(L.T("set_allowdel"),Settings.AllowDelete,ref y);
            chkTray=Check(L.T("set_tray"),Settings.Tray,ref y);
            chkPos=Check(L.T("set_savepos"),Settings.SavePos,ref y);
            chkBeep=Check(L.T("set_beep"),Settings.Beep,ref y);
            chkLog=Check(L.T("set_log"),Settings.LogFile,ref y);

            Label lp=MakeLbl(L.T("set_pass"),20,y); Controls.Add(lp);
            txtPass=new TextBox();
            txtPass.Location=new Point(230,y-3); txtPass.Size=new Size(180,24);
            Controls.Add(txtPass);
            y+=34;
            Label hint=MakeLbl(L.T("set_pass_hint"),20,y);
            hint.Size=new Size(390,30); hint.ForeColor=Theme.Mix(t.Text,Color.Gray,0.2);
            Controls.Add(hint);
            y+=36;

            Label lf=MakeLbl(L.T("set_font"),20,y); Controls.Add(lf);
            cboFont=new ComboBox();
            cboFont.DropDownStyle=ComboBoxStyle.DropDownList;
            cboFont.Location=new Point(230,y-3); cboFont.Size=new Size(180,24);
            cboFont.Items.Add(L.T("font_s")); cboFont.Items.Add(L.T("font_m")); cboFont.Items.Add(L.T("font_l"));
            cboFont.SelectedIndex=Settings.FontSize;
            Controls.Add(cboFont);
            y+=44;

            GoldButton ok=new GoldButton();
            ok.Text=L.T("save"); ok.Location=new Point(80,y); ok.Size=new Size(130,36);
            ok.Click+=new EventHandler(delegate { SaveAndClose(); });
            Controls.Add(ok);

            GoldButton cancel=new GoldButton();
            cancel.Text=L.T("cancel"); cancel.Location=new Point(230,y); cancel.Size=new Size(130,36);
            cancel.Click+=new EventHandler(delegate { DialogResult=DialogResult.Cancel; Close(); });
            Controls.Add(cancel);
        }

        private Label MakeLbl(string s,int x,int y)
        {
            Label l=new Label();
            l.Text=s; l.Location=new Point(x,y); l.Size=new Size(205,20);
            l.ForeColor=Theme.Current.Text;
            return l;
        }

        private TextBox Field(string lbl,string val,ref int y)
        {
            Controls.Add(MakeLbl(lbl,20,y));
            TextBox tb=new TextBox();
            tb.Text=val; tb.Location=new Point(230,y-3); tb.Size=new Size(180,24);
            Controls.Add(tb);
            y+=34;
            return tb;
        }

        private CheckBox Check(string lbl,bool val,ref int y)
        {
            CheckBox c=new CheckBox();
            c.Text=lbl; c.Checked=val;
            c.Location=new Point(22,y); c.Size=new Size(390,22);
            c.ForeColor=Theme.Current.Text;
            Controls.Add(c);
            y+=28;
            return c;
        }

        private void SaveAndClose()
        {
            int p,u,sp;
            if(!int.TryParse(txtPort.Text.Trim(),out p)||p<1||p>65535)
            { MessageBox.Show(L.T("msg_bad_port"),"GoldShare"); return; }
            if(!int.TryParse(txtUp.Text.Trim(),out u)||u<1) u=512;
            if(!int.TryParse(txtSpeed.Text.Trim(),out sp)||sp<0) sp=0;

            oldFont=Settings.FontSize;

            Settings.Port=p; Settings.UploadMB=u; Settings.SpeedKB=sp;
            Settings.AutoStart=chkAuto.Checked;
            Settings.AllowDelete=chkDel.Checked;
            Settings.Tray=chkTray.Checked;
            Settings.SavePos=chkPos.Checked;
            Settings.Beep=chkBeep.Checked;
            Settings.LogFile=chkLog.Checked;
            Settings.FontSize=cboFont.SelectedIndex;
            string pin=txtPass.Text.Trim();
            Settings.PasswordHash=(pin.Length==0)?"":Settings.Hash(pin);
            Settings.Save();
            FontNeedsRestart=(Settings.FontSize!=oldFont);
            DialogResult=DialogResult.OK;
            Close();
        }
    }

    // ========================================================================
    //  QR ENCODER (byte mode, EC level M, versions 1-10, ISO 18004)
    // ========================================================================
    public class QrCode
    {
        public readonly int Size;
        public readonly bool[,] Modules;
        private int version;
        private bool[,] isFunction;

        private QrCode(int size)
        { Size=size; Modules=new bool[size,size]; isFunction=new bool[size,size]; }

        private static readonly int[] ByteCap={14,26,42,62,84,106,122,152,180,213};
        private static readonly int[] EcPerBlock={10,16,26,18,24,16,18,22,22,26};
        private static readonly int[] G1Blocks={1,1,1,2,2,4,4,2,3,4};
        private static readonly int[] G1Data={16,28,44,32,43,27,31,38,36,43};
        private static readonly int[] G2Blocks={0,0,0,0,0,0,0,2,2,1};
        private static readonly int[] G2Data={0,0,0,0,0,0,0,39,37,44};
        private static readonly int[][] AlignPos=new int[][]{
            new int[]{}, new int[]{6,18}, new int[]{6,22}, new int[]{6,26},
            new int[]{6,30}, new int[]{6,34}, new int[]{6,22,38},
            new int[]{6,24,42}, new int[]{6,26,46}, new int[]{6,28,50}};
        private static readonly bool[] FinderSeq=new bool[]{true,false,true,true,true,false,true};

        private static readonly byte[] GfExp=new byte[512];
        private static readonly byte[] GfLog=new byte[256];
        static QrCode()
        {
            int x=1;
            for(int i=0;i<255;i++)
            { GfExp[i]=(byte)x; GfLog[x]=(byte)i; x<<=1; if((x&0x100)!=0)x^=0x11D; }
            for(int i=255;i<512;i++) GfExp[i]=GfExp[i-255];
        }
        private static byte GfMul(byte a,byte b)
        { if(a==0||b==0) return 0; return GfExp[GfLog[a]+GfLog[b]]; }

        public static QrCode Encode(byte[] data)
        {
            if(data==null||data.Length==0) throw new ArgumentException("empty");
            int version=0;
            for(int v=1;v<=10;v++) if(data.Length<=ByteCap[v-1]){ version=v; break; }
            if(version==0) throw new ArgumentException("too long");

            List<bool> bits=new List<bool>();
            AppendBits(bits,4,4);
            AppendBits(bits,data.Length,version<=9?8:16);
            for(int i=0;i<data.Length;i++) AppendBits(bits,data[i],8);
            int nBlocks=G1Blocks[version-1]+G2Blocks[version-1];
            int capBits=(TotalFor(version)-EcPerBlock[version-1]*nBlocks)*8;
            int term=capBits-bits.Count; if(term>4)term=4; if(term<0)term=0;
            AppendBits(bits,0,term);
            while(bits.Count%8!=0) bits.Add(false);
            byte[] pads=new byte[]{0xEC,0x11}; int p=0;
            while(bits.Count<capBits){ AppendBits(bits,pads[p%2],8); p++; }
            byte[] dataCw=BitsToBytes(bits);

            int ecLen=EcPerBlock[version-1];
            byte[][] blocks=new byte[nBlocks][];
            byte[][] ecs=new byte[nBlocks][];
            int idx=0;
            for(int b=0;b<G1Blocks[version-1];b++)
            { blocks[b]=new byte[G1Data[version-1]]; Array.Copy(dataCw,idx,blocks[b],0,G1Data[version-1]); idx+=G1Data[version-1]; ecs[b]=ReedSolomon(blocks[b],ecLen); }
            for(int b=0;b<G2Blocks[version-1];b++)
            {
                int bi=G1Blocks[version-1]+b;
                blocks[bi]=new byte[G2Data[version-1]];
                Array.Copy(dataCw,idx,blocks[bi],0,G2Data[version-1]); idx+=G2Data[version-1];
                ecs[bi]=ReedSolomon(blocks[bi],ecLen);
            }
            List<byte> final=new List<byte>();
            int maxLen=0;
            for(int b=0;b<nBlocks;b++) if(blocks[b].Length>maxLen) maxLen=blocks[b].Length;
            for(int i=0;i<maxLen;i++) for(int b=0;b<nBlocks;b++) if(i<blocks[b].Length) final.Add(blocks[b][i]);
            for(int i=0;i<ecLen;i++) for(int b=0;b<nBlocks;b++) final.Add(ecs[b][i]);
            List<bool> allBits=BytesToBits(final.ToArray());

            QrCode best=null; int bestPen=int.MaxValue;
            for(int mask=0;mask<8;mask++)
            {
                QrCode qr=new QrCode(version*4+17);
                qr.version=version;
                qr.DrawFunctionPatterns();
                qr.PlaceData(allBits);
                qr.ApplyMask(mask);
                qr.DrawFormat(mask);
                int pen=qr.Penalty();
                if(pen<bestPen){ bestPen=pen; best=qr; }
            }
            return best;
        }

        private static int TotalFor(int v)
        {
            switch(v){ case 1:return 26; case 2:return 44; case 3:return 70; case 4:return 100;
                case 5:return 134; case 6:return 172; case 7:return 196; case 8:return 242;
                case 9:return 292; default:return 346; }
        }

        private static void AppendBits(List<bool> bits,int val,int len)
        { for(int i=len-1;i>=0;i--) bits.Add(((val>>i)&1)!=0); }

        private static byte[] BitsToBytes(List<bool> bits)
        {
            byte[] o=new byte[bits.Count/8];
            for(int i=0;i<o.Length;i++)
            { int v=0; for(int b=0;b<8;b++) if(bits[i*8+b]) v|=1<<(7-b); o[i]=(byte)v; }
            return o;
        }
        private static List<bool> BytesToBits(byte[] b)
        {
            List<bool> bits=new List<bool>(b.Length*8);
            for(int i=0;i<b.Length;i++) for(int k=7;k>=0;k--) bits.Add(((b[i]>>k)&1)!=0);
            return bits;
        }
        private static bool GetBit(int x,int i){ return ((x>>i)&1)!=0; }

        private static byte[] ReedSolomon(byte[] block,int ecLen)
        {
            byte[] gen=BuildGenerator(ecLen);
            byte[] rem=new byte[ecLen];
            for(int i=0;i<block.Length;i++)
            {
                byte f=(byte)(block[i]^rem[0]);
                Array.Copy(rem,1,rem,0,ecLen-1);
                rem[ecLen-1]=0;
                if(f!=0) for(int j=0;j<ecLen;j++) rem[j]^=GfMul(gen[j+1],f);
            }
            return rem;
        }
        private static byte[] BuildGenerator(int n)
        {
            List<byte> poly=new List<byte>(); poly.Add(1);
            for(int i=0;i<n;i++)
            {
                List<byte> np=new List<byte>();
                byte alpha=GfExp[i];
                for(int j=0;j<=poly.Count;j++)
                {
                    byte v=0;
                    if(j<poly.Count) v^=poly[j];
                    if(j>0) v^=GfMul(poly[j-1],alpha);
                    np.Add(v);
                }
                poly=np;
            }
            return poly.ToArray();
        }

        private void SetFunction(int r,int c,bool dark)
        { Modules[r,c]=dark; isFunction[r,c]=true; }

        private void DrawFunctionPatterns()
        {
            DrawFinder(0,0); DrawFinder(Size-7,0); DrawFinder(0,Size-7);
            for(int i=8;i<Size-8;i++){ bool dk=(i%2)==0; SetFunction(6,i,dk); SetFunction(i,6,dk); }
            int[] pos=AlignPos[version-1];
            for(int a=0;a<pos.Length;a++) for(int b=0;b<pos.Length;b++)
            {
                int r=pos[a],c=pos[b];
                if((r==6&&c==6)||(r==6&&c==Size-7)||(r==Size-7&&c==6)) continue;
                DrawAlignment(r,c);
            }
            if(version>=7)
            {
                int rem=version;
                for(int i=0;i<12;i++) rem=(rem<<1)^((rem>>11)*0x1F25);
                int vb=(version<<12)|rem;
                for(int i=0;i<18;i++)
                {
                    bool bit=(vb&1)!=0; vb>>=1;
                    int ra=Size-11+i%3, rb=i/3;
                    SetFunction(rb,ra,bit); SetFunction(ra,rb,bit);
                }
            }
        }

        private void DrawFinder(int row,int col)
        {
            for(int r=-1;r<=7;r++)
            {
                int rr=row+r; if(rr<0||rr>=Size) continue;
                for(int c=-1;c<=7;c++)
                {
                    int cc=col+c; if(cc<0||cc>=Size) continue;
                    bool dark=(r>=0&&r<=6&&(c==0||c==6))||(c>=0&&c<=6&&(r==0||r==6))||
                              (r>=2&&r<=4&&c>=2&&c<=4);
                    SetFunction(rr,cc,dark);
                }
            }
        }
        private void DrawAlignment(int row,int col)
        {
            for(int r=-2;r<=2;r++) for(int c=-2;c<=2;c++)
            {
                int d=Math.Abs(r); if(Math.Abs(c)>d) d=Math.Abs(c);
                SetFunction(row+r,col+c,d!=1);
            }
        }
        private void DrawFormat(int mask)
        {
            int data=mask;
            int rem=data<<10;
            for(int i=4;i>=0;i--)
                if(((rem>>(10+i))&1)!=0) rem^=0x537<<i;
            int fb=((data<<10)|rem)^0x5412;
            for(int i=0;i<=5;i++) SetFunction(i,8,GetBit(fb,i));
            SetFunction(7,8,GetBit(fb,6));
            SetFunction(8,8,GetBit(fb,7));
            SetFunction(8,7,GetBit(fb,8));
            for(int i=9;i<15;i++) SetFunction(8,14-i,GetBit(fb,i));
            for(int i=0;i<8;i++) SetFunction(8,Size-1-i,GetBit(fb,i));
            for(int i=8;i<15;i++) SetFunction(Size-15+i,8,GetBit(fb,i));
            SetFunction(8,Size-8,true);
        }
        private void PlaceData(List<bool> bits)
        {
            int i=0;
            for(int right=Size-1;right>=1;right-=2)
            {
                if(right==6) right=5;
                for(int vert=0;vert<Size;vert++)
                {
                    for(int j=0;j<2;j++)
                    {
                        int col=right-j;
                        bool up=((right+1)&2)==0;
                        int row=up?(Size-1-vert):vert;
                        if(!isFunction[row,col])
                        {
                            bool dk=false;
                            if(i<bits.Count){ dk=bits[i]; i++; }
                            Modules[row,col]=dk;
                        }
                    }
                }
            }
        }
        private void ApplyMask(int mask)
        {
            for(int r=0;r<Size;r++) for(int c=0;c<Size;c++)
                if(!isFunction[r,c]&&MaskBit(mask,r,c)) Modules[r,c]=!Modules[r,c];
        }
        private static bool MaskBit(int m,int r,int c)
        {
            switch(m)
            {
                case 0: return (r+c)%2==0;
                case 1: return r%2==0;
                case 2: return c%3==0;
                case 3: return (r+c)%3==0;
                case 4: return (r/2+c/3)%2==0;
                case 5: return (r*c)%2+(r*c)%3==0;
                case 6: return ((r*c)%2+(r*c)%3)%2==0;
                default: return ((r+c)%2+(r*c)%3)%2==0;
            }
        }
        private int Penalty()
        {
            int n=Size,res=0,x,y,k,run; bool color;
            for(y=0;y<n;y++)
            {
                color=Modules[y,0]; run=1;
                for(x=1;x<n;x++)
                { if(Modules[y,x]==color){ run++; if(run==6)res+=3; else if(run>6)res++; } else{ color=Modules[y,x]; run=1; } }
            }
            for(x=0;x<n;x++)
            {
                color=Modules[0,x]; run=1;
                for(y=1;y<n;y++)
                { if(Modules[y,x]==color){ run++; if(run==6)res+=3; else if(run>6)res++; } else{ color=Modules[y,x]; run=1; } }
            }
            for(y=0;y<n-1;y++) for(x=0;x<n-1;x++)
                if(Modules[y,x]==Modules[y,x+1]&&Modules[y,x]==Modules[y+1,x]&&Modules[y,x]==Modules[y+1,x+1]) res+=3;
            for(y=0;y<n;y++) for(x=0;x<=n-7;x++)
            {
                bool m=true;
                for(k=0;k<7;k++) if(Modules[y,x+k]!=FinderSeq[k]){ m=false; break; }
                if(!m) continue;
                bool before=(x>=4);
                if(before) for(k=1;k<=4;k++) if(Modules[y,x-k]){ before=false; break; }
                bool after=(x+10<n);
                if(after) for(k=7;k<=10;k++) if(Modules[y,x+k]){ after=false; break; }
                if(before||after) res+=40;
            }
            for(x=0;x<n;x++) for(y=0;y<=n-7;y++)
            {
                bool m=true;
                for(k=0;k<7;k++) if(Modules[y+k,x]!=FinderSeq[k]){ m=false; break; }
                if(!m) continue;
                bool before=(y>=4);
                if(before) for(k=1;k<=4;k++) if(Modules[y-k,x]){ before=false; break; }
                bool after=(y+10<n);
                if(after) for(k=7;k<=10;k++) if(Modules[y+k,x]){ after=false; break; }
                if(before||after) res+=40;
            }
            int dc=0;
            for(y=0;y<n;y++) for(x=0;x<n;x++) if(Modules[y,x]) dc++;
            int total=n*n;
            int kk=(Math.Abs(dc*20-total*10)+total-1)/total-1;
            return res+kk*10;
        }

        public Bitmap ToBitmap(int scale)
        {
            int quiet=4;
            int dim=(Size+quiet*2)*scale;
            Bitmap bmp=new Bitmap(dim,dim,PixelFormat.Format24bppRgb);
            using(Graphics g=Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using(SolidBrush br=new SolidBrush(Color.FromArgb(30,22,0)))
                    for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
                        if(Modules[y,x])
                            g.FillRectangle(br,(x+quiet)*scale,(y+quiet)*scale,scale,scale);
            }
            return bmp;
        }
    }

    // ========================================================================
    //  QR DIALOG
    // ========================================================================
    public class QrForm : Form
    {
        private List<string> urls;
        private int currentIndex;
        private Bitmap qrBmp;
        private PictureBox pb;
        private Label urlLabel;

        public QrForm(List<string> urlList,int initialIndex)
        {
            urls=urlList;
            currentIndex=(initialIndex>=0&&initialIndex<urls.Count)?initialIndex:0;

            Text="GoldShare - QR";
            FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false;
            StartPosition=FormStartPosition.CenterParent;
            RightToLeft=L.IsArabic?RightToLeft.Yes:RightToLeft.No;

            Theme t=Theme.Current;
            BackColor=t.Bg;

            Panel head=new Panel();
            head.Dock=DockStyle.Top; head.Height=56;
            head.Paint+=new PaintEventHandler(delegate(object s,PaintEventArgs e)
            {
                Rectangle r=new Rectangle(0,0,head.Width,head.Height);
                using(LinearGradientBrush b=new LinearGradientBrush(r,t.Dark,t.Mid,LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(b,r);
                using(Pen p=new Pen(t.Light,3))
                    e.Graphics.DrawLine(p,0,head.Height-2,head.Width,head.Height-2);
                TextRenderer.DrawText(e.Graphics,L.T("qr_header"),
                    new Font("Tahoma",12f,FontStyle.Bold),head.ClientRectangle,
                    Theme.Mix(t.Light,Color.White,0.88),
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            });
            Controls.Add(head);

            Label hint=new Label();
            hint.Text=L.T("qr_hint");
            hint.Location=new Point(20,64); hint.Size=new Size(520,18);
            hint.ForeColor=t.Text;
            Controls.Add(hint);

            int y=86;
            if(urls.Count>1)
            {
                Label ls=new Label();
                ls.Text=L.T("qr_for_addr");
                ls.Location=new Point(20,y+3); ls.Size=new Size(100,18);
                ls.ForeColor=t.Text;
                Controls.Add(ls);
                ComboBox cbo=new ComboBox();
                cbo.DropDownStyle=ComboBoxStyle.DropDownList;
                cbo.Location=new Point(124,y); cbo.Size=new Size(416,24);
                cbo.BackColor=t.FieldBg; cbo.ForeColor=t.Text;
                cbo.Font=new Font("Verdana",8.25f,FontStyle.Bold);
                foreach(string u in urls) cbo.Items.Add(u);
                cbo.SelectedIndex=currentIndex;
                cbo.SelectedIndexChanged+=new EventHandler(delegate(object s,EventArgs e)
                { currentIndex=cbo.SelectedIndex; RegenerateQr(); });
                Controls.Add(cbo);
                y+=30;
            }

            pb=new PictureBox();
            pb.BorderStyle=BorderStyle.FixedSingle;
            pb.BackColor=Color.White;
            pb.Size=new Size(352,352);
            pb.SizeMode=PictureBoxSizeMode.CenterImage;
            pb.Location=new Point(104,y);
            Controls.Add(pb);
            y+=362;

            urlLabel=new Label();
            urlLabel.Location=new Point(20,y); urlLabel.Size=new Size(520,20);
            urlLabel.TextAlign=ContentAlignment.MiddleCenter;
            urlLabel.ForeColor=Theme.Mix(t.Text,Color.Black,0.15);
            urlLabel.Font=new Font("Verdana",9f,FontStyle.Bold);
            Controls.Add(urlLabel);
            y+=28;

            GoldButton btnSave=new GoldButton();
            btnSave.Text=L.T("save_png");
            btnSave.Location=new Point(20,y); btnSave.Size=new Size(160,38);
            btnSave.Click+=new EventHandler(delegate
            {
                using(SaveFileDialog sfd=new SaveFileDialog())
                {
                    sfd.Filter="PNG Image|*.png";
                    sfd.FileName="GoldShare-QR.png";
                    if(sfd.ShowDialog(this)==DialogResult.OK)
                    {
                        qrBmp.Save(sfd.FileName,ImageFormat.Png);
                        MessageBox.Show(string.Format(L.T("msg_saved"),sfd.FileName),"GoldShare",
                            MessageBoxButtons.OK,MessageBoxIcon.Information);
                    }
                }
            });
            Controls.Add(btnSave);

            GoldButton btnCopy=new GoldButton();
            btnCopy.Text=L.T("copy_url");
            btnCopy.Location=new Point(200,y); btnCopy.Size=new Size(160,38);
            btnCopy.Click+=new EventHandler(delegate
            { try{ Clipboard.SetText(urls[currentIndex]); MessageBox.Show(L.T("msg_url_copied"),"GoldShare"); } catch { } });
            Controls.Add(btnCopy);

            GoldButton btnClose=new GoldButton();
            btnClose.Text=L.T("close");
            btnClose.Location=new Point(380,y); btnClose.Size=new Size(160,38);
            btnClose.Click+=new EventHandler(delegate { Close(); });
            Controls.Add(btnClose);

            ClientSize=new Size(560,y+38+14);
            RegenerateQr();
            FormClosed+=new FormClosedEventHandler(delegate(object s,FormClosedEventArgs e)
            { if(qrBmp!=null){ pb.Image=null; qrBmp.Dispose(); } });
        }

        private void RegenerateQr()
        {
            try
            {
                Bitmap old=qrBmp;
                QrCode qr=QrCode.Encode(Encoding.UTF8.GetBytes(urls[currentIndex]));
                int scale=320/(qr.Size+8);
                if(scale<4)scale=4; if(scale>10)scale=10;
                qrBmp=qr.ToBitmap(scale);
                pb.Image=qrBmp;
                urlLabel.Text=urls[currentIndex];
                if(old!=null) old.Dispose();
            }
            catch(Exception ex)
            { MessageBox.Show(string.Format(L.T("msg_qr_error"),ex.Message),"GoldShare"); }
        }
    }

    // ========================================================================
    //  GOLD BUTTON (themed)
    // ========================================================================
    public class GoldButton : Control
    {
        public static float BaseFontSize=8.5f;
        private bool hovered, pressed;

        public GoldButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|
                     ControlStyles.UserPaint|ControlStyles.ResizeRedraw|
                     ControlStyles.SupportsTransparentBackColor,true);
            BackColor=Color.Transparent;
            Font=new Font("Tahoma",BaseFontSize,FontStyle.Bold);
            Cursor=Cursors.Hand;
            Size=new Size(120,34);
        }

        protected override void OnMouseEnter(EventArgs e){ hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e){ hovered=false; pressed=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e){ pressed=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e){ pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e){ Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t=Theme.Current;
            Graphics g=e.Graphics;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle r=new Rectangle(0,0,Width-1,Height-1);
            GraphicsPath gp=RoundPath(r,10);
            Color top,bottom,border;
            if(!Enabled)
            { top=Color.FromArgb(226,224,218); bottom=Color.FromArgb(206,204,198); border=Color.FromArgb(190,188,182); }
            else if(pressed)
            { top=Theme.Mix(t.Mid,t.Dark,0.55); bottom=Theme.Mix(t.Mid,t.Dark,0.2); border=t.Dark; }
            else if(hovered)
            { top=Theme.Mix(t.Mid,t.Light,0.5); bottom=Theme.Mix(t.Mid,t.Dark,0.15); border=Theme.Mix(t.Dark,t.Mid,0.2); }
            else
            { top=Theme.Mix(t.Mid,t.Light,0.3); bottom=t.Mid; border=Theme.Mix(t.Dark,t.Mid,0.25); }
            using(LinearGradientBrush br=new LinearGradientBrush(r,top,bottom,LinearGradientMode.Vertical))
                g.FillPath(br,gp);
            using(Pen p=new Pen(border)) g.DrawPath(p,gp);
            Color txt=Enabled?Theme.Mix(t.Light,Color.White,0.92):Color.FromArgb(140,138,132);
            TextRenderer.DrawText(g,Text,Font,
                new Rectangle(r.X,r.Y-1,r.Width,r.Height),txt,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            gp.Dispose();
        }

        private static GraphicsPath RoundPath(Rectangle r,int rad)
        {
            GraphicsPath p=new GraphicsPath();
            int d=rad*2;
            p.AddArc(r.X,r.Y,d,d,180,90);
            p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);
            p.AddArc(r.X,r.Bottom-d,d,d,90,90);
            p.CloseFigure();
            return p;
        }
    }
}