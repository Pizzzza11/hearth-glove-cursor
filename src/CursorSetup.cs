using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Win32;

// Uses only .NET 2.0 APIs, available with Windows 7's .NET 3.5 installation.
internal static class CursorSetup
{
    private const string CursorKey = @"Control Panel\Cursors";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "HearthGloveCursor";
    private const string Scheme = "\u7089\u77f3\u98ce\u624b\u5957";
    private static readonly string[] Roles = { "Arrow", "Hand", "IBeam", "AppStarting", "Wait", "", "Scheme Source" };
    private static readonly string[] Assets = { "glove-normal.cur", "glove-pressed.cur", "glove-ibeam.cur", "glove-working.ani", "glove-busy.ani" };
    private static readonly string[] Commands = { "Start.cmd", "Stop.cmd", "Status.cmd", "Uninstall.cmd" };
    private static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),AppName); } }
    private static string Executable { get { return Path.Combine(Folder,AppName+".exe"); } }
    private static string StopFile { get { return Path.Combine(Folder,AppName+".stop"); } }
    private static string Backup { get { return Path.Combine(Folder,"original-backup-v2.xml"); } }

    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner,string text,string caption,uint type);
    [DllImport("user32.dll", SetLastError=true)]
    private static extern bool SystemParametersInfo(uint a,uint b,IntPtr c,uint d);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    private static extern IntPtr LoadCursorFromFile(string path);
    [DllImport("user32.dll")]
    private static extern bool DestroyCursor(IntPtr cursor);

    private static void Snapshot(XmlDocument doc,XmlElement root,string subkey,string name)
    {
        XmlElement record=doc.CreateElement("record");
        record.SetAttribute("key",subkey); record.SetAttribute("name",name);
        using(RegistryKey key=Registry.CurrentUser.OpenSubKey(subkey))
        {
            bool exists=key!=null && Array.IndexOf(key.GetValueNames(),name)>=0;
            record.SetAttribute("exists",exists.ToString());
            if(exists)
            {
                RegistryValueKind kind=key.GetValueKind(name);
                record.SetAttribute("kind",kind.ToString());
                object value=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                if(kind==RegistryValueKind.Binary) record.InnerText=Convert.ToBase64String((byte[])value);
                else if(kind==RegistryValueKind.MultiString)
                    foreach(string item in (string[])value) { XmlElement child=doc.CreateElement("item");child.InnerText=item;record.AppendChild(child); }
                else record.InnerText=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        root.AppendChild(record);
    }

    private static void Restore(XmlDocument doc,string testKey)
    {
        foreach(XmlElement record in doc.DocumentElement.SelectNodes("record"))
        {
            string subkey=record.GetAttribute("key");
            if(subkey!=CursorKey && subkey!=CursorKey+@"\Schemes" && subkey!=RunKey && subkey!=testKey) throw new InvalidDataException("Unexpected backup registry key.");
            using(RegistryKey key=Registry.CurrentUser.CreateSubKey(subkey))
            {
                string name=record.GetAttribute("name");
                if(!bool.Parse(record.GetAttribute("exists"))) { key.DeleteValue(name,false); continue; }
                RegistryValueKind kind=(RegistryValueKind)Enum.Parse(typeof(RegistryValueKind),record.GetAttribute("kind"));
                object value=record.InnerText;
                if(kind==RegistryValueKind.DWord) value=int.Parse(record.InnerText,System.Globalization.CultureInfo.InvariantCulture);
                else if(kind==RegistryValueKind.QWord) value=long.Parse(record.InnerText,System.Globalization.CultureInfo.InvariantCulture);
                else if(kind==RegistryValueKind.Binary) value=Convert.FromBase64String(record.InnerText);
                else if(kind==RegistryValueKind.MultiString) { List<string> values=new List<string>();foreach(XmlElement item in record.SelectNodes("item")) values.Add(item.InnerText);value=values.ToArray(); }
                key.SetValue(name,value,kind);
            }
        }
    }

    private static void SaveBackup()
    {
        if(File.Exists(Backup)) return;
        // Prototype installers use a different backup format; do not overwrite their baseline.
        if(File.Exists(Path.Combine(Folder,"cursor-backup.json")) || File.Exists(Path.Combine(Folder,"original-backup-v1.json")))
            throw new InvalidOperationException("A prototype installation was found. Uninstall that version using its original Uninstall.ps1/Uninstall.cmd first. Keep its backup files; then move its installation directory aside before installing this release.");
        XmlDocument doc=new XmlDocument();XmlElement root=doc.CreateElement("backup");doc.AppendChild(root);
        root.SetAttribute("version","2");
        foreach(string role in Roles) Snapshot(doc,root,CursorKey,role);
        Snapshot(doc,root,CursorKey+@"\Schemes",Scheme);
        Snapshot(doc,root,RunKey,AppName);
        doc.Save(Backup);
    }

    private static List<Process> Processes()
    {
        List<Process> found=new List<Process>();
        foreach(Process process in Process.GetProcessesByName(AppName))
        {
            try { if(string.Equals(process.MainModule.FileName,Executable,StringComparison.OrdinalIgnoreCase)) found.Add(process);else process.Dispose(); }
            catch { process.Dispose(); }
        }
        return found;
    }

    private static void Stop()
    {
        if(!Directory.Exists(Folder)) return;
        File.WriteAllText(StopFile,"Paused");
        foreach(Process process in Processes())
        {
            try { if(process.Id!=Process.GetCurrentProcess().Id) { process.Kill();process.WaitForExit(3000); } }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private static void Reload()
    {
        if(!SystemParametersInfo(0x57,0,IntPtr.Zero,0)) throw new InvalidOperationException("Windows could not reload the cursor scheme.");
    }

    private static void Start()
    {
        foreach(string asset in Assets) if(!File.Exists(Path.Combine(Folder,asset))) throw new FileNotFoundException("Install first. Missing cursor asset: "+asset);
        if(!File.Exists(Executable)) throw new FileNotFoundException("Install first.");
        if(File.Exists(StopFile)) File.Delete(StopFile);
        Process.Start(new ProcessStartInfo(Executable) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Folder });
        Thread.Sleep(2000);
        List<Process> running=Processes();int count=running.Count;foreach(Process p in running)p.Dispose();
        // Installed management commands are themselves running from the installed folder.
        int self=string.Equals(Process.GetCurrentProcess().MainModule.FileName,Executable,StringComparison.OrdinalIgnoreCase)?1:0;
        if(count-self<2) throw new InvalidOperationException("Guard or watchdog failed to start. Check HearthGloveCursor.log in "+Folder);
    }

    private static void CopyUnlessSame(string from,string to)
    {
        if(!string.Equals(Path.GetFullPath(from),Path.GetFullPath(to),StringComparison.OrdinalIgnoreCase)) File.Copy(from,to,true);
    }

    private static void Install(bool noStartup)
    {
        string source=AppDomain.CurrentDomain.BaseDirectory;
        foreach(string asset in Assets)
        {
            string file=Path.Combine(source,Path.Combine("cursor-pack",asset));
            IntPtr cursor=LoadCursorFromFile(file);
            if(cursor==IntPtr.Zero) throw new InvalidDataException("Incomplete or unsupported release asset: "+file);
            DestroyCursor(cursor);
        }
        Directory.CreateDirectory(Folder);
        SaveBackup();
        Stop();
        CopyUnlessSame(Process.GetCurrentProcess().MainModule.FileName,Executable);
        CopyUnlessSame(Path.Combine(source,AppName+".exe.config"),Executable+".config");
        foreach(string asset in Assets) CopyUnlessSame(Path.Combine(source,Path.Combine("cursor-pack",asset)),Path.Combine(Folder,asset));
        foreach(string command in Commands) CopyUnlessSame(Path.Combine(source,command),Path.Combine(Folder,command));
        if(!noStartup)
            using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunKey)) key.SetValue(AppName,"\""+Executable+"\"",RegistryValueKind.String);
        Start();
    }

    private static void Uninstall()
    {
        if(!File.Exists(Backup)) throw new FileNotFoundException("Original backup was not found; settings have not been changed.");
        XmlDocument doc=new XmlDocument();doc.XmlResolver=null;doc.Load(Backup);
        Stop();Restore(doc,null);Reload();
        File.Move(Backup,Path.Combine(Folder,"restored-backup-"+DateTime.Now.ToString("yyyyMMddHHmmssfff")+".xml"));
    }

    private static string Status()
    {
        List<Process> processes=Processes();int count=processes.Count;foreach(Process p in processes)p.Dispose();
        if(string.Equals(Process.GetCurrentProcess().MainModule.FileName,Executable,StringComparison.OrdinalIgnoreCase))count--;
        StringBuilder text=new StringBuilder();
        text.AppendLine("Installation: "+Folder);text.AppendLine("Guard processes (main + watchdog; expected 2): "+count);
        text.AppendLine("Paused: "+File.Exists(StopFile));text.AppendLine("Backup: "+File.Exists(Backup));
        using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunKey))text.AppendLine("Login: "+(key==null?"":Convert.ToString(key.GetValue(AppName))));
        using(RegistryKey key=Registry.CurrentUser.OpenSubKey(CursorKey))if(key!=null)foreach(string role in Roles)text.AppendLine(role+": "+Convert.ToString(key.GetValue(role)));
        text.AppendLine("Runtime: "+Environment.Version);return text.ToString();
    }

    internal static int Run(string[] args)
    {
        bool quiet=Array.IndexOf(args,"--quiet")>=0;int exitCode=0;string message;
        try
        {
            switch(args[0])
            {
                case "--install": Install(Array.IndexOf(args,"--no-startup")>=0);message="Installed and enabled.\r\n"+Folder;break;
                case "--start": Start();message="Cursor set enabled.";break;
                case "--stop": Stop();Reload();message="Paused. Run Start.cmd to resume. Use Uninstall.cmd to restore the previous scheme.";break;
                case "--uninstall": Uninstall();message="Previous cursor and login settings restored. Files retained at:\r\n"+Folder;break;
                case "--status": message=Status();break;
                case "--self-test": SelfTest();message="Registry backup/restore self-test passed.";break;
                default: throw new ArgumentException("Unknown command: "+args[0]);
            }
        }
        catch(Exception error) { message=error.Message;exitCode=1; }
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(),"HearthGloveCursor-management.txt"),message,Encoding.UTF8); } catch { }
        if(!quiet)MessageBox(IntPtr.Zero,message,"Hearth Glove Cursor",exitCode==0?0x40u:0x10u);
        return exitCode;
    }

    private static void SelfTest()
    {
        string test=CursorKey+"Tests\\"+Guid.NewGuid().ToString("N");
        try
        {
            using(RegistryKey key=Registry.CurrentUser.CreateSubKey(test))
            {
                key.SetValue("","Default");key.SetValue("Expand","%SystemRoot%\\Cursors",RegistryValueKind.ExpandString);key.SetValue("Number",7,RegistryValueKind.DWord);
            }
            XmlDocument doc=new XmlDocument();XmlElement root=doc.CreateElement("backup");doc.AppendChild(root);
            foreach(string name in new string[]{"","Expand","Number","Absent"})Snapshot(doc,root,test,name);
            XmlDocument roundtrip=new XmlDocument();roundtrip.LoadXml(doc.OuterXml);
            using(RegistryKey key=Registry.CurrentUser.CreateSubKey(test))
                foreach(string name in new string[]{"","Expand","Number","Absent"})key.SetValue(name,"changed",RegistryValueKind.String);
            Restore(roundtrip,test);
            foreach(XmlElement record in roundtrip.DocumentElement.SelectNodes("record"))
            {
                string name=record.GetAttribute("name");
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(test))
                {
                    bool exists=Array.IndexOf(key.GetValueNames(),name)>=0;
                    if(exists!=bool.Parse(record.GetAttribute("exists")))throw new Exception("Existence mismatch.");
                    if(exists && key.GetValueKind(name).ToString()!=record.GetAttribute("kind"))throw new Exception("Kind mismatch.");
                    if(exists && Convert.ToString(key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames),System.Globalization.CultureInfo.InvariantCulture)!=record.InnerText)throw new Exception("Value mismatch.");
                }
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(test); }
    }
}
