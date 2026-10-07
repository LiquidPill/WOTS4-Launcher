/*
 * Way of the Samurai 4 Launcher
 * Copyright (C) 2026 WOTS4-Launcher contributors
 * SPDX-License-Identifier: GPL-3.0-only
 * See the top-level LICENSE file for license terms.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Wots4Launcher {
    class TextConfig {
        public string Text; public Encoding Encoding;
        public TextConfig(string path) {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding = new UTF8Encoding(false);
            if (bytes.Length >= 3 && bytes[0]==239 && bytes[1]==187 && bytes[2]==191) Encoding = new UTF8Encoding(true);
            else if (bytes.Length >= 2 && bytes[0]==255 && bytes[1]==254) Encoding = Encoding.Unicode;
            else if (bytes.Length >= 2 && bytes[0]==254 && bytes[1]==255) Encoding = Encoding.BigEndianUnicode;
            else Encoding = Encoding.Default;
            using (var reader = new StreamReader(path, Encoding, true)) Text = reader.ReadToEnd();
        }
        public string Get(string section, string key, string fallback) {
            bool active = section == null;
            foreach (string line in Regex.Split(Text, "\r?\n")) {
                var s = Regex.Match(line, @"^\s*\[([^\]]+)\]");
                if (s.Success) { active = section == null || s.Groups[1].Value.Equals(section, StringComparison.OrdinalIgnoreCase); continue; }
                var m = Regex.Match(line, @"^\s*"+Regex.Escape(key)+@"\s*=\s*([^;#]*)(?:[;#].*)?$", RegexOptions.IgnoreCase);
                if (active && m.Success) return m.Groups[1].Value.Trim();
            }
            return fallback;
        }
        public void Set(string section, string key, string value) {
            string nl = Text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = Regex.Split(Text, "\r?\n").ToList();
            bool active = section == null, foundSection = active;
            int insert = lines.Count;
            for (int i=0; i<lines.Count; i++) {
                var s = Regex.Match(lines[i], @"^\s*\[([^\]]+)\]");
                if (s.Success) {
                    if (active && section != null) { insert=i; active=false; }
                    if (section != null && s.Groups[1].Value.Equals(section, StringComparison.OrdinalIgnoreCase)) { active=true; foundSection=true; insert=i+1; }
                    continue;
                }
                if (!active) continue;
                insert=i+1;
                var m = Regex.Match(lines[i], @"^(\s*"+Regex.Escape(key)+@"\s*=\s*)([^;#]*)(.*)$", RegexOptions.IgnoreCase);
                if (m.Success) { lines[i]=m.Groups[1].Value+value+(m.Groups[3].Value.Length>0 ? " "+m.Groups[3].Value : ""); Text=String.Join(nl,lines); return; }
            }
            if (!foundSection) { lines.Add("["+section+"]"); insert=lines.Count; }
            lines.Insert(insert, key+"="+value);
            Text=String.Join(nl,lines);
        }
        public void Write(string path) { File.WriteAllText(path, Text, Encoding); }
    }

    class Settings {
        public bool Borderless=true, FocusFix=true, Dof=false, Bloom=false;
        public int Width=2560, Height=1440, Fov=85, Anisotropy=16, Fps=0;
    }

    static class LauncherPreferences {
        const string KeyPath=@"Software\WOTS4-Launcher";
        static object Get(string name,object fallback) { using(var key=Registry.CurrentUser.OpenSubKey(KeyPath,false)) return key==null?fallback:key.GetValue(name,fallback); }
        static void Put(string name,object value) { using(var key=Registry.CurrentUser.CreateSubKey(KeyPath)) key.SetValue(name,value); }
        static int Int(string name,int fallback) { try { return Convert.ToInt32(Get(name,fallback)); } catch { return fallback; } }
        public static bool HasUiSettings { get { return Int("HasUiSettings",0)!=0; } }
        public static string GameFolder { get { return Convert.ToString(Get("GameFolder", "")); } }
        public static bool HasUiSettingsFor(string folder) {
            if(!HasUiSettings||String.IsNullOrEmpty(folder))return false;
            try { return String.Equals(Path.GetFullPath(GameFolder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        public static string UnpackedExecutable { get { return Convert.ToString(Get("UnpackedExecutable", "")); } }
        public static bool Borderless { get { return Int("Borderless",1)!=0; } }
        public static int Width { get { return Int("Width",2560); } }
        public static int Height { get { return Int("Height",1440); } }
        public static int Fov { get { return Int("Fov",85); } }
        public static int Anisotropy { get { return Int("Anisotropy",16); } }
        public static bool Dof { get { return Int("Dof",0)!=0; } }
        public static bool Bloom { get { return Int("Bloom",0)!=0; } }
        public static bool FocusFix { get { return Int("FocusFix",1)!=0; } }
        public static bool ShowLoadingOverlay { get { return Int("ShowLoadingOverlay",1)!=0; } set { Put("ShowLoadingOverlay",value?1:0); } }
        public static void SaveUi(string gameFolder,string unpacked,bool borderless,int width,int height,int fov,int anisotropy,bool dof,bool bloom,bool focusFix) {
            Put("GameFolder",gameFolder??""); Put("UnpackedExecutable",unpacked??""); Put("Borderless",borderless?1:0); Put("Width",width); Put("Height",height); Put("Fov",fov); Put("Anisotropy",anisotropy); Put("Dof",dof?1:0); Put("Bloom",bloom?1:0); Put("FocusFix",focusFix?1:0); Put("HasUiSettings",1);
        }
    }

    static class Engine {
        public const string DllHash="265888C31CA78DFFA290C39CB7E50BFB02762590E41927906E46FB32F01497FA";
        public static readonly string[] Files={"S4-SETTINGS.ini","dxvk.conf","d3d9.dll","WayOfTheSamurai4.exe","Common\\Character\\Action\\MotionDatabase.l"};
        public static readonly string Game60Sha="DC07F234CC03A7FA4FBCC51230981A5B319378279C13D5ECBF98939A0A875ED5";
        public static readonly string GameExeSha="DC7F896B0C76BC4EEAFF031638CA6521613A6B444FBC27765AEEDDF2504BA597";
        public static readonly string StockMotionSha="50829EDF1A6D934CBC7FB758259A421DF464DFFE877A7CCF7CD8FD13943EE7A1";
        public static string Hash(string path) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""); }
        public static void Validate(string folder) {
            if (!File.Exists(Path.Combine(folder,"WayOfTheSamurai4.exe"))) throw new Exception("Select the folder containing WayOfTheSamurai4.exe.");
            if (!File.Exists(Path.Combine(folder,"S4-SETTINGS.ini"))) throw new Exception("S4-SETTINGS.ini is missing. Start the game once to create its settings, then close it.");
        }
        public static bool Running() { return Process.GetProcessesByName("WayOfTheSamurai4").Length>0; }
        static int Number(string value,int fallback) { int n; return Int32.TryParse(value,out n) ? n : fallback; }
        public static Settings Read(string folder) {
            Validate(folder); var ini=new TextConfig(Path.Combine(folder,Files[0]));
            var s=new Settings();
            s.Borderless=ini.Get("Graphics","FullScreen","0")=="1";
            s.Width=Number(ini.Get("Graphics","Width","2560"),2560); s.Height=Number(ini.Get("Graphics","Height","1440"),1440);
            s.Fov=Number(ini.Get("Graphics","FOV","75"),75);
            s.Dof=ini.Get("Graphics","EnableDOF","1")!="0"; s.Bloom=ini.Get("Graphics","EnableHDR","1")!="0";
            if(File.Exists(Path.Combine(folder,Files[1]))) {
                var cfg=new TextConfig(Path.Combine(folder,Files[1]));
                s.FocusFix=!cfg.Get(null,"d3d9.deviceLossOnFocusLoss","False").Equals("True",StringComparison.OrdinalIgnoreCase);
                s.Anisotropy=Number(cfg.Get(null,"d3d9.samplerAnisotropy","-1"),-1);
            }
            s.Fps=FpsForInstalled(folder);
            return s;
        }
        static int FpsForInstalled(string folder) {
            string root=Path.Combine(folder,"WOTS4-Launcher-Backups","FPSVariants");
            if(!Directory.Exists(root)) return 0;
            string exe=Path.Combine(folder,"WayOfTheSamurai4.exe"), motion=Path.Combine(folder,"Common\\Character\\Action\\MotionDatabase.l");
            if(File.Exists(exe)&&File.Exists(motion)&&Hash(exe)==Game60Sha&&Hash(motion)=="FF38D25A61ADB7E9B8834B3C5F1912E7A46705F0EFC8E852CF9E8CA498E75632") return 60;
            string stockExe=Path.Combine(root,"WayOfTheSamurai4.exe.30"), stockMotion=Path.Combine(root,"MotionDatabase.l.30");
            if(File.Exists(stockExe)&&File.Exists(exe)&&File.Exists(stockMotion)&&File.Exists(motion)&&Hash(exe)==Hash(stockExe)&&Hash(motion)==Hash(stockMotion)) return 30;
            { string e=Path.Combine(root,"WayOfTheSamurai4.exe.60"), m=Path.Combine(root,"MotionDatabase.l.60"); if(File.Exists(e)&&File.Exists(m)&&File.Exists(exe)&&File.Exists(motion)&&Hash(exe)==Hash(e)&&Hash(motion)==Hash(m)) return 60; }
            return 0;
        }
        public static string BuildFpsProfiles(string folder,string unpackedExe,string payload,string patcher,string license) {
            Validate(folder); if(Running()) throw new Exception("Save and close the game before building FPS profiles.");
            if(!File.Exists(unpackedExe)||Hash(unpackedExe)!=GameExeSha) throw new Exception("This is not the supported unpacked Steam executable. Expected SHA-256: "+GameExeSha);
            string motion=Path.Combine(folder,"Common\\Character\\Action\\MotionDatabase.l");
            string motionBackup=motion+".orig_bak";
            string stock=File.Exists(motionBackup)&&Hash(motionBackup)==StockMotionSha?motionBackup:(File.Exists(motion)&&Hash(motion)==StockMotionSha?motion:null);
            if(stock==null) throw new Exception("The stock MotionDatabase.l was not found. Steam verification or an original backup is needed before building FPS profiles.");
            if(!File.Exists(patcher)||!File.Exists(license)) throw new Exception("The FPS patch source or GPL license is missing from the launcher package.");
            string build=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WOTS4-Launcher","FPSBuild",Guid.NewGuid().ToString("N"));
            string action=Path.Combine(build,"Common\\Character\\Action"); Directory.CreateDirectory(action);
            File.Copy(unpackedExe,Path.Combine(build,"WayOfTheSamurai4.exe")); File.Copy(stock,Path.Combine(action,"MotionDatabase.l"));
            // Windows PowerShell installations may not auto-load its utility module.
            // Keep the upstream patcher's hash checks working with an explicit SHA-256 shim.
            string hashShim="function Get-FileHash { param([Parameter(Position=0)][string]$Path,[string]$Algorithm='SHA256') $sha=[Security.Cryptography.SHA256]::Create(); try { $stream=[IO.File]::OpenRead($Path); try { $hex=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $stream.Dispose() }; [pscustomobject]@{Hash=$hex} } finally { $sha.Dispose() } }\r\n";
            File.WriteAllText(Path.Combine(build,"patch.ps1"),hashShim+File.ReadAllText(patcher),new UTF8Encoding(true));
            using(var proc=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(build,"patch.ps1")+"\"") { WorkingDirectory=build,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true })) {
                proc.StandardInput.Close(); string stdout=proc.StandardOutput.ReadToEnd(), stderr=proc.StandardError.ReadToEnd(); proc.WaitForExit();
                if(proc.ExitCode!=0) throw new Exception("The FPS patcher failed safely:\r\n"+stdout+"\r\n"+stderr);
            }
            string root=Path.Combine(folder,"WOTS4-Launcher-Backups","FPSVariants"); Directory.CreateDirectory(root);
            File.Copy(unpackedExe,Path.Combine(root,"WayOfTheSamurai4.exe.30"),true); File.Copy(stock,Path.Combine(root,"MotionDatabase.l.30"),true);
            string e60=Path.Combine(build,"WayOfTheSamurai4.exe.fps60"), m60=Path.Combine(action,"MotionDatabase.l.fps60");
            if(!File.Exists(e60)||!File.Exists(m60)) throw new Exception("The verified 60 FPS patch build is incomplete.");
            File.Copy(e60,Path.Combine(root,"WayOfTheSamurai4.exe.60"),true); File.Copy(m60,Path.Combine(root,"MotionDatabase.l.60"),true);
            if(Hash(Path.Combine(root,"WayOfTheSamurai4.exe.60"))!="DC07F234CC03A7FA4FBCC51230981A5B319378279C13D5ECBF98939A0A875ED5"||Hash(Path.Combine(root,"MotionDatabase.l.60"))!="FF38D25A61ADB7E9B8834B3C5F1912E7A46705F0EFC8E852CF9E8CA498E75632") throw new Exception("Generated 60 FPS files did not match the verified upstream build.");
            Directory.Delete(build,true);
            return root;
        }
        static string Snapshot(string folder) {
            string root=Path.Combine(folder,"WOTS4-Launcher-Backups"); Directory.CreateDirectory(root);
            string id=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"); string dir=Path.Combine(root,id); Directory.CreateDirectory(dir);
            var existing=new List<string>();
            foreach(string name in Files) if(File.Exists(Path.Combine(folder,name))) {
                string destination=Path.Combine(dir,name); Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(Path.Combine(folder,name),destination); existing.Add(name);
            }
            File.WriteAllLines(Path.Combine(dir,"existing.txt"),existing);
            return dir;
        }
        static void RestoreSnapshot(string folder,string dir) {
            var existing=File.ReadAllLines(Path.Combine(dir,"existing.txt"));
            if(existing.Any(n=>!Files.Contains(n))) throw new Exception("Invalid backup manifest.");
            foreach(string name in existing) if(!File.Exists(Path.Combine(dir,name))) throw new Exception("Backup is incomplete: "+name);
            foreach(string name in Files) {
                string target=Path.Combine(folder,name);
                if(existing.Contains(name)) { Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(Path.Combine(dir,name),target,true); }
                else if(File.Exists(target)) File.Delete(target);
            }
        }
        public static string Apply(string folder, Settings settings, string payload) {
            Validate(folder);
            if(Running()) throw new Exception("Save and close the game before changing settings.");
            if(Hash(payload)!=DllHash) throw new Exception("The bundled DXVK file failed its integrity check. Extract the complete launcher ZIP again.");
            byte[] exe=File.ReadAllBytes(Path.Combine(folder,"WayOfTheSamurai4.exe"));
            if(exe.Length<64) throw new Exception("Invalid game executable.");
            int offset=BitConverter.ToInt32(exe,60);
            if(offset<0 || offset>exe.Length-6 || BitConverter.ToUInt16(exe,offset+4)!=0x14c) throw new Exception("This launcher supports the 32-bit Windows game.");
            string dll=Path.Combine(folder,Files[2]);
            if(File.Exists(dll) && Hash(dll)!=DllHash) throw new Exception("A different d3d9.dll is already installed. Remove or relocate that graphics mod before using this launcher; it will not be overwritten.");
            var ini=new TextConfig(Path.Combine(folder,Files[0]));
            ini.Set("Graphics","FullScreen",settings.Borderless?"1":"0");
            ini.Set("Graphics","Width",settings.Width.ToString()); ini.Set("Graphics","Height",settings.Height.ToString());
            ini.Set("Graphics","FOV",settings.Fov.ToString()); ini.Set("Graphics","EnableDOF",settings.Dof?"1":"0"); ini.Set("Graphics","EnableHDR",settings.Bloom?"1":"0");
            string conf=Path.Combine(folder,Files[1]);
            if(!File.Exists(conf)) { /* Create only after the snapshot below. */ }
            string backup=Snapshot(folder);
            try {
                if(!File.Exists(conf)) File.WriteAllText(conf,"# Way of the Samurai 4 launcher - DXVK settings\r\n",Encoding.ASCII);
                var cfg=new TextConfig(conf);
                cfg.Set(null,"dxvk.allowFse","False");
                cfg.Set(null,"d3d9.deviceLossOnFocusLoss",settings.FocusFix?"False":"True");
                cfg.Set(null,"d3d9.samplerAnisotropy",settings.Anisotropy.ToString());
                if(settings.Fps>0) {
                    string variants=Path.Combine(folder,"WOTS4-Launcher-Backups","FPSVariants"); string rate=settings.Fps.ToString();
                    string sourceExe=Path.Combine(variants,"WayOfTheSamurai4.exe."+rate), sourceMotion=Path.Combine(variants,"MotionDatabase.l."+rate);
                    if(!File.Exists(sourceExe)||!File.Exists(sourceMotion)) throw new Exception("Build the FPS profile before selecting it.");
                    File.Copy(sourceExe,Path.Combine(folder,"WayOfTheSamurai4.exe"),true);
                    File.Copy(sourceMotion,Path.Combine(folder,"Common\\Character\\Action\\MotionDatabase.l"),true);
                }
                File.Copy(payload,dll,true); ini.Write(Path.Combine(folder,Files[0])); cfg.Write(conf);
                File.WriteAllText(Path.Combine(folder,"WOTS4-Launcher-Backups","latest.txt"),Path.GetFileName(backup));
            } catch { RestoreSnapshot(folder,backup); throw; }
            return backup;
        }
        public static void Restore(string folder) {
            Validate(folder); if(Running()) throw new Exception("Close the game before restoring settings.");
            string root=Path.Combine(folder,"WOTS4-Launcher-Backups"), pointer=Path.Combine(root,"latest.txt");
            if(!File.Exists(pointer)) throw new Exception("No settings backup has been made by this launcher yet.");
            string id=File.ReadAllText(pointer).Trim();
            if(!Regex.IsMatch(id,@"^\d{8}-\d{6}-\d{7}$")) throw new Exception("Invalid backup reference.");
            RestoreSnapshot(folder,Path.Combine(root,id));
        }
        public static string Discover() {
            string here=AppDomain.CurrentDomain.BaseDirectory;
            var candidates=new List<string>{here,Directory.GetParent(here.TrimEnd(Path.DirectorySeparatorChar)).FullName};
            var steam=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string;
            if(String.IsNullOrEmpty(steam)) steam=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");
            var libraries=new List<string>{steam}; string vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");
            if(File.Exists(vdf)) foreach(Match m in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s*\"([^\"]+)\"")) libraries.Add(m.Groups[1].Value.Replace(@"\\",@"\"));
            foreach(string library in libraries) {
                string common=Path.Combine(library,"steamapps","common");
                candidates.Add(Path.Combine(common,"Way of the Samurai 4"));
                string manifest=Path.Combine(library,"steamapps","appmanifest_312780.acf");
                if(File.Exists(manifest)) { var m=Regex.Match(File.ReadAllText(manifest),"\"installdir\"\\s*\"([^\"]+)\""); if(m.Success) candidates.Add(Path.Combine(common,m.Groups[1].Value)); }
            }
            return candidates.FirstOrDefault(p=>File.Exists(Path.Combine(p,"WayOfTheSamurai4.exe"))) ?? "";
        }
    }

    class LoadingOverlayForm : Form {
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

        readonly Process game;
        readonly Timer timer;
        DateTime blackSince = DateTime.MinValue;
        DateTime nonBlackSince = DateTime.MinValue;
        int dotCount;
        bool shown;
        Bitmap captureBitmap;
        byte[] captureRowBuffer;
        int captureWidth, captureHeight;
        static readonly Color KeyColor = Color.Magenta;

        public LoadingOverlayForm(Process process) : this(process,false) { }
        public LoadingOverlayForm(Process process,bool preview) {
            game=process;
            FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual;
            BackColor=preview?Color.Black:KeyColor; if(!preview)TransparencyKey=KeyColor; DoubleBuffered=true;
            // Full-client capture is intentionally limited to 1 Hz to avoid adding load-time overhead.
            timer=new Timer { Interval=1000 }; timer.Tick+=Tick; if(!preview)timer.Start();
            if(preview) { shown=true; Bounds=new Rectangle(0,0,1280,720); }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get { var cp=base.CreateParams; cp.ExStyle|=0x00000020|0x00000080|0x08000000; return cp; } // transparent, tool window, no-activate
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Stop(); timer.Dispose(); if(captureBitmap!=null)captureBitmap.Dispose(); game.Dispose(); base.OnFormClosed(e); }

        void Tick(object sender, EventArgs e) {
            try {
                if(game.HasExited) { Close(); return; }
                game.Refresh(); IntPtr hwnd=game.MainWindowHandle;
                IntPtr foreground=GetForegroundWindow(); uint foregroundId; GetWindowThreadProcessId(foreground,out foregroundId);
                bool gameFocused=(int)foregroundId==game.Id, overlayFocused=shown&&foreground==Handle;
                if(hwnd==IntPtr.Zero || (!gameFocused&&!overlayFocused)) { blackSince=DateTime.MinValue; nonBlackSince=DateTime.MinValue; SetOverlayVisible(false); return; }
                NativeRect rect; if(!GetClientRect(hwnd,out rect)) { SetOverlayVisible(false); return; }
                int width=rect.Right-rect.Left, height=rect.Bottom-rect.Top;
                if(width<320||height<200) { SetOverlayVisible(false); return; }
                NativePoint origin=new NativePoint { X=0,Y=0 }; if(!ClientToScreen(hwnd,ref origin)) { SetOverlayVisible(false); return; }
                Bounds=new Rectangle(origin.X,origin.Y,width,height);
                bool black=LooksCompletelyBlack(origin.X,origin.Y,width,height,shown);
                if(black) {
                    nonBlackSince=DateTime.MinValue;
                    if(blackSince==DateTime.MinValue) blackSince=DateTime.UtcNow;
                    if((DateTime.UtcNow-blackSince).TotalMilliseconds>=3000) SetOverlayVisible(true);
                } else {
                    blackSince=DateTime.MinValue;
                    if(shown) {
                        if(nonBlackSince==DateTime.MinValue) nonBlackSince=DateTime.UtcNow;
                        else if((DateTime.UtcNow-nonBlackSince).TotalMilliseconds>=2000) { nonBlackSince=DateTime.MinValue; SetOverlayVisible(false); }
                    }
                }
            } catch(InvalidOperationException) { if(!IsDisposed) Close(); }
              catch { blackSince=DateTime.MinValue; if(shown) { if(nonBlackSince==DateTime.MinValue) nonBlackSince=DateTime.UtcNow; else if((DateTime.UtcNow-nonBlackSince).TotalMilliseconds>=2000) { nonBlackSince=DateTime.MinValue; SetOverlayVisible(false); } } }
        }
        bool LooksCompletelyBlack(int x,int y,int width,int height,bool excludeOverlayPanel) {
            // Require every captured game pixel outside our own message panel to be RGB(0,0,0).
            if(captureBitmap==null||captureWidth!=width||captureHeight!=height) {
                if(captureBitmap!=null) captureBitmap.Dispose();
                captureBitmap=new Bitmap(width,height,PixelFormat.Format24bppRgb);
                captureWidth=width; captureHeight=height; captureRowBuffer=new byte[captureBitmap.Width*3+((4-(captureBitmap.Width*3)%4)%4)];
            }
            using(var g=Graphics.FromImage(captureBitmap)) g.CopyFromScreen(x,y,0,0,new Size(width,height),CopyPixelOperation.SourceCopy);
            var data=captureBitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
            try {
                int stride=data.Stride;
                int panelWidth=Math.Min(660,Math.Max(280,width-40)), panelHeight=172;
                int panelLeft=(width-panelWidth)/2, panelTop=(height-panelHeight)/2;
                for(int row=0;row<height;row++) {
                    Marshal.Copy(IntPtr.Add(data.Scan0,row*stride),captureRowBuffer,0,captureRowBuffer.Length);
                    for(int col=0;col<width;col++) {
                        if(excludeOverlayPanel&&col>=panelLeft&&col<panelLeft+panelWidth&&row>=panelTop&&row<panelTop+panelHeight) continue;
                        int pixel=col*3;
                        if(captureRowBuffer[pixel]!=0||captureRowBuffer[pixel+1]!=0||captureRowBuffer[pixel+2]!=0) return false;
                    }
                }
                return true;
            } finally { captureBitmap.UnlockBits(data); }
        }
        void SetOverlayVisible(bool value) {
            if(value==shown) { if(value) Invalidate(); return; } shown=value;
            if(value) { dotCount=0; Show(); timer.Start(); }
            else if(Visible) Hide();
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); if(!shown) return;
            dotCount=(dotCount+1)%4;
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int panelWidth=Math.Min(660,Math.Max(280,ClientSize.Width-40)), panelHeight=172;
            Rectangle panel=new Rectangle((ClientSize.Width-panelWidth)/2,(ClientSize.Height-panelHeight)/2,panelWidth,panelHeight);
            using(var path=ClippedRectangle(panel,12)) using(var brush=new SolidBrush(Color.FromArgb(249,20,22,23))) e.Graphics.FillPath(brush,path);
            using(var path=ClippedRectangle(panel,12)) using(var frame=new Pen(Color.FromArgb(205,167,139,91),1)) e.Graphics.DrawPath(frame,path);
            Rectangle inner=Rectangle.Inflate(panel,-6,-6);
            using(var path=ClippedRectangle(inner,9)) using(var frame=new Pen(Color.FromArgb(95,167,139,91),1)) e.Graphics.DrawPath(frame,path);
            using(var red=new SolidBrush(Color.FromArgb(190,135,37,39))) e.Graphics.FillRectangle(red,panel.Left+13,panel.Top+13,4,panel.Height-26);
            using(var smallFont=new Font("Segoe UI",9,FontStyle.Bold,GraphicsUnit.Point)) using(var smallBrush=new SolidBrush(Color.FromArgb(224,192,164,113)))
                e.Graphics.DrawString("WAY OF THE SAMURAI 4",smallFont,smallBrush,panel.Left+28,panel.Top+16);
            string title="LOADING"+new string('.',dotCount);
            using(var titleFont=new Font("Georgia",24,FontStyle.Bold,GraphicsUnit.Point)) using(var titleBrush=new SolidBrush(Color.FromArgb(249,241,222))) {
                var size=e.Graphics.MeasureString(title,titleFont); e.Graphics.DrawString(title,titleFont,titleBrush,panel.Left+(panel.Width-size.Width)/2,panel.Top+38);
            }
            using(var warningFont=new Font("Segoe UI",9,FontStyle.Bold,GraphicsUnit.Point)) using(var warningBrush=new SolidBrush(Color.FromArgb(231,194,110,87))) {
                const string info="DO NOT ALT+TAB DURING LOADING";
                using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center })
                    e.Graphics.DrawString(info,warningFont,warningBrush,new RectangleF(panel.Left+22,panel.Top+105,panel.Width-44,22),format);
            }
            using(var infoFont=new Font("Segoe UI",9,FontStyle.Regular,GraphicsUnit.Point)) using(var infoBrush=new SolidBrush(Color.FromArgb(212,217,209,194))) {
                const string info="Switching programs during this screen may crash the game.";
                using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center })
                    e.Graphics.DrawString(info,infoFont,infoBrush,new RectangleF(panel.Left+22,panel.Top+130,panel.Width-44,22),format);
            }
        }
        static System.Drawing.Drawing2D.GraphicsPath ClippedRectangle(Rectangle r,int cut) {
            var p=new System.Drawing.Drawing2D.GraphicsPath();
            p.AddPolygon(new[] { new Point(r.Left+cut,r.Top),new Point(r.Right-cut,r.Top),new Point(r.Right,r.Top+cut),
                new Point(r.Right,r.Bottom-cut),new Point(r.Right-cut,r.Bottom),new Point(r.Left+cut,r.Bottom),
                new Point(r.Left,r.Bottom-cut),new Point(r.Left,r.Top+cut) }); return p;
        }
    }

    class LauncherForm : Form {
        [DllImport("dwmapi.dll", PreserveSig=true)] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        TextBox folder=new TextBox(); ComboBox mode=new ComboBox(), filtering=new ComboBox(); Button fpsAction; string unpackedPath="";
        NumericUpDown width=new NumericUpDown(), height=new NumericUpDown(), fov=new NumericUpDown();
        CheckBox focus=new CheckBox(), dof=new CheckBox(), bloom=new CheckBox(), loadingOverlay=new CheckBox(); Label status=new Label();
        ToolTip tips=new ToolTip(); bool loaded; Image headerImage;
        readonly Color ink=Color.FromArgb(239,231,213), muted=Color.FromArgb(173,162,143), accent=Color.FromArgb(139,42,37);
        readonly Color page=Color.FromArgb(25,26,28), surface=Color.FromArgb(34,32,30), input=Color.FromArgb(44,41,38), gold=Color.FromArgb(190,158,101);
        public LauncherForm(string initial) {
            Text="Way of the Samurai 4 • Launcher"; ClientSize=new Size(760,820); MinimumSize=new Size(776,859);
            Font=new Font("Segoe UI",10); BackColor=page; ForeColor=ink;
            AutoScaleMode=AutoScaleMode.Dpi; StartPosition=FormStartPosition.CenterScreen;
            var root=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=5, Padding=new Padding(24), BackColor=page };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,120)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,85)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,92)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,70)); Controls.Add(root);
            var header=new Panel { Dock=DockStyle.Fill };
            header.BackColor=surface;
            string bannerPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","WOTS4-Launcher-Header.png");
            if(File.Exists(bannerPath)) using(var source=Image.FromFile(bannerPath)) headerImage=new Bitmap(source);
            header.Paint+=delegate(object sender,PaintEventArgs e) {
                Rectangle dest=header.ClientRectangle;
                if(headerImage!=null&&dest.Width>0&&dest.Height>0) {
                    int sourceHeight=Math.Min(headerImage.Height,(int)Math.Round(headerImage.Width*(double)dest.Height/dest.Width));
                    int sourceTop=(headerImage.Height-sourceHeight)/2;
                    e.Graphics.DrawImage(headerImage,dest,new Rectangle(0,sourceTop,headerImage.Width,sourceHeight),GraphicsUnit.Pixel);
                    using(var shade=new System.Drawing.Drawing2D.LinearGradientBrush(dest,Color.FromArgb(155,12,13,16),Color.FromArgb(75,12,13,16),System.Drawing.Drawing2D.LinearGradientMode.Horizontal)) e.Graphics.FillRectangle(shade,dest);
                }
                using(var titleFont=new Font("Georgia",20,FontStyle.Bold)) using(var titleBrush=new SolidBrush(gold)) e.Graphics.DrawString("WAY OF THE SAMURAI 4",titleFont,titleBrush,6,13);
                using(var subFont=new Font("Segoe UI",9,FontStyle.Bold)) using(var subBrush=new SolidBrush(muted)) e.Graphics.DrawString("DISPLAY & GRAPHICS",subFont,subBrush,8,59);
                using(var jpFont=new Font("Yu Gothic UI",24,FontStyle.Bold)) using(var jpBrush=new SolidBrush(accent)) e.Graphics.DrawString("侍道４",jpFont,jpBrush,header.ClientSize.Width-140,35);
                using(var pen=new Pen(accent,2)) e.Graphics.DrawLine(pen,0,header.ClientSize.Height-3,header.ClientSize.Width,header.ClientSize.Height-3);
                using(var pen=new Pen(gold,1)) e.Graphics.DrawLine(pen,0,header.ClientSize.Height-1,header.ClientSize.Width,header.ClientSize.Height-1);
            };
            root.Controls.Add(header,0,0);
            var pathPanel=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=2 };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
            pathPanel.Controls.Add(new Label { Text="GAME FOLDER",AutoSize=true,Font=new Font(Font,FontStyle.Bold) },0,0);
            folder.Dock=DockStyle.Fill; folder.ReadOnly=true; pathPanel.Controls.Add(folder,0,1);
            var browse=Button("Browse…",delegate { using(var d=new FolderBrowserDialog { Description="Select the folder containing WayOfTheSamurai4.exe" }) { if(Directory.Exists(folder.Text)) d.SelectedPath=folder.Text; if(d.ShowDialog()==DialogResult.OK) LoadFolder(d.SelectedPath); } }); pathPanel.Controls.Add(browse,1,1); root.Controls.Add(pathPanel,0,1);
            var grid=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=10,Padding=new Padding(0,4,0,0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,205)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int i=0;i<9;i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute,i==2?48:36)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            mode.DropDownStyle=ComboBoxStyle.DropDownList; mode.Items.AddRange(new object[]{"Borderless fullscreen","Windowed"}); mode.Dock=DockStyle.Fill; Row(grid,0,"Display mode",mode);
            var resolution=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false }; Configure(width,640,16384,2560); Configure(height,360,16384,1440); width.Width=90; height.Width=90;
            resolution.Controls.Add(width); resolution.Controls.Add(new Label { Text="×",AutoSize=true,Padding=new Padding(0,5,0,0) }); resolution.Controls.Add(height);
            resolution.Controls.Add(Button("Use desktop",delegate { var b=Screen.FromControl(this).Bounds; width.Value=b.Width; height.Value=b.Height; })); Row(grid,1,"Resolution",resolution);
            var displayNote=new Label { Text="Borderless uses DXVK with exclusive fullscreen disabled. Match your desktop resolution to avoid a resolution switch.",Dock=DockStyle.Fill,ForeColor=muted,Font=new Font("Segoe UI",9) }; grid.Controls.Add(displayNote,1,2);
            Configure(fov,50,120,85); fov.Width=90; Row(grid,3,"Field of view",fov); tips.SetToolTip(fov,"Gameplay camera field of view. Does not change cutscenes.");
            filtering.DropDownStyle=ComboBoxStyle.DropDownList; filtering.Items.AddRange(new object[]{"Game default","1× (no anisotropy)","2×","4×","8×","16×"}); filtering.Dock=DockStyle.Fill; Row(grid,4,"Texture filtering",filtering);
            dof.Text="Enable depth of field"; dof.AutoSize=true; Row(grid,5,"Depth of field",dof);
            bloom.Text="Enable bloom / glare"; bloom.AutoSize=true; Row(grid,6,"Bloom",bloom); tips.SetToolTip(bloom,"Controls the game's bloom/glare effect, independently of Windows or monitor HDR.");
            focus.Text="Prevent Alt+Tab device loss (recommended)"; focus.AutoSize=true; Row(grid,7,"Alt+Tab fix",focus); tips.SetToolTip(focus,"Keeps DXVK from reporting a lost DirectX 9 device on focus changes. Turning this off keeps DXVK installed but restores focus-loss signaling.");
            loadingOverlay.Text="Detect full-black loading screens and show a warning"; loadingOverlay.AutoSize=true; loadingOverlay.Checked=LauncherPreferences.ShowLoadingOverlay;
            loadingOverlay.CheckedChanged+=delegate { LauncherPreferences.ShowLoadingOverlay=loadingOverlay.Checked; };
            Row(grid,8,"Loading-screen warning",loadingOverlay); tips.SetToolTip(loadingOverlay,"When enabled, check the game image in memory once per second. Show the warning only if the whole screen stays black for at least 3 seconds. Turn this off to skip both detection and the overlay.");
            root.Controls.Add(grid,0,2);
            var actions=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty };
            actions.RowStyles.Add(new RowStyle(SizeType.Percent,50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            var mainActions=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty };
            var launch=Button("Apply Settings + Launch",delegate { Save(true); }); launch.BackColor=accent; launch.ForeColor=Color.White; launch.FlatStyle=FlatStyle.Flat; launch.Width=210;
            mainActions.Controls.Add(launch); mainActions.Controls.Add(Button("Apply Settings",delegate { Save(false); }));
            var otherActions=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty };
            fpsAction=Button("Enable 60 FPS",delegate { ToggleFps(); }); fpsAction.Width=175; tips.SetToolTip(fpsAction,"Switch between the tested 60 FPS patch and the stock 30 FPS game. A backup is made automatically."); otherActions.Controls.Add(fpsAction);
            otherActions.Controls.Add(Button("Restore Previous Backup",delegate { Run(delegate { Engine.Restore(folder.Text); LoadFolder(folder.Text); status.Text="Previous game files and settings restored."; }); }));
            otherActions.Controls.Add(Button("Reload Game Settings",delegate { LoadFolder(folder.Text); }));
            actions.Controls.Add(mainActions,0,0); actions.Controls.Add(otherActions,0,1); root.Controls.Add(actions,0,3);
            status.Dock=DockStyle.Fill; status.ForeColor=muted; status.Text="Select your game folder to begin."; root.Controls.Add(status,0,4);
            ApplyTheme(this);
            header.BackColor=surface; launch.BackColor=accent; launch.ForeColor=Color.FromArgb(255,246,232); launch.FlatAppearance.BorderColor=gold; launch.FlatAppearance.MouseOverBackColor=Color.FromArgb(164,54,45);
            fpsAction.BackColor=accent; fpsAction.ForeColor=Color.FromArgb(255,246,232); fpsAction.FlatAppearance.BorderColor=gold; fpsAction.FlatAppearance.MouseOverBackColor=Color.FromArgb(164,54,45);
            unpackedPath=LauncherPreferences.UnpackedExecutable;
            HookRememberedSettings();
            string remembered=String.IsNullOrEmpty(initial)?LauncherPreferences.GameFolder:initial;
            LoadFolder(String.IsNullOrEmpty(remembered)?Engine.Discover():remembered);
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            try { int dark=1; if(DwmSetWindowAttribute(Handle,20,ref dark,4)!=0) DwmSetWindowAttribute(Handle,19,ref dark,4); } catch { }
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { if(headerImage!=null)headerImage.Dispose(); base.OnFormClosed(e); }
        void ApplyTheme(Control parent) {
            foreach(Control c in parent.Controls) {
                if(!(c is Label)) c.ForeColor=ink;
                if(c is TableLayoutPanel||c is FlowLayoutPanel) c.BackColor=page;
                if(c is TextBox) { c.BackColor=input; ((TextBox)c).BorderStyle=BorderStyle.FixedSingle; }
                else if(c is ComboBox) {
                    c.BackColor=input; var combo=(ComboBox)c; combo.FlatStyle=FlatStyle.Flat; combo.DrawMode=DrawMode.OwnerDrawFixed; combo.ItemHeight=22;
                    combo.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                        if(e.Index<0) return;
                        bool selected=(e.State&DrawItemState.Selected)==DrawItemState.Selected;
                        Color fill=selected?Color.FromArgb(91,62,43):input;
                        using(var brush=new SolidBrush(fill)) e.Graphics.FillRectangle(brush,e.Bounds);
                        TextRenderer.DrawText(e.Graphics,combo.Items[e.Index].ToString(),combo.Font,e.Bounds,ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
                    };
                }
                else if(c is NumericUpDown) { c.BackColor=input; ((NumericUpDown)c).BorderStyle=BorderStyle.FixedSingle; }
                else if(c is CheckBox) { c.BackColor=Color.Transparent; ((CheckBox)c).FlatStyle=FlatStyle.Standard; ((CheckBox)c).UseVisualStyleBackColor=true; }
                else if(c is Button) {
                    var b=(Button)c; b.BackColor=Color.FromArgb(51,46,40); b.ForeColor=ink; b.FlatStyle=FlatStyle.Flat; b.UseVisualStyleBackColor=false;
                    b.FlatAppearance.BorderColor=Color.FromArgb(111,97,75); b.FlatAppearance.MouseOverBackColor=Color.FromArgb(69,57,45);
                } else if(c is Panel) c.BackColor=surface;
                ApplyTheme(c);
            }
        }
        Button Button(string text,EventHandler click) { var b=new Button { Text=text,AutoSize=true,Height=32,Padding=new Padding(7,2,7,2),Margin=new Padding(4),UseVisualStyleBackColor=true }; b.Click+=click; return b; }
        void Row(TableLayoutPanel p,int row,string title,Control control) { p.Controls.Add(new Label { Text=title,AutoSize=true,Padding=new Padding(0,5,0,0) },0,row); p.Controls.Add(control,1,row); }
        static void Configure(NumericUpDown n,int min,int max,int value) { n.Minimum=min;n.Maximum=max;n.Value=value; }
        static void SetNumber(NumericUpDown n,int v) { n.Value=Math.Max(n.Minimum,Math.Min(n.Maximum,v)); }
        void Run(Action action) { try { action(); } catch(Exception e) { status.Text=e.Message; MessageBox.Show(this,e.Message,"Launcher",MessageBoxButtons.OK,MessageBoxIcon.Warning); } }
        void HookRememberedSettings() {
            mode.SelectedIndexChanged+=delegate { RememberUi(); }; filtering.SelectedIndexChanged+=delegate { RememberUi(); };
            width.ValueChanged+=delegate { RememberUi(); }; height.ValueChanged+=delegate { RememberUi(); }; fov.ValueChanged+=delegate { RememberUi(); };
            dof.CheckedChanged+=delegate { RememberUi(); }; bloom.CheckedChanged+=delegate { RememberUi(); }; focus.CheckedChanged+=delegate { RememberUi(); };
        }
        void RememberUi() {
            if(!loaded||mode.SelectedIndex<0||filtering.SelectedIndex<0)return;
            int[] levels={-1,1,2,4,8,16};
            LauncherPreferences.SaveUi(folder.Text,unpackedPath,mode.SelectedIndex==0,(int)width.Value,(int)height.Value,(int)fov.Value,levels[filtering.SelectedIndex],dof.Checked,bloom.Checked,focus.Checked);
        }
        void LoadFolder(string path) {
            loaded=false; folder.Text=path;
            if(String.IsNullOrEmpty(path)) { RefreshFpsButton(); return; }
            Run(delegate {
                var s=Engine.Read(path); mode.SelectedIndex=s.Borderless?0:1; SetNumber(width,s.Width);SetNumber(height,s.Height);SetNumber(fov,s.Fov); dof.Checked=s.Dof;bloom.Checked=s.Bloom;focus.Checked=s.FocusFix;
                int[] values={-1,1,2,4,8,16}; int index=Array.IndexOf(values,s.Anisotropy); filtering.SelectedIndex=index<0?0:index;
                if(LauncherPreferences.HasUiSettingsFor(path)) {
                    mode.SelectedIndex=LauncherPreferences.Borderless?0:1; SetNumber(width,LauncherPreferences.Width); SetNumber(height,LauncherPreferences.Height); SetNumber(fov,LauncherPreferences.Fov);
                    dof.Checked=LauncherPreferences.Dof; bloom.Checked=LauncherPreferences.Bloom; focus.Checked=LauncherPreferences.FocusFix;
                    index=Array.IndexOf(values,LauncherPreferences.Anisotropy); filtering.SelectedIndex=index<0?0:index;
                }
                loaded=true; LauncherPreferences.SaveUi(path,unpackedPath,mode.SelectedIndex==0,(int)width.Value,(int)height.Value,(int)fov.Value,values[filtering.SelectedIndex],dof.Checked,bloom.Checked,focus.Checked); RefreshFpsButton();
                status.Text="Settings remembered. Use Apply Settings to write them to the game.";
            });
        }
        Settings CurrentSettings() { return new Settings { Borderless=mode.SelectedIndex==0,Width=(int)width.Value,Height=(int)height.Value,Fov=(int)fov.Value,Dof=dof.Checked,Bloom=bloom.Checked,FocusFix=focus.Checked,Anisotropy=new int[]{-1,1,2,4,8,16}[filtering.SelectedIndex] }; }
        void Save(bool launch) { Run(delegate {
            if(!loaded) throw new Exception("Select a valid game folder first.");
            var s=CurrentSettings(); s.Fps=0;
            string backup=Engine.Apply(folder.Text,s,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","d3d9.dll"));
            RememberUi();
            status.Text="Settings applied. Your previous game files are backed up.";
            if(launch) {
                var game=Process.Start(new ProcessStartInfo(Path.Combine(folder.Text,"WayOfTheSamurai4.exe")) { WorkingDirectory=folder.Text,UseShellExecute=true });
                if(game!=null) { if(loadingOverlay.Checked) { var overlay=new LoadingOverlayForm(game); overlay.Show(); } WindowState=FormWindowState.Minimized; }
                status.Text=loadingOverlay.Checked?"Game started. Settings and backup saved; black-screen detection is enabled.":"Game started. Settings and backup saved; loading-screen detection is off.";
            }
        }); }
        void RefreshFpsButton() {
            if(fpsAction==null)return;
            int rate=0; if(loaded) { try { rate=Engine.Read(folder.Text).Fps; } catch { } }
            fpsAction.Text=rate==60?"Restore 30 FPS":"Enable 60 FPS"; fpsAction.Enabled=loaded;
            tips.SetToolTip(fpsAction,rate==60?"Return to the stock 30 FPS game. A backup is made automatically.":"Install the tested 60 FPS patch. A backup is made automatically.");
        }
        void ToggleFps() { Run(delegate {
            if(!loaded) throw new Exception("Choose a valid game folder first.");
            if(Engine.Running()) throw new Exception("Save and close the game before changing its frame rate.");
            int current=Engine.Read(folder.Text).Fps, target=current==60?30:60;
            string variants=Path.Combine(folder.Text,"WOTS4-Launcher-Backups","FPSVariants");
            if(!File.Exists(Path.Combine(variants,"WayOfTheSamurai4.exe."+target))||!File.Exists(Path.Combine(variants,"MotionDatabase.l."+target))) {
                string candidate=unpackedPath;
                if(String.IsNullOrEmpty(candidate)||!File.Exists(candidate)) {
                    using(var dialog=new OpenFileDialog { Title="Choose your unpacked Steam game executable", Filter="Unpacked game executable|*.exe|All files|*.*", FileName="WayOfTheSamurai4.exe.unpacked.exe" }) {
                        string saved=LauncherPreferences.UnpackedExecutable; if(File.Exists(saved))dialog.FileName=saved;
                        if(dialog.ShowDialog(this)!=DialogResult.OK)return; candidate=dialog.FileName;
                    }
                }
                string baseDir=AppDomain.CurrentDomain.BaseDirectory;
                MessageBox.Show(this,"The launcher will verify your unpacked Steam executable and prepare the tested 60 FPS patch and matching stock 30 FPS files. This only needs to be done once.","Prepare frame-rate options",MessageBoxButtons.OK,MessageBoxIcon.Information);
                Engine.BuildFpsProfiles(folder.Text,candidate,Path.Combine(baseDir,"runtime","d3d9.dll"),Path.Combine(baseDir,"runtime","FPSPatch","patch.ps1"),Path.Combine(baseDir,"runtime","FPSPatch","LICENSE"));
                unpackedPath=candidate; RememberUi();
            }
            Settings active=Engine.Read(folder.Text); active.Fps=target;
            string backup=Engine.Apply(folder.Text,active,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","d3d9.dll"));
            RefreshFpsButton(); status.Text=target==60?"Tested 60 FPS is enabled. A backup was saved; click Restore 30 FPS to return to stock.":"Stock 30 FPS is restored. A backup was saved.";
        }); }
    }

    static class Program {
        [STAThread] static int Main(string[] args) {
            try {
                if(args.Length>0 && args[0]=="--self-test") { SelfTest(); return 0; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if(args.Length>1 && args[0]=="--overlay-preview") { using(var form=new LoadingOverlayForm(Process.GetCurrentProcess(),true)) { form.Show(); Application.DoEvents(); using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]); } } return 0; }
                if(args.Length>1 && args[0]=="--preview") { using(var form=new LauncherForm(args.Length>2?args[2]:"")) { form.Show(); Application.DoEvents(); using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]); } } return 0; }
                Application.Run(new LauncherForm(args.Length>0?args[0]:"")); return 0;
            } catch(Exception e) { if(args.Length>0 && args[0]=="--self-test") { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),e.ToString()); return 1; } MessageBox.Show(e.Message,"Launcher error");return 1; }
        }
        static void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
        static void SelfTest() {
            string dir=Path.Combine(Path.GetTempPath(),"wots4-launcher-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try {
                byte[] exe=new byte[128]; exe[60]=64;exe[68]=0x4c;exe[69]=0x01;File.WriteAllBytes(Path.Combine(dir,"WayOfTheSamurai4.exe"),exe);
                string original="[Graphics]\r\nFullScreen=0\r\nWidth=1920\r\nHeight=1080\r\nFOV=75\r\nEnableDOF=1\r\nEnableHDR=1\r\nFrameRateLimit=30\r\n[Controls]\r\nFOV=999\r\nKEY_OK=57\r\n";
                File.WriteAllText(Path.Combine(dir,"S4-SETTINGS.ini"),original,Encoding.ASCII);
                string payload=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","d3d9.dll");
                Engine.Apply(dir,new Settings(),payload); var s=Engine.Read(dir);
                Assert(s.Borderless&&s.FocusFix&&!s.Dof&&!s.Bloom&&s.Fov==85&&s.Anisotropy==16,"Settings round trip failed");
                string changed=File.ReadAllText(Path.Combine(dir,"S4-SETTINGS.ini"));Assert(changed.Contains("FrameRateLimit=30")&&changed.Contains("FOV=999")&&changed.Contains("KEY_OK=57"),"Unrelated settings changed");
                Engine.Restore(dir);Assert(File.ReadAllText(Path.Combine(dir,"S4-SETTINGS.ini"))==original,"Original INI was not restored");Assert(!File.Exists(Path.Combine(dir,"d3d9.dll"))&&!File.Exists(Path.Combine(dir,"dxvk.conf")),"Absent files were not removed on restore");
                File.WriteAllText(Path.Combine(dir,"dxvk.conf"),"# Preserve me\nd3d9.maxFrameRate = 30\nd3d9.deviceLossOnFocusLoss = False\n",new UTF8Encoding(true));
                byte[] oldConfig=File.ReadAllBytes(Path.Combine(dir,"dxvk.conf"));
                Engine.Apply(dir,new Settings {Borderless=false,FocusFix=false,Fov=90,Anisotropy=4},payload);s=Engine.Read(dir);Assert(!s.Borderless&&!s.FocusFix&&s.Fov==90&&s.Anisotropy==4,"Toggle handling failed");Assert(File.ReadAllText(Path.Combine(dir,"dxvk.conf")).Contains("d3d9.maxFrameRate = 30"),"Frame timing changed");
                Engine.Restore(dir);Assert(File.ReadAllBytes(Path.Combine(dir,"dxvk.conf")).SequenceEqual(oldConfig),"Encoding or content rollback failed");
                string variants=Path.Combine(dir,"WOTS4-Launcher-Backups","FPSVariants"), motionDir=Path.Combine(dir,"Common","Character","Action"); Directory.CreateDirectory(variants); Directory.CreateDirectory(motionDir);
                byte[] rateExe={1,2,3}, rateMotion={4,5,6}; File.WriteAllBytes(Path.Combine(variants,"WayOfTheSamurai4.exe.30"),rateExe); File.WriteAllBytes(Path.Combine(variants,"MotionDatabase.l.30"),rateMotion);
                var rateSettings=new Settings(); rateSettings.Fps=30; Engine.Apply(dir,rateSettings,payload);
                Assert(File.ReadAllBytes(Path.Combine(dir,"WayOfTheSamurai4.exe")).SequenceEqual(rateExe)&&File.ReadAllBytes(Path.Combine(motionDir,"MotionDatabase.l")).SequenceEqual(rateMotion),"Matched FPS files were not installed together");
                Engine.Restore(dir);Assert(!File.Exists(Path.Combine(motionDir,"MotionDatabase.l")),"FPS file pair was not rolled back");
                File.WriteAllText(Path.Combine(dir,"d3d9.dll"),"another mod");bool refused=false;try{Engine.Apply(dir,new Settings(),payload);}catch(Exception){refused=true;}Assert(refused&&File.ReadAllText(Path.Combine(dir,"d3d9.dll"))=="another mod","Existing mod was overwritten");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),"PASS: remembered settings support, section isolation, frame timing preservation, matched FPS pair install and rollback, absent/existing file rollback, encoding preservation, and mod conflict protection.");
            } finally { Directory.Delete(dir,true); }
        }
    }
}

