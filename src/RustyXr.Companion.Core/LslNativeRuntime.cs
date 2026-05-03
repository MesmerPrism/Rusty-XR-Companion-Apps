using System.Globalization;
using System.Runtime.InteropServices;

namespace RustyXr.Companion.Core;

public sealed record LslRuntimeState(bool Available, string Detail);

public static class LslNativeRuntime
{
    private const int ChannelFormatDouble64 = 2;
    private const int ChannelFormatString = 3;
    private static readonly object ResolverLock = new();
    private static string? _explicitLibraryPath;
    private static nint _loadedHandle;
    private static bool _resolverInstalled;

    public static void Configure(string? explicitLibraryPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitLibraryPath))
        {
            _explicitLibraryPath = Path.GetFullPath(explicitLibraryPath);
        }
    }

    public static LslRuntimeState GetRuntimeState(string? explicitLibraryPath = null)
    {
        Configure(explicitLibraryPath);

        if (!OperatingSystem.IsWindows())
        {
            return new LslRuntimeState(false, "LSL diagnostics currently require Windows because the companion loads lsl.dll through the native C API.");
        }

        EnsureResolverInstalled();
        if (!TryLoad(out var detail))
        {
            return new LslRuntimeState(false, detail);
        }

        try
        {
            var info = NativeMethods.LibraryInfo();
            return new LslRuntimeState(
                true,
                string.IsNullOrWhiteSpace(info)
                    ? $"Loaded lsl.dll from {detail}."
                    : $"{info} Loaded from {detail}.");
        }
        catch (Exception ex)
        {
            return new LslRuntimeState(false, $"lsl.dll loaded from {detail}, but initialization failed: {ex.Message}");
        }
    }

    internal static double LocalClock()
    {
        EnsureAvailable();
        return NativeMethods.LocalClock();
    }

    internal static LslDoubleOutlet CreateDoubleOutlet(string name, string type, string sourceId, int channelCount)
    {
        EnsureAvailable();
        return new LslDoubleOutlet(name, type, sourceId, channelCount);
    }

    internal static LslDoubleInlet ResolveDoubleInlet(string property, string value, TimeSpan timeout, int channelCount)
    {
        EnsureAvailable();
        var streamInfo = ResolveOne(property, value, timeout);
        return new LslDoubleInlet(streamInfo, channelCount);
    }

    internal static LslStringInlet ResolveStringInlet(string property, string value, TimeSpan timeout, int channelCount = 1)
    {
        EnsureAvailable();
        var streamInfo = ResolveOne(property, value, timeout);
        return new LslStringInlet(streamInfo, channelCount);
    }

    internal static LslTimeCorrectionSample GetTimeCorrection(nint inlet, TimeSpan timeout)
    {
        var remote = 0d;
        var uncertainty = 0d;
        var error = 0;
        var correction = NativeMethods.TimeCorrectionEx(inlet, ref remote, ref uncertainty, timeout.TotalSeconds, ref error);
        if (error != 0)
        {
            throw new InvalidOperationException($"LSL time correction failed ({ErrorName(error)}): {NativeMethods.LastError()}");
        }

        return new LslTimeCorrectionSample(correction, remote, uncertainty);
    }

    private static nint ResolveOne(string property, string value, TimeSpan timeout)
    {
        var buffer = new IntPtr[8];
        var count = NativeMethods.ResolveByProperty(buffer, (uint)buffer.Length, property, value, 1, timeout.TotalSeconds);
        if (count < 0)
        {
            throw new InvalidOperationException($"LSL stream resolution failed ({ErrorName(count)}): {NativeMethods.LastError()}");
        }

        if (count == 0 || buffer[0] == IntPtr.Zero)
        {
            throw new TimeoutException($"No LSL stream resolved for {property}={value} within {timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds.");
        }

        for (var index = 1; index < buffer.Length; index++)
        {
            if (buffer[index] != IntPtr.Zero)
            {
                NativeMethods.DestroyStreamInfo(buffer[index]);
            }
        }

        return buffer[0];
    }

    private static void EnsureAvailable()
    {
        var state = GetRuntimeState();
        if (!state.Available)
        {
            throw new InvalidOperationException(state.Detail);
        }
    }

    private static void EnsureResolverInstalled()
    {
        lock (ResolverLock)
        {
            if (_resolverInstalled)
            {
                return;
            }

            try
            {
                NativeLibrary.SetDllImportResolver(
                    typeof(LslNativeRuntime).Assembly,
                    static (libraryName, assembly, searchPath) =>
                    {
                        if (!IsLslLibraryName(libraryName))
                        {
                            return IntPtr.Zero;
                        }

                        return TryLoad(out _) ? _loadedHandle : IntPtr.Zero;
                    });
            }
            catch (InvalidOperationException)
            {
                // Another type in this assembly already installed the resolver.
            }

            _resolverInstalled = true;
        }
    }

    private static bool TryLoad(out string detail)
    {
        lock (ResolverLock)
        {
            if (_loadedHandle != IntPtr.Zero)
            {
                detail = "previously loaded native handle";
                return true;
            }

            foreach (var candidate in CandidateLibraryPaths())
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    _loadedHandle = NativeLibrary.Load(candidate);
                    detail = candidate;
                    return true;
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
                {
                    detail = $"{candidate}: {ex.Message}";
                }
            }

            if (NativeLibrary.TryLoad("lsl", typeof(LslNativeRuntime).Assembly, DllImportSearchPath.SafeDirectories, out _loadedHandle) ||
                NativeLibrary.TryLoad("lsl.dll", typeof(LslNativeRuntime).Assembly, DllImportSearchPath.SafeDirectories, out _loadedHandle))
            {
                detail = "system native-library search path";
                return true;
            }

            detail = "lsl.dll was not found. Pass --lsl-dll <path>, set RUSTY_XR_LSL_DLL, place lsl.dll beside the companion executable, or put it on PATH.";
            return false;
        }
    }

    private static IEnumerable<string> CandidateLibraryPaths()
    {
        if (!string.IsNullOrWhiteSpace(_explicitLibraryPath))
        {
            yield return _explicitLibraryPath;
        }

        var env = Environment.GetEnvironmentVariable("RUSTY_XR_LSL_DLL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return env;
        }

        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "lsl.dll");
        yield return Path.Combine(baseDirectory, "runtimes", "win-x64", "native", "lsl.dll");

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(segment, "lsl.dll");
        }
    }

    private static bool IsLslLibraryName(string libraryName) =>
        string.Equals(libraryName, "lsl", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(libraryName, "lsl.dll", StringComparison.OrdinalIgnoreCase);

    private static string ErrorName(int code) =>
        code switch
        {
            0 => "no error",
            -1 => "timeout",
            -2 => "stream lost",
            -3 => "invalid argument",
            -4 => "internal error",
            _ => "unknown error"
        };

    internal sealed class LslDoubleOutlet : IDisposable
    {
        private nint _streamInfo;
        private nint _outlet;
        private readonly int _channelCount;

        public LslDoubleOutlet(string name, string type, string sourceId, int channelCount)
        {
            _channelCount = Math.Clamp(channelCount, 1, 128);
            _streamInfo = NativeMethods.CreateStreamInfo(name, type, _channelCount, 0d, ChannelFormatDouble64, sourceId);
            if (_streamInfo == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Could not create LSL stream info: {NativeMethods.LastError()}");
            }

            _outlet = NativeMethods.CreateOutlet(_streamInfo, 1, 8);
            if (_outlet == IntPtr.Zero)
            {
                Dispose();
                throw new InvalidOperationException($"Could not create LSL outlet: {NativeMethods.LastError()}");
            }
        }

        public void Push(double[] values, double timestampSeconds)
        {
            if (values.Length != _channelCount)
            {
                throw new ArgumentException($"Expected {_channelCount} LSL channels, got {values.Length}.", nameof(values));
            }

            var result = NativeMethods.PushDoubleSample(_outlet, values, timestampSeconds, 1);
            if (result != 0)
            {
                throw new InvalidOperationException($"LSL double sample push failed ({ErrorName(result)}): {NativeMethods.LastError()}");
            }
        }

        public void Dispose()
        {
            if (_outlet != IntPtr.Zero)
            {
                NativeMethods.DestroyOutlet(_outlet);
                _outlet = IntPtr.Zero;
            }

            if (_streamInfo != IntPtr.Zero)
            {
                NativeMethods.DestroyStreamInfo(_streamInfo);
                _streamInfo = IntPtr.Zero;
            }
        }
    }

    internal sealed class LslDoubleInlet : IDisposable
    {
        private nint _inlet;
        private readonly int _channelCount;

        public LslDoubleInlet(nint streamInfo, int channelCount)
        {
            _channelCount = Math.Clamp(channelCount, 1, 128);
            _inlet = NativeMethods.CreateInlet(streamInfo, 8, 1, 1);
            NativeMethods.DestroyStreamInfo(streamInfo);
            if (_inlet == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Could not create LSL inlet: {NativeMethods.LastError()}");
            }
        }

        public nint Handle => _inlet;

        public void Open(TimeSpan timeout)
        {
            var error = 0;
            NativeMethods.OpenStream(_inlet, timeout.TotalSeconds, ref error);
            if (error != 0)
            {
                throw new InvalidOperationException($"LSL double stream open failed ({ErrorName(error)}): {NativeMethods.LastError()}");
            }
        }

        public LslDoubleSample? Pull(TimeSpan timeout)
        {
            var values = new double[_channelCount];
            var error = 0;
            var timestamp = NativeMethods.PullDoubleSample(_inlet, values, values.Length, timeout.TotalSeconds, ref error);
            if (error != 0)
            {
                throw new InvalidOperationException($"LSL double sample pull failed ({ErrorName(error)}): {NativeMethods.LastError()}");
            }

            return timestamp <= 0d ? null : new LslDoubleSample(timestamp, values);
        }

        public void Dispose()
        {
            if (_inlet != IntPtr.Zero)
            {
                NativeMethods.DestroyInlet(_inlet);
                _inlet = IntPtr.Zero;
            }
        }
    }

    internal sealed class LslStringInlet : IDisposable
    {
        private nint _inlet;
        private readonly int _channelCount;

        public LslStringInlet(nint streamInfo, int channelCount)
        {
            _channelCount = Math.Clamp(channelCount, 1, 128);
            _inlet = NativeMethods.CreateInlet(streamInfo, 8, 1, 1);
            NativeMethods.DestroyStreamInfo(streamInfo);
            if (_inlet == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Could not create LSL inlet: {NativeMethods.LastError()}");
            }
        }

        public nint Handle => _inlet;

        public void Open(TimeSpan timeout)
        {
            var error = 0;
            NativeMethods.OpenStream(_inlet, timeout.TotalSeconds, ref error);
            if (error != 0)
            {
                throw new InvalidOperationException($"LSL string stream open failed ({ErrorName(error)}): {NativeMethods.LastError()}");
            }
        }

        public LslStringSample? Pull(TimeSpan timeout)
        {
            var values = new IntPtr[_channelCount];
            var error = 0;
            var timestamp = NativeMethods.PullStringSample(_inlet, values, values.Length, timeout.TotalSeconds, ref error);
            if (error != 0)
            {
                throw new InvalidOperationException($"LSL string sample pull failed ({ErrorName(error)}): {NativeMethods.LastError()}");
            }

            if (timestamp <= 0d)
            {
                return null;
            }

            var strings = new string[_channelCount];
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] == IntPtr.Zero)
                {
                    strings[index] = string.Empty;
                    continue;
                }

                try
                {
                    strings[index] = Marshal.PtrToStringAnsi(values[index]) ?? string.Empty;
                }
                finally
                {
                    NativeMethods.DestroyString(values[index]);
                }
            }

            return new LslStringSample(timestamp, strings);
        }

        public void Dispose()
        {
            if (_inlet != IntPtr.Zero)
            {
                NativeMethods.DestroyInlet(_inlet);
                _inlet = IntPtr.Zero;
            }
        }
    }

    private static class NativeMethods
    {
        [DllImport("lsl", EntryPoint = "lsl_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_last_error();

        [DllImport("lsl", EntryPoint = "lsl_library_info", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lsl_library_info();

        [DllImport("lsl", EntryPoint = "lsl_local_clock", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_local_clock();

        [DllImport("lsl", EntryPoint = "lsl_create_streaminfo", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern nint lsl_create_streaminfo(string name, string type, int channelCount, double nominalRate, int channelFormat, string sourceId);

        [DllImport("lsl", EntryPoint = "lsl_destroy_streaminfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_streaminfo(nint streamInfo);

        [DllImport("lsl", EntryPoint = "lsl_create_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_create_outlet(nint streamInfo, int chunkSize, int maxBuffered);

        [DllImport("lsl", EntryPoint = "lsl_destroy_outlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_outlet(nint outlet);

        [DllImport("lsl", EntryPoint = "lsl_push_sample_dtp", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lsl_push_sample_dtp(nint outlet, double[] data, double timestamp, int pushThrough);

        [DllImport("lsl", EntryPoint = "lsl_resolve_byprop", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int lsl_resolve_byprop(
            [Out] IntPtr[] buffer,
            uint bufferElements,
            string property,
            string value,
            int minimum,
            double timeoutSeconds);

        [DllImport("lsl", EntryPoint = "lsl_create_inlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint lsl_create_inlet(nint streamInfo, int maxBufferLength, int maxChunkLength, int recover);

        [DllImport("lsl", EntryPoint = "lsl_destroy_inlet", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_inlet(nint inlet);

        [DllImport("lsl", EntryPoint = "lsl_open_stream", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_open_stream(nint inlet, double timeoutSeconds, ref int errorCode);

        [DllImport("lsl", EntryPoint = "lsl_pull_sample_d", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_pull_sample_d(nint inlet, double[] buffer, int bufferElements, double timeout, ref int errorCode);

        [DllImport("lsl", EntryPoint = "lsl_pull_sample_str", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_pull_sample_str(nint inlet, [Out] IntPtr[] buffer, int bufferElements, double timeout, ref int errorCode);

        [DllImport("lsl", EntryPoint = "lsl_destroy_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lsl_destroy_string(IntPtr value);

        [DllImport("lsl", EntryPoint = "lsl_time_correction_ex", CallingConvention = CallingConvention.Cdecl)]
        private static extern double lsl_time_correction_ex(nint inlet, ref double remoteTime, ref double uncertainty, double timeout, ref int errorCode);

        internal static string LastError() => PtrToString(lsl_last_error());

        internal static string LibraryInfo() => PtrToString(lsl_library_info());

        internal static double LocalClock() => lsl_local_clock();

        internal static nint CreateStreamInfo(string name, string type, int channelCount, double nominalRate, int channelFormat, string sourceId) =>
            lsl_create_streaminfo(name, type, channelCount, nominalRate, channelFormat, sourceId);

        internal static void DestroyStreamInfo(nint streamInfo) => lsl_destroy_streaminfo(streamInfo);

        internal static nint CreateOutlet(nint streamInfo, int chunkSize, int maxBuffered) => lsl_create_outlet(streamInfo, chunkSize, maxBuffered);

        internal static void DestroyOutlet(nint outlet) => lsl_destroy_outlet(outlet);

        internal static int PushDoubleSample(nint outlet, double[] values, double timestamp, int pushThrough) =>
            lsl_push_sample_dtp(outlet, values, timestamp, pushThrough);

        internal static int ResolveByProperty(IntPtr[] buffer, uint bufferElements, string property, string value, int minimum, double timeoutSeconds) =>
            lsl_resolve_byprop(buffer, bufferElements, property, value, minimum, timeoutSeconds);

        internal static nint CreateInlet(nint streamInfo, int maxBufferLength, int maxChunkLength, int recover) =>
            lsl_create_inlet(streamInfo, maxBufferLength, maxChunkLength, recover);

        internal static void DestroyInlet(nint inlet) => lsl_destroy_inlet(inlet);

        internal static void OpenStream(nint inlet, double timeoutSeconds, ref int errorCode) =>
            lsl_open_stream(inlet, timeoutSeconds, ref errorCode);

        internal static double PullDoubleSample(nint inlet, double[] buffer, int bufferElements, double timeout, ref int errorCode) =>
            lsl_pull_sample_d(inlet, buffer, bufferElements, timeout, ref errorCode);

        internal static double PullStringSample(nint inlet, IntPtr[] buffer, int bufferElements, double timeout, ref int errorCode) =>
            lsl_pull_sample_str(inlet, buffer, bufferElements, timeout, ref errorCode);

        internal static void DestroyString(IntPtr value) => lsl_destroy_string(value);

        internal static double TimeCorrectionEx(nint inlet, ref double remoteTime, ref double uncertainty, double timeout, ref int errorCode) =>
            lsl_time_correction_ex(inlet, ref remoteTime, ref uncertainty, timeout, ref errorCode);

        private static string PtrToString(IntPtr pointer) =>
            pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
    }
}

internal sealed record LslDoubleSample(double TimestampSeconds, double[] Values);

internal sealed record LslStringSample(double TimestampSeconds, string[] Values);

public sealed record LslTimeCorrectionSample(
    double OffsetSeconds,
    double RemoteTimeSeconds,
    double UncertaintySeconds);
