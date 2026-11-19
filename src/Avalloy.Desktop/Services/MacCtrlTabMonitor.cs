using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Avalloy.Services;

/// <summary>
/// macOS intercept for physical Ctrl+Tab via an
/// <c>NSEvent.addLocalMonitorForEventsMatchingMask:handler:</c> hook. AppKit
/// treats Ctrl+Tab as a keyboard focus-traversal gesture and consumes it
/// before Avalonia's KeyDown tunnel runs, so an Avalonia-layer KeyBinding
/// can't catch it. Installing a local NSEvent monitor lets us see the event
/// in the AppKit dispatch stage and either forward it to a managed callback
/// (returning <c>nil</c> to swallow the event) or pass it through.
/// </summary>
/// <remarks>
/// Same Obj-C interop pattern used elsewhere for macOS shell integration:
/// <list type="bullet">
///   <item>Cached classes/selectors resolved once in <see cref="EnsureInitialized"/>.</item>
///   <item>NativeLibrary.Load for AppKit so objc_getClass resolves in bare
///   console / test-runner processes that don't pull AppKit in transitively.</item>
///   <item>An immortal block (<c>BLOCK_IS_GLOBAL</c>) backed by a managed
///   <see cref="GCHandle"/> to thread state through to the Obj-C runtime.</item>
///   <item>The block's invoke function pointer is wired via
///   <c>[UnmanagedCallersOnly]</c> for direct Cdecl entry from AppKit.</item>
/// </list>
/// Returning <see cref="IntPtr.Zero"/> from the block consumes the event so
/// AppKit's focus traversal doesn't run after we've handled it.
/// </remarks>
public static unsafe class MacCtrlTabMonitor
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";
    private const string Libsystem = "/usr/lib/libSystem.dylib";

    private const nuint NSEventMaskKeyDown = 1u << 10;
    private const nuint NSEventModifierFlagShift = 1u << 17;
    private const nuint NSEventModifierFlagControl = 1u << 18;
    private const nuint NSEventModifierFlagOption = 1u << 19;
    private const nuint NSEventModifierFlagCommand = 1u << 20;
    private const ushort TabKeyCode = 48;

    private const int BLOCK_IS_GLOBAL = 1 << 28;

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Libobjc, CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_addLocalMonitor(IntPtr receiver, IntPtr selector, nuint mask, IntPtr block);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_removeMonitor(IntPtr receiver, IntPtr selector, IntPtr monitor);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern ushort msg_ushort(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern nuint msg_nuint(IntPtr receiver, IntPtr selector);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventMonitorBlock
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
        public IntPtr HandleId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    private sealed class MonitorState
    {
        public required Action<bool> OnCtrlTab;
    }

    private sealed class Registration : IDisposable
    {
        // Held as fields so the GC roots them for the lifetime of the
        // Registration even though we never free them — see Dispose.
#pragma warning disable IDE0052
        private readonly GCHandle _stateHandle;
        private readonly IntPtr _block;
#pragma warning restore IDE0052
        private IntPtr _monitor;
        private bool _disposed;

        public Registration(IntPtr monitor, IntPtr block, GCHandle stateHandle)
        {
            _monitor = monitor;
            _block = block;
            _stateHandle = stateHandle;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_monitor != IntPtr.Zero && s_NSEvent != IntPtr.Zero && s_removeMonitor != IntPtr.Zero)
            {
                msg_removeMonitor(s_NSEvent, s_removeMonitor, _monitor);
                _monitor = IntPtr.Zero;
            }

            // DO NOT free _block or _stateHandle. removeMonitor: only releases
            // AppKit's retain on the _NSEventObserver — the observer can still
            // be sitting in an autorelease pool or mid-flight event delivery.
            // When it eventually dealloc's, it sends release to the handler
            // block. If we've FreeHGlobal'd the block by then, objc_release
            // reads Isa from freed memory and SIGSEGVs (observed on Cmd+Q
            // right after launch — crash report 2026-05-20). The block is a
            // global block carrying just a function pointer + a GCHandle; a
            // few dozen bytes leaked for the process lifetime is the right
            // trade.
        }
    }

    private static IntPtr s_NSEvent;
    private static IntPtr s_addLocalMonitor;
    private static IntPtr s_removeMonitor;
    private static IntPtr s_keyCode;
    private static IntPtr s_modifierFlags;
    private static IntPtr s_descriptorPtr;
    private static IntPtr s_NSConcreteGlobalBlock;

    // Initialization state: 0 = untried, 1 = ready, 2 = permanently failed.
    // Matches the pattern in MacQuickLookIcon so an init failure stays sticky
    // and we don't repeatedly retry framework loading on every Install call.
    private static int s_initState;
    private static readonly object s_initLock = new();

    /// <summary>True only on macOS — the monitor is a no-op everywhere else.</summary>
    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>Diagnostic — describes the most recent failure step in
    /// <see cref="Install"/>, or null when the last call succeeded.
    /// Mirrors the convention used by <see cref="MacShellIcon"/> and
    /// <see cref="MacQuickLookIcon"/>.</summary>
    public static string? LastErrorStep { get; private set; }

    public static IDisposable? Install(Action<bool> onCtrlTab)
    {
        LastErrorStep = null;
        if (!IsSupported) { LastErrorStep = "not macos"; return null; }
        if (!EnsureInitialized()) { LastErrorStep ??= "init failed"; return null; }

        var state = new MonitorState { OnCtrlTab = onCtrlTab };
        var stateHandle = GCHandle.Alloc(state);
        var block = Marshal.AllocHGlobal(Marshal.SizeOf<EventMonitorBlock>());
        var blockValue = new EventMonitorBlock
        {
            Isa = s_NSConcreteGlobalBlock,
            Flags = BLOCK_IS_GLOBAL,
            Reserved = 0,
            Invoke = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&BlockInvoke,
            Descriptor = s_descriptorPtr,
            HandleId = GCHandle.ToIntPtr(stateHandle),
        };
        Marshal.StructureToPtr(blockValue, block, false);

        var monitor = msg_addLocalMonitor(s_NSEvent, s_addLocalMonitor, NSEventMaskKeyDown, block);
        if (monitor == IntPtr.Zero)
        {
            LastErrorStep = "addLocalMonitor returned nil";
            stateHandle.Free();
            Marshal.FreeHGlobal(block);
            return null;
        }

        return new Registration(monitor, block, stateHandle);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr BlockInvoke(IntPtr block, IntPtr nsEvent)
    {
        try
        {
            if (nsEvent == IntPtr.Zero) return nsEvent;
            if (msg_ushort(nsEvent, s_keyCode) != TabKeyCode) return nsEvent;

            var modifiers = msg_nuint(nsEvent, s_modifierFlags);
            if ((modifiers & NSEventModifierFlagControl) == 0) return nsEvent;
            if ((modifiers & (NSEventModifierFlagOption | NSEventModifierFlagCommand)) != 0) return nsEvent;

            var b = (EventMonitorBlock*)block;
            var handle = GCHandle.FromIntPtr(b->HandleId);
            if (handle.Target is MonitorState state)
                state.OnCtrlTab((modifiers & NSEventModifierFlagShift) != 0);

            // Returning nil consumes the event before AppKit focus traversal.
            return IntPtr.Zero;
        }
        catch
        {
            return nsEvent;
        }
    }

    private static bool EnsureInitialized()
    {
        var state = Volatile.Read(ref s_initState);
        if (state == 1) return true;
        if (state == 2) return false;

        lock (s_initLock)
        {
            if (s_initState != 0) return s_initState == 1;

            try
            {
                // Load AppKit lazily so we don't run +load handlers in contexts
                // that lack a pumping run loop (e.g. headless console runners).
                try { NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"); } catch { }

                var libsystem = NativeLibrary.Load(Libsystem);
                s_NSConcreteGlobalBlock = NativeLibrary.GetExport(libsystem, "_NSConcreteGlobalBlock");

                var descriptor = new BlockDescriptor
                {
                    Reserved = 0,
                    Size = (nuint)Marshal.SizeOf<EventMonitorBlock>(),
                };
                s_descriptorPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
                Marshal.StructureToPtr(descriptor, s_descriptorPtr, false);

                s_NSEvent = objc_getClass("NSEvent");
                s_addLocalMonitor = sel_registerName("addLocalMonitorForEventsMatchingMask:handler:");
                s_removeMonitor = sel_registerName("removeMonitor:");
                s_keyCode = sel_registerName("keyCode");
                s_modifierFlags = sel_registerName("modifierFlags");

                var ok = s_NSEvent != IntPtr.Zero
                         && s_NSConcreteGlobalBlock != IntPtr.Zero
                         && s_addLocalMonitor != IntPtr.Zero
                         && s_removeMonitor != IntPtr.Zero
                         && s_keyCode != IntPtr.Zero
                         && s_modifierFlags != IntPtr.Zero;
                if (!ok) LastErrorStep = "missing class/symbol";
                s_initState = ok ? 1 : 2;
                return ok;
            }
            catch (Exception ex)
            {
                LastErrorStep = $"init: {ex.GetType().Name}: {ex.Message}";
                s_initState = 2;
                return false;
            }
        }
    }
}
