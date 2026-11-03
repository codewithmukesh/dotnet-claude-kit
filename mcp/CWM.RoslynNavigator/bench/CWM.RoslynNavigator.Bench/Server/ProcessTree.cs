using System.Runtime.InteropServices;

namespace CWM.RoslynNavigator.Bench.Server;

/// <summary>
/// Parent/child process relationships. The stdio transport does not expose the process it
/// starts, and the server itself spawns MSBuild build hosts, so the only reliable way to
/// tell "the server" from "its build hosts" is the process tree under the harness.
/// Supported on Windows (Toolhelp snapshot) and Linux (/proc); elsewhere memory is "n/a".
/// </summary>
internal static class ProcessTree
{
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>Map of pid → parent pid for every process visible to this user.</summary>
    public static Dictionary<int, int> Snapshot()
    {
        if (OperatingSystem.IsWindows()) return WindowsSnapshot();
        if (OperatingSystem.IsLinux()) return LinuxSnapshot();
        return [];
    }

    /// <summary>Descendants of <paramref name="root"/> with their depth below it (children are depth 1).</summary>
    public static List<(int Pid, int Depth)> Descendants(int root)
    {
        var parents = Snapshot();
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, parent) in parents)
        {
            if (pid == parent) continue;
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
            list.Add(pid);
        }

        var result = new List<(int, int)>();
        var queue = new Queue<(int Pid, int Depth)>();
        queue.Enqueue((root, 0));
        var seen = new HashSet<int> { root };

        while (queue.Count > 0)
        {
            var (pid, depth) = queue.Dequeue();
            if (!children.TryGetValue(pid, out var kids)) continue;
            foreach (var kid in kids)
            {
                if (!seen.Add(kid)) continue;
                result.Add((kid, depth + 1));
                queue.Enqueue((kid, depth + 1));
            }
        }

        return result;
    }

    private static Dictionary<int, int> LinuxSnapshot()
    {
        var map = new Dictionary<int, int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(System.IO.Path.GetFileName(directory), out var pid)) continue;
            try
            {
                // /proc/<pid>/stat: "pid (comm) state ppid ..." — comm may contain spaces, so
                // parse from the last ')'.
                var stat = File.ReadAllText(System.IO.Path.Combine(directory, "stat"));
                var close = stat.LastIndexOf(')');
                var fields = stat[(close + 2)..].Split(' ');
                if (fields.Length > 1 && int.TryParse(fields[1], out var parent)) map[pid] = parent;
            }
            catch (IOException)
            {
                // The process exited between enumeration and read.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return map;
    }

    private static Dictionary<int, int> WindowsSnapshot()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle) return map;

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return map;

            do
            {
                map[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
    }

    private const uint SnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
