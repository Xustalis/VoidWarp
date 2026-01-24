using System.Runtime.InteropServices;

namespace VoidWarp.Windows.Native
{
    /// <summary>
    /// P/Invoke bindings to the VoidWarp Rust core library
    /// </summary>
    public static partial class NativeBindings
    {
        private const string DllName = "voidwarp_core";

        #region Handle Management

        [LibraryImport(DllName, EntryPoint = "voidwarp_init", StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr voidwarp_init_native(string deviceName);

        public static IntPtr voidwarp_init(string deviceName) => voidwarp_init_native(deviceName);

        [LibraryImport(DllName, EntryPoint = "voidwarp_destroy")]
        private static partial void voidwarp_destroy_native(IntPtr handle);

        public static void voidwarp_destroy(IntPtr handle) => voidwarp_destroy_native(handle);

        #endregion

        #region Device Info

        [LibraryImport(DllName, EntryPoint = "voidwarp_get_device_id")]
        private static partial IntPtr voidwarp_get_device_id_native(IntPtr handle);

        public static IntPtr voidwarp_get_device_id(IntPtr handle) => voidwarp_get_device_id_native(handle);

        [LibraryImport(DllName, EntryPoint = "voidwarp_generate_pairing_code")]
        private static partial IntPtr voidwarp_generate_pairing_code_native();

        public static IntPtr voidwarp_generate_pairing_code() => voidwarp_generate_pairing_code_native();

        [LibraryImport(DllName, EntryPoint = "voidwarp_free_string")]
        private static partial void voidwarp_free_string_native(IntPtr s);

        public static void voidwarp_free_string(IntPtr s) => voidwarp_free_string_native(s);

        #endregion

        #region Discovery

        [LibraryImport(DllName, EntryPoint = "voidwarp_start_discovery")]
        private static partial int voidwarp_start_discovery_native(IntPtr handle, ushort port);

        public static int voidwarp_start_discovery(IntPtr handle, ushort port) => voidwarp_start_discovery_native(handle, port);

        [LibraryImport(DllName, EntryPoint = "voidwarp_start_discovery_with_ip", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int voidwarp_start_discovery_with_ip_native(IntPtr handle, ushort port, string ipAddress);

        public static int voidwarp_start_discovery_with_ip(IntPtr handle, ushort port, string ipAddress) =>
            voidwarp_start_discovery_with_ip_native(handle, port, ipAddress);

        [LibraryImport(DllName, EntryPoint = "voidwarp_stop_discovery")]
        private static partial void voidwarp_stop_discovery_native(IntPtr handle);

        public static void voidwarp_stop_discovery(IntPtr handle) => voidwarp_stop_discovery_native(handle);

        [LibraryImport(DllName, EntryPoint = "voidwarp_add_manual_peer", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int voidwarp_add_manual_peer_native(
            IntPtr handle,
            string deviceId,
            string deviceName,
            string ipAddress,
            ushort port
        );

        public static int voidwarp_add_manual_peer(
            IntPtr handle,
            string deviceId,
            string deviceName,
            string ipAddress,
            ushort port
        ) => voidwarp_add_manual_peer_native(handle, deviceId, deviceName, ipAddress, port);

        [LibraryImport(DllName, EntryPoint = "voidwarp_get_peers")]
        private static partial FfiPeerList voidwarp_get_peers_native(IntPtr handle);

        public static FfiPeerList voidwarp_get_peers(IntPtr handle) => voidwarp_get_peers_native(handle);

        [LibraryImport(DllName, EntryPoint = "voidwarp_free_peer_list")]
        private static partial void voidwarp_free_peer_list_native(FfiPeerList list);

        public static void voidwarp_free_peer_list(FfiPeerList list) => voidwarp_free_peer_list_native(list);

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

        [LibraryImport(DllName, EntryPoint = "voidwarp_create_sender", StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr voidwarp_create_sender_native(string path);

        public static IntPtr voidwarp_create_sender(string path) => voidwarp_create_sender_native(path);

        [LibraryImport(DllName, EntryPoint = "voidwarp_sender_get_size")]
        private static partial ulong voidwarp_sender_get_size_native(IntPtr sender);

        public static ulong voidwarp_sender_get_size(IntPtr sender) => voidwarp_sender_get_size_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_sender_get_name")]
        private static partial IntPtr voidwarp_sender_get_name_native(IntPtr sender);

        public static IntPtr voidwarp_sender_get_name(IntPtr sender) => voidwarp_sender_get_name_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_sender_read_chunk")]
        private static partial FfiChunk voidwarp_sender_read_chunk_native(IntPtr sender);

        public static FfiChunk voidwarp_sender_read_chunk(IntPtr sender) => voidwarp_sender_read_chunk_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_free_chunk")]
        private static partial void voidwarp_free_chunk_native(FfiChunk chunk);

        public static void voidwarp_free_chunk(FfiChunk chunk) => voidwarp_free_chunk_native(chunk);

        [LibraryImport(DllName, EntryPoint = "voidwarp_sender_get_progress")]
        private static partial FfiTransferProgress voidwarp_sender_get_progress_native(IntPtr sender);

        public static FfiTransferProgress voidwarp_sender_get_progress(IntPtr sender) => voidwarp_sender_get_progress_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_sender_cancel")]
        private static partial void voidwarp_sender_cancel_native(IntPtr sender);

        public static void voidwarp_sender_cancel(IntPtr sender) => voidwarp_sender_cancel_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_destroy_sender")]
        private static partial void voidwarp_destroy_sender_native(IntPtr sender);

        public static void voidwarp_destroy_sender(IntPtr sender) => voidwarp_destroy_sender_native(sender);

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

        [LibraryImport(DllName, EntryPoint = "voidwarp_create_receiver")]
        private static partial IntPtr voidwarp_create_receiver_native();

        public static IntPtr voidwarp_create_receiver() => voidwarp_create_receiver_native();

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_port")]
        private static partial ushort voidwarp_receiver_get_port_native(IntPtr receiver);

        public static ushort voidwarp_receiver_get_port(IntPtr receiver) => voidwarp_receiver_get_port_native(receiver);

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_start")]
        private static partial void voidwarp_receiver_start_native(IntPtr receiver);

        public static void voidwarp_receiver_start(IntPtr receiver) => voidwarp_receiver_start_native(receiver);

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_stop")]
        private static partial void voidwarp_receiver_stop_native(IntPtr receiver);

        public static void voidwarp_receiver_stop(IntPtr receiver) => voidwarp_receiver_stop_native(receiver);

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_state")]
        private static partial int voidwarp_receiver_get_state_native(IntPtr receiver);

        public static int voidwarp_receiver_get_state(IntPtr receiver) => voidwarp_receiver_get_state_native(receiver);

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_pending")]
        private static partial FfiPendingTransfer voidwarp_receiver_get_pending_native(IntPtr receiver);

        public static FfiPendingTransfer voidwarp_receiver_get_pending(IntPtr receiver) => voidwarp_receiver_get_pending_native(receiver);

        [LibraryImport(DllName, EntryPoint = "voidwarp_free_pending_transfer")]
        private static partial void voidwarp_free_pending_transfer_native(FfiPendingTransfer transfer);

        public static void voidwarp_free_pending_transfer(FfiPendingTransfer transfer) => voidwarp_free_pending_transfer_native(transfer);

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_accept", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int voidwarp_receiver_accept_native(IntPtr receiver, string savePath);

        public static int voidwarp_receiver_accept(IntPtr receiver, string savePath) => voidwarp_receiver_accept_native(receiver, savePath);

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_reject")]
        private static partial int voidwarp_receiver_reject_native(IntPtr receiver);

        public static int voidwarp_receiver_reject(IntPtr receiver) => voidwarp_receiver_reject_native(receiver);

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_progress")]
        private static partial float voidwarp_receiver_get_progress_native(IntPtr receiver);

        public static float voidwarp_receiver_get_progress(IntPtr receiver) => voidwarp_receiver_get_progress_native(receiver);

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_bytes_received")]
        private static partial ulong voidwarp_receiver_get_bytes_received_native(IntPtr receiver);

        public static ulong voidwarp_receiver_get_bytes_received(IntPtr receiver) => voidwarp_receiver_get_bytes_received_native(receiver);

        [LibraryImport(DllName, EntryPoint = "voidwarp_destroy_receiver")]
        private static partial void voidwarp_destroy_receiver_native(IntPtr receiver);

        public static void voidwarp_destroy_receiver(IntPtr receiver) => voidwarp_destroy_receiver_native(receiver);

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

        #region TCP Sender

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_create", StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr voidwarp_tcp_sender_create_native(string filePath);

        public static IntPtr voidwarp_tcp_sender_create(string filePath) => voidwarp_tcp_sender_create_native(filePath);

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_start", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int voidwarp_tcp_sender_start_native(
            IntPtr sender,
            string ipAddress,
            ushort port,
            string senderName
        );

        public static int voidwarp_tcp_sender_start(
            IntPtr sender,
            string ipAddress,
            ushort port,
            string senderName
        ) => voidwarp_tcp_sender_start_native(sender, ipAddress, port, senderName);

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_get_checksum")]
        private static partial IntPtr voidwarp_tcp_sender_get_checksum_native(IntPtr sender);

        public static IntPtr voidwarp_tcp_sender_get_checksum(IntPtr sender) => voidwarp_tcp_sender_get_checksum_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_get_file_size")]
        private static partial ulong voidwarp_tcp_sender_get_file_size_native(IntPtr sender);

        public static ulong voidwarp_tcp_sender_get_file_size(IntPtr sender) => voidwarp_tcp_sender_get_file_size_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_get_progress")]
        private static partial float voidwarp_tcp_sender_get_progress_native(IntPtr sender);

        public static float voidwarp_tcp_sender_get_progress(IntPtr sender) => voidwarp_tcp_sender_get_progress_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_cancel")]
        private static partial void voidwarp_tcp_sender_cancel_native(IntPtr sender);

        public static void voidwarp_tcp_sender_cancel(IntPtr sender) => voidwarp_tcp_sender_cancel_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_destroy")]
        private static partial void voidwarp_tcp_sender_destroy_native(IntPtr sender);

        public static void voidwarp_tcp_sender_destroy(IntPtr sender) => voidwarp_tcp_sender_destroy_native(sender);

        #endregion

        [LibraryImport(DllName, EntryPoint = "voidwarp_transport_start_server")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static partial bool voidwarp_transport_start_server_native(ushort port);

        public static bool voidwarp_transport_start_server(ushort port) => voidwarp_transport_start_server_native(port);

        [LibraryImport(DllName, EntryPoint = "voidwarp_transport_ping", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static partial bool voidwarp_transport_ping_native(
            string ipAddress,
            ushort port
        );

        public static bool voidwarp_transport_ping(string ipAddress, ushort port) => voidwarp_transport_ping_native(ipAddress, port);
    }
}
