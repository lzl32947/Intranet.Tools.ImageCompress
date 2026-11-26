using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ImagePress.Shell;

// Native COM ABI for Windows x64. Every returned command/enumerator owns a
// reference. Explorer can release or clone either in any order.
public static unsafe class ExplorerExtension
{
    private const int Ok = 0, False = 1, NoInterface = unchecked((int)0x80004002),
        Pointer = unchecked((int)0x80004003), NotImplemented = unchecked((int)0x80004001);
    private const int Factory = 0, Command = 1, Enumerator = 2;
    private static readonly Guid ClassId = new("5f9dd25e-9bb3-46fc-9bab-921bfb5d5d1f");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid FactoryId = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid CommandId = new("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9");
    private static readonly Guid EnumId = new("a88826f8-186f-4987-aade-ea0cef8fbfe8");
    private static readonly string[] Actions = ["configure", "configure", "repeat", "500", "1000", "main"];
    private static readonly string[] Captions = ["压缩图片", "压缩…", "使用上次参数压缩", "压缩至 500 KB", "压缩至 1000 KB", "打开压缩主窗口"];

    [StructLayout(LayoutKind.Sequential)]
    private struct Node { public nint Table; public int References; public int Type; public int Index; }

    private static readonly nint UnknownQuery = (nint)(delegate* unmanaged<Node*, Guid*, nint*, int>)&Query;
    private static readonly nint UnknownAdd = (nint)(delegate* unmanaged<Node*, uint>)&Add;
    private static readonly nint UnknownRelease = (nint)(delegate* unmanaged<Node*, uint>)&Release;
    private static readonly nint FactoryTable = Table(UnknownQuery, UnknownAdd, UnknownRelease,
        (nint)(delegate* unmanaged<Node*, nint, Guid*, nint*, int>)&CreateInstance,
        (nint)(delegate* unmanaged<Node*, int, int>)&LockServer);
    private static readonly nint CommandTable = Table(UnknownQuery, UnknownAdd, UnknownRelease,
        (nint)(delegate* unmanaged<Node*, nint, nint*, int>)&Title,
        (nint)(delegate* unmanaged<Node*, nint, nint*, int>)&Icon,
        (nint)(delegate* unmanaged<Node*, nint, nint*, int>)&Tip,
        (nint)(delegate* unmanaged<Node*, Guid*, int>)&CanonicalName,
        (nint)(delegate* unmanaged<Node*, nint, int, uint*, int>)&State,
        (nint)(delegate* unmanaged<Node*, nint, nint, int>)&Invoke,
        (nint)(delegate* unmanaged<Node*, uint*, int>)&Flags,
        (nint)(delegate* unmanaged<Node*, nint*, int>)&Subcommands);
    private static readonly nint EnumTable = Table(UnknownQuery, UnknownAdd, UnknownRelease,
        (nint)(delegate* unmanaged<Node*, uint, nint*, uint*, int>)&Next,
        (nint)(delegate* unmanaged<Node*, uint, int>)&Skip,
        (nint)(delegate* unmanaged<Node*, int>)&Reset,
        (nint)(delegate* unmanaged<Node*, nint*, int>)&Clone);

    private static nint Table(params nint[] entries)
    {
        var ptr = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(ExplorerExtension), entries.Length * sizeof(nint));
        entries.AsSpan().CopyTo(new Span<nint>(ptr, entries.Length));
        return (nint)ptr;
    }

    private static Node* New(int type, int index = 0)
    {
        var obj = (Node*)NativeMemory.AllocZeroed((nuint)sizeof(Node));
        obj->Table = type == Factory ? FactoryTable : type == Command ? CommandTable : EnumTable;
        obj->Type = type;
        obj->References = 1;
        obj->Index = index;
        return obj;
    }

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static int GetClassObject(Guid* clsid, Guid* iid, nint* result)
    {
        if (result == null || clsid == null || iid == null) return Pointer;
        *result = 0;
        if (*clsid != ClassId) return unchecked((int)0x80040111);
        if (*iid != FactoryId && *iid != UnknownId) return NoInterface;
        *result = (nint)New(Factory);
        return Ok;
    }

    // Native AOT libraries cannot safely be unloaded from a live host.
    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int CanUnload() => False;

    [UnmanagedCallersOnly]
    private static int Query(Node* self, Guid* iid, nint* result)
    {
        if (result == null || iid == null) return Pointer;
        *result = 0;
        Guid expected = self->Type == Factory ? FactoryId : self->Type == Command ? CommandId : EnumId;
        if (*iid != UnknownId && *iid != expected) return NoInterface;
        Interlocked.Increment(ref self->References);
        *result = (nint)self;
        return Ok;
    }
    [UnmanagedCallersOnly] private static uint Add(Node* self) => (uint)Interlocked.Increment(ref self->References);
    [UnmanagedCallersOnly] private static uint Release(Node* self)
    {
        int left = Interlocked.Decrement(ref self->References);
        if (left == 0) NativeMemory.Free(self);
        return (uint)left;
    }
    [UnmanagedCallersOnly]
    private static int CreateInstance(Node* _, nint outer, Guid* iid, nint* result)
    {
        if (result == null || iid == null) return Pointer;
        *result = 0;
        if (outer != 0) return unchecked((int)0x80040110);
        if (*iid != CommandId && *iid != UnknownId) return NoInterface;
        *result = (nint)New(Command);
        return Ok;
    }
    [UnmanagedCallersOnly] private static int LockServer(Node* _, int takeLock) => Ok;

    [UnmanagedCallersOnly]
    private static int Title(Node* self, nint selection, nint* text)
    {
        if (text == null) return Pointer;
        *text = 0;
        try
        {
            string caption = Captions[self->Index];
            if (self->Index == 2 && UserState.ForUser().Read() is { } settings)
                caption += $"（{settings.Kilobytes} KB）";
            *text = Marshal.StringToCoTaskMemUni(caption);
            return Ok;
        }
        catch (Exception ex) { return ex.HResult; }
    }
    [UnmanagedCallersOnly]
    private static int Icon(Node* self, nint selection, nint* result)
    {
        if (result == null) return Pointer;
        *result = 0;
        if (self->Index != 0) return NotImplemented;
        try { *result = Marshal.StringToCoTaskMemUni(Executable() + ",0"); return Ok; }
        catch (Exception ex) { return ex.HResult; }
    }
    [UnmanagedCallersOnly]
    private static int Tip(Node* _, nint selection, nint* result)
    { if (result == null) return Pointer; *result = 0; return NotImplemented; }

    [UnmanagedCallersOnly]
    private static int CanonicalName(Node* self, Guid* result)
    {
        if (result == null) return Pointer;
        *result = self->Index == 0 ? ClassId : new Guid(0x26600110 + self->Index, 0x1542, 0x4fa5, 0xa3, 0x26, 0xc3, 0x37, 0x3b, 0xb6, 0x23, 0x85);
        return Ok;
    }
    [UnmanagedCallersOnly]
    private static int State(Node* self, nint selection, int allowSlow, uint* result)
    {
        if (result == null) return Pointer;
        *result = 0;
        try
        {
            if (self->Index == 0 && selection != 0 && !VisitSelection(selection, null)) *result = 2;
            else if (self->Index == 2 && UserState.ForUser().Read() is null) *result = 1;
            return Ok;
        }
        catch { *result = self->Index == 0 ? 2u : 1u; return Ok; }
    }

    [UnmanagedCallersOnly]
    private static int Invoke(Node* self, nint selection, nint bindContext)
    {
        try
        {
            var paths = new List<string>();
            if (!VisitSelection(selection, paths)) return unchecked((int)0x80070057);
            var store = UserState.ForUser();
            Guid job = store.QueueSelection(paths);
            try
            {
                var start = new ProcessStartInfo(Executable()) { UseShellExecute = false };
                start.ArgumentList.Add("--shell");
                start.ArgumentList.Add(Actions[self->Index]);
                start.ArgumentList.Add(job.ToString("N"));
                using var app = Process.Start(start);
            }
            catch { store.DiscardSelection(job); throw; }
            return Ok;
        }
        catch (Exception ex) { return ex.HResult; }
    }

    private static bool VisitSelection(nint selection, List<string>? paths)
    {
        if (selection == 0) return false;
        nint* array = *(nint**)selection;
        uint count = 0;
        if (((delegate* unmanaged<nint, uint*, int>)array[7])(selection, &count) < 0 || count == 0) return false;
        for (uint i = 0; i < count; i++)
        {
            nint item = 0, name = 0;
            if (((delegate* unmanaged<nint, uint, nint*, int>)array[8])(selection, i, &item) < 0 || item == 0) return false;
            var methods = *(nint**)item;
            try
            {
                if (((delegate* unmanaged<nint, uint, nint*, int>)methods[5])(item, 0x80058000, &name) < 0) return false;
                string? path = Marshal.PtrToStringUni(name);
                if (path is null || !ImageTypes.Includes(path)) return false;
                paths?.Add(path);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
                ((delegate* unmanaged<nint, uint>)methods[2])(item);
            }
        }
        return true;
    }

    [UnmanagedCallersOnly] private static int Flags(Node* self, uint* flags)
    { if (flags == null) return Pointer; *flags = self->Index == 0 ? 1u : 0u; return Ok; }
    [UnmanagedCallersOnly] private static int Subcommands(Node* self, nint* result)
    {
        if (result == null) return Pointer;
        *result = 0;
        if (self->Index != 0) return NotImplemented;
        *result = (nint)New(Enumerator, 1);
        return Ok;
    }
    [UnmanagedCallersOnly]
    private static int Next(Node* self, uint count, nint* commands, uint* fetched)
    {
        if (commands == null || (count != 1 && fetched == null)) return Pointer;
        uint got = 0;
        while (got < count && self->Index < Captions.Length)
            commands[got++] = (nint)New(Command, self->Index++);
        if (fetched != null) *fetched = got;
        return got == count ? Ok : False;
    }
    [UnmanagedCallersOnly] private static int Skip(Node* self, uint count)
    {
        uint amount = Math.Min(count, (uint)(Captions.Length - self->Index));
        self->Index += (int)amount;
        return amount == count ? Ok : False;
    }
    [UnmanagedCallersOnly] private static int Reset(Node* self) { self->Index = 1; return Ok; }
    [UnmanagedCallersOnly] private static int Clone(Node* self, nint* result)
    { if (result == null) return Pointer; *result = (nint)New(Enumerator, self->Index); return Ok; }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetModuleHandleExW(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetModuleFileNameW(nint module, char* path, uint length);

    private static string Executable()
    {
        var address = (nint)(delegate* unmanaged<Guid*, Guid*, nint*, int>)&GetClassObject;
        if (GetModuleHandleExW(6, address, out nint module) == 0) throw new IOException("找不到菜单扩展。");
        char[] buffer = new char[32768];
        fixed (char* start = buffer)
        {
            uint count = GetModuleFileNameW(module, start, (uint)buffer.Length);
            if (count == 0 || count >= buffer.Length) throw new IOException("菜单扩展路径无效。");
            return Path.Combine(Path.GetDirectoryName(new string(start, 0, (int)count))!, "ImagePress.exe");
        }
    }
}
