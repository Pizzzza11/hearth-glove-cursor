using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32;

internal static class HearthGloveCursor
{
    private const uint CursorArrow = 32512;
    private const uint CursorIBeam = 32513;
    private const uint CursorWait = 32514;
    private const uint CursorHand = 32649;
    private const uint CursorAppStarting = 32650;
    private const uint SpiSetCursors = 0x0057;
    private const int MinimumPressedMilliseconds = 85;
    private const int PollMilliseconds = 8;
    private const int GuardMilliseconds = 1000;
    private const string SchemeName = "\u7089\u77f3\u98ce\u624b\u5957";
    private const string MainMutexName = "Local\\HearthGloveCursor-7D12A0C8";
    private const string WatchdogMutexName = "Local\\HearthGloveCursorWatchdog-8E4BCF13";

    private static readonly int[] MouseButtons = { 0x01, 0x02, 0x04, 0x05, 0x06 };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadCursorFromFile(string fileName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemCursor(IntPtr cursor, uint cursorId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCursor(IntPtr cursor);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BitmapInfo bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetBitmapBits(IntPtr bitmap, int count, byte[] bits);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    private static string _pidPath;
    private static string _logPath;
    private static string _normalCursor;
    private static string _pressedCursor;
    private static string _ibeamCursor;
    private static string _workingCursor;
    private static string _busyCursor;
    private static string _stopPath;
    private static volatile bool _exiting;

    private static void Log(string message)
    {
        try
        {
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1024 * 1024)
                File.Move(_logPath, _logPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".old");
            File.AppendAllText(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
        }
        catch { }
    }

    private static void ApplyCursor(string cursorPath, params uint[] cursorIds)
    {
        foreach (uint cursorId in cursorIds)
        {
            IntPtr handle = LoadCursorFromFile(cursorPath);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("Unable to load cursor: " + cursorPath);
            if (!SetSystemCursor(handle, cursorId))
            {
                int error = Marshal.GetLastWin32Error();
                DestroyCursor(handle);
                throw new InvalidOperationException("SetSystemCursor failed: " + error);
            }
        }
    }

    private static void ApplyPointer(bool pressed)
    {
        ApplyCursor(pressed ? _pressedCursor : _normalCursor, CursorArrow, CursorHand);
    }

    private static bool AnyButtonPressed()
    {
        foreach (int button in MouseButtons)
            if ((GetAsyncKeyState(button) & 0x8000) != 0) return true;
        return false;
    }

    private static string CursorValue(RegistryKey key, string name)
    {
        object value = key.GetValue(name);
        return value == null ? string.Empty : Convert.ToString(value);
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RegistryMatches()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", false))
            {
                if (key == null) return false;
                return SamePath(CursorValue(key, "Arrow"), _normalCursor)
                    && SamePath(CursorValue(key, "Hand"), _normalCursor)
                    && SamePath(CursorValue(key, "IBeam"), _ibeamCursor)
                    && SamePath(CursorValue(key, "AppStarting"), _workingCursor)
                    && SamePath(CursorValue(key, "Wait"), _busyCursor)
                    && string.Equals(CursorValue(key, string.Empty), SchemeName, StringComparison.Ordinal)
                    && Convert.ToInt32(key.GetValue("Scheme Source", 0)) == 0;
            }
        }
        catch { return false; }
    }

    private static void EnsureRegistry()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors"))
        {
            key.SetValue("Arrow", _normalCursor, RegistryValueKind.String);
            key.SetValue("Hand", _normalCursor, RegistryValueKind.String);
            key.SetValue("IBeam", _ibeamCursor, RegistryValueKind.String);
            key.SetValue("AppStarting", _workingCursor, RegistryValueKind.String);
            key.SetValue("Wait", _busyCursor, RegistryValueKind.String);
            key.SetValue(string.Empty, SchemeName, RegistryValueKind.String);
            key.SetValue("Scheme Source", 0, RegistryValueKind.DWord);

            string[] entries = {
                _normalCursor,
                CursorValue(key, "Help"),
                _workingCursor,
                _busyCursor,
                CursorValue(key, "Crosshair"),
                _ibeamCursor,
                CursorValue(key, "NWPen"),
                CursorValue(key, "No"),
                CursorValue(key, "SizeNS"),
                CursorValue(key, "SizeWE"),
                CursorValue(key, "SizeNWSE"),
                CursorValue(key, "SizeNESW"),
                CursorValue(key, "SizeAll"),
                CursorValue(key, "UpArrow"),
                _normalCursor,
                CursorValue(key, "Pin"),
                CursorValue(key, "Person")
            };
            using (RegistryKey schemes = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors\Schemes"))
                schemes.SetValue(SchemeName, string.Join(",", entries), RegistryValueKind.String);
        }
    }

    private static void AddBitmapBytes(List<byte> bytes, IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        BitmapInfo bitmap;
        if (GetObject(handle, Marshal.SizeOf(typeof(BitmapInfo)), out bitmap) == 0) return;
        bytes.AddRange(BitConverter.GetBytes(bitmap.Width));
        bytes.AddRange(BitConverter.GetBytes(bitmap.Height));
        bytes.AddRange(BitConverter.GetBytes(bitmap.WidthBytes));
        bytes.AddRange(BitConverter.GetBytes(bitmap.BitsPixel));
        int length = Math.Abs(bitmap.Height) * bitmap.WidthBytes;
        byte[] pixels = new byte[length];
        GetBitmapBits(handle, length, pixels);
        bytes.AddRange(pixels);
    }

    private static byte[] CursorSignature(IntPtr cursor)
    {
        if (cursor == IntPtr.Zero) return null;
        IconInfo info;
        if (!GetIconInfo(cursor, out info)) return null;
        try
        {
            List<byte> bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(info.HotspotX));
            bytes.AddRange(BitConverter.GetBytes(info.HotspotY));
            AddBitmapBytes(bytes, info.MaskBitmap);
            AddBitmapBytes(bytes, info.ColorBitmap);
            using (SHA256 hash = SHA256.Create()) return hash.ComputeHash(bytes.ToArray());
        }
        finally
        {
            if (info.MaskBitmap != IntPtr.Zero) DeleteObject(info.MaskBitmap);
            if (info.ColorBitmap != IntPtr.Zero) DeleteObject(info.ColorBitmap);
        }
    }

    private static bool SignaturesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
            if (left[index] != right[index]) return false;
        return true;
    }

    private static bool CursorMatches(string path, uint cursorId)
    {
        IntPtr expected = LoadCursorFromFile(path);
        if (expected == IntPtr.Zero) return false;
        try
        {
            IntPtr actual = LoadCursor(IntPtr.Zero, new IntPtr(cursorId));
            return SignaturesEqual(CursorSignature(expected), CursorSignature(actual));
        }
        finally { DestroyCursor(expected); }
    }

    private static bool CompleteSetMatches(bool pressed)
    {
        string pointer = pressed ? _pressedCursor : _normalCursor;
        return RegistryMatches()
            && CursorMatches(pointer, CursorArrow)
            && CursorMatches(pointer, CursorHand)
            && CursorMatches(_ibeamCursor, CursorIBeam)
            && CursorMatches(_workingCursor, CursorAppStarting)
            && CursorMatches(_busyCursor, CursorWait);
    }

    private static void RestoreCompleteSet(bool pressed, string reason)
    {
        EnsureRegistry();
        ApplyCursor(_ibeamCursor, CursorIBeam);
        ApplyCursor(_workingCursor, CursorAppStarting);
        ApplyCursor(_busyCursor, CursorWait);
        ApplyPointer(pressed);
        Log("Restored complete cursor set. Reason=" + reason);
    }

    private static void RestoreOnExit(object sender, EventArgs eventArgs)
    {
        _exiting = true;
        SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, 0);
        try { if (_pidPath != null) File.Delete(_pidPath); } catch { }
    }

    private static ProcessStartInfo SelfStartInfo(string arguments)
    {
        string executable = Process.GetCurrentProcess().MainModule.FileName;
        return new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
        };
    }

    private static bool WatchdogExists()
    {
        try
        {
            using (Mutex existing = Mutex.OpenExisting(WatchdogMutexName)) return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static void EnsureWatchdog(int mainProcessId)
    {
        if (File.Exists(_stopPath) || WatchdogExists()) return;
        try { Process.Start(SelfStartInfo("--watchdog " + mainProcessId)); }
        catch (Exception exception) { Log("Unable to start watchdog: " + exception.Message); }
    }

    private static int WatchdogMain(string directory, string[] args)
    {
        string stopPath = Path.Combine(directory, "HearthGloveCursor.stop");
        string logPath = Path.Combine(directory, "HearthGloveCursor.log");
        bool ownsMutex;
        using (Mutex mutex = new Mutex(true, WatchdogMutexName, out ownsMutex))
        {
            if (!ownsMutex) return 0;
            int mainProcessId;
            if (args.Length < 2 || !int.TryParse(args[1], out mainProcessId)) return 2;

            while (!File.Exists(stopPath))
            {
                try
                {
                    using (Process main = Process.GetProcessById(mainProcessId)) main.WaitForExit();
                }
                catch { }

                if (File.Exists(stopPath)) break;
                Thread.Sleep(2000);
                if (File.Exists(stopPath)) break;

                try
                {
                    Process replacement = Process.Start(SelfStartInfo(string.Empty));
                    mainProcessId = replacement.Id;
                    try
                    {
                        File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                            + " Watchdog restarted guard. PID=" + mainProcessId + Environment.NewLine);
                    }
                    catch { }
                }
                catch (Exception exception)
                {
                    try
                    {
                        File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                            + " Watchdog restart error: " + exception.Message + Environment.NewLine);
                    }
                    catch { }
                    Thread.Sleep(3000);
                }
            }
            return 0;
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string directory = AppDomain.CurrentDomain.BaseDirectory;
        if (args.Length > 0 && args[0].StartsWith("--") && args[0] != "--watchdog" && args[0] != "--verify-assets")
            return CursorSetup.Run(args);
        if (args.Length > 0 && args[0] == "--verify-assets")
        {
            foreach (string file in new string[] { "glove-normal.cur", "glove-pressed.cur", "glove-ibeam.cur", "glove-working.ani", "glove-busy.ani" })
            {
                IntPtr cursor = LoadCursorFromFile(Path.Combine(directory, file));
                if (cursor == IntPtr.Zero) return 2;
                DestroyCursor(cursor);
            }
            return 0;
        }
        if (args.Length > 0 && string.Equals(args[0], "--watchdog", StringComparison.OrdinalIgnoreCase))
            return WatchdogMain(directory, args);

        _stopPath = Path.Combine(directory, "HearthGloveCursor.stop");
        if (File.Exists(_stopPath)) return 0;
        _normalCursor = args.Length > 0 ? args[0] : Path.Combine(directory, "glove-normal.cur");
        _pressedCursor = args.Length > 1 ? args[1] : Path.Combine(directory, "glove-pressed.cur");
        _ibeamCursor = args.Length > 2 ? args[2] : Path.Combine(directory, "glove-ibeam.cur");
        _workingCursor = args.Length > 3 ? args[3] : Path.Combine(directory, "glove-working.ani");
        _busyCursor = args.Length > 4 ? args[4] : Path.Combine(directory, "glove-busy.ani");
        _pidPath = Path.Combine(directory, "HearthGloveCursor.pid");
        _logPath = Path.Combine(directory, "HearthGloveCursor.log");

        try
        {
            bool ownsMutex;
            using (Mutex mutex = new Mutex(true, MainMutexName, out ownsMutex))
            {
                if (!ownsMutex) return 0;
                AppDomain.CurrentDomain.ProcessExit += RestoreOnExit;
                File.WriteAllText(_pidPath, Process.GetCurrentProcess().Id.ToString());

                bool displayedPressed = AnyButtonPressed();
                DateTime pressedUntil = displayedPressed
                    ? DateTime.UtcNow.AddMilliseconds(MinimumPressedMilliseconds)
                    : DateTime.MinValue;
                RestoreCompleteSet(displayedPressed, "startup");
                EnsureWatchdog(Process.GetCurrentProcess().Id);
                DateTime nextGuard = DateTime.UtcNow.AddMilliseconds(GuardMilliseconds);
                Log("Started successfully. PID=" + Process.GetCurrentProcess().Id);

                while (!_exiting && !File.Exists(_stopPath))
                {
                    bool pressed = AnyButtonPressed();
                    DateTime now = DateTime.UtcNow;

                    if (pressed && !displayedPressed)
                    {
                        displayedPressed = true;
                        pressedUntil = now.AddMilliseconds(MinimumPressedMilliseconds);
                        ApplyPointer(true);
                    }
                    else if (!pressed && displayedPressed && now >= pressedUntil)
                    {
                        displayedPressed = false;
                        ApplyPointer(false);
                    }

                    if (now >= nextGuard)
                    {
                        EnsureWatchdog(Process.GetCurrentProcess().Id);
                        if (!CompleteSetMatches(displayedPressed))
                            RestoreCompleteSet(displayedPressed, "system or application changed one or more cursor roles");
                        nextGuard = now.AddMilliseconds(GuardMilliseconds);
                    }
                    Thread.Sleep(PollMilliseconds);
                }
                return 0;
            }
        }
        catch (Exception exception)
        {
            Log("Fatal error: " + exception);
            return 1;
        }
    }
}
