// ============================================================================
//  GoldShare.cs — Premium File Sharing Server (ShareIt-style)
//  FLICKER-FREE build: composited form, change-only updates, fixed-width
//  smoothed speed readout, single 1s timer, append-only capped log, cached
//  header gradient, zero UI work when minimized.
//  8 themes • 7 languages • QR engine • tray • settings • login PIN •
//  delete PIN • ZIP download • thumbnails • resumable downloads •
//  speed limit • traffic monitor • notes→clipboard • camera upload •
//  search/sort/grid • log file • auto-start • single instance
//  Target: .NET Framework 2.0 — Windows XP
//  Compile: C:\WINDOWS\Microsoft.NET\Framework\v2.0.50727\csc.exe
//           /target:winexe /optimize+ /out:GoldShare.exe GoldShare.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
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
    //  THEME
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
        public static string Hex(Color c){ return "#"+c.R.ToString("x2")+c.G.ToString("x2")+c.B.ToString("x2"); }
    }

    public static class Themes
    {
        public static readonly Theme[] All = new Theme[]
        {
            new Theme("gold",     Color.FromArgb(112,79,5),   Color.FromArgb(184,134,11), Color.FromArgb(233,215,138), Color.FromArgb(236,217,160), Color.FromArgb(253,250,243), Color.FromArgb(90,66,26),  Color.FromArgb(255,253,244), false),
            new Theme("sea",      Color.FromArgb(5,68,94),    Color.FromArgb(24,154,180), Color.FromArgb(117,230,218), Color.FromArgb(190,230,238), Color.FromArgb(240,250,252), Color.FromArgb(15,80,105), Color.FromArgb(244,252,254), false),
            new Theme("nature",   Color.FromArgb(25,70,28),   Color.FromArgb(62,137,72),  Color.FromArgb(163,217,119), Color.FromArgb(205,235,190), Color.FromArgb(244,252,242), Color.FromArgb(35,85,40),  Color.FromArgb(247,253,245), false),
            new Theme("fruits",   Color.FromArgb(192,57,43),  Color.FromArgb(230,126,34), Color.FromArgb(249,199,79),  Color.FromArgb(250,220,180), Color.FromArgb(255,250,243), Color.FromArgb(150,60,25), Color.FromArgb(255,250,244), false),
            new Theme("space",    Color.FromArgb(26,16,64),   Color.FromArgb(75,46,131),  Color.FromArgb(157,141,241), Color.FromArgb(205,198,240), Color.FromArgb(244,243,252), Color.FromArgb(45,35,90),  Color.FromArgb(247,246,254), false),
            new Theme("fire",     Color.FromArgb(127,29,13),  Color.FromArgb(220,75,26),  Color.FromArgb(249,160,63),  Color.FromArgb(253,245,241), Color.FromArgb(140,40,18), Color.FromArgb(255,247,243), false),
            new Theme("moon",     Color.FromArgb(45,55,72),   Color.FromArgb(100,116,139),Color.FromArgb(203,213,225), Color.FromArgb(215,222,232), Color.FromArgb(246,248,251), Color.FromArgb(55,65,80),  Color.FromArgb(249,251,253), false),
            new Theme("midnight", Color.FromArgb(8,12,28),    Color.FromArgb(30,41,82),   Color.FromArgb(96,120,200),  Color.FromArgb(38,50,92),    Color.FromArgb(15,20,40),   Color.FromArgb(215,224,248),Color.FromArgb(24,32,62),   true)
        };
        public static Theme Get(string code){ for(int i=0;i<All.Length;i++) if(All[i].Code==code) return All[i]; return All[0]; }
        public static int IndexOf(string code){ for(int i=0;i<All.Length;i++) if(All[i].Code==code) return i; return 0; }
    }

    // ========================================================================
    //  SETTINGS
    // ========================================================================
    public static class Settings
    {
        public static int Port=8080, UploadMB=512, SpeedKB=0, FontSize=1;
        public static string Language="en", Theme="gold", PasswordHash="", Folder="";
        public static bool AutoStart=false, AllowDelete=false, Tray=true,
                           SavePos=false, Beep=true, LogFile=true;
        public static int PosX=-1, PosY=-1;

        public static string IniPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GoldShare.ini"); } }
        public static string ShareFolder {
            get {
                if(Folder==null||Folder.Length==0)
                    Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"GoldShare");
                return Folder;
            }
        }

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
                        case "folder": Folder=v; break;
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
                sb.AppendLine("Folder="+Folder);
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
    //  LOG FILE (1 MB rotation)
    // ========================================================================
    public static class LogF
    {
        private static object lk=new object();
        public static void W(string s)
        {
            if(!Settings.LogFile) return;
            if(s==null||s.Length==0) return;
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
    //  Compact "key|value" tables. English is always loaded as the base;
    //  the selected language overlays it, so missing keys fall back to EN.
    // ========================================================================
    public static class L
    {
        private static string lang="en";
        private static Dictionary<string,string> dict;
        public static readonly List<string> Langs=new List<string>(
            new string[]{"en","fr","ar","es","de","tr","ru"});

        public static string Lang { get { return lang; } }

        public static void Set(string code)
        {
            lang=Langs.Contains(code)?code:"en";
            dict=Make(lang);
        }
        public static string T(string key)
        {
            string s;
            if(dict!=null&&dict.TryGetValue(key,out s)) return s;
            return key;
        }

        private static Dictionary<string,string> Make(string code)
        {
            string[] d;
            if(code=="fr") d=Fr; else if(code=="ar") d=Ar; else if(code=="es") d=Es;
            else if(code=="de") d=De; else if(code=="tr") d=Tr; else if(code=="ru") d=Ru;
            else d=En;
            Dictionary<string,string> m=new Dictionary<string,string>();
            Fill(m,En);
            if(d!=En) Fill(m,d);
            return m;
        }
        private static void Fill(Dictionary<string,string> m,string[] d)
        {
            for(int i=0;i<d.Length;i++)
            {
                int p=d[i].IndexOf('|');
                if(p>0) m[d[i].Substring(0,p)]=d[i].Substring(p+1);
            }
        }

        private static string[] En=new string[]{
        "title|GoldShare - Premium File Sharing","start|START SERVER","stop|STOP SERVER",
        "open_browser|OPEN IN BROWSER","open_folder|OPEN FOLDER","copy_url|COPY URL",
        "qr_scan|QR / SCAN","gear|\u2699 SETTINGS","port|Port:","folder|Folder:",
        "addresses|Server addresses - select one, then COPY URL or QR / SCAN:",
        "log|Activity log:","language|Language:","theme_lbl|Theme:",
        "status_run|\u25CF RUNNING :{0}","status_stop|\u25CB STOPPED","conns_fmt|\u2022 {0} conns",
        "up_lbl|UP","down_lbl|DOWN","tray_open|Open","tray_exit|Exit",
        "set_title|GoldShare - Settings","set_port|Port:","set_uplim|Upload limit (MB):",
        "set_speed|Speed limit (KB/s, 0 = unlimited):","set_pass|Access PIN (blank = none):",
        "set_font|Font size:","font_s|Small","font_m|Medium","font_l|Large",
        "set_autostart|Start server automatically on launch",
        "set_allowdel|Allow deleting files from the web page (needs PIN)",
        "set_tray|Minimize to system tray","set_savepos|Remember window position",
        "set_beep|Sound when a file is received","set_log|Write activity to GoldShare.log",
        "save|SAVE","cancel|CANCEL",
        "msg_ready|Ready. Press 'START SERVER'.","msg_started|Server started on port {0}",
        "msg_stopped|Server stopped.","msg_start_fail|Could not start server:\r\n{0}\r\n\r\nTry another port.",
        "msg_bad_port|Enter a valid port (1-65535).","msg_start_first|Start the server first.",
        "msg_copied|Copied: {0}","msg_url_copied|URL copied to clipboard.",
        "msg_upload_done|Upload finished: {0}","msg_port_restart|Port changed. Restart the server to apply.",
        "msg_tray_min|Still running here in the tray.","msg_already|GoldShare is already running.",
        "msg_deleted|Deleted: {0}","msg_del_deny|Delete denied (wrong PIN or disabled).",
        "msg_qr_saved|QR image saved:\r\n{0}","msg_zip_none|No files selected.",
        "page_title|GoldShare - File Sharing","page_sub|Premium File Sharing \u2014 Upload & Download",
        "page_dl|\u2B07 DOWNLOAD FILES","page_up|\u2B06 UPLOAD TO THIS DEVICE",
        "page_search_ph|\uD83D\uDD0D Search files...","page_no_files|No files yet.",
        "page_upload_btn|UPLOAD","page_cam|\uD83D\uDCF7 Photo",
        "page_note_h|\uD83D\uDCDD NOTE TO PC","page_note_ph|Type a message... it appears on the PC & its clipboard",
        "page_note_send|SEND \u27A4","btn_zip|\u2B07 ZIP SELECTED",
        "view_grid|\u25A6 Grid","view_list|\u2630 List",
        "sort_name|A-Z","sort_size|Size","sort_new|New",
        "page_del_ask|PIN to delete:","page_wrong_pin|Wrong PIN or delete disabled.",
        "page_login_h|\uD83D\uDD12 GoldShare","page_login_btn|ENTER","page_bad_pass|Wrong PIN. Try again.",
        "page_received|\uD83D\uDCE6 FILES","page_storage_f|\uD83D\uDCBE {0} free of {1}"};

        private static string[] Fr=new string[]{
        "title|GoldShare - Partage de fichiers Premium","start|D\u00C9MARRER LE SERVEUR","stop|ARR\u00CATER LE SERVEUR",
        "open_browser|OUVRIR LE NAVIGATEUR","open_folder|OUVRIR LE DOSSIER","copy_url|COPIER L'URL",
        "qr_scan|QR / SCAN","gear|\u2699 R\u00C9GLAGES","port|Port :","folder|Dossier :",
        "addresses|Adresses du serveur - s\u00E9lectionnez, puis COPIER ou QR / SCAN :",
        "log|Journal :","language|Langue :","theme_lbl|Th\u00E8me :",
        "status_run|\u25CF EN MARCHE :{0}","status_stop|\u25CB ARR\u00CAT\u00C9","conns_fmt|\u2022 {0} conn.",
        "up_lbl|ENVOI","down_lbl|RE\u00C7U","tray_open|Ouvrir","tray_exit|Quitter",
        "set_title|GoldShare - R\u00E9glages","set_port|Port :","set_uplim|Limite d'envoi (Mo) :",
        "set_speed|Limite de vitesse (Ko/s, 0 = illimit\u00E9) :","set_pass|PIN d'acc\u00E8s (vide = aucun) :",
        "set_font|Taille de police :","font_s|Petite","font_m|Moyenne","font_l|Grande",
        "set_autostart|D\u00E9marrer le serveur au lancement",
        "set_allowdel|Autoriser la suppression via la page (PIN requis)",
        "set_tray|R\u00E9duire dans la zone de notification","set_savepos|M\u00E9moriser la position",
        "set_beep|Son quand un fichier est re\u00E7u","set_log|\u00C9crire le journal dans GoldShare.log",
        "save|ENREGISTRER","cancel|ANNULER",
        "msg_ready|Pr\u00EAt. Cliquez sur \u00AB D\u00C9MARRER \u00BB.","msg_started|Serveur d\u00E9marr\u00E9 sur le port {0}",
        "msg_stopped|Serveur arr\u00EAt\u00E9.","msg_start_fail|Impossible de d\u00E9marrer :\r\n{0}\r\n\r\nEssayez un autre port.",
        "msg_bad_port|Entrez un port valide (1-65535).","msg_start_first|D\u00E9marrez d'abord le serveur.",
        "msg_copied|Copi\u00E9 : {0}","msg_url_copied|URL copi\u00E9e.",
        "msg_upload_done|Envoi termin\u00E9 : {0}","msg_port_restart|Port modifi\u00E9. Red\u00E9marrez le serveur.",
        "msg_tray_min|Le serveur tourne dans la zone de notification.","msg_already|GoldShare est d\u00E9j\u00E0 en cours.",
        "msg_deleted|Supprim\u00E9 : {0}","msg_del_deny|Suppression refus\u00E9e (PIN faux ou d\u00E9sactiv\u00E9e).",
        "msg_qr_saved|Image QR enregistr\u00E9e :\r\n{0}","msg_zip_none|Aucun fichier s\u00E9lectionn\u00E9.",
        "page_title|GoldShare - Partage","page_sub|Partage Premium \u2014 Envoi & T\u00E9l\u00E9chargement",
        "page_dl|\u2B07 T\u00C9L\u00C9CHARGER","page_up|\u2B06 ENVOYER VERS CE PC",
        "page_search_ph|\uD83D\uDD0D Rechercher...","page_no_files|Aucun fichier.",
        "page_upload_btn|ENVOYER","page_cam|\uD83D\uDCF7 Photo",
        "page_note_h|\uD83D\uDCDD NOTE VERS PC","page_note_ph|\u00C9crivez un message... affich\u00E9 sur le PC + presse-papiers",
        "page_note_send|ENVOYER \u27A4","btn_zip|\u2B07 ZIP S\u00C9LECTIONN\u00C9",
        "view_grid|\u25A6 Grille","view_list|\u2630 Liste",
        "sort_name|A-Z","sort_size|Taille","sort_new|Nouveau",
        "page_del_ask|PIN pour supprimer :","page_wrong_pin|PIN faux ou suppression d\u00E9sactiv\u00E9e.",
        "page_login_h|\uD83D\uDD12 GoldShare","page_login_btn|ENTRER","page_bad_pass|PIN incorrect.",
        "page_received|\uD83D\uDCE6 FICHIERS","page_storage_f|\uD83D\uDCBE {0} libres sur {1}"};

        private static string[] Ar=new string[]{
        "title|GoldShare - \u0645\u0634\u0627\u0631\u0643\u0629 \u0627\u0644\u0645\u0644\u0641\u0627\u062A \u0627\u0644\u0645\u0645\u064A\u0632\u0629","start|\u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645","stop|\u0625\u064A\u0642\u0627\u0641 \u0627\u0644\u062E\u0627\u062F\u0645",
        "open_browser|\u0641\u062A\u062D \u0641\u064A \u0627\u0644\u0645\u062A\u0635\u0641\u062D","open_folder|\u0641\u062A\u062D \u0627\u0644\u0645\u062C\u0644\u062F","copy_url|\u0646\u0633\u062E \u0627\u0644\u0631\u0627\u0628\u0637",
        "qr_scan|\u0631\u0645\u0632 QR","gear|\u2699 \u0627\u0644\u0625\u0639\u062F\u0627\u062F\u0627\u062A","port|\u0627\u0644\u0645\u0646\u0641\u0630:","folder|\u0627\u0644\u0645\u062C\u0644\u062F:",
        "addresses|\u0639\u0646\u0627\u0648\u064A\u0646 \u0627\u0644\u062E\u0627\u062F\u0645 - \u062D\u062F\u062F \u0639\u0646\u0648\u0627\u0646\u0627\u064B \u062B\u0645 \u0646\u0633\u062E \u0623\u0648 QR:",
        "log|\u0627\u0644\u0633\u062C\u0644:","language|\u0627\u0644\u0644\u063A\u0629:","theme_lbl|\u0627\u0644\u0633\u0645\u0629:",
        "status_run|\u25CF \u064A\u0639\u0645\u0644 :{0}","status_stop|\u25CB \u0645\u0648\u0642\u0641","conns_fmt|\u2022 {0} \u0627\u062A\u0635\u0627\u0644",
        "up_lbl|\u0631\u0641\u0639","down_lbl|\u062A\u0646\u0632\u064A\u0644","tray_open|\u0641\u062A\u062D","tray_exit|\u062E\u0631\u0648\u062C",
        "set_title|GoldShare - \u0627\u0644\u0625\u0639\u062F\u0627\u062F\u0627\u062A","set_port|\u0627\u0644\u0645\u0646\u0641\u0630:","set_uplim|\u062D\u062F \u0627\u0644\u0631\u0641\u0639 (\u0645\u0628):",
        "set_speed|\u062D\u062F \u0627\u0644\u0633\u0631\u0639\u0629 (\u0643\u0628/\u062B\u060C 0=\u0628\u0644\u0627 \u062D\u062F):","set_pass|\u0631\u0642\u0645 \u0627\u0644\u062F\u062E\u0648\u0644 (\u0641\u0627\u0631\u063A=\u0628\u0644\u0627):",
        "set_font|\u062D\u062C\u0645 \u0627\u0644\u062E\u0637:","font_s|\u0635\u063A\u064A\u0631","font_m|\u0645\u062A\u0648\u0633\u0637","font_l|\u0643\u0628\u064A\u0631",
        "set_autostart|\u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645 \u062A\u0644\u0642\u0627\u0626\u064A\u0627\u064B",
        "set_allowdel|\u0627\u0644\u0633\u0645\u0627\u062D \u0628\u0627\u0644\u062D\u0630\u0641 \u0645\u0646 \u0627\u0644\u0635\u0641\u062D\u0629 (\u064A\u0644\u0632\u0645 \u0631\u0642\u0645)",
        "set_tray|\u062A\u0635\u063A\u064A\u0631 \u0625\u0644\u0649 \u0639\u062F\u0629 \u0627\u0644\u0646\u0638\u0627\u0645","set_savepos|\u062A\u0630\u0643\u0651\u0631 \u0645\u0648\u0636\u0639 \u0627\u0644\u0646\u0627\u0641\u0630\u0629",
        "set_beep|\u0635\u0648\u062A \u0639\u0646\u062F \u0627\u0633\u062A\u0644\u0627\u0645 \u0645\u0644\u0641","set_log|\u062A\u0633\u062C\u064A\u0644 \u0627\u0644\u0646\u0634\u0627\u0637 \u0641\u064A GoldShare.log",
        "save|\u062D\u0641\u0638","cancel|\u0625\u0644\u063A\u0627\u0621",
        "msg_ready|\u062C\u0627\u0647\u0632. \u0627\u0636\u063A\u0637 '\u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645'.","msg_started|\u062A\u0645 \u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645 \u0639\u0644\u0649 \u0627\u0644\u0645\u0646\u0641\u0630 {0}",
        "msg_stopped|\u062A\u0645 \u0625\u064A\u0642\u0627\u0641 \u0627\u0644\u062E\u0627\u062F\u0645.","msg_start_fail|\u062A\u0639\u0630\u0651\u0631 \u0627\u0644\u062A\u0634\u063A\u064A\u0644:\r\n{0}\r\n\r\n\u062C\u0631\u0651\u0628 \u0645\u0646\u0641\u0630\u0627\u064B \u0622\u062E\u0631.",
        "msg_bad_port|\u0623\u062F\u062E\u0644 \u0645\u0646\u0641\u0630\u0627\u064B \u0635\u062D\u064A\u062D\u0627\u064B (1-65535).","msg_start_first|\u0634\u063A\u0651\u0644 \u0627\u0644\u062E\u0627\u062F\u0645 \u0623\u0648\u0644\u0627\u064B.",
        "msg_copied|\u062A\u0645 \u0627\u0644\u0646\u0633\u062E: {0}","msg_url_copied|\u062A\u0645 \u0646\u0633\u062E \u0627\u0644\u0631\u0627\u0628\u0637.",
        "msg_upload_done|\u0627\u0646\u062A\u0647\u0649 \u0627\u0644\u0631\u0641\u0639: {0}","msg_port_restart|\u062A\u063A\u064A\u0651\u0631 \u0627\u0644\u0645\u0646\u0641\u0630. \u0623\u0639\u062F \u062A\u0634\u063A\u064A\u0644 \u0627\u0644\u062E\u0627\u062F\u0645.",
        "msg_tray_min|\u0627\u0644\u062E\u0627\u062F\u0645 \u064A\u0639\u0645\u0644 \u0641\u064A \u0639\u062F\u0629 \u0627\u0644\u0646\u0638\u0627\u0645.","msg_already|GoldShare \u064A\u0639\u0645\u0644 \u0628\u0627\u0644\u0641\u0639\u0644.",
        "msg_deleted|\u062D\u064F\u0630\u0641: {0}","msg_del_deny|\u0631\u064F\u0641\u0636 \u0627\u0644\u062D\u0630\u0641 (\u0631\u0642\u0645 \u062E\u0627\u0637\u0623 \u0623\u0648 \u0645\u0639\u0637\u0651\u0644).",
        "msg_qr_saved|\u062D\u064F\u0641\u0638\u062A \u0635\u0648\u0631\u0629 QR:\r\n{0}","msg_zip_none|\u0644\u0627 \u0645\u0644\u0641\u0627\u062A \u0645\u062D\u062F\u062F\u0629.",
        "page_title|GoldShare - \u0645\u0634\u0627\u0631\u0643\u0629 \u0627\u0644\u0645\u0644\u0641\u0627\u062A","page_sub|\u0645\u0634\u0627\u0631\u0643\u0629 \u0645\u0645\u064A\u0632\u0629 \u2014 \u0631\u0641\u0639 \u0648\u062A\u0646\u0632\u064A\u0644",
        "page_dl|\u2B07 \u062A\u0646\u0632\u064A\u0644 \u0627\u0644\u0645\u0644\u0641\u0627\u062A","page_up|\u2B06 \u0631\u0641\u0639 \u0625\u0644\u0649 \u0647\u0630\u0627 \u0627\u0644\u062C\u0647\u0627\u0632",
        "page_search_ph|\uD83D\uDD0D \u0628\u062D\u062B...","page_no_files|\u0644\u0627 \u0645\u0644\u0641\u0627\u062A \u0628\u0639\u062F.",
        "page_upload_btn|\u0631\u0641\u0639","page_cam|\uD83D\uDCF7 \u0635\u0648\u0631\u0629",
        "page_note_h|\uD83D\uDCDD \u0645\u0644\u0627\u062D\u0638\u0629 \u0625\u0644\u0649 \u0627\u0644\u0643\u0645\u0628\u064A\u0648\u062A\u0631","page_note_ph|\u0627\u0643\u062A\u0628 \u0631\u0633\u0627\u0644\u0629... \u0633\u062A\u0638\u0647\u0631 \u0639\u0644\u0649 \u0627\u0644\u0643\u0645\u0628\u064A\u0648\u062A\u0631 \u0648\u0627\u0644\u062D\u0627\u0641\u0638\u0629",
        "page_note_send|\u0625\u0631\u0633\u0627\u0644 \u27A4","btn_zip|\u2B07 \u0636\u063A\u0637 \u0627\u0644\u0645\u062D\u062F\u062F",
        "view_grid|\u25A6 \u0634\u0628\u0643\u0629","view_list|\u2630 \u0642\u0627\u0626\u0645\u0629",
        "sort_name|\u0623-\u064A","sort_size|\u0627\u0644\u062D\u062C\u0645","sort_new|\u0627\u0644\u0623\u062D\u062F\u062B",
        "page_del_ask|\u0631\u0642\u0645 \u0627\u0644\u062D\u0630\u0641:","page_wrong_pin|\u0631\u0642\u0645 \u062E\u0627\u0637\u0623 \u0623\u0648 \u0627\u0644\u062D\u0630\u0641 \u0645\u0639\u0637\u0651\u0644.",
        "page_login_h|\uD83D\uDD12 GoldShare","page_login_btn|\u062F\u062E\u0648\u0644","page_bad_pass|\u0631\u0642\u0645 \u062E\u0627\u0637\u0623.",
        "page_received|\uD83D\uDCE6 \u0627\u0644\u0645\u0644\u0641\u0627\u062A","page_storage_f|\uD83D\uDCBE {0} \u0645\u062A\u0627\u062D \u0645\u0646 {1}"};

        private static string[] Es=new string[]{
        "title|GoldShare - Compartici\u00F3n de archivos Premium","start|INICIAR SERVIDOR","stop|DETENER SERVIDOR",
        "open_browser|ABRIR EN NAVEGADOR","open_folder|ABRIR CARPETA","copy_url|COPIAR URL",
        "qr_scan|QR / ESCANEAR","gear|\u2699 AJUSTES","port|Puerto:","folder|Carpeta:",
        "addresses|Direcciones - seleccione y COPIAR URL o QR / ESCANEAR:",
        "log|Registro:","language|Idioma:","theme_lbl|Tema:",
        "status_run|\u25CF ACTIVO :{0}","status_stop|\u25CB DETENIDO","conns_fmt|\u2022 {0} conex.",
        "up_lbl|SUBIDA","down_lbl|BAJADA","tray_open|Abrir","tray_exit|Salir",
        "set_title|GoldShare - Ajustes","set_port|Puerto:","set_uplim|L\u00EDmite de subida (MB):",
        "set_speed|L\u00EDmite de velocidad (KB/s, 0 = sin l\u00EDmite):","set_pass|PIN de acceso (vac\u00EDo = ninguno):",
        "set_font|Tama\u00F1o de fuente:","font_s|Peque\u00F1o","font_m|Medio","font_l|Grande",
        "set_autostart|Iniciar servidor autom\u00E1ticamente",
        "set_allowdel|Permitir borrar desde la p\u00E1gina (requiere PIN)",
        "set_tray|Minimizar a la bandeja","set_savepos|Recordar posici\u00F3n de la ventana",
        "set_beep|Sonido al recibir archivo","set_log|Escribir registro en GoldShare.log",
        "save|GUARDAR","cancel|CANCELAR",
        "msg_ready|Listo. Pulse 'INICIAR SERVIDOR'.","msg_started|Servidor iniciado en el puerto {0}",
        "msg_stopped|Servidor detenido.","msg_start_fail|No se pudo iniciar:\r\n{0}\r\n\r\nPruebe otro puerto.",
        "msg_bad_port|Introduzca un puerto v\u00E1lido (1-65535).","msg_start_first|Inicie el servidor primero.",
        "msg_copied|Copiado: {0}","msg_url_copied|URL copiada.",
        "msg_upload_done|Subida completada: {0}","msg_port_restart|Puerto cambiado. Reinicie el servidor.",
        "msg_tray_min|Sigue funcionando en la bandeja.","msg_already|GoldShare ya est\u00E1 en ejecuci\u00F3n.",
        "msg_deleted|Eliminado: {0}","msg_del_deny|Eliminaci\u00F3n denegada (PIN falso o desactivada).",
        "msg_qr_saved|Imagen QR guardada:\r\n{0}","msg_zip_none|No hay archivos seleccionados.",
        "page_title|GoldShare - Archivos","page_sub|Compartici\u00F3n Premium \u2014 Subir y Descargar",
        "page_dl|\u2B07 DESCARGAR","page_up|\u2B06 SUBIR A ESTE EQUIPO",
        "page_search_ph|\uD83D\uDD0D Buscar...","page_no_files|Sin archivos.",
        "page_upload_btn|SUBIR","page_cam|\uD83D\uDCF7 Foto",
        "page_note_h|\uD83D\uDCDD NOTA AL PC","page_note_ph|Escribe un mensaje... llega al PC y su portapapeles",
        "page_note_send|ENVIAR \u27A4","btn_zip|\u2B07 ZIP SELECC.",
        "view_grid|\u25A6 Cuadr\u00EDcula","view_list|\u2630 Lista",
        "sort_name|A-Z","sort_size|Tama\u00F1o","sort_new|Nuevo",
        "page_del_ask|PIN para borrar:","page_wrong_pin|PIN incorrecto o borrado desactivado.",
        "page_login_h|\uD83D\uDD12 GoldShare","page_login_btn|ENTRAR","page_bad_pass|PIN incorrecto.",
        "page_received|\uD83D\uDCE6 ARCHIVOS","page_storage_f|\uD83D\uDCBE {0} libres de {1}"};

        private static string[] De=new string[]{
        "title|GoldShare - Premium-Dateifreigabe","start|SERVER STARTEN","stop|SERVER STOPPEN",
        "open_browser|IM BROWSER \u00D6FFNEN","open_folder|ORDNER \u00D6FFNEN","copy_url|URL KOPIEREN",
        "qr_scan|QR / SCAN","gear|\u2699 EINSTELLUNGEN","port|Port:","folder|Ordner:",
        "addresses|Adressen - w\u00E4hlen, dann URL KOPIEREN oder QR / SCAN:",
        "log|Protokoll:","language|Sprache:","theme_lbl|Thema:",
        "status_run|\u25CF L\u00C4UFT :{0}","status_stop|\u25CB GESTOPPT","conns_fmt|\u2022 {0} Verb.",
        "up_lbl|UPLOAD","down_lbl|DOWNLOAD","tray_open|\u00D6ffnen","tray_exit|Beenden",
        "set_title|GoldShare - Einstellungen","set_port|Port:","set_uplim|Upload-Limit (MB):",
        "set_speed|Geschwindigkeitslimit (KB/s, 0 = unbegrenzt):","set_pass|Zugangs-PIN (leer = keine):",
        "set_font|Schriftgr\u00F6\u00DFe:","font_s|Klein","font_m|Mittel","font_l|Gro\u00DF",
        "set_autostart|Server automatisch starten",
        "set_allowdel|L\u00F6schen \u00FCber die Seite erlauben (PIN n\u00F6tig)",
        "set_tray|In den Tray minimieren","set_savepos|Fensterposition merken",
        "set_beep|Ton bei Dateieingang","set_log|Protokoll in GoldShare.log schreiben",
        "save|SPEICHERN","cancel|ABBRECHEN",
        "msg_ready|Bereit. 'SERVER STARTEN' dr\u00FCcken.","msg_started|Server gestartet auf Port {0}",
        "msg_stopped|Server gestoppt.","msg_start_fail|Start fehlgeschlagen:\r\n{0}\r\n\r\nAnderen Port versuchen.",
        "msg_bad_port|G\u00FCltigen Port eingeben (1-65535).","msg_start_first|Erst den Server starten.",
        "msg_copied|Kopiert: {0}","msg_url_copied|URL kopiert.",
        "msg_upload_done|Upload fertig: {0}","msg_port_restart|Port ge\u00E4ndert. Server neu starten.",
        "msg_tray_min|L\u00E4uft weiter im Tray.","msg_already|GoldShare l\u00E4uft bereits.",
        "msg_deleted|Gel\u00F6scht: {0}","msg_del_deny|L\u00F6schen verweigert (falsche PIN oder deaktiviert).",
        "msg_qr_saved|QR-Bild gespeichert:\r\n{0}","msg_zip_none|Keine Dateien ausgew\u00E4hlt.",
        "page_title|GoldShare - Dateien","page_sub|Premium-Freigabe \u2014 Upload & Download",
        "page_dl|\u2B07 HERUNTERLADEN","page_up|\u2B06 ZU DIESEM PC HOCHLADEN",
        "page_search_ph|\uD83D\uDD0D Suchen...","page_no_files|Keine Dateien.",
        "page_upload_btn|HOCHLADEN","page_cam|\uD83D\uDCF7 Foto",
        "page_note_h|\uD83D\uDCDD NOTIZ AN PC","page_note_ph|Nachricht schreiben... erscheint auf dem PC + Zwischenablage",
        "page_note_send|SENDEN \u27A4","btn_zip|\u2B07 ZIP AUSWAHL",
        "view_grid|\u25A6 Raster","view_list|\u2630 Liste",
        "sort_name|A-Z","sort_size|Gr\u00F6\u00DFe","sort_new|Neu",
        "page_del_ask|PIN zum L\u00F6schen:","page_wrong_pin|Falsche PIN oder L\u00F6schen deaktiviert.",
        "page_login_h|\uD83D\uDD12 GoldShare","page_login_btn|OFFEN","page_bad_pass|Falsche PIN.",
        "page_received|\uD83D\uDCE6 DATEIEN","page_storage_f|\uD83D\uDCBE {0} frei von {1}"};

        private static string[] Tr=new string[]{
        "title|GoldShare - Premium Dosya Payla\u015F\u0131m\u0131","start|SUNUCUYU BA\u015ELAT","stop|SUNUCUYU DURDUR",
        "open_browser|TARAYICIDA A\u00C7","open_folder|KLAS\u00D6R\u00DC A\u00C7","copy_url|URL KOPYALA",
        "qr_scan|QR / TARA","gear|\u2699 AYARLAR","port|Port:","folder|Klas\u00F6r:",
        "addresses|Sunucu adresleri - se\u00E7in, sonra URL KOPYALA veya QR / TARA:",
        "log|G\u00FCnl\u00FCk:","language|Dil:","theme_lbl|Tema:",
        "status_run|\u25CF \u00C7ALI\u015EIYOR :{0}","status_stop|\u25CB DURDURULDU","conns_fmt|\u2022 {0} ba\u011Fl.",
        "up_lbl|Y\u00DCKLEME","down_lbl|\u0130ND\u0130RME","tray_open|A\u00E7","tray_exit|\u00C7\u0131k\u0131\u015F",
        "set_title|GoldShare - Ayarlar","set_port|Port:","set_uplim|Y\u00FCkleme limiti (MB):",
        "set_speed|H\u0131z limiti (KB/s, 0 = s\u0131n\u0131rs\u0131z):","set_pass|Eri\u015Fim PIN'i (bo\u015F = yok):",
        "set_font|Yaz\u0131 boyutu:","font_s|K\u00FC\u00E7\u00FCk","font_m|Orta","font_l|B\u00FCy\u00FCk",
        "set_autostart|Ba\u015Flang\u0131\u00E7ta sunucuyu otomatik ba\u015Flat",
        "set_allowdel|Sayfadan silmeye izin ver (PIN gerekli)",
        "set_tray|Sistem tepsisine k\u00FC\u00E7\u00FClt","set_savepos|Pencere konumunu hat\u0131rla",
        "set_beep|Dosya al\u0131nca ses \u00E7al","set_log|GoldShare.log'a g\u00FCnl\u00FCk yaz",
        "save|KAYDET","cancel|\u0130PTAL",
        "msg_ready|Haz\u0131r. 'SUNUCUYU BA\u015ELAT'a bas\u0131n.","msg_started|Sunucu {0} portunda ba\u015Flad\u0131",
        "msg_stopped|Sunucu durduruldu.","msg_start_fail|Ba\u015Flat\u0131lamad\u0131:\r\n{0}\r\n\r\nBa\u015Fka port deneyin.",
        "msg_bad_port|Ge\u00E7erli port girin (1-65535).","msg_start_first|\u00D6nce sunucuyu ba\u015Flat\u0131n.",
        "msg_copied|Kopyaland\u0131: {0}","msg_url_copied|URL kopyaland\u0131.",
        "msg_upload_done|Y\u00FCkleme bitti: {0}","msg_port_restart|Port de\u011Fi\u015Fti. Sunucuyu yeniden ba\u015Flat\u0131n.",
        "msg_tray_min|Tepside \u00E7al\u0131\u015F\u0131yor.","msg_already|GoldShare zaten \u00E7al\u0131\u015F\u0131yor.",
        "msg_deleted|Silindi: {0}","msg_del_deny|Silme reddedildi (yanl\u0131\u015F PIN veya kapal\u0131).",
        "msg_qr_saved|QR g\u00F6rseli kaydedildi:\r\n{0}","msg_zip_none|Dosya se\u00E7ilmedi.",
        "page_title|GoldShare - Dosyalar","page_sub|Premium Payla\u015F\u0131m \u2014 Y\u00FCkle & \u0130ndir",
        "page_dl|\u2B07 \u0130ND\u0130R","page_up|\u2B06 BU C\u0130HAZA Y\u00DCKLE",
        "page_search_ph|\uD83D\uDD0D Ara...","page_no_files|Dosya yok.",
        "page_upload_btn|Y\u00DCKLE","page_cam|\uD83D\uDCF7 Foto\u011Fraf",
        "page_note_h|\uD83D\uDCDD PC'YE NOT","page_note_ph|Mesaj yaz\u0131n... PC'de ve panoda g\u00F6r\u00FCn\u00FCr",
        "page_note_send|G\u00D6NDER \u27A4","btn_zip|\u2B07 SE\u00C7\u0130L\u0130 Z\u0130P",
        "view_grid|\u25A6 Izgara","view_list|\u2630 Liste",
        "sort_name|A-Z","sort_size|Boyut","sort_new|Yeni",
        "page_del_ask|Silmek i\u00E7in PIN:","page_wrong_pin|Yanl\u0131\u015F PIN veya silme kapal\u0131.",
        "page_login_h|\uD83D\uDD12 GoldShare","page_login_btn|G\u0130R","page_bad_pass|Yanl\u0131\u015F PIN.",
        "page_received|\uD83D\uDCE6 DOSYALAR","page_storage_f|\uD83D\uDCBE {1} i\u00E7inde {0} bo\u015F"};

        private static string[] Ru=new string[]{
        "title|GoldShare - \u041F\u0440\u0435\u043C\u0438\u0443\u043C \u043E\u0431\u043C\u0435\u043D \u0444\u0430\u0439\u043B\u0430\u043C\u0438","start|\u0417\u0410\u041F\u0423\u0421\u0422\u0418\u0422\u042C \u0421\u0415\u0420\u0412\u0415\u0420","stop|\u041E\u0421\u0422\u0410\u041D\u041E\u0412\u0418\u0422\u042C \u0421\u0415\u0420\u0412\u0415\u0420",
        "open_browser|\u041E\u0422\u041A\u0420\u042B\u0422\u042C \u0412 \u0411\u0420\u0410\u0423\u0417\u0415\u0420\u0415","open_folder|\u041E\u0422\u041A\u0420\u042B\u0422\u042C \u041F\u0410\u041F\u041A\u0423","copy_url|\u041A\u041E\u041F\u0418\u0420\u041E\u0412\u0410\u0422\u042C URL",
        "qr_scan|QR / \u0421\u041A\u0410\u041D","gear|\u2699 \u041D\u0410\u0421\u0422\u0420\u041E\u0419\u041A\u0418","port|\u041F\u043E\u0440\u0442:","folder|\u041F\u0430\u043F\u043A\u0430:",
        "addresses|\u0410\u0434\u0440\u0435\u0441\u0430 \u0441\u0435\u0440\u0432\u0435\u0440\u0430 - \u0432\u044B\u0431\u0435\u0440\u0438\u0442\u0435, \u0437\u0430\u0442\u0435\u043C \u041A\u041E\u041F\u0418\u0420\u041E\u0412\u0410\u0422\u042C \u0438\u043B\u0438 QR / \u0421\u041A\u0410\u041D:",
        "log|\u0416\u0443\u0440\u043D\u0430\u043B:","language|\u042F\u0437\u044B\u043A:","theme_lbl|\u0422\u0435\u043C\u0430:",
        "status_run|\u25CF \u0420\u0410\u0411\u041E\u0422\u0410\u0415\u0422 :{0}","status_stop|\u25CB \u041E\u0421\u0422\u0410\u041D\u041E\u0412\u041B\u0415\u041D","conns_fmt|\u2022 {0} \u0441\u043E\u0435\u0434.",
        "up_lbl|\u041E\u0422\u0414\u0410\u041A\u0410","down_lbl|\u041F\u0420\u0418\u0415\u041C","tray_open|\u041E\u0442\u043A\u0440\u044B\u0442\u044C","tray_exit|\u0412\u044B\u0445\u043E\u0434",
        "set_title|GoldShare - \u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438","set_port|\u041F\u043E\u0440\u0442:","set_uplim|\u041B\u0438\u043C\u0438\u0442 \u043E\u0442\u043F\u0440\u0430\u0432\u043A\u0438 (\u041C\u0411):",
        "set_speed|\u041B\u0438\u043C\u0438\u0442 \u0441\u043A\u043E\u0440\u043E\u0441\u0442\u0438 (\u041A\u0411/\u0441, 0 = \u0431\u0435\u0437 \u043B\u0438\u043C\u0438\u0442\u0430):","set_pass|PIN-\u043A\u043E\u0434 (\u043F\u0443\u0441\u0442\u043E = \u043D\u0435\u0442):",
        "set_font|\u0420\u0430\u0437\u043C\u0435\u0440 \u0448\u0440\u0438\u0444\u0442\u0430:","font_s|\u041C\u0430\u043B\u044B\u0439","font_m|\u0421\u0440\u0435\u0434\u043D\u0438\u0439","font_l|\u0411\u043E\u043B\u044C\u0448\u043E\u0439",
        "set_autostart|\u0417\u0430\u043F\u0443\u0441\u043A\u0430\u0442\u044C \u0441\u0435\u0440\u0432\u0435\u0440 \u0430\u0432\u0442\u043E\u043C\u0430\u0442\u0438\u0447\u0435\u0441\u043A\u0438",
        "set_allowdel|\u0420\u0430\u0437\u0440\u0435\u0448\u0438\u0442\u044C \u0443\u0434\u0430\u043B\u0435\u043D\u0438\u0435 \u0441\u043E \u0441\u0442\u0440\u0430\u043D\u0438\u0446\u044B (\u043D\u0443\u0436\u0435\u043D PIN)",
        "set_tray|\u0421\u0432\u0435\u0440\u043D\u0443\u0442\u044C \u0432 \u0442\u0440\u0435\u0439","set_savepos|\u041F\u043E\u043C\u043D\u0438\u0442\u044C \u043F\u043E\u0437\u0438\u0446\u0438\u044E \u043E\u043A\u043D\u0430",
        "set_beep|\u0417\u0432\u0443\u043A \u043F\u0440\u0438 \u043F\u043E\u043B\u0443\u0447\u0435\u043D\u0438\u0438 \u0444\u0430\u0439\u043B\u0430","set_log|\u041F\u0438\u0441\u0430\u0442\u044C \u0436\u0443\u0440\u043D\u0430\u043B \u0432 GoldShare.log",
        "save|\u0421\u041E\u0425\u0420\u0410\u041D\u0418\u0422\u042C","cancel|\u041E\u0422\u041C\u0415\u041D\u0410",
        "msg_ready|\u0413\u043E\u0442\u043E\u0432. \u041D\u0430\u0436\u043C\u0438\u0442\u0435 '\u0417\u0410\u041F\u0423\u0421\u0422\u0418\u0422\u042C'.","msg_started|\u0421\u0435\u0440\u0432\u0435\u0440 \u0437\u0430\u043F\u0443\u0449\u0435\u043D \u043D\u0430 \u043F\u043E\u0440\u0442\u0443 {0}",
        "msg_stopped|\u0421\u0435\u0440\u0432\u0435\u0440 \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D.","msg_start_fail|\u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u044C:\r\n{0}\r\n\r\n\u041F\u043E\u043F\u0440\u043E\u0431\u0443\u0439\u0442\u0435 \u0434\u0440\u0443\u0433\u043E\u0439 \u043F\u043E\u0440\u0442.",
        "msg_bad_port|\u0412\u0432\u0435\u0434\u0438\u0442\u0435 \u043F\u043E\u0440\u0442 (1-65535).","msg_start_first|\u0421\u043D\u0430\u0447\u0430\u043B\u0430 \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0441\u0435\u0440\u0432\u0435\u0440.",
        "msg_copied|\u0421\u043A\u043E\u043F\u0438\u0440\u043E\u0432\u0430\u043D\u043E: {0}","msg_url_copied|URL \u0441\u043A\u043E\u043F\u0438\u0440\u043E\u0432\u0430\u043D.",
        "msg_upload_done|\u0417\u0430\u0433\u0440\u0443\u0437\u043A\u0430 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0430: {0}","msg_port_restart|\u041F\u043E\u0440\u0442 \u0438\u0437\u043C\u0435\u043D\u0451\u043D. \u041F\u0435\u0440\u0435\u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0441\u0435\u0440\u0432\u0435\u0440.",
        "msg_tray_min|\u041F\u0440\u043E\u0434\u043E\u043B\u0436\u0430\u0435\u0442 \u0440\u0430\u0431\u043E\u0442\u0430\u0442\u044C \u0432 \u0442\u0440\u0435\u0435.","msg_already|GoldShare \u0443\u0436\u0435 \u0437\u0430\u043F\u0443\u0449\u0435\u043D.",
        "msg_deleted|\u0423\u0434\u0430\u043B\u0435\u043D\u043E: {0}","msg_del_deny|\u0423\u0434\u0430\u043B\u0435\u043D\u0438\u0435 \u043E\u0442\u043A\u043B\u043E\u043D\u0435\u043D\u043E (\u043D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 PIN \u0438\u043B\u0438 \u043E\u0442\u043A\u043B\u044E\u0447\u0435\u043D\u043E).",
        "msg_qr_saved|QR \u0441\u043E\u0445\u0440\u0430\u043D\u0451\u043D:\r\n{0}","msg_zip_none|\u0424\u0430\u0439\u043B\u044B \u043D\u0435 \u0432\u044B\u0431\u0440\u0430\u043D\u044B.",
        "page_title|GoldShare - \u0424\u0430\u0439\u043B\u044B","page_sub|\u041F\u0440\u0435\u043C\u0438\u0443\u043C \u043E\u0431\u043C\u0435\u043D \u2014 \u043E\u0442\u043F\u0440\u0430\u0432\u043A\u0430 & \u0437\u0430\u0433\u0440\u0443\u0437\u043A\u0430",
        "page_dl|\u2B07 \u0421\u041A\u0410\u0427\u0410\u0422\u042C","page_up|\u2B06 \u041E\u0422\u041F\u0420\u0410\u0412\u0418\u0422\u042C \u041D\u0410 \u042D\u0422\u041E\u0422 \u041F\u041A",
        "page_search_ph|\uD83D\uDD0D \u041F\u043E\u0438\u0441\u043A...","page_no_files|\u0424\u0430\u0439\u043B\u043E\u0432 \u043D\u0435\u0442.",
        "page_upload_btn|\u041E\u0422\u041F\u0420\u0410\u0412\u0418\u0422\u042C","page_cam|\uD83D\uDCF7 \u0424\u043E\u0442\u043E",
        "page_note_h|\uD83D\uDCDD \u0417\u0410\u041C\u0415\u0422\u041A\u0410 \u041D\u0410 \u041F\u041A","page_note_ph|\u041D\u0430\u043F\u0438\u0448\u0438\u0442\u0435 \u0441\u043E\u043E\u0431\u0449\u0435\u043D\u0438\u0435... \u043F\u043E\u044F\u0432\u0438\u0442\u0441\u044F \u043D\u0430 \u041F\u041A \u0438 \u0432 \u0431\u0443\u0444\u0435\u0440\u0435",
        "page_note_send|\u041E\u0422\u041F\u0420\u0410\u0412\u0418\u0422\u042C \u27A4","btn_zip|\u2B07 ZIP \u0412\u042B\u0411\u0420\u0410\u041D\u041D\u041E\u0415",
        "view_grid|\u25A6 \u041F\u043B\u0438\u0442\u043A\u0438","view_list|\u2630 \u0421\u043F\u0438\u0441\u043E\u043A",
        "sort_name|A-\u042F","sort_size|\u0420\u0430\u0437\u043C\u0435\u0440","sort_new|\u041D\u043E\u0432\u044B\u0435",
        "page_del_ask|PIN \u0434\u043B\u044F \u0443\u0434\u0430\u043B\u0435\u043D\u0438\u044F:","page_wrong_pin|\u041D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 PIN \u0438\u043B\u0438 \u0443\u0434\u0430\u043B\u0435\u043D\u0438\u0435 \u043E\u0442\u043A\u043B\u044E\u0447\u0435\u043D\u043E.",
        "page_login_h|\uD83D\uDD12 GoldShare","page_login_btn|\u0412\u041E\u0419\u0422\u0418","page_bad_pass|\u041D\u0435\u0432\u0435\u0440\u043D\u044B\u0439 PIN.",
        "page_received|\uD83D\uDCE6 \u0424\u0410\u0419\u041B\u042B","page_storage_f|\uD83D\uDCBE {0} \u0441\u0432\u043E\u0431\u043E\u0434\u043D\u043E \u0438\u0437 {1}"};
    }

    // ========================================================================
    //  QR ENGINE — byte mode, ECC level M, versions 1-4 (URLs up to 62 chars)
    //  Pure .NET 2.0: no Func/Action<> generics.
    // ========================================================================
    public static class Qr
    {
        static byte[] exp=new byte[512], log=new byte[256];
        static Qr()
        {
            int x=1;
            for(int i=0;i<255;i++){ exp[i]=(byte)x; log[(byte)x]=(byte)i; x<<=1; if((x&0x100)!=0)x^=0x11d; }
            for(int i=255;i<512;i++) exp[i]=exp[i-255];
        }
        static int Gmul(int a,int b){ if(a==0||b==0)return 0; return exp[log[a]+log[b]]; }

        static byte[] GenPoly(int n)
        {
            List<byte> g=new List<byte>(); g.Add(1);
            for(int i=0;i<n;i++)
            {
                byte[] ng=new byte[g.Count+1];
                for(int j=0;j<g.Count;j++){ ng[j]^=(byte)Gmul(g[j],exp[i]); ng[j+1]^=g[j]; }
                g=new List<byte>(ng);
            }
            return g.ToArray();
        }
        static byte[] RsEnc(byte[] data,int ecLen)
        {
            byte[] gen=GenPoly(ecLen);
            byte[] res=new byte[data.Length+ecLen];
            Array.Copy(data,res,data.Length);
            for(int i=0;i<data.Length;i++)
            {
                int c=res[i]; if(c==0) continue;
                for(int j=0;j<gen.Length;j++) res[i+j]^=(byte)Gmul(gen[j],c);
            }
            byte[] ec=new byte[ecLen];
            Array.Copy(res,data.Length,ec,0,ecLen);
            return ec;
        }

        // version params: {size, dataCW, ecLen, blocks}
        static int[] VerParam(int v)
        {
            if(v==1) return new int[]{21,16,10,1};
            if(v==2) return new int[]{25,28,16,1};
            if(v==3) return new int[]{29,44,26,1};
            return new int[]{33,64,18,2};   // v4
        }
        static int[] Caps=new int[]{14,26,42,62};

        static int FmtBits(int mask)
        {
            int d=mask;                 // ECC M = 00
            int v=d<<10;
            for(int i=14;i>=10;i--) if(((v>>i)&1)!=0) v^=0x537<<(i-10);
            return ((d<<10)|(v&0x3FF))^0x5412;
        }
        static bool FBit(int f,int b){ return ((f>>b)&1)!=0; }

        static void AddVal(List<bool> l,int val,int cnt)
        { for(int i=cnt-1;i>=0;i--) l.Add(((val>>i)&1)!=0); }

        public static Bitmap Make(string text,int px)
        {
            byte[] data=Encoding.UTF8.GetBytes(text);
            int ver=0;
            for(int i=0;i<4;i++){ if(data.Length<=Caps[i]){ ver=i+1; break; } }
            if(ver==0){ ver=4; if(data.Length>Caps[3]){ byte[] t=new byte[Caps[3]]; Array.Copy(data,t,Caps[3]); data=t; } }
            int[] pr=VerParam(ver);
            int n=pr[0], totalData=pr[1], ecLen=pr[2], nBlocks=pr[3];

            // bitstream
            List<bool> bs=new List<bool>();
            AddVal(bs,4,4); AddVal(bs,data.Length,8);
            for(int i=0;i<data.Length;i++) AddVal(bs,data[i],8);
            int cap=totalData*8;
            int term=cap-bs.Count; if(term>4)term=4;
            AddVal(bs,0,term);
            while(bs.Count%8!=0) bs.Add(false);
            List<byte> dc=new List<byte>();
            for(int i=0;i<bs.Count;i+=8){ int b=0; for(int j=0;j<8;j++) if(bs[i+j])b|=1<<(7-j); dc.Add((byte)b); }
            bool flip=true;
            while(dc.Count<totalData){ dc.Add(flip?(byte)0xEC:(byte)0x11); flip=!flip; }

            // blocks + ec + interleave
            int blkData=totalData/nBlocks;
            List<byte> final=new List<byte>();
            byte[][] ecs=new byte[nBlocks][];
            for(int b=0;b<nBlocks;b++)
            {
                byte[] blk=new byte[blkData];
                for(int i=0;i<blkData;i++) blk[i]=dc[b*blkData+i];
                ecs[b]=RsEnc(blk,ecLen);
            }
            for(int i=0;i<blkData;i++) for(int b=0;b<nBlocks;b++) final.Add(dc[b*blkData+i]);
            for(int i=0;i<ecLen;i++)   for(int b=0;b<nBlocks;b++) final.Add(ecs[b][i]);

            List<bool> stream=new List<bool>(final.Count*8);
            for(int i=0;i<final.Count;i++) AddVal(stream,final[i],8);

            bool[,] m=new bool[n,n], fn=new bool[n,n];

            DrawFinder(m,fn,n,0,0); DrawFinder(m,fn,n,0,n-7); DrawFinder(m,fn,n,n-7,0);
            for(int i=8;i<n-8;i++){ fn[6,i]=true; fn[i,6]=true; if(i%2==0){ m[6,i]=true; m[i,6]=true; } }
            if(ver>=2){ int c=4*ver+10; DrawAlign(m,fn,n,c,c); }
            m[n-8,8]=true; fn[n-8,8]=true;   // dark module
            for(int i=0;i<9;i++){ if(i!=6){ fn[8,i]=true; fn[i,8]=true; } }
            for(int i=0;i<8;i++){ fn[8,n-1-i]=true; fn[n-1-i,8]=true; }

            // zigzag data placement
            int idx=0; bool up=true;
            for(int col=n-1;col>0;col-=2)
            {
                if(col==6) col=5;
                for(int i=0;i<n;i++)
                {
                    int row=up?(n-1-i):i;
                    for(int c=col;c>=col-1;c--)
                    {
                        if(!fn[row,c])
                        {
                            bool bit=false;
                            if(idx<stream.Count) bit=stream[idx];
                            idx++;
                            m[row,c]=bit;
                        }
                    }
                }
                up=!up;
            }

            // try all 8 masks, keep lowest penalty
            int bestPen=int.MaxValue;
            bool[,] best=new bool[n,n];
            for(int mk=0;mk<8;mk++)
            {
                bool[,] w=new bool[n,n];
                for(int r=0;r<n;r++) for(int c2=0;c2<n;c2++) w[r,c2]=m[r,c2];
                for(int r=0;r<n;r++) for(int c2=0;c2<n;c2++) if(!fn[r,c2]&&Masked(mk,r,c2)) w[r,c2]=!w[r,c2];
                WriteFormat(w,n,FmtBits(mk));
                int pen=Penalty(w,n);
                if(pen<bestPen){ bestPen=pen; best=w; }
            }
            return Render(best,n,px);
        }

        static bool Masked(int mk,int i,int j)
        {
            switch(mk)
            {
                case 0: return (i+j)%2==0;
                case 1: return i%2==0;
                case 2: return j%3==0;
                case 3: return (i+j)%3==0;
                case 4: return (i/2+j/3)%2==0;
                case 5: return (i*j)%2+(i*j)%3==0;
                case 6: return ((i*j)%2+(i*j)%3)%2==0;
                default: return ((i+j)%2+(i*j)%3)%2==0;
            }
        }

        static void WriteFormat(bool[,] w,int n,int f)
        {
            // copy 1 (top-left)
            int[] rr=new int[]{0,1,2,3,4,5,7,8,8,8,8,8,8,8,8};
            int[] cc=new int[]{8,8,8,8,8,8,8,8,7,5,4,3,2,1,0};
            for(int i=0;i<15;i++) w[rr[i],cc[i]]=FBit(f,14-i);
            // copy 2: bits 0..6 column 8, rows n-1..n-7
            for(int i=0;i<7;i++) w[n-1-i,8]=FBit(f,i);
            // copy 2: bits 8..14 row 8, cols n-7..n-1 (bit 8 at n-7, bit 14 at n-1)
            for(int i=8;i<15;i++) w[8,n-15+i]=FBit(f,i);
        }

        static int Penalty(bool[,] m,int n)
        {
            int p=0;
            for(int r=0;r<n;r++)
            {
                int run=1;
                for(int c=1;c<n;c++){ if(m[r,c]==m[r,c-1]) run++; else { if(run>=5)p+=3+run-5; run=1; } }
                if(run>=5)p+=3+run-5;
            }
            for(int c=0;c<n;c++)
            {
                int run=1;
                for(int r=1;r<n;r++){ if(m[r,c]==m[r-1,c]) run++; else { if(run>=5)p+=3+run-5; run=1; } }
                if(run>=5)p+=3+run-5;
            }
            for(int r=0;r<n-1;r++) for(int c=0;c<n-1;c++)
            {
                bool v=m[r,c];
                if(m[r,c+1]==v&&m[r+1,c]==v&&m[r+1,c+1]==v) p+=3;
            }
            int[] pat=new int[]{1,0,1,1,1,0,1,0,0,0,0};
            int[] pat2=new int[]{0,0,0,0,1,0,1,1,1,0,1};
            for(int r=0;r<n;r++) for(int c=0;c<=n-11;c++)
            {
                bool ok1=true,ok2=true;
                for(int i=0;i<11;i++){ int v=m[r,c+i]?1:0; if(v!=pat[i])ok1=false; if(v!=pat2[i])ok2=false; if(!ok1&&!ok2)break; }
                if(ok1||ok2)p+=40;
            }
            for(int c=0;c<n;c++) for(int r=0;r<=n-11;r++)
            {
                bool ok1=true,ok2=true;
                for(int i=0;i<11;i++){ int v=m[r+i,c]?1:0; if(v!=pat[i])ok1=false; if(v!=pat2[i])ok2=false; if(!ok1&&!ok2)break; }
                if(ok1||ok2)p+=40;
            }
            int dark=0;
            for(int r=0;r<n;r++) for(int c=0;c<n;c++) if(m[r,c])dark++;
            int pct=dark*100/(n*n);
            int dev=Math.Abs(pct-50)/5;
            p+=dev*10;
            return p;
        }

        static void DrawFinder(bool[,] m,bool[,] fn,int n,int r0,int c0)
        {
            for(int r=-1;r<=7;r++) for(int c=-1;c<=7;c++)
            {
                int r2=r0+r,c2=c0+c;
                if(r2<0||c2<0||r2>=n||c2>=n) continue;
                fn[r2,c2]=true;
                bool inR=r>=0&&r<7,inC=c>=0&&c<7;
                if(!inR||!inC){ m[r2,c2]=false; continue; }
                bool ring=(r==0||r==6||c==0||c==6);
                bool core=(r>=2&&r<=4&&c>=2&&c<=4);
                m[r2,c2]=ring||core;
            }
        }
        static void DrawAlign(bool[,] m,bool[,] fn,int n,int cr,int cc)
        {
            for(int r=-2;r<=2;r++) for(int c=-2;c<=2;c++)
            {
                int r2=cr+r,c2=cc+c;
                if(r2<0||c2<0||r2>=n||c2>=n) continue;
                fn[r2,c2]=true;
                m[r2,c2]=(Math.Max(Math.Abs(r),Math.Abs(c))!=1);
            }
        }

        static Bitmap Render(bool[,] m,int n,int px)
        {
            int quiet=4, scale=Math.Max(3,px/(n+quiet*2)), sz=(n+quiet*2)*scale;
            Bitmap bmp=new Bitmap(sz,sz,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(bmp))
            {
                g.SmoothingMode=SmoothingMode.None;
                g.Clear(Color.White);
                using(SolidBrush br=new SolidBrush(Color.Black))
                    for(int r=0;r<n;r++) for(int c=0;c<n;c++)
                        if(m[r,c]) g.FillRectangle(br,(c+quiet)*scale,(r+quiet)*scale,scale,scale);
            }
            return bmp;
        }
    }

    // ========================================================================
    //  HTTP SERVER — thread-per-connection, byte counters, resume, throttle
    // ========================================================================
    public delegate void LogEvent(string s);
    public delegate void UploadEvent(string name,string size);

    public static class Server
    {
        public static long Sent=0, Recv=0;
        public static int Active=0;
        public static bool Running=false;
        public static int Port=8080;
        public static event LogEvent OnLog;
        public static event UploadEvent OnUpload;

        static TcpListener lis;
        static Thread accThread;

        static void FireLog(string s){ LogF.W(s); if(OnLog!=null) OnLog(s); }

        public static string Start(int port,string folder)
        {
            Stop();
            try
            {
                Directory.CreateDirectory(folder);
                lis=new TcpListener(IPAddress.Any,port);
                lis.Start();
                Port=port; Running=true;
                accThread=new Thread(new ThreadStart(AcceptLoop));
                accThread.IsBackground=true;
                accThread.Start();
                FireLog("LISTEN :"+port);
                return null;
            }
            catch(Exception ex){ Running=false; return ex.Message; }
        }

        public static void Stop()
        {
            bool was=Running;
            Running=false;
            try{ if(lis!=null) lis.Stop(); }catch{ }
            try{ if(accThread!=null&&accThread.IsAlive) accThread.Abort(); }catch{ }
            lis=null; accThread=null;
            if(was) FireLog("SERVER STOPPED");
        }

        static void AcceptLoop()
        {
            while(Running)
            {
                TcpClient c=null;
                try{ c=lis.AcceptTcpClient(); }catch{ break; }
                Thread t=new Thread(new ParameterizedThreadStart(Handle));
                t.IsBackground=true;
                t.Start(c);
            }
        }

        static string HeaderGet(Dictionary<string,string> h,string k)
        {
            string v;
            return h.TryGetValue(k,out v)?v:"";
        }

        static bool AuthOk(Dictionary<string,string> h)
        {
            if(Settings.PasswordHash.Length==0) return true;
            string ck=HeaderGet(h,"cookie");
            return ck.IndexOf("gs="+Settings.PasswordHash)>=0;
        }

        static void Handle(object o)
        {
            TcpClient c=(TcpClient)o;
            Interlocked.Increment(ref Active);
            NetworkStream st=null;
            try
            {
                c.ReceiveTimeout=15000; c.SendTimeout=300000;
                st=c.GetStream();
                string reqline=ReadLine(st);
                if(reqline==null||reqline.Length==0) return;
                string[] pp=reqline.Split(' ');
                if(pp.Length<2) return;
                string method=pp[0], target=pp[1];
                string path=target, query="";
                int q=target.IndexOf('?');
                if(q>=0){ path=target.Substring(0,q); query=target.Substring(q+1); }

                Dictionary<string,string> hd=new Dictionary<string,string>();
                int headTotal=0;
                while(true)
                {
                    string ln=ReadLine(st);
                    if(ln==null) return;
                    headTotal+=ln.Length+2;
                    if(headTotal>32768) return;
                    if(ln.Length==0) break;
                    int col=ln.IndexOf(':');
                    if(col>0) hd[ln.Substring(0,col).Trim().ToLower()]=ln.Substring(col+1).Trim();
                }

                byte[] body=null;
                if(method=="POST")
                {
                    int clen=0;
                    int.TryParse(HeaderGet(hd,"content-length"),out clen);
                    if(clen>Settings.UploadMB*1048576) clen=Settings.UploadMB*1048576;
                    if(clen>0)
                    {
                        int got=0;
                        byte[] tmp=new byte[65536];
                        using(MemoryStream ms=new MemoryStream())
                        {
                            while(got<clen)
                            {
                                int want=Math.Min(tmp.Length,clen-got);
                                int n=st.Read(tmp,0,want);
                                if(n<=0) break;
                                ms.Write(tmp,0,n);
                                Interlocked.Add(ref Recv,n);
                                got+=n;
                            }
                            body=ms.ToArray();
                        }
                    }
                }

                // PIN gate
                if(Settings.PasswordHash.Length>0&&!AuthOk(hd)&&path!="/login")
                {
                    if(path=="/"||path=="/index.html") SendText(st,200,Page.BuildLogin(false),null);
                    else SendText(st,302,"","Location: /\r\n");
                    return;
                }

                if(path=="/"||path=="/index.html") SendText(st,200,Page.Build(),null);
                else if(path=="/login") DoLogin(st,query,body);
                else if(path=="/f") DoDownload(st,QueryVal(query,"n"),HeaderGet(hd,"range"));
                else if(path=="/t") DoThumb(st,QueryVal(query,"n"));
                else if(path=="/zip") DoZip(st,query);
                else if(path=="/up") DoUpload(st,body);
                else if(path=="/note") DoNote(st,body);
                else if(path=="/del") DoDelete(st,query);
                else SendText(st,404,"Not found",null);
            }
            catch{ }
            finally
            {
                Interlocked.Decrement(ref Active);
                try{ c.Close(); }catch{ }
            }
        }

        static string ReadLine(NetworkStream st)
        {
            StringBuilder sb=new StringBuilder();
            int prev=-1;
            while(true)
            {
                int b=st.ReadByte();
                if(b<0) return sb.Length>0?sb.ToString():null;
                if(prev=='\r'&&b=='\n') return sb.ToString(0,sb.Length-1);
                sb.Append((char)b);
                if(sb.Length>8192) return sb.ToString();
                prev=b;
            }
        }

        static string QueryVal(string q,string k)
        {
            if(q==null||q.Length==0) return "";
            string[] parts=q.Split('&');
            for(int i=0;i<parts.Length;i++)
            {
                int eq=parts[i].IndexOf('=');
                if(eq<=0) continue;
                if(parts[i].Substring(0,eq)==k) return UrlDec(parts[i].Substring(eq+1));
            }
            return "";
        }

        public static string UrlDec(string s)
        {
            if(s==null) return "";
            StringBuilder sb=new StringBuilder();
            for(int i=0;i<s.Length;i++)
            {
                char ch=s[i];
                if(ch=='+') sb.Append(' ');
                else if(ch=='%'&&i+2<s.Length)
                {
                    int v;
                    if(int.TryParse(s.Substring(i+1,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out v))
                    { sb.Append((char)v); i+=2; }
                    else sb.Append(ch);
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }
        public static string UrlEnc(string s)
        {
            StringBuilder sb=new StringBuilder();
            byte[] b=Encoding.UTF8.GetBytes(s);
            for(int i=0;i<b.Length;i++)
            {
                char ch=(char)b[i];
                if((ch>='A'&&ch<='Z')||(ch>='a'&&ch<='z')||(ch>='0'&&ch<='9')||ch=='-'||ch=='_'||ch=='.'||ch=='~') sb.Append(ch);
                else sb.Append('%').Append(b[i].ToString("x2"));
            }
            return sb.ToString();
        }

        static void SendHead(NetworkStream st,int code,string ctype,long len,string extra)
        {
            StringBuilder sb=new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ');
            if(code==200) sb.Append("OK"); else if(code==206) sb.Append("Partial Content");
            else if(code==302) sb.Append("Found"); else if(code==404) sb.Append("Not Found");
            else sb.Append("Error");
            sb.Append("\r\nContent-Type: ").Append(ctype);
            sb.Append("\r\nContent-Length: ").Append(len);
            sb.Append("\r\nAccept-Ranges: bytes\r\nConnection: close\r\n");
            if(extra!=null) sb.Append(extra);
            sb.Append("\r\n");
            byte[] h=Encoding.UTF8.GetBytes(sb.ToString());
            st.Write(h,0,h.Length);
            Interlocked.Add(ref Sent,h.Length);
        }
        static void SendText(NetworkStream st,int code,string body,string extra)
        {
            byte[] b=Encoding.UTF8.GetBytes(body);
            SendHead(st,code,"text/html; charset=utf-8",b.Length,extra);
            st.Write(b,0,b.Length);
            Interlocked.Add(ref Sent,b.Length);
        }

        static void DoLogin(NetworkStream st,string query,byte[] body)
        {
            string p=QueryVal(query,"p");
            if(p.Length==0&&body!=null&&body.Length>0&&body.Length<512)
            {
                string b=UrlDec(Encoding.UTF8.GetString(body)).Trim();
                if(b.StartsWith("p=")) p=b.Substring(2); else p=b;
            }
            if(Settings.Hash(p)==Settings.PasswordHash)
            {
                SendText(st,302,"","Location: /\r\nSet-Cookie: gs="+Settings.PasswordHash+"; Path=/\r\n");
                FireLog("LOGIN OK");
            }
            else
            {
                FireLog("LOGIN FAIL");
                SendText(st,200,Page.BuildLogin(true),null);
            }
        }

        static string SafeName(string n)
        {
            char[] bad=new char[]{'\\','/',':','*','?','\"','<','>','|'};
            for(int i=0;i<bad.Length;i++) n=n.Replace(bad[i],'_');
            n=n.TrimStart('.',' ');
            if(n.Length==0) n="file";
            if(n.Length>150) n=n.Substring(n.Length-150);
            return n;
        }

        public static string UniqueName(string folder,string name)
        {
            string p=Path.Combine(folder,name);
            if(!File.Exists(p)) return name;
            string ext=Path.GetExtension(name), baseN=Path.GetFileNameWithoutExtension(name);
            for(int i=1;i<999;i++)
            {
                string t=baseN+" ("+i+")"+ext;
                if(!File.Exists(Path.Combine(folder,t))) return t;
            }
            return Guid.NewGuid().ToString("N")+ext;
        }

        static void DoDownload(NetworkStream st,string name,string range)
        {
            string fp=Path.Combine(Settings.ShareFolder,SafeName(name));
            if(!File.Exists(fp)){ SendText(st,404,"Not found",null); return; }
            long len=new FileInfo(fp).Length, start=0, end=len-1;
            bool partial=false;
            if(range!=null&&range.StartsWith("bytes="))
            {
                string r=range.Substring(6);
                int dash=r.IndexOf('-');
                long s2;
                if(dash>0&&long.TryParse(r.Substring(0,dash),out s2))
                { if(s2>=0&&s2<len){ start=s2; partial=true; } }
            }
            FileStream fs=null;
            try
            {
                fs=new FileStream(fp,FileMode.Open,FileAccess.Read,FileShare.Read);
                string fn=SafeName(name);
                bool ascii=true;
                for(int i=0;i<fn.Length;i++) if(fn[i]>126){ ascii=false; break; }
                string disp="attachment; filename=\""+(ascii?fn:"download"+Path.GetExtension(fn))+"\""+
                            (ascii?"":"; filename*=UTF-8''"+UrlEnc(fn));
                if(partial)
                {
                    fs.Seek(start,SeekOrigin.Begin);
                    SendHead(st,206,"application/octet-stream",end-start+1,
                        "Content-Range: bytes "+start+"-"+end+"/"+len+"\r\nContent-Disposition: "+disp+"\r\n");
                }
                else SendHead(st,200,"application/octet-stream",len,"Content-Disposition: "+disp+"\r\n");

                byte[] buf=new byte[65536];
                long remaining=partial?(end-start+1):len;
                long sentNow=0;
                Stopwatch watch=new Stopwatch(); watch.Start();
                int limit=Settings.SpeedKB*1024;
                while(remaining>0&&Running)
                {
                    int want=(int)Math.Min(buf.Length,remaining);
                    int n=fs.Read(buf,0,want);
                    if(n<=0) break;
                    st.Write(buf,0,n);
                    Interlocked.Add(ref Sent,n);
                    sentNow+=n; remaining-=n;
                    if(limit>0)
                    {
                        double expectMs=sentNow*1000.0/limit;
                        double el=watch.Elapsed.TotalMilliseconds;
                        if(el<expectMs) Thread.Sleep((int)(expectMs-el));
                    }
                }
                FireLog("SENT "+fn+" ("+FmtBytes(partial?(end-start+1):len)+")");
            }
            catch{ }
            finally{ try{ if(fs!=null) fs.Close(); }catch{ } }
        }

        static void DoThumb(NetworkStream st,string name)
        {
            string fp=Path.Combine(Settings.ShareFolder,SafeName(name));
            string ext=Path.GetExtension(fp).ToLower();
            if(ext!=".jpg"&&ext!=".jpeg"&&ext!=".png"&&ext!=".gif"&&ext!=".bmp"){ SendText(st,404,"x",null); return; }
            try
            {
                byte[] raw=File.ReadAllBytes(fp);
                using(MemoryStream ms=new MemoryStream(raw))
                using(Image img=Image.FromStream(ms))
                {
                    int tw=160, th=160;
                    double sc=Math.Min(tw/(double)img.Width,th/(double)img.Height);
                    if(sc>1) sc=1;
                    int w=Math.Max(1,(int)(img.Width*sc)), h=Math.Max(1,(int)(img.Height*sc));
                    using(Image th2=img.GetThumbnailImage(w,h,null,IntPtr.Zero))
                    using(MemoryStream outMs=new MemoryStream())
                    {
                        th2.Save(outMs,ImageFormat.Jpeg);
                        byte[] b=outMs.ToArray();
                        SendHead(st,200,"image/jpeg",b.Length,null);
                        st.Write(b,0,b.Length);
                        Interlocked.Add(ref Sent,b.Length);
                    }
                }
            }
            catch{ SendText(st,404,"x",null); }
        }

        static void DoZip(NetworkStream st,string query)
        {
            string ids=QueryVal(query,"n");
            string[] names=ids.Split('|');
            List<string> files=new List<string>();
            for(int i=0;i<names.Length;i++)
            {
                if(names[i].Length==0) continue;
                string nm=SafeName(UrlDec(names[i]));
                string fp=Path.Combine(Settings.ShareFolder,nm);
                if(File.Exists(fp)) files.Add(fp);
            }
            if(files.Count==0){ SendText(st,200,"No files",null); return; }
            string zname="GoldShare_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".zip";
            try
            {
                using(MemoryStream ms=new MemoryStream())
                {
                    List<long> centr=new List<long>();
                    List<uint> crcs=new List<uint>();
                    List<byte[]> datas=new List<byte[]>();
                    Crc32 crc=new Crc32();
                    for(int i=0;i<files.Count;i++)
                    {
                        byte[] data=File.ReadAllBytes(files[i]);
                        datas.Add(data);
                        crcs.Add(crc.Compute(data));
                        centr.Add(ms.Position);
                        byte[] nameB=Encoding.UTF8.GetBytes(Path.GetFileName(files[i]));
                        BinaryWriter w=new BinaryWriter(ms);
                        w.Write(0x04034b50); w.Write((short)20); w.Write((short)0x0800);
                        w.Write((short)0); w.Write((short)0); w.Write((short)0);
                        w.Write(crcs[i]); w.Write((int)data.Length); w.Write((int)data.Length);
                        w.Write((short)nameB.Length); w.Write((short)0);
                        w.Write(nameB); w.Write(data);
                    }
                    long cdStart=ms.Position;
                    for(int i=0;i<files.Count;i++)
                    {
                        byte[] nameB=Encoding.UTF8.GetBytes(Path.GetFileName(files[i]));
                        BinaryWriter w=new BinaryWriter(ms);
                        w.Write(0x02014b50); w.Write((short)20); w.Write((short)20);
                        w.Write((short)0x0800); w.Write((short)0); w.Write((short)0); w.Write((short)0);
                        w.Write(crcs[i]); w.Write((int)datas[i].Length); w.Write((int)datas[i].Length);
                        w.Write((short)nameB.Length); w.Write((short)0); w.Write((short)0);
                        w.Write((short)0); w.Write((short)0); w.Write((int)0); w.Write((int)centr[i]);
                        w.Write(nameB);
                    }
                    long cdEnd=ms.Position;
                    BinaryWriter we=new BinaryWriter(ms);
                    we.Write(0x06054b50); we.Write((short)0); we.Write((short)0);
                    we.Write((short)files.Count); we.Write((short)files.Count);
                    we.Write((int)(cdEnd-cdStart)); we.Write((int)cdStart); we.Write((short)0);
                    byte[] z=ms.ToArray();
                    SendHead(st,200,"application/zip",z.Length,"Content-Disposition: attachment; filename=\""+zname+"\"\r\n");
                    st.Write(z,0,z.Length);
                    Interlocked.Add(ref Sent,z.Length);
                    FireLog("ZIP "+files.Count+" file(s) "+FmtBytes(z.Length));
                }
            }
            catch(Exception ex){ FireLog("ZIP ERROR "+ex.Message); }
        }

        static void DoUpload(NetworkStream st,byte[] body)
        {
            if(body==null||body.Length==0){ SendText(st,200,"No data",null); return; }
            try
            {
                string bnd="";
                int nl=IndexOf(body,0,new byte[]{13,10});
                if(nl>2)
                {
                    bnd=Encoding.ASCII.GetString(body,0,nl);
                    if(bnd.StartsWith("--")) bnd=bnd.Substring(2); else bnd="";
                }
                if(bnd.Length==0){ SendText(st,200,"No boundary",null); return; }
                byte[] bB=Encoding.ASCII.GetBytes("--"+bnd);
                int pos=0; int saved=0;
                while(true)
                {
                    int s=IndexOf(body,pos,bB);
                    if(s<0) break;
                    s+=bB.Length;
                    if(s+1<body.Length&&body[s]=='-'&&body[s+1]=='-') break;
                    if(s+1<body.Length&&body[s]==13&&body[s+1]==10) s+=2;
                    int he=IndexOf(body,s,new byte[]{13,10,13,10});
                    if(he<0) break;
                    string head=Encoding.UTF8.GetString(body,s,he-s);
                    int ds=head.IndexOf("filename=\"");
                    if(ds>=0)
                    {
                        int de=head.IndexOf('\"',ds+10);
                        string fname=de>ds?head.Substring(ds+10,de-ds-10):"upload.bin";
                        if(fname.Length==0) fname="camera.jpg";
                        if(Path.GetExtension(fname).Length==0)
                            fname=fname+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".jpg";
                        fname=UniqueName(Settings.ShareFolder,SafeName(fname));
                        int cs=he+4;
                        int ce=IndexOf(body,cs,bB);
                        if(ce<0) ce=body.Length;
                        int end=ce;
                        if(end-2>=cs&&body[end-1]==10&&body[end-2]==13) end-=2;
                        if(end>cs)
                        {
                            using(FileStream fs=File.Create(Path.Combine(Settings.ShareFolder,fname)))
                                fs.Write(body,cs,end-cs);
                            saved++;
                            string szz=FmtBytes(end-cs);
                            FireLog("RECV "+fname+" ("+szz+")");
                            if(OnUpload!=null) OnUpload(fname,szz);
                        }
                        pos=ce>cs?ce:cs;
                    }
                    else pos=s;
                    if(saved>50) break;
                }
                SendText(st,200,"OK",null);
                if(saved==0) FireLog("UPLOAD: no file parts found");
            }
            catch(Exception ex){ FireLog("UPLOAD ERROR "+ex.Message); SendText(st,200,"ERR",null); }
        }

        static void DoNote(NetworkStream st,byte[] body)
        {
            string txt=body!=null?Encoding.UTF8.GetString(body):"";
            txt=txt.Trim();
            if(txt.Length>0)
            {
                if(txt.Length>2000) txt=txt.Substring(0,2000);
                string t=txt;
                FireLog("NOTE: "+t.Replace("\r"," ").Replace("\n"," "));
                try
                {
                    MainForm.Instance.BeginInvoke((MethodInvoker)delegate{
                        try{ Clipboard.SetText(t); }catch{ }
                    });
                }catch{ }
            }
            SendText(st,200,"OK",null);
        }

        static void DoDelete(NetworkStream st,string query)
        {
            string p=QueryVal(query,"p"), nm=QueryVal(query,"n");
            bool ok=false;
            if(Settings.AllowDelete&&Settings.PasswordHash.Length>0&&Settings.Hash(p)==Settings.PasswordHash)
            {
                string fp=Path.Combine(Settings.ShareFolder,SafeName(nm));
                if(File.Exists(fp)){ File.Delete(fp); ok=true; }
            }
            if(ok) FireLog("DELETE "+nm);
            else FireLog("DELETE DENIED "+nm);
            SendText(st,200,ok?"OK":"DENY",null);
        }

        static int IndexOf(byte[] hay,int from,byte[] needle)
        {
            int lim=hay.Length-needle.Length;
            for(int i=from;i<=lim;i++)
            {
                int j=0;
                while(j<needle.Length&&hay[i+j]==needle[j]) j++;
                if(j==needle.Length) return i;
            }
            return -1;
        }

        public static string FmtBytes(double b)
        {
            string[] u=new string[]{"B","KB","MB","GB","TB"};
            int i=0;
            while(b>=1024&&i<4){ b/=1024; i++; }
            return b.ToString(i==0?"0":"0.#",CultureInfo.InvariantCulture)+" "+u[i];
        }
    }

    public class Crc32
    {
        static uint[] tab;
        public Crc32()
        {
            if(tab!=null) return;
            uint[] t=new uint[256];
            for(uint i=0;i<256;i++)
            {
                uint c=i;
                for(int k=0;k<8;k++) c=((c&1)!=0)?(0xEDB88320^(c>>1)):(c>>1);
                t[i]=c;
            }
            tab=t;
        }
        public uint Compute(byte[] d)
        {
            uint c=0xFFFFFFFF;
            for(int i=0;i<d.Length;i++) c=tab[(c^d[i])&0xFF]^(c>>8);
            return c^0xFFFFFFFF;
        }
    }

    // ========================================================================
    //  WEB PAGE
    // ========================================================================
    public class FItem
    {
        public string Name; public long Size; public DateTime Mod; public bool Img;
    }

    public static class Page
    {
        static string Esc(string s)
        {
            return s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;")
                    .Replace("\"","&quot;").Replace("'","&#39;");
        }

        public static string BuildLogin(bool bad)
        {
            Theme th=Theme.Current;
            StringBuilder sb=new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>").Append(Esc(L.T("title"))).Append("</title><style>");
            sb.Append("body{font-family:Segoe UI,Arial,sans-serif;background:").Append(Theme.Hex(th.Bg));
            sb.Append(";color:").Append(Theme.Hex(th.Text)).Append(";display:flex;justify-content:center;align-items:center;height:100vh;margin:0}");
            sb.Append(".box{background:").Append(Theme.Hex(th.FieldBg)).Append(";border:1px solid ");
            sb.Append(Theme.Hex(th.Soft)).Append(";border-radius:14px;padding:34px 40px;box-shadow:0 6px 30px rgba(0,0,0,.15);text-align:center}");
            sb.Append("h1{margin:0 0 18px;font-size:26px;color:").Append(Theme.Hex(th.Dark)).Append("}");
            sb.Append("input{padding:11px 14px;border:1px solid ").Append(Theme.Hex(th.Mid));
            sb.Append(";border-radius:8px;font-size:16px;width:170px;text-align:center;letter-spacing:4px}");
            sb.Append("button{margin-top:14px;padding:11px 34px;border:0;border-radius:8px;background:");
            sb.Append(Theme.Hex(th.Mid)).Append(";color:#fff;font-size:15px;font-weight:bold;cursor:pointer}");
            sb.Append(".err{color:#c0392b;margin:8px 0;font-size:13px}</style></head><body><div class='box'>");
            sb.Append("<h1>").Append(L.T("page_login_h")).Append("</h1>");
            if(bad) sb.Append("<div class='err'>").Append(Esc(L.T("page_bad_pass"))).Append("</div>");
            sb.Append("<form method='POST' action='/login'><input type='password' name='p' autofocus autocomplete='off'><br>");
            sb.Append("<button type='submit'>").Append(Esc(L.T("page_login_btn"))).Append("</button></form></div></body></html>");
            return sb.ToString();
        }

        public static string Build()
        {
            return BuildMain();
        }

        // renamed from Main() — a method named "Main" with no params triggers
        // CS0028 "wrong signature to be an entry point"
        static string BuildMain()
        {
            Theme th=Theme.Current;
            string folder=Settings.ShareFolder;
            List<FItem> items=new List<FItem>();
            try
            {
                DirectoryInfo di=new DirectoryInfo(folder);
                FileInfo[] fs=di.GetFiles();
                for(int i=0;i<fs.Length;i++)
                {
                    string e=fs[i].Extension.ToLower();
                    bool img=(e==".jpg"||e==".jpeg"||e==".png"||e==".gif"||e==".bmp");
                    FItem it=new FItem();
                    it.Name=fs[i].Name; it.Size=fs[i].Length; it.Mod=fs[i].LastWriteTime; it.Img=img;
                    items.Add(it);
                }
            }
            catch{ }

            string freeS="",totS="";
            try
            {
                DriveInfo drv=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder)));
                freeS=Server.FmtBytes(drv.AvailableFreeSpace);
                totS=Server.FmtBytes(drv.TotalSize);
            }catch{ }

            StringBuilder sb=new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>").Append(Esc(L.T("page_title"))).Append("</title><style>");
            sb.Append("*{box-sizing:border-box}");
            sb.Append("body{font-family:Segoe UI,Arial,sans-serif;background:").Append(Theme.Hex(th.Bg));
            sb.Append(";color:").Append(Theme.Hex(th.Text)).Append(";margin:0}");
            sb.Append("header{background:linear-gradient(90deg,").Append(Theme.Hex(th.Dark)).Append(",");
            sb.Append(Theme.Hex(th.Mid)).Append(");color:#fff;padding:16px 22px}");
            sb.Append("header h1{margin:0;font-size:22px}header p{margin:4px 0 0;font-size:12px;opacity:.85}");
            sb.Append(".bar{display:flex;flex-wrap:wrap;gap:8px;padding:10px 14px;align-items:center;background:");
            sb.Append(Theme.Hex(th.Soft)).Append(";position:sticky;top:0;z-index:5}");
            sb.Append("input[type=text]{padding:8px 12px;border:1px solid ").Append(Theme.Hex(th.Mid));
            sb.Append(";border-radius:8px;font-size:14px;flex:1;min-width:120px;background:").Append(Theme.Hex(th.FieldBg)).Append("}");
            sb.Append("button,.bt{padding:8px 14px;border:0;border-radius:8px;background:").Append(Theme.Hex(th.Mid));
            sb.Append(";color:#fff;font-size:13px;font-weight:bold;cursor:pointer;text-decoration:none;display:inline-block}");
            sb.Append(".mini{background:").Append(Theme.Hex(th.Dark)).Append(";padding:6px 10px;font-size:12px}");
            sb.Append("#grid{display:flex;flex-wrap:wrap;gap:10px;padding:14px}");
            sb.Append(".c{background:").Append(Theme.Hex(th.FieldBg)).Append(";border:1px solid ").Append(Theme.Hex(th.Soft));
            sb.Append(";border-radius:10px;padding:8px;width:150px;word-break:break-all}");
            sb.Append(".list .c{width:100%;display:flex;align-items:center;gap:10px}");
            sb.Append(".th{width:100%;height:96px;object-fit:cover;border-radius:6px}");
            sb.Append(".list .th{width:56px;height:44px}");
            sb.Append(".nm{font-size:12px;font-weight:bold;margin-top:5px}");
            sb.Append(".list .nm{flex:1;margin:0}");
            sb.Append(".sz{font-size:11px;opacity:.7}");
            sb.Append(".acts{display:flex;gap:5px;margin-top:6px;align-items:center}");
            sb.Append(".note{margin:6px 14px 14px;background:").Append(Theme.Hex(th.FieldBg));
            sb.Append(";border:1px solid ").Append(Theme.Hex(th.Soft)).Append(";border-radius:10px;padding:10px}");
            sb.Append("textarea{width:100%;height:56px;border:1px solid ").Append(Theme.Hex(th.Mid));
            sb.Append(";border-radius:8px;padding:8px;font-size:13px;font-family:inherit}");
            sb.Append("#prog{position:fixed;top:0;left:0;height:4px;background:").Append(Theme.Hex(th.Light));
            sb.Append(";width:0%;z-index:99;transition:width .2s}");
            sb.Append(".stor{padding:0 14px 6px;font-size:12px;opacity:.75}");
            sb.Append("@media(max-width:600px){.c{width:calc(50% - 10px)}}");
            sb.Append("</style></head><body><div id='prog'></div>");
            sb.Append("<header><h1>").Append(Esc(L.T("page_title"))).Append("</h1><p>").Append(Esc(L.T("page_sub"))).Append("</p></header>");

            sb.Append("<div class='bar'>");
            sb.Append("<input type='text' id='q' placeholder='").Append(Esc(L.T("page_search_ph"))).Append("' oninput='filt()'>");
            sb.Append("<button class='mini' onclick=\"sortB('n')\">").Append(Esc(L.T("sort_name"))).Append("</button>");
            sb.Append("<button class='mini' onclick=\"sortB('s')\">").Append(Esc(L.T("sort_size"))).Append("</button>");
            sb.Append("<button class='mini' onclick=\"sortB('d')\">").Append(Esc(L.T("sort_new"))).Append("</button>");
            sb.Append("<button class='mini' onclick=\"viewT()\">").Append(Esc(L.T("view_grid"))).Append("/").Append(Esc(L.T("view_list"))).Append("</button>");
            sb.Append("<button class='mini' onclick=\"zipSel()\">").Append(Esc(L.T("btn_zip"))).Append("</button>");
            sb.Append("<label class='bt mini' style='position:relative;overflow:hidden'>").Append(Esc(L.T("page_upload_btn")));
            sb.Append("<input type='file' multiple style='position:absolute;left:-9999px' onchange='upChg(this)'></label>");
            sb.Append("<label class='bt mini' style='position:relative;overflow:hidden'>").Append(Esc(L.T("page_cam")));
            sb.Append("<input type='file' accept='image/*' capture='camera' style='position:absolute;left:-9999px' onchange='upChg(this)'></label>");
            sb.Append("</div>");
            sb.Append("<div class='stor'>").Append(Esc(string.Format(L.T("page_storage_f"),freeS,totS))).Append("</div>");

            sb.Append("<div id='grid'>");
            if(items.Count==0) sb.Append("<p style='opacity:.6'>").Append(Esc(L.T("page_no_files"))).Append("</p>");
            for(int i=0;i<items.Count;i++)
            {
                FItem it=items[i];
                string eu=Server.UrlEnc(it.Name);
                sb.Append("<div class='c' data-n=\"").Append(Esc(it.Name)).Append("\" data-s='").Append(it.Size);
                sb.Append("' data-t='").Append(it.Mod.Ticks).Append("'>");
                if(it.Img) sb.Append("<img class='th' src='/t?n=").Append(eu).Append("' alt=''>");
                sb.Append("<div class='nm'>").Append(Esc(it.Name)).Append("</div>");
                sb.Append("<div class='sz'>").Append(Server.FmtBytes(it.Size)).Append("</div>");
                sb.Append("<div class='acts'><input type='checkbox' class='ck' onclick=\"event.stopPropagation()\">");
                sb.Append("<a class='bt mini' href='/f?n=").Append(eu).Append("'>\u2B07</a>");
                if(Settings.AllowDelete&&Settings.PasswordHash.Length>0)
                    sb.Append("<span class='bt mini' style='background:#c0392b' onclick=\"delN(this)\">&times;</span>");
                sb.Append("</div></div>");
            }
            sb.Append("</div>");

            sb.Append("<div class='note'><b>").Append(Esc(L.T("page_note_h"))).Append("</b><br>");
            sb.Append("<textarea id='nt' placeholder='").Append(Esc(L.T("page_note_ph"))).Append("'></textarea>");
            sb.Append("<button style='margin-top:6px' onclick=\"sendN()\">").Append(Esc(L.T("page_note_send"))).Append("</button></div>");

            sb.Append("<script>var busy=0,grid=document.getElementById('grid');\n");
            sb.Append("function filt(){var q=document.getElementById('q').value.toLowerCase();");
            sb.Append("var cs=grid.children;for(var i=0;i<cs.length;i++){var n=cs[i].getAttribute('data-n').toLowerCase();");
            sb.Append("cs[i].style.display=n.indexOf(q)>=0?'':'none';}}\n");
            sb.Append("function sortB(k){var cs=[].slice.call(grid.children);cs.sort(function(a,b){");
            sb.Append("if(k=='n')return a.getAttribute('data-n').localeCompare(b.getAttribute('data-n'));");
            sb.Append("if(k=='s')return (+b.getAttribute('data-s'))-(+a.getAttribute('data-s'));");
            sb.Append("return (+b.getAttribute('data-t'))-(+a.getAttribute('data-t'));});");
            sb.Append("for(var i=0;i<cs.length;i++)grid.appendChild(cs[i]);}\n");
            sb.Append("var gv=1;function viewT(){gv=!gv;grid.className=gv?'':'list';}\n");
            sb.Append("function zipSel(){var cs=grid.querySelectorAll('.ck:checked');var ns=[];");
            sb.Append("for(var i=0;i<cs.length;i++)ns.push(cs[i].parentNode.parentNode.getAttribute('data-n'));");
            sb.Append("if(!ns.length){alert('").Append(Esc(L.T("msg_zip_none").Replace("'","\\'"))).Append("');return;}");
            sb.Append("location='/zip?n='+encodeURIComponent(ns.join('|'));}\n");
            sb.Append("function bar(p){document.getElementById('prog').style.width=p+'%';}\n");
            sb.Append("function upChg(inp){if(!window.FormData){return;}");
            sb.Append("var fs=inp.files;for(var i=0;i<fs.length;i++)upFile(fs[i]);inp.value='';}\n");
            sb.Append("function upFile(f){busy=1;var fd=new FormData();fd.append('f',f,f.name);");
            sb.Append("var x=new XMLHttpRequest();x.open('POST','/up');");
            sb.Append("x.upload.onprogress=function(e){if(e.lengthComputable)bar(e.loaded/e.total*100);};");
            sb.Append("x.onload=function(){busy=0;bar(0);location.reload();};");
            sb.Append("x.onerror=function(){busy=0;bar(0);};x.send(fd);}\n");
            sb.Append("function sendN(){var t=document.getElementById('nt').value;if(!t)return;");
            sb.Append("var x=new XMLHttpRequest();x.open('POST','/note');x.onload=function(){");
            sb.Append("document.getElementById('nt').value='';};x.send(t);}\n");
            sb.Append("function delN(el){var nm=el.parentNode.parentNode.getAttribute('data-n');");
            sb.Append("var p=prompt('").Append(Esc(L.T("page_del_ask").Replace("'","\\'"))).Append("','');");
            sb.Append("if(p==null)return;var x=new XMLHttpRequest();x.open('POST','/del?p='+encodeURIComponent(p)+'&n='+encodeURIComponent(nm));");
            sb.Append("x.onload=function(){if(x.responseText=='OK')location.reload();");
            sb.Append("else alert('").Append(Esc(L.T("page_wrong_pin").Replace("'","\\'"))).Append("');};x.send();}\n");
            sb.Append("window.onfocus=function(){if(!busy&&!document.getElementById('nt').value)location.reload();};\n");
            sb.Append("setInterval(function(){if(busy)return;var e=document.activeElement;");
            sb.Append("if(e&&e.id=='nt')return;location.reload();},45000);\n");
            sb.Append("</script></body></html>");
            return sb.ToString();
        }
    }

    // ========================================================================
    //  MAIN FORM — flicker-free UI
    // ========================================================================
    public class MainForm : Form
    {
        public static MainForm Instance;

        Button btnStart, btnBrowser, btnFolder, btnCopy, btnQR, btnGear;
        TextBox txtPort, txtFolder;
        ListBox lstAddr, lstLog;
        Panel header, statusStrip;
        Label lblTitle, lblSub, lblStatus, lblConns, lblDown, lblUp;
        NotifyIcon tray;
        ContextMenu trayMenu;
        System.Windows.Forms.Timer uiTimer;
        Bitmap headerCache;

        long pLastSent=0, pLastRecv=0;
        int  pLastMs=-1;
        double pSmoothUp=0, pSmoothDown=0;
        string pLastTray="";
        bool exitApp=false;
        int addrRetry=0;

        public MainForm()
        {
            Instance=this;
            Theme.Current=Themes.Get(Settings.Theme);
            L.Set(Settings.Language);

            float fsz=8.25f;
            if(Settings.FontSize==0) fsz=7.5f; else if(Settings.FontSize==2) fsz=10f;
            this.Font=new Font("Segoe UI",fsz);
            if(Environment.OSVersion.Version.Major<6) this.Font=new Font("Tahoma",fsz);

            BuildUi();
            BuildTray();

            this.Text=L.T("title");
            this.StartPosition=FormStartPosition.CenterScreen;
            this.MinimumSize=new Size(660,520);
            this.ClientSize=new Size(760,600);
            this.AllowDrop=true;
            this.DragEnter+=new DragEventHandler(FormDragEnter);
            this.DragDrop+=new DragEventHandler(FormDragDrop);
            this.FormClosing+=new FormClosingEventHandler(FormClose);
            this.Resize+=new EventHandler(FormResized);

            if(Settings.SavePos&&Settings.PosX>=0&&Settings.PosY>=0)
            { this.StartPosition=FormStartPosition.Manual; this.Location=new Point(Settings.PosX,Settings.PosY); }

            txtPort.Text=Settings.Port.ToString();
            txtFolder.Text=Settings.ShareFolder;
            lblStatus.Text=string.Format(L.T("status_stop"));
            AddLogLine(L.T("msg_ready"));

            Server.OnLog+=new LogEvent(ServerLog);
            Server.OnUpload+=new UploadEvent(UploadDone);

            InitUiPatch();
            RefreshAddresses();

            if(Settings.AutoStart) StartServer();
        }

        // ---------------- flicker-free core ----------------
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp=base.CreateParams;
                cp.ExStyle|=0x02000000;              // WS_EX_COMPOSITED
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CacheHeader();
        }

        static void SetTxt(Control c,string s){ if(c.Text!=s) c.Text=s; }

        static void EnableDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance|
                    System.Reflection.BindingFlags.NonPublic)
                    .SetValue(c,true,null);
            }catch{ }
        }

        string FmtSpeed(double bytesPerSec,ref double smooth)
        {
            smooth=smooth*0.6+bytesPerSec*0.4;
            if(smooth<0) smooth=0;
            double v=smooth/1024.0;
            string u="KB/s";
            if(v>=1024.0){ v/=1024.0; u="MB/s"; }
            return string.Format("{0,8:0.#} {1,-4}",v,u);   // FIXED width
        }

        void SetTrayText(string s)
        {
            if(s==pLastTray) return;
            pLastTray=s;
            try{ tray.Text=s.Length>63?s.Substring(0,63):s; }catch{ }
        }

        void InitUiPatch()
        {
            EnableDoubleBuffer(lstLog);
            EnableDoubleBuffer(lstAddr);

            uiTimer=new System.Windows.Forms.Timer();
            uiTimer.Interval=1000;                              // ONE timer, 1 s
            uiTimer.Tick+=new EventHandler(UiTimerTick);
            uiTimer.Start();
        }

        void UiTimerTick(object sender,EventArgs e)
        {
            long sent=Interlocked.Read(ref Server.Sent);
            long recv=Interlocked.Read(ref Server.Recv);
            int now=Environment.TickCount;
            int dt=(pLastMs<0)?1000:(now-pLastMs);
            if(dt<1) dt=1;
            double up=(sent-pLastSent)*1000.0/dt;
            double down=(recv-pLastRecv)*1000.0/dt;
            pLastSent=sent; pLastRecv=recv; pLastMs=now;

            if(!Visible||WindowState==FormWindowState.Minimized)
            {
                SetTrayText(Server.Running
                    ? string.Format(L.T("status_run"),Settings.Port)
                    : L.T("status_stop"));
                return;
            }

            SetTxt(lblDown,"D"+FmtSpeed(down,ref pSmoothDown));
            SetTxt(lblUp,  "U"+FmtSpeed(up,  ref pSmoothUp));
            SetTxt(lblConns,string.Format(L.T("conns_fmt"),Server.Active));

            if(!Server.Running&&lstAddr.Items.Count==0&&++addrRetry>=10)
            { addrRetry=0; RefreshAddresses(); }
        }

        void AddLogLine(string line)
        {
            try
            {
                lstLog.BeginUpdate();
                lstLog.Items.Add(DateTime.Now.ToString("HH:mm:ss")+"  "+line);
                while(lstLog.Items.Count>200) lstLog.Items.RemoveAt(0);
                lstLog.TopIndex=lstLog.Items.Count-1;
                lstLog.EndUpdate();
            }catch{ }
        }

        void ServerLog(string s)
        {
            try{ BeginInvoke((MethodInvoker)delegate{ AddLogLine(s); }); }catch{ }
        }

        void UploadDone(string name,string size)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate{
                    if(Settings.Beep){ try{ Console.Beep(880,180); }catch{ } }
                    tray.ShowBalloonTip(1200,"GoldShare",
                        string.Format(L.T("msg_upload_done"),name)+" ("+size+")",ToolTipIcon.Info);
                });
            }catch{ }
        }

        // ---------------- UI construction ----------------
        void BuildUi()
        {
            Theme th=Theme.Current;

            header=new Panel();
            header.Dock=DockStyle.Top;
            header.Height=64;
            header.Paint+=new PaintEventHandler(HeaderPaint);
            this.Controls.Add(header);

            lblTitle=new Label();
            lblTitle.AutoSize=true;
            lblTitle.Font=new Font(this.Font.FontFamily,this.Font.Size+6,FontStyle.Bold);
            lblTitle.ForeColor=Color.White;
            lblTitle.BackColor=Color.Transparent;
            lblTitle.Location=new Point(14,8);
            lblTitle.Text="GoldShare";
            header.Controls.Add(lblTitle);

            lblSub=new Label();
            lblSub.AutoSize=true;
            lblSub.ForeColor=Color.White;
            lblSub.BackColor=Color.Transparent;
            lblSub.Location=new Point(16,40);
            lblSub.Text=L.T("page_sub");
            header.Controls.Add(lblSub);

            statusStrip=new Panel();
            statusStrip.Dock=DockStyle.Bottom;
            statusStrip.Height=30;
            statusStrip.BackColor=th.Soft;
            this.Controls.Add(statusStrip);

            lblStatus=new Label();
            lblStatus.AutoSize=false; lblStatus.Width=170; lblStatus.TextAlign=ContentAlignment.MiddleLeft;
            lblStatus.ForeColor=th.Dark; lblStatus.Font=new Font(this.Font,FontStyle.Bold);
            lblStatus.Location=new Point(8,7);
            statusStrip.Controls.Add(lblStatus);

            lblConns=new Label();
            lblConns.AutoSize=false; lblConns.Width=90; lblConns.TextAlign=ContentAlignment.MiddleLeft;
            lblConns.ForeColor=th.Text;
            lblConns.Location=new Point(184,7);
            statusStrip.Controls.Add(lblConns);

            lblDown=new Label();
            lblDown.AutoSize=false; lblDown.Width=180; lblDown.TextAlign=ContentAlignment.MiddleRight;
            lblDown.ForeColor=th.Text; lblDown.Font=new Font(FontFamily.GenericMonospace,this.Font.Size);
            lblDown.Location=new Point(300,7);
            lblDown.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            statusStrip.Controls.Add(lblDown);

            lblUp=new Label();
            lblUp.AutoSize=false; lblUp.Width=180; lblUp.TextAlign=ContentAlignment.MiddleRight;
            lblUp.ForeColor=th.Text; lblUp.Font=new Font(FontFamily.GenericMonospace,this.Font.Size);
            lblUp.Location=new Point(486,7);
            lblUp.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            statusStrip.Controls.Add(lblUp);

            statusStrip.Resize+=new EventHandler(StatusResize);

            btnStart=BigButton(L.T("start"),th.Mid,new EventHandler(OnStart));
            btnBrowser=BigButton(L.T("open_browser"),th.Dark,new EventHandler(OnBrowser));
            btnFolder=BigButton(L.T("open_folder"),th.Dark,new EventHandler(OnFolder));
            btnCopy=BigButton(L.T("copy_url"),th.Dark,new EventHandler(OnCopy));
            btnQR=BigButton(L.T("qr_scan"),th.Dark,new EventHandler(OnQR));
            btnGear=BigButton(L.T("gear"),th.Dark,new EventHandler(OnGear));

            Label lPort=new Label(); lPort.Text=L.T("port"); lPort.AutoSize=true; lPort.Location=new Point(14,6);
            txtPort=new TextBox(); txtPort.Width=64; txtPort.Location=new Point(14,24);
            Label lFold=new Label(); lFold.Text=L.T("folder"); lFold.AutoSize=true; lFold.Location=new Point(88,6);
            txtFolder=new TextBox(); txtFolder.Width=130; txtFolder.Location=new Point(88,24); txtFolder.ReadOnly=true;

            Label lAddr=new Label();
            lAddr.Text=L.T("addresses");
            lAddr.AutoSize=true;
            lAddr.Location=new Point(228,6);

            lstAddr=new ListBox();
            lstAddr.Location=new Point(228,24);
            lstAddr.Size=new Size(230,64);
            lstAddr.SelectionMode=SelectionMode.One;
            lstAddr.HorizontalScrollbar=true;

            lstLog=new ListBox();
            lstLog.Location=new Point(12,360);
            lstLog.Size=new Size(730,200);
            lstLog.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
            lstLog.Font=new Font(FontFamily.GenericMonospace,this.Font.Size-0.75f);

            Panel mid=new Panel();
            mid.Dock=DockStyle.Fill;
            mid.BackColor=th.Bg;
            mid.Controls.Add(btnStart);
            mid.Controls.Add(btnBrowser);
            mid.Controls.Add(btnFolder);
            mid.Controls.Add(btnCopy);
            mid.Controls.Add(btnQR);
            mid.Controls.Add(btnGear);
            mid.Controls.Add(lPort);
            mid.Controls.Add(txtPort);
            mid.Controls.Add(lFold);
            mid.Controls.Add(txtFolder);
            mid.Controls.Add(lAddr);
            mid.Controls.Add(lstAddr);
            mid.Controls.Add(lstLog);
            mid.Resize+=new EventHandler(MidResized);
            this.Controls.Add(mid);
            mid.BringToFront();

            ApplyMidLayout(mid);
        }

        Button BigButton(string text,Color bg,EventHandler onClick)
        {
            Button b=new Button();
            b.Text=text;
            b.FlatStyle=FlatStyle.Flat;
            b.FlatAppearance.BorderSize=0;
            b.BackColor=bg;
            b.ForeColor=Color.White;
            b.Font=new Font(this.Font,FontStyle.Bold);
            b.Size=new Size(200,34);
            b.Location=new Point(12,150);
            b.Click+=onClick;
            return b;
        }

        void ApplyMidLayout(Panel mid)
        {
            int y=150;
            btnStart.Location=new Point(12,y); y+=40;
            btnBrowser.Location=new Point(12,y); y+=30;
            btnFolder.Location=new Point(12,y); y+=30;
            btnCopy.Location=new Point(12,y); y+=30;
            btnQR.Location=new Point(12,y); y+=30;
            btnGear.Location=new Point(12,y);
            lstLog.Location=new Point(12,360);
            lstLog.Size=new Size(mid.Width-24,Math.Max(60,mid.Height-375));
            lstAddr.Size=new Size(Math.Max(120,mid.Width-250),64);
        }
        void MidResized(object s,EventArgs e){ ApplyMidLayout((Panel)s); }

        void StatusResize(object s,EventArgs e)
        {
            lblDown.Location=new Point(statusStrip.Width-366,7);
            lblUp.Location=new Point(statusStrip.Width-186,7);
        }

        void CacheHeader()
        {
            try
            {
                if(headerCache!=null) headerCache.Dispose();
                int w=Math.Max(1,header.Width);
                headerCache=new Bitmap(w,header.Height,PixelFormat.Format32bppPArgb);
                using(Graphics g=Graphics.FromImage(headerCache))
                using(LinearGradientBrush br=new LinearGradientBrush(
                         new Rectangle(0,0,w,header.Height),
                         Theme.Current.Dark,Theme.Current.Mid,LinearGradientMode.Horizontal))
                    g.FillRectangle(br,0,0,w,header.Height);
                header.Invalidate();
            }catch{ }
        }
        void HeaderPaint(object s,PaintEventArgs e)
        {
            if(headerCache!=null) e.Graphics.DrawImageUnscaled(headerCache,0,0);
            else e.Graphics.Clear(Theme.Current.Dark);
        }
        void FormResized(object s,EventArgs e){ CacheHeader(); }

        void BuildTray()
        {
            trayMenu=new ContextMenu();
            trayMenu.MenuItems.Add(L.T("tray_open"),new EventHandler(TrayOpen));
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add(L.T("tray_exit"),new EventHandler(TrayExit));

            tray=new NotifyIcon();
            tray.Icon=MakeIcon();
            tray.ContextMenu=trayMenu;
            tray.DoubleClick+=new EventHandler(TrayOpen);
            tray.Visible=true;
        }

        Icon MakeIcon()
        {
            Bitmap b=new Bitmap(16,16,PixelFormat.Format32bppArgb);
            using(Graphics g=Graphics.FromImage(b))
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(SolidBrush br=new SolidBrush(Theme.Current.Mid)) g.FillEllipse(br,1,1,14,14);
                using(Font f=new Font("Arial",9,FontStyle.Bold))
                using(SolidBrush w=new SolidBrush(Color.White))
                    g.DrawString("G",f,w,-1,-1);
            }
            return Icon.FromHandle(b.GetHicon());
        }

        // ---------------- actions ----------------
        void ApplyTheme()
        {
            Theme th=Theme.Current;
            statusStrip.BackColor=th.Soft;
            lblStatus.ForeColor=th.Dark;
            lblConns.ForeColor=th.Text; lblDown.ForeColor=th.Text; lblUp.ForeColor=th.Text;
            btnStart.BackColor=th.Mid;
            btnBrowser.BackColor=th.Dark; btnFolder.BackColor=th.Dark;
            btnCopy.BackColor=th.Dark; btnQR.BackColor=th.Dark; btnGear.BackColor=th.Dark;
            CacheHeader();
            this.Invalidate(true);
        }

        void OnStart(object s,EventArgs e)
        {
            if(Server.Running)
            {
                Server.Stop();
                pSmoothUp=0; pSmoothDown=0;
                SetTxt(lblStatus,string.Format(L.T("status_stop")));
                btnStart.Text=L.T("start");
                txtPort.ReadOnly=false;
                return;
            }
            int port;
            if(!int.TryParse(txtPort.Text.Trim(),out port)||port<1||port>65535)
            { MessageBox.Show(L.T("msg_bad_port"),this.Text); return; }
            Settings.Port=port;
            string err=Server.Start(port,Settings.ShareFolder);
            if(err!=null)
            {
                MessageBox.Show(string.Format(L.T("msg_start_fail"),err),this.Text);
                return;
            }
            SetTxt(lblStatus,string.Format(L.T("status_run"),port));
            btnStart.Text=L.T("stop");
            txtPort.ReadOnly=true;
            RefreshAddresses();
            AddLogLine(string.Format(L.T("msg_started"),port));
        }
        void StartServer()
        {
            if(Server.Running) return;
            txtPort.Text=Settings.Port.ToString();
            OnStart(null,null);
        }

        string SelectedUrl()
        {
            if(lstAddr.SelectedIndex<0)
            {
                if(lstAddr.Items.Count>0) lstAddr.SelectedIndex=0;
                else return "";
            }
            return lstAddr.Text;
        }

        void OnBrowser(object s,EventArgs e)
        {
            if(!Server.Running){ MessageBox.Show(L.T("msg_start_first"),this.Text); return; }
            try{ Process.Start(SelectedUrl()); }catch{ }
        }
        void OnFolder(object s,EventArgs e)
        {
            try{ Process.Start("explorer.exe",Settings.ShareFolder); }catch{ }
        }
        void OnCopy(object s,EventArgs e)
        {
            if(!Server.Running){ MessageBox.Show(L.T("msg_start_first"),this.Text); return; }
            string u=SelectedUrl();
            if(u.Length==0) return;
            try{ Clipboard.SetText(u); AddLogLine(string.Format(L.T("msg_copied"),u)); }catch{ }
        }
        void OnQR(object s,EventArgs e)
        {
            string u=SelectedUrl();
            if(u.Length==0&&!Server.Running) u="http://"+Environment.MachineName+":"+Settings.Port+"/";
            ShowQr(u);
        }
        void OnGear(object s,EventArgs e){ ShowSettings(); }

        void RefreshAddresses()
        {
            try
            {
                lstAddr.BeginUpdate();
                lstAddr.Items.Clear();
                string host=Dns.GetHostName();
                IPAddress[] ads=Dns.GetHostAddresses(host);
                List<string> v4=new List<string>();
                for(int i=0;i<ads.Length;i++)
                    if(ads[i].AddressFamily==AddressFamily.InterNetwork) v4.Add(ads[i].ToString());
                v4.Add("127.0.0.1");
                for(int i=0;i<v4.Count;i++)
                    lstAddr.Items.Add("http://"+v4[i]+":"+Settings.Port+"/");
                if(lstAddr.Items.Count>0) lstAddr.SelectedIndex=0;
                lstAddr.EndUpdate();
            }catch{ }
        }

        void ShowQr(string url)
        {
            Form f=new Form();
            f.Text=L.T("qr_scan");
            f.FormBorderStyle=FormBorderStyle.FixedDialog;
            f.MaximizeBox=false; f.MinimizeBox=false;
            f.StartPosition=FormStartPosition.CenterParent;
            f.ClientSize=new Size(340,430);
            f.BackColor=Theme.Current.Bg;

            Bitmap bmp=Qr.Make(url,300);
            PictureBox pb=new PictureBox();
            pb.Image=bmp; pb.SizeMode=PictureBoxSizeMode.Zoom;
            pb.Location=new Point(20,40); pb.Size=new Size(300,300);
            pb.BackColor=Color.White;

            Label lbl=new Label();
            lbl.Text=url;
            lbl.TextAlign=ContentAlignment.MiddleCenter;
            lbl.Dock=DockStyle.Top; lbl.Height=34;

            Button sv=new Button();
            sv.Text=L.T("save_png");
            sv.FlatStyle=FlatStyle.Flat; sv.BackColor=Theme.Current.Mid; sv.ForeColor=Color.White;
            sv.Location=new Point(90,352); sv.Size=new Size(160,32);
            sv.Click+=delegate{
                try
                {
                    SaveFileDialog sd=new SaveFileDialog();
                    sd.Filter="PNG|*.png"; sd.FileName="GoldShareQR.png";
                    if(sd.ShowDialog(f)==DialogResult.OK)
                    {
                        bmp.Save(sd.FileName,ImageFormat.Png);
                        MessageBox.Show(string.Format(L.T("msg_qr_saved"),sd.FileName));
                    }
                }catch{ }
            };

            f.Controls.Add(pb); f.Controls.Add(sv); f.Controls.Add(lbl);
            f.ShowDialog(this);
            bmp.Dispose();
        }

        void ShowSettings()
        {
            Form f=new Form();
            f.Text=L.T("set_title");
            f.FormBorderStyle=FormBorderStyle.FixedDialog;
            f.MaximizeBox=false; f.MinimizeBox=false;
            f.StartPosition=FormStartPosition.CenterParent;
            f.ClientSize=new Size(440,440);
            f.BackColor=Theme.Current.Bg;

            int y=14;
            Label l1=new Label(); l1.Text=L.T("set_port"); l1.Location=new Point(14,y); l1.AutoSize=true;
            TextBox t1=new TextBox(); t1.Text=Settings.Port.ToString(); t1.Location=new Point(230,y-3); t1.Width=70;
            y+=32;
            Label l2=new Label(); l2.Text=L.T("set_uplim"); l2.Location=new Point(14,y); l2.AutoSize=true;
            TextBox t2=new TextBox(); t2.Text=Settings.UploadMB.ToString(); t2.Location=new Point(230,y-3); t2.Width=70;
            y+=32;
            Label l3=new Label(); l3.Text=L.T("set_speed"); l3.Location=new Point(14,y); l3.AutoSize=true;
            TextBox t3=new TextBox(); t3.Text=Settings.SpeedKB.ToString(); t3.Location=new Point(230,y-3); t3.Width=70;
            y+=32;
            Label l4=new Label(); l4.Text=L.T("set_pass"); l4.Location=new Point(14,y); l4.AutoSize=true;
            TextBox t4=new TextBox(); t4.Location=new Point(230,y-3); t4.Width=110;
            y+=32;
            Label l5=new Label(); l5.Text=L.T("language"); l5.Location=new Point(14,y); l5.AutoSize=true;
            ComboBox c1=new ComboBox(); c1.DropDownStyle=ComboBoxStyle.DropDownList;
            for(int i=0;i<L.Langs.Count;i++) c1.Items.Add(L.Langs[i]);
            c1.SelectedItem=L.Lang; c1.Location=new Point(230,y-5); c1.Width=110;
            y+=32;
            Label l6=new Label(); l6.Text=L.T("theme_lbl"); l6.Location=new Point(14,y); l6.AutoSize=true;
            ComboBox c2=new ComboBox(); c2.DropDownStyle=ComboBoxStyle.DropDownList;
            for(int i=0;i<Themes.All.Length;i++) c2.Items.Add(Themes.All[i].Code);
            c2.SelectedItem=Settings.Theme; c2.Location=new Point(230,y-5); c2.Width=110;
            y+=32;
            Label l7=new Label(); l7.Text=L.T("set_font"); l7.Location=new Point(14,y); l7.AutoSize=true;
            ComboBox c3=new ComboBox(); c3.DropDownStyle=ComboBoxStyle.DropDownList;
            c3.Items.Add(L.T("font_s")); c3.Items.Add(L.T("font_m")); c3.Items.Add(L.T("font_l"));
            c3.SelectedIndex=Settings.FontSize; c3.Location=new Point(230,y-5); c3.Width=110;
            y+=34;

            CheckBox k1=new CheckBox(); k1.Text=L.T("set_autostart"); k1.Checked=Settings.AutoStart; k1.Location=new Point(14,y); k1.AutoSize=true; y+=24;
            CheckBox k2=new CheckBox(); k2.Text=L.T("set_allowdel"); k2.Checked=Settings.AllowDelete; k2.Location=new Point(14,y); k2.AutoSize=true; y+=24;
            CheckBox k3=new CheckBox(); k3.Text=L.T("set_tray"); k3.Checked=Settings.Tray; k3.Location=new Point(14,y); k3.AutoSize=true; y+=24;
            CheckBox k4=new CheckBox(); k4.Text=L.T("set_savepos"); k4.Checked=Settings.SavePos; k4.Location=new Point(14,y); k4.AutoSize=true; y+=24;
            CheckBox k5=new CheckBox(); k5.Text=L.T("set_beep"); k5.Checked=Settings.Beep; k5.Location=new Point(14,y); k5.AutoSize=true; y+=24;
            CheckBox k6=new CheckBox(); k6.Text=L.T("set_log"); k6.Checked=Settings.LogFile; k6.Location=new Point(14,y); k6.AutoSize=true; y+=34;

            Button ok=new Button(); ok.Text=L.T("save");
            ok.FlatStyle=FlatStyle.Flat; ok.BackColor=Theme.Current.Mid; ok.ForeColor=Color.White;
            ok.Location=new Point(190,y); ok.Size=new Size(110,30);
            Button ca=new Button(); ca.Text=L.T("cancel");
            ca.FlatStyle=FlatStyle.Flat;
            ca.Location=new Point(310,y); ca.Size=new Size(100,30);

            ok.Click+=delegate{
                int p,u2,sp;
                if(!int.TryParse(t1.Text.Trim(),out p)||p<1||p>65535){ MessageBox.Show(L.T("msg_bad_port")); return; }
                int.TryParse(t2.Text.Trim(),out u2); if(u2<1)u2=512; if(u2>4096)u2=4096;
                int.TryParse(t3.Text.Trim(),out sp); if(sp<0)sp=0;
                bool portChanged=(p!=Settings.Port);
                Settings.Port=p; Settings.UploadMB=u2; Settings.SpeedKB=sp;
                Settings.PasswordHash=t4.Text.Trim().Length>0?Settings.Hash(t4.Text.Trim()):"";
                Settings.Language=(string)c1.SelectedItem; L.Set(Settings.Language);
                Settings.Theme=(string)c2.SelectedItem; Theme.Current=Themes.Get(Settings.Theme);
                Settings.FontSize=c3.SelectedIndex;
                Settings.AutoStart=k1.Checked; Settings.AllowDelete=k2.Checked;
                Settings.Tray=k3.Checked; Settings.SavePos=k4.Checked;
                Settings.Beep=k5.Checked; Settings.LogFile=k6.Checked;
                Settings.Save();
                ApplyTheme();
                ReLocalize();
                txtPort.Text=Settings.Port.ToString();
                if(portChanged) MessageBox.Show(L.T("msg_port_restart"));
                f.Close();
            };
            ca.Click+=delegate{ f.Close(); };

            f.Controls.Add(l1); f.Controls.Add(t1); f.Controls.Add(l2); f.Controls.Add(t2);
            f.Controls.Add(l3); f.Controls.Add(t3); f.Controls.Add(l4); f.Controls.Add(t4);
            f.Controls.Add(l5); f.Controls.Add(c1); f.Controls.Add(l6); f.Controls.Add(c2);
            f.Controls.Add(l7); f.Controls.Add(c3);
            f.Controls.Add(k1); f.Controls.Add(k2); f.Controls.Add(k3);
            f.Controls.Add(k4); f.Controls.Add(k5); f.Controls.Add(k6);
            f.Controls.Add(ok); f.Controls.Add(ca);
            f.ShowDialog(this);
        }

        void ReLocalize()
        {
            this.Text=L.T("title");
            lblSub.Text=L.T("page_sub");
            btnStart.Text=Server.Running?L.T("stop"):L.T("start");
            btnBrowser.Text=L.T("open_browser");
            btnFolder.Text=L.T("open_folder");
            btnCopy.Text=L.T("copy_url");
            btnQR.Text=L.T("qr_scan");
            btnGear.Text=L.T("gear");
            SetTxt(lblStatus,Server.Running?string.Format(L.T("status_run"),Settings.Port):string.Format(L.T("status_stop")));
            trayMenu.MenuItems[0].Text=L.T("tray_open");
            trayMenu.MenuItems[2].Text=L.T("tray_exit");
            AddLogLine(L.T("msg_ready"));
        }

        // ---------------- drag & drop ----------------
        void FormDragEnter(object s,DragEventArgs e)
        {
            if(e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect=DragDropEffects.Copy;
        }
        void FormDragDrop(object s,DragEventArgs e)
        {
            try
            {
                string[] fl=(string[])e.Data.GetData(DataFormats.FileDrop);
                int n=0;
                for(int i=0;i<fl.Length;i++)
                {
                    if(File.Exists(fl[i]))
                    {
                        string nm=Server.UniqueName(Settings.ShareFolder,Path.GetFileName(fl[i]));
                        File.Copy(fl[i],Path.Combine(Settings.ShareFolder,nm),false);
                        n++;
                    }
                }
                if(n>0) AddLogLine("DRAG&DROP: "+n+" file(s) added");
            }catch{ }
        }

        // ---------------- close / tray ----------------
        void TrayOpen(object s,EventArgs e)
        {
            Show(); WindowState=FormWindowState.Normal; Activate();
        }
        void TrayExit(object s,EventArgs e)
        {
            exitApp=true;
            tray.Visible=false;
            Close();
        }
        void FormClose(object s,FormClosingEventArgs e)
        {
            if(!exitApp&&Settings.Tray&&e.CloseReason==CloseReason.UserClosing)
            {
                e.Cancel=true;
                Hide();
                tray.ShowBalloonTip(1000,"GoldShare",L.T("msg_tray_min"),ToolTipIcon.Info);
                return;
            }
            if(WindowState!=FormWindowState.Minimized&&Settings.SavePos)
            { Settings.PosX=Location.X; Settings.PosY=Location.Y; }
            Settings.Save();
            Server.Stop();
            if(uiTimer!=null) uiTimer.Stop();
            if(headerCache!=null) headerCache.Dispose();
            try{ tray.Visible=false; tray.Dispose(); }catch{ }
        }
    }

    // ========================================================================
    //  ENTRY
    // ========================================================================
    class Program
    {
        [STAThread]
        static void Main()
        {
            bool ok;
            Mutex mx=new Mutex(true,"GoldShare_SingleInstance",out ok);
            if(!ok)
            {
                MessageBox.Show(L.T("msg_already"),"GoldShare",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }
            Settings.Load();
            L.Set(Settings.Language);
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
            try{ mx.ReleaseMutex(); }catch{ }
        }
    }
}