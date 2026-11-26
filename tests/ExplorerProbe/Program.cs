using System.Runtime.InteropServices;

internal static unsafe class Program
{
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, int flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(in Guid clsid, nint outer, int context, in Guid iid, out nint instance);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern nint ILCreateFromPathW(string path);
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromIDLists(uint count, nint* pidls, out nint array);

    private static nint* Methods(nint obj) => *(nint**)obj;
    private static void Release(nint obj) { if (obj != 0) ((delegate* unmanaged<nint, uint>)Methods(obj)[2])(obj); }
    private static string Title(nint cmd, nint items)
    {
        nint name = 0;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, nint, nint*, int>)Methods(cmd)[3])(cmd, items, &name));
        try { return Marshal.PtrToStringUni(name)!; } finally { Marshal.FreeCoTaskMem(name); }
    }
    private static uint State(nint cmd, nint items)
    {
        uint state = 0;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, nint, int, uint*, int>)Methods(cmd)[7])(cmd, items, 1, &state));
        return state;
    }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("ExplorerProbe <0=list|1..5=invoke> <file> [...]"); return 2; }
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
        nint root = 0, selection = 0, enumeration = 0;
        var pidls = args.Skip(1).Select(p => ILCreateFromPathW(Path.GetFullPath(p))).ToArray();
        try
        {
            Guid clsid = new("5f9dd25e-9bb3-46fc-9bab-921bfb5d5d1f"), iid = new("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9");
            int activation = CoCreateInstance(clsid, 0, 5, iid, out root);
            if (args[0] == "-1")
            {
                Console.WriteLine($"Uninstalled shell activation HRESULT: 0x{activation:X8}");
                return activation == unchecked((int)0x80040154) ? 0 : 1;
            }
            Marshal.ThrowExceptionForHR(activation);
            fixed (nint* list = pidls) Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, list, out selection));
            Console.WriteLine($"ROOT {Title(root, selection)} state={State(root, selection)}");
            Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, nint*, int>)Methods(root)[10])(root, &enumeration));
            int trigger = int.Parse(args[0]), count = 0;
            string[] expected = ["压缩…", "使用上次参数压缩", "压缩至 500 KB", "压缩至 1000 KB", "打开压缩主窗口"];
            while (true)
            {
                nint cmd = 0;
                uint got = 0;
                int hr = ((delegate* unmanaged<nint, uint, nint*, uint*, int>)Methods(enumeration)[3])(enumeration, 1, &cmd, &got);
                Marshal.ThrowExceptionForHR(hr);
                if (got == 0) break;
                try
                {
                    string title = Title(cmd, selection);
                    uint state = State(cmd, selection);
                    if (count >= expected.Length || !title.StartsWith(expected[count], StringComparison.Ordinal)) throw new Exception("Unexpected " + title);
                    Console.WriteLine($"{++count}. {title} state={state}");
                    if (count == trigger)
                    {
                        if (state != 0) throw new Exception("Disabled command");
                        Marshal.ThrowExceptionForHR(((delegate* unmanaged<nint, nint, nint, int>)Methods(cmd)[8])(cmd, selection, 0));
                    }
                }
                finally { Release(cmd); }
            }
            return count == 5 ? 0 : 1;
        }
        finally
        {
            Release(enumeration); Release(selection); Release(root);
            foreach (nint pidl in pidls) Marshal.FreeCoTaskMem(pidl);
            CoUninitialize();
        }
    }
}
