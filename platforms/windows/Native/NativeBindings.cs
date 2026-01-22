using System;
using System.Runtime.InteropServices;

namespace VoidWarp.Windows.Native
{
    /// <summary>
    /// P/Invoke bindings to the VoidWarp Rust core library
    /// </summary>
    public static class NativeBindings
    {
        private const string DllName = "voidwarp_core";

        #region Handle Management

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr voidwarp_init([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_destroy(IntPtr handle);

        #endregion

        #region Device Info

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr voidwarp_get_device_id(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr voidwarp_generate_pairing_code();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_free_string(IntPtr s);

        #endregion

        #region Discovery

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int voidwarp_start_discovery(IntPtr handle, ushort port);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_stop_discovery(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern FfiPeerList voidwarp_get_peers(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_free_peer_list(FfiPeerList list);

        #endregion

        #region FFI Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct FfiPeer
        {
            public IntPtr DeviceId;
            public IntPtr DeviceName;
            public IntPtr IpAddress;
            public ushort Port;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FfiPeerList
        {
            public IntPtr Peers;
            public nuint Count;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Safely get a string from a native pointer and free it
        /// </summary>
        public static string? GetStringAndFree(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            string? result = Marshal.PtrToStringUTF8(ptr);
            voidwarp_free_string(ptr);
            return result;
        }

        #endregion

        #region File Transfer

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr voidwarp_create_sender([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong voidwarp_sender_get_size(IntPtr sender);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr voidwarp_sender_get_name(IntPtr sender);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern FfiChunk voidwarp_sender_read_chunk(IntPtr sender);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_free_chunk(FfiChunk chunk);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern FfiTransferProgress voidwarp_sender_get_progress(IntPtr sender);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_sender_cancel(IntPtr sender);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_destroy_sender(IntPtr sender);

        [StructLayout(LayoutKind.Sequential)]
        public struct FfiChunk
        {
            public ulong Index;
            public IntPtr Data;
            public nuint Len;
            [MarshalAs(UnmanagedType.I1)]
            public bool IsLast;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FfiTransferProgress
        {
            public ulong BytesTransferred;
            public ulong TotalBytes;
            public float Percentage;
            public float SpeedMbps;
            public int State;
        }

        #endregion

        #region File Receiver

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr voidwarp_create_receiver();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ushort voidwarp_receiver_get_port(IntPtr receiver);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_receiver_start(IntPtr receiver);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_receiver_stop(IntPtr receiver);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int voidwarp_receiver_get_state(IntPtr receiver);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern FfiPendingTransfer voidwarp_receiver_get_pending(IntPtr receiver);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_free_pending_transfer(FfiPendingTransfer transfer);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int voidwarp_receiver_accept(IntPtr receiver, [MarshalAs(UnmanagedType.LPUTF8Str)] string savePath);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int voidwarp_receiver_reject(IntPtr receiver);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float voidwarp_receiver_get_progress(IntPtr receiver);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong voidwarp_receiver_get_bytes_received(IntPtr receiver);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void voidwarp_destroy_receiver(IntPtr receiver);

        [StructLayout(LayoutKind.Sequential)]
        public struct FfiPendingTransfer
        {
            public IntPtr SenderName;
            public IntPtr SenderAddr;
            public IntPtr FileName;
            public ulong FileSize;
            [MarshalAs(UnmanagedType.I1)]
            public bool IsValid;
        }

        #endregion
    }
}
