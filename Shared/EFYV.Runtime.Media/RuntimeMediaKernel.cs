using System;
using System.Runtime.InteropServices;

namespace EFYV.Runtime.Media
{
    public enum RuntimeMediaKernelMode
    {
        Managed = 0,
        NativeV1 = 1,
    }

    public static class RuntimeMediaKernel
    {
        private const uint ExpectedAbiVersion = 1;
        private static RuntimeMediaKernelMode mode;

        public static RuntimeMediaKernelMode Mode => mode;

        public static bool TryEnableNativeV1()
        {
            try
            {
                if (NativeV1.AbiVersion() != ExpectedAbiVersion) return false;
                mode = RuntimeMediaKernelMode.NativeV1;
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (BadImageFormatException) { return false; }
        }

        public static void UseManagedFallback() => mode = RuntimeMediaKernelMode.Managed;

        public static unsafe void BlendRgbaBatch(uint* destination, uint* source, int count, byte opacity, byte transparentThreshold)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count != 0 && destination == null) throw new ArgumentNullException(nameof(destination));
            if (count != 0 && source == null) throw new ArgumentNullException(nameof(source));
            if (mode == RuntimeMediaKernelMode.NativeV1)
            {
                try
                {
                    if (NativeV1.BlendRgbaBatch(destination, source, (nuint)count, opacity, transparentThreshold) == 0) return;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                catch (BadImageFormatException) { }
                mode = RuntimeMediaKernelMode.Managed;
            }
            RgbaCompositor.BlendBatchManaged(destination, source, count, opacity, transparentThreshold);
        }

        public static unsafe uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
        {
            if (mode == RuntimeMediaKernelMode.NativeV1 && data.Length != 0)
            {
                try
                {
                    fixed (byte* pointer = data)
                    {
                        uint result = 0;
                        uint initial = crc ^ PngContract.FinalCrcMask;
                        if (NativeV1.Crc32(pointer, (nuint)data.Length, initial, &result) == 0)
                            return result ^ PngContract.FinalCrcMask;
                    }
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                catch (BadImageFormatException) { }
                mode = RuntimeMediaKernelMode.Managed;
            }
            return Crc32.Update(crc, data);
        }

        private static class NativeV1
        {
            private const string Library = "efyv_runtime_kernel";

            [DllImport(Library, EntryPoint = "efyv_runtime_v1_abi_version", CallingConvention = CallingConvention.Cdecl)]
            internal static extern uint AbiVersion();

            [DllImport(Library, EntryPoint = "efyv_runtime_v1_rgba_blend_batch", CallingConvention = CallingConvention.Cdecl)]
            internal static extern unsafe int BlendRgbaBatch(uint* destination, uint* source, nuint pixelCount, byte opacity, byte transparentThreshold);

            [DllImport(Library, EntryPoint = "efyv_runtime_v1_crc32", CallingConvention = CallingConvention.Cdecl)]
            internal static extern unsafe int Crc32(byte* data, nuint length, uint initial, uint* result);
        }
    }
}
