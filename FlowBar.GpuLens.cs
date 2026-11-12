using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal sealed unsafe class GpuLens : IDisposable
{
    const uint Bgra = 87;
    const int NotFound = unchecked((int)0x887A0002);
    const int CaptureTimeout = unchecked((int)0x887A0027);
    const int StillDrawing = unchecked((int)0x887A000A);
    static readonly Guid Factory2 = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    static readonly Guid Output1 = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
    static readonly Guid Adapter3 = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
    static readonly Guid DxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    static readonly Guid CompositionDevice = new Guid("c37ea93a-e7aa-450d-b16f-9746cb0407f3");
    static readonly Guid Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(Guid* iid, out IntPtr factory);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int type, IntPtr module,
        uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, IntPtr featureLevel, out IntPtr context);
    [DllImport("dcomp.dll")] static extern int DCompositionCreateDevice(IntPtr device, Guid* iid, out IntPtr composition);
    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)] static extern int D3DCompile(
        byte* src, UIntPtr size, string name, IntPtr defines, IntPtr include, string entry, string target,
        uint flags, uint effects, out IntPtr code, out IntPtr errors);

    [StructLayout(LayoutKind.Sequential)] struct OutputDesc
    {
        public fixed char Name[32];
        public int Left, Top, Right, Bottom, Attached, Rotation;
        public IntPtr Monitor;
    }
    [StructLayout(LayoutKind.Sequential)] struct TextureDesc
    {
        public uint Width, Height, Mips, ArraySize, Format, Samples, Quality, Usage, Bind, Cpu, Misc;
    }
    [StructLayout(LayoutKind.Sequential)] struct SwapDesc
    {
        public uint Width, Height, Format, Stereo, Samples, Quality, Usage, Buffers, Scaling, Effect, Alpha, Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct SamplerDesc
    {
        public uint Filter, U, V, W;
        public float Bias;
        public uint Anisotropy, Comparison;
        public float Red, Green, Blue, Alpha, MinLod, MaxLod;
    }
    [StructLayout(LayoutKind.Sequential)] struct BufferDesc
    {
        public uint Bytes, Usage, Bind, Cpu, Misc, Stride;
    }
    [StructLayout(LayoutKind.Sequential)] struct UavDesc
    {
        public uint Format, Dimension, First, Count, Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct FrameInfo
    {
        public long LastPresent, LastMouse;
        public uint Accumulated, Coalesced, Masked;
        public int PointerX, PointerY, PointerVisible;
        public uint Metadata, PointerBytes;
    }
    [StructLayout(LayoutKind.Sequential)] struct Box
    {
        public uint Left, Top, Front, Right, Bottom, Back;
        public Box(int x, int y, int width, int height)
        {
            Left = (uint)x; Top = (uint)y; Front = 0;
            Right = (uint)(x + width); Bottom = (uint)(y + height); Back = 1;
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct Viewport
    {
        public float X, Y, Width, Height, MinDepth, MaxDepth;
    }
    [StructLayout(LayoutKind.Sequential)] struct Mapped
    {
        public IntPtr Data;
        public uint RowPitch, DepthPitch;
    }
    [StructLayout(LayoutKind.Sequential)] struct MemoryInfo
    {
        public ulong Budget, Usage, AvailableForReservation, Reservation;
    }

    IntPtr factory, adapter, output, device, context, duplication, composition, target, visual, swap;
    IntPtr strip, srv, vertex, pixel, sampler, constants, renderView, backBuffer;
    IntPtr previous, previousView, compare, compareFlag, flagView, flagReadback;
    uint tilesX, tilesY;
    bool hasPresented, opacityChanged;
    readonly float[] shaderParameters = new float[16];
    readonly IntPtr[] probes;
    readonly long[] stamps = new long[4];
    int written, read;
    Box stripBox, probeBox;
    public ulong MemoryBefore { get; private set; }
    public int AccumulatedFrames { get; private set; }

    public GpuLens(string display, Rectangle screen, Rectangle lens, IntPtr hwnd, bool ios, bool diagnostics, float radius, float band, float shift, int blur)
    {
        probes = new IntPtr[diagnostics ? 4 : 0];
        try
        {
            var iid = Factory2;
            Check(CreateDXGIFactory1(&iid, out factory), "CreateDXGIFactory1");
            FindOutput(display);
            if (diagnostics) MemoryBefore = LocalMemory();
            Check(D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, IntPtr.Zero, out context), "D3D11CreateDevice");
            var out1 = Query(output, Output1);
            try
            {
                IntPtr capture;
                Check(Method<DuplicateOutput>(out1, 22)(out1,device,&capture), "DuplicateOutput");
                duplication = capture;
            }
            finally { Release(ref out1); }

            var source = lens;
            int padding = (int)Math.Ceiling(shift) + blur + 2;
            source.Inflate(padding, padding);
            source.Intersect(screen);
            stripBox = new Box(source.Left - screen.Left, source.Top - screen.Top, source.Width, source.Height);
            probeBox = new Box(lens.Width / 2, lens.Height / 2, 1, 1);
            strip = MakeTexture(source.Width, source.Height, 0, 8, 0);
            IntPtr view;
            Check(Method<CreateView>(device, 7)(device,strip,IntPtr.Zero,&view), "CreateShaderResourceView");
            srv = view;
            previous = MakeTexture(source.Width, source.Height, 0, 8, 0);
            Check(Method<CreateView>(device, 7)(device,previous,IntPtr.Zero,&view), "Create previous frame view");
            previousView = view;
            tilesX = (uint)(source.Width + 15) / 16;
            tilesY = (uint)(source.Height + 15) / 16;
            for (int i = 0; i < probes.Length; i++) probes[i] = MakeTexture(1, 1, 3, 0, 0x20000);
            CreateComposition(hwnd, lens);
            PreparePipeline(lens, source, ios, screen, radius, band, shift, blur);
        }
        catch { Dispose(); throw; }
    }

    void FindOutput(string name)
    {
        for (uint i = 0; ; i++)
        {
            IntPtr candidate;
            int result = Method<Enumerate>(factory, 12)(factory,i,&candidate);
            if (result == NotFound) throw new InvalidOperationException("DXGI output missing: " + name);
            Check(result, "EnumAdapters1");
            try
            {
                for (uint j = 0; ; j++)
                {
                    IntPtr monitor;
                    result = Method<Enumerate>(candidate, 7)(candidate,j,&monitor);
                    if (result == NotFound) break;
                    Check(result, "EnumOutputs");
                    try
                    {
                        OutputDesc desc;
                        Check(Method<GetOutputDescription>(monitor, 7)(monitor,&desc), "GetOutputDesc");
                        if (new string(desc.Name) != name) continue;
                        if (desc.Rotation > 1) throw new NotSupportedException("The probe requires a landscape output.");
                        adapter = candidate; candidate = IntPtr.Zero; output = monitor; monitor = IntPtr.Zero;
                        return;
                    }
                    finally { Release(ref monitor); }
                }
            }
            finally { Release(ref candidate); }
        }
    }

    void CreateComposition(IntPtr hwnd, Rectangle lens)
    {
        var desc = new SwapDesc { Width = (uint)lens.Width, Height = (uint)lens.Height,
            Format = Bgra, Samples = 1, Usage = 0x20, Buffers = 2, Effect = 3, Alpha = 1 };
        IntPtr chain;
        Check(Method<CreateSwapChain>(factory, 24)(factory,device,&desc,IntPtr.Zero,&chain), "CreateSwapChainForComposition");
        swap = chain;
        var dxgi = Query(device, DxgiDevice);
        var iid = CompositionDevice;
        try { Check(DCompositionCreateDevice(dxgi, &iid, out composition), "DCompositionCreateDevice"); }
        finally { Release(ref dxgi); }
        IntPtr handle;
        Check(Method<CreateTarget>(composition, 6)(composition,hwnd,1,&handle), "CreateTargetForHwnd");
        target = handle;
        Check(Method<MakeObject>(composition, 7)(composition,&handle), "CreateVisual");
        visual = handle;
        Check(Method<SetObject>(visual, 15)(visual,swap), "SetContent");
        Check(Method<SetObject>(target, 3)(target,visual), "SetRoot");
        Check(Method<NoArgs>(composition, 3)(composition), "Commit");
        iid = Texture2D;
        Check(Method<GetBuffer>(swap, 9)(swap,0,&iid,&handle), "GetBuffer");
        backBuffer = handle;
        Check(Method<CreateView>(device, 9)(device,backBuffer,IntPtr.Zero,&handle), "CreateRenderTargetView");
        renderView = handle;
    }

    void PreparePipeline(Rectangle lens, Rectangle source, bool ios, Rectangle screen, float radius, float band, float shift, int blur)
    {
        byte[] code;
        using (var shader = typeof(GpuLens).Assembly.GetManifestResourceStream("FlowBar.Glass.hlsl"))
        using (var text = new StreamReader(shader))
            code = Encoding.UTF8.GetBytes(text.ReadToEnd());
        vertex = MakeShader(code, "Fullscreen", "vs_4_0", 12);
        pixel = MakeShader(code, "Refract", "ps_4_0", 15);
        compare = MakeShader(code, "CheckChanges", "cs_5_0", 18);
        compareFlag = MakeBuffer(new BufferDesc { Bytes = 4, Bind = 0x80 });
        flagReadback = MakeBuffer(new BufferDesc { Bytes = 4, Usage = 3, Cpu = 0x20000 });
        var uav = new UavDesc { Format = 42, Dimension = 1, Count = 1 };
        IntPtr handle;
        Check(Method<CreateUav>(device, 8)(device,compareFlag,&uav,&handle), "Create change flag view");
        flagView = handle;
        var desc = new SamplerDesc { Filter = 0x15, U = 3, V = 3, W = 3, Comparison = 8, MaxLod = float.MaxValue };
        Check(Method<CreateSampler>(device, 23)(device,&desc,&handle), "CreateSamplerState");
        sampler = handle;
        constants = MakeBuffer(new BufferDesc { Bytes = 64, Bind = 4 });
        float half = Math.Min(lens.Width, lens.Height) * 0.5f;
        var data = shaderParameters;
        data[0] = lens.Width; data[1] = lens.Height;
        data[2] = lens.X - source.X; data[3] = lens.Y - source.Y;
        data[4] = source.Width; data[5] = source.Height;
        data[6] = Math.Min(radius, half); data[7] = Math.Min(band, half);
        data[8] = shift; data[9] = blur; data[10] = ios ? 1 : 0; data[11] = 1;
        data[12] = lens.Left - screen.Left < shift ? -1 : 1;
        data[13] = lens.Top - screen.Top < shift ? -1 : 1;
        data[14] = screen.Right - lens.Right < shift ? -1 : 1;
        data[15] = screen.Bottom - lens.Bottom < shift ? -1 : 1;
        UploadParameters();
        var viewport = new Viewport { Width = lens.Width, Height = lens.Height, MaxDepth = 1 };
        Method<SetViewport>(context, 44)(context,1,&viewport);
        Method<SetTopology>(context, 24)(context,4);
        Method<SetShader>(context, 11)(context,vertex,IntPtr.Zero,0);
        Method<SetShader>(context, 9)(context,pixel,IntPtr.Zero,0);
        handle = sampler;
        Method<BindViews>(context, 10)(context,0,1,&handle);
        handle = constants;
        Method<BindViews>(context, 16)(context,0,1,&handle);
    }

    void UploadParameters()
    {
        fixed (float* data = shaderParameters)
            Method<UpdateResource>(context, 48)(context,constants,0,IntPtr.Zero,data,0,0);
        opacityChanged = false;
    }

    IntPtr MakeShader(byte[] source, string entry, string profile, int method)
    {
        fixed (byte* data = source)
        {
            IntPtr code, errors;
            int hr = D3DCompile(data, (UIntPtr)(uint)source.Length, null, IntPtr.Zero, IntPtr.Zero, entry, profile, 1u << 15, 0, out code, out errors);
            try
            {
                if (hr < 0 && errors != IntPtr.Zero)
                    throw new InvalidOperationException(Marshal.PtrToStringAnsi(BlobPointer(errors)));
                Check(hr, "D3DCompile " + entry);
                IntPtr shader;
                UIntPtr size = Method<GetBlobSize>(code, 4)(code);
                Check(Method<CreateShader>(device, method)(device,BlobPointer(code),size,IntPtr.Zero,&shader), "Create " + entry);
                return shader;
            }
            finally { Release(ref errors); Release(ref code); }
        }
    }

    IntPtr MakeTexture(int w, int h, uint usage, uint bind, uint cpu)
    {
        var desc = new TextureDesc { Width = (uint)w, Height = (uint)h, Mips = 1, ArraySize = 1,
            Format = Bgra, Samples = 1, Usage = usage, Bind = bind, Cpu = cpu };
        IntPtr texture;
        Check(Method<CreateTexture>(device, 5)(device,&desc,IntPtr.Zero,&texture), "CreateTexture2D");
        return texture;
    }

    IntPtr MakeBuffer(BufferDesc desc)
    {
        IntPtr buffer;
        Check(Method<CreateBuffer>(device, 3)(device,&desc,IntPtr.Zero,&buffer), "CreateBuffer");
        return buffer;
    }

    bool BackgroundChanged()
    {
        uint* clear = stackalloc uint[4];
        for (int i = 0; i < 4; i++) clear[i] = 0;
        Method<ClearUav>(context, 51)(context,flagView,clear);
        IntPtr* views = stackalloc IntPtr[2];
        views[0] = srv; views[1] = previousView;
        IntPtr flag = flagView;
        Method<BindViews>(context, 67)(context,0,2,views);
        Method<BindUavs>(context, 68)(context,0,1,&flag,null);
        Method<SetShader>(context, 69)(context,compare,IntPtr.Zero,0);
        Method<Dispatch>(context, 41)(context,tilesX,tilesY,1);
        views[0] = views[1] = flag = IntPtr.Zero;
        Method<BindViews>(context, 67)(context,0,2,views);
        Method<BindUavs>(context, 68)(context,0,1,&flag,null);
        Method<CopyResource>(context, 47)(context,flagReadback,compareFlag);
        Mapped map;
        Check(Method<MapResource>(context, 14)(context,flagReadback,0,1,0,&map), "Map change flag");
        bool changed = *(uint*)map.Data != 0;
        Method<UnmapResource>(context, 15)(context,flagReadback,0);
        return changed;
    }

    void Copy(IntPtr to, IntPtr from, Box area)
    {
        Method<CopyRegion>(context, 46)(context,to,0,0,0,0,from,0,&area);
    }

    bool Capture(out long timestamp)
    {
        timestamp = 0;
        FrameInfo info;
        IntPtr resource;
        int hr = Method<AcquireFrame>(duplication, 8)(duplication,10,&info,&resource);
        if (hr == CaptureTimeout) return false;
        Check(hr, "AcquireNextFrame");
        try
        {
            if (info.LastPresent == 0) return false;
            AccumulatedFrames += (int)Math.Max(0L, (long)info.Accumulated - 1);
            var captured = Query(resource, Texture2D);
            try { Copy(strip, captured, stripBox); }
            finally { Release(ref captured); }
            timestamp = info.LastPresent;
        }
        finally
        {
            Release(ref resource);
            Check(Method<NoArgs>(duplication, 14)(duplication), "ReleaseFrame");
        }
        return true;
    }

    public bool DrawNext()
    {
        long timestamp;
        bool changed = Capture(out timestamp) && (!hasPresented || BackgroundChanged());
        if (changed) Method<CopyResource>(context, 47)(context,previous,strip);
        else if (!hasPresented || !opacityChanged) return false;
        if (opacityChanged) UploadParameters();
        IntPtr view = renderView;
        Method<SetTargets>(context, 33)(context,1,&view,IntPtr.Zero);
        view = srv;
        Method<BindViews>(context, 8)(context,0,1,&view);
        Method<Draw>(context, 13)(context,3,0);
        view = IntPtr.Zero;
        Method<BindViews>(context, 8)(context,0,1,&view);
        Method<SetTargets>(context, 33)(context,0,null,IntPtr.Zero);
        if (written - read < probes.Length)
        {
            int slot = written++ % probes.Length;
            Copy(probes[slot], backBuffer, probeBox);
            stamps[slot] = timestamp;
        }
        Check(Method<Present>(swap, 8)(swap,0,0), "Present");
        hasPresented = true;
        return true;
    }

    public void SetOpacity(float alpha)
    {
        if (shaderParameters[11] == alpha) return;
        shaderParameters[11] = alpha;
        opacityChanged = true;
    }

    public bool ReadProbe(out int rgb, out long stamp)
    {
        rgb = 0; stamp = 0;
        if (written == read) return false;
        int slot = read % probes.Length;
        Mapped map;
        int hr = Method<MapResource>(context, 14)(context,probes[slot],0,1,0x100000,&map);
        if (hr == StillDrawing) return false;
        Check(hr, "Map probe");
        rgb = *(int*)map.Data & 0xFFFFFF;
        stamp = stamps[slot];
        Method<UnmapResource>(context, 15)(context,probes[slot],0);
        read++;
        return true;
    }

    public ulong LocalMemory()
    {
        var mem = Query(adapter, Adapter3);
        try
        {
            MemoryInfo info;
            Check(Method<QueryMemory>(mem, 14)(mem,0,0,&info), "QueryVideoMemoryInfo");
            return info.Usage;
        }
        finally { Release(ref mem); }
    }

    public void Dispose()
    {
        for (int i = 0; i < probes.Length; i++) Release(ref probes[i]);
        Release(ref flagReadback); Release(ref flagView); Release(ref compareFlag); Release(ref compare);
        Release(ref previousView); Release(ref previous);
        Release(ref constants); Release(ref sampler); Release(ref pixel); Release(ref vertex);
        Release(ref renderView); Release(ref backBuffer); Release(ref srv); Release(ref strip);
        if (target != IntPtr.Zero)
        {
            Method<SetObject>(target, 3)(target, IntPtr.Zero);
            Method<NoArgs>(composition, 3)(composition);
        }
        Release(ref visual); Release(ref target); Release(ref composition); Release(ref swap);
        Release(ref duplication); Release(ref context); Release(ref device);
        Release(ref output); Release(ref adapter); Release(ref factory);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int DuplicateOutput(IntPtr self, IntPtr device, IntPtr* result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateView(IntPtr self, IntPtr resource, IntPtr description, IntPtr* view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Enumerate(IntPtr self, uint index, IntPtr* result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetOutputDescription(IntPtr self, void* description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateSwapChain(IntPtr self, IntPtr device, SwapDesc* description, IntPtr output, IntPtr* chain);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateTarget(IntPtr self, IntPtr hwnd, int topmost, IntPtr* target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int MakeObject(IntPtr self, IntPtr* result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetObject(IntPtr self, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int NoArgs(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetBuffer(IntPtr self, uint index, Guid* iid, IntPtr* buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateUav(IntPtr self, IntPtr resource, UavDesc* description, IntPtr* view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateSampler(IntPtr self, SamplerDesc* description, IntPtr* sampler);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void UpdateResource(IntPtr self, IntPtr resource, uint subresource, IntPtr box, void* source, uint rowPitch, uint depthPitch);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void SetViewport(IntPtr self, uint count, Viewport* viewports);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void SetTopology(IntPtr self, uint topology);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void SetShader(IntPtr self, IntPtr shader, IntPtr classes, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void BindViews(IntPtr self, uint first, uint count, IntPtr* views);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate UIntPtr GetBlobSize(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateShader(IntPtr self, IntPtr code, UIntPtr size, IntPtr linkage, IntPtr* shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateTexture(IntPtr self, TextureDesc* description, IntPtr data, IntPtr* texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateBuffer(IntPtr self, BufferDesc* description, IntPtr data, IntPtr* buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void ClearUav(IntPtr self, IntPtr view, uint* values);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void BindUavs(IntPtr self, uint first, uint count, IntPtr* views, uint* initialCounts);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void Dispatch(IntPtr self, uint x, uint y, uint z);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void CopyResource(IntPtr self, IntPtr destination, IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int MapResource(IntPtr self, IntPtr resource, uint subresource, uint mode, uint flags, Mapped* mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void UnmapResource(IntPtr self, IntPtr resource, uint subresource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void CopyRegion(IntPtr self, IntPtr destination, uint destSubresource, uint x, uint y, uint z, IntPtr source, uint sourceSubresource, Box* box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int AcquireFrame(IntPtr self, uint timeout, FrameInfo* info, IntPtr* resource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void SetTargets(IntPtr self, uint count, IntPtr* targets, IntPtr depth);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void Draw(IntPtr self, uint count, uint start);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Present(IntPtr self, uint syncInterval, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int QueryMemory(IntPtr self, uint node, uint segment, MemoryInfo* info);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate IntPtr GetBlobPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int QueryInterface(IntPtr self, Guid* iid, IntPtr* result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint ReleaseObject(IntPtr self);

    static class NativeMethods<T> where T : class
    {
        [ThreadStatic] public static Dictionary<IntPtr, T> Cache;
    }

    static T Method<T>(IntPtr instance, int index) where T : class
    {
        IntPtr address = (*(IntPtr**)instance)[index];
        var cache = NativeMethods<T>.Cache ?? (NativeMethods<T>.Cache = new Dictionary<IntPtr, T>());
        T method;
        if (!cache.TryGetValue(address, out method))
        {
            method = (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
            cache.Add(address, method);
        }
        return method;
    }

    static IntPtr BlobPointer(IntPtr blob) { return Method<GetBlobPointer>(blob, 3)(blob); }
    static IntPtr Query(IntPtr instance, Guid iid)
    {
        IntPtr result;
        Check(Method<QueryInterface>(instance, 0)(instance,&iid,&result), "QueryInterface " + iid);
        return result;
    }
    static void Release(ref IntPtr instance)
    {
        if (instance == IntPtr.Zero) return;
        Method<ReleaseObject>(instance, 2)(instance);
        instance = IntPtr.Zero;
    }
    static void Check(int hr, string operation)
    {
        if (hr < 0) throw new COMException(operation + " (0x" + hr.ToString("X8") + ")", hr);
    }
}
