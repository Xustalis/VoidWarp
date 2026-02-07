using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VoidWarp.Windows.Native
{
    /// <summary>
    /// P/Invoke bindings to the VoidWarp Rust core library.
    /// Features reliable DLL loading via NativeLibrary.SetDllImportResolver.
    /// </summary>
    public static partial class NativeBindings
    {
        private const string DllName = "voidwarp_core";
        private const string DllFileName = "voidwarp_core.dll";

        private static bool _initialized = false;
        private static bool _dllLoaded = false;
        private static string? _loadError = null;
        private static readonly object _initLock = new();

        #region Static Constructor - CRITICAL for DLL Loading

        /// <summary>
        /// Static constructor - CRITICAL for reliable DLL loading.
        /// Registers custom DLL resolver BEFORE any P/Invoke calls.
        /// </summary>
        static NativeBindings()
        {
            lock (_initLock)
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    // Register our custom resolver for all DllImport calls in this assembly
                    NativeLibrary.SetDllImportResolver(typeof(NativeBindings).Assembly, ResolveDllImport);
                    Debug.WriteLine($"[NativeBindings] DllImportResolver registered for assembly: {typeof(NativeBindings).Assembly.FullName}");
                }
                catch (Exception ex)
                {
                    _loadError = $"Failed to set DllImportResolver: {ex.Message}";
                    Debug.WriteLine($"[NativeBindings] CRITICAL ERROR: {_loadError}");
                }
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns true if the native library was loaded successfully.
        /// </summary>
        public static bool IsLoaded => _dllLoaded;

        /// <summary>
        /// Returns any error message from loading, or null if successful.
        /// </summary>
        public static string? LoadError => _loadError;

        #endregion

        #region Custom DLL Resolver

        /// <summary>
        /// Custom DLL resolver that searches AppDomain.CurrentDomain.BaseDirectory FIRST.
        /// This is the KEY to reliable DLL loading - allows users to copy voidwarp_core.dll
        /// to the application directory and have it found reliably.
        /// </summary>
        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // Only handle our specific DLL
            if (!libraryName.Equals(DllName, StringComparison.OrdinalIgnoreCase) &&
                !libraryName.Equals(DllFileName, StringComparison.OrdinalIgnoreCase))
            {
                // Let the default resolver handle other libraries
                return IntPtr.Zero;
            }

            Debug.WriteLine($"[NativeBindings] Resolving '{libraryName}'...");

            // CRITICAL: Get the base directory where the application is running
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                _loadError = "AppDomain.CurrentDomain.BaseDirectory is null or empty";
                Debug.WriteLine($"[NativeBindings] ERROR: {_loadError}");
                return IntPtr.Zero;
            }

            Debug.WriteLine($"[NativeBindings] Base directory: {baseDir}");

            // Search paths in priority order
            var searchPaths = new[]
            {
                // 1. Application base directory (HIGHEST PRIORITY - user can copy DLL here)
                baseDir,
                
                // 2. NuGet-style runtime folder
                Path.Combine(baseDir, "runtimes", "win-x64", "native"),
                
                // 3. Native subfolder
                Path.Combine(baseDir, "native"),
                
                // 4. x64 subfolder (common for native builds)
                Path.Combine(baseDir, "x64"),
                
                // 5. Assembly location fallback
                Path.GetDirectoryName(assembly.Location) ?? "",
                
                // 6. Current working directory
                Environment.CurrentDirectory
            };

            foreach (var dir in searchPaths)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    continue;
                }

                var dllPath = Path.Combine(dir, DllFileName);
                Debug.WriteLine($"[NativeBindings] Checking: {dllPath}");

                if (File.Exists(dllPath))
                {
                    Debug.WriteLine($"[NativeBindings] Found DLL at: {dllPath}");

                    // Try to load the DLL
                    if (NativeLibrary.TryLoad(dllPath, out var handle))
                    {
                        _dllLoaded = true;
                        _loadError = null;
                        Debug.WriteLine($"[NativeBindings] SUCCESS: Loaded from {dllPath}");
                        return handle;
                    }
                    else
                    {
                        var error = $"Found DLL but failed to load: {dllPath}";
                        Debug.WriteLine($"[NativeBindings] WARNING: {error}");
                        // Continue searching - maybe another copy works
                    }
                }
            }

            // Build detailed error message
            _loadError = $"Could not find or load '{DllFileName}'. Searched paths:\n" +
                         string.Join("\n", searchPaths.Where(p => !string.IsNullOrEmpty(p)));
            Debug.WriteLine($"[NativeBindings] FAILED: {_loadError}");
            
            return IntPtr.Zero;
        }

        #endregion

        #region Handle Management

        [LibraryImport(DllName, EntryPoint = "voidwarp_init", StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr voidwarp_init_native(string deviceName);

        /// <summary>
        /// Initialize the VoidWarp engine with a device name.
        /// </summary>
        public static IntPtr voidwarp_init(string deviceName)
        {
            try
            {
                return voidwarp_init_native(deviceName);
            }
            catch (DllNotFoundException ex)
            {
                _loadError = $"DLL not found: {ex.Message}";
                Debug.WriteLine($"[NativeBindings] {_loadError}");
                return IntPtr.Zero;
            }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_destroy")]
        private static partial void voidwarp_destroy_native(IntPtr handle);

        /// <summary>
        /// Destroy the VoidWarp engine handle.
        /// </summary>
        public static void voidwarp_destroy(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                try { voidwarp_destroy_native(handle); }
                catch (Exception ex) { Debug.WriteLine($"[NativeBindings] destroy error: {ex.Message}"); }
            }
        }

        #endregion

        #region Device Info

        [LibraryImport(DllName, EntryPoint = "voidwarp_get_device_id")]
        private static partial IntPtr voidwarp_get_device_id_native(IntPtr handle);

        public static IntPtr voidwarp_get_device_id(IntPtr handle) => 
            handle != IntPtr.Zero ? voidwarp_get_device_id_native(handle) : IntPtr.Zero;

        [LibraryImport(DllName, EntryPoint = "voidwarp_generate_pairing_code")]
        private static partial IntPtr voidwarp_generate_pairing_code_native();

        public static IntPtr voidwarp_generate_pairing_code() => voidwarp_generate_pairing_code_native();

        [LibraryImport(DllName, EntryPoint = "voidwarp_free_string")]
        private static partial void voidwarp_free_string_native(IntPtr s);

        public static void voidwarp_free_string(IntPtr s)
        {
            if (s != IntPtr.Zero)
            {
                try { voidwarp_free_string_native(s); }
                catch { }
            }
        }

        #endregion

        #region Discovery

        [LibraryImport(DllName, EntryPoint = "voidwarp_start_discovery")]
        private static partial int voidwarp_start_discovery_native(IntPtr handle, ushort port);

        /// <summary>
        /// Start device discovery on the network.
        /// </summary>
        public static int voidwarp_start_discovery(IntPtr handle, ushort port)
        {
            if (handle == IntPtr.Zero) return -1;
            try { return voidwarp_start_discovery_native(handle, port); }
            catch (Exception ex) { Debug.WriteLine($"[NativeBindings] start_discovery error: {ex.Message}"); return -1; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_start_discovery_with_ip", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int voidwarp_start_discovery_with_ip_native(IntPtr handle, ushort port, string ipAddress);

        /// <summary>
        /// Start discovery with explicit IP address for mDNS.
        /// </summary>
        public static int voidwarp_start_discovery_with_ip(IntPtr handle, ushort port, string ipAddress)
        {
            if (handle == IntPtr.Zero) return -1;
            try { return voidwarp_start_discovery_with_ip_native(handle, port, ipAddress); }
            catch (Exception ex) { Debug.WriteLine($"[NativeBindings] start_discovery_with_ip error: {ex.Message}"); return -1; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_stop_discovery")]
        private static partial void voidwarp_stop_discovery_native(IntPtr handle);

        public static void voidwarp_stop_discovery(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                try { voidwarp_stop_discovery_native(handle); }
                catch { }
            }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_add_manual_peer", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int voidwarp_add_manual_peer_native(IntPtr handle, string deviceId, string deviceName, string ipAddress, ushort port);

        public static int voidwarp_add_manual_peer(IntPtr handle, string deviceId, string deviceName, string ipAddress, ushort port)
        {
            if (handle == IntPtr.Zero) return -1;
            try { return voidwarp_add_manual_peer_native(handle, deviceId, deviceName, ipAddress, port); }
            catch (Exception ex) { Debug.WriteLine($"[NativeBindings] add_manual_peer error: {ex.Message}"); return -1; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_get_peers")]
        private static partial FfiPeerList voidwarp_get_peers_native(IntPtr handle);

        public static FfiPeerList voidwarp_get_peers(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return default;
            try { return voidwarp_get_peers_native(handle); }
            catch { return default; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_free_peer_list")]
        private static partial void voidwarp_free_peer_list_native(FfiPeerList list);

        public static void voidwarp_free_peer_list(FfiPeerList list)
        {
            if (list.Peers != IntPtr.Zero)
            {
                try { voidwarp_free_peer_list_native(list); }
                catch { }
            }
        }

        #endregion

        #region TCP Sender

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_create", StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr voidwarp_tcp_sender_create_native(string filePath);

        public static IntPtr voidwarp_tcp_sender_create(string filePath)
        {
            try { return voidwarp_tcp_sender_create_native(filePath); }
            catch (Exception ex) { Debug.WriteLine($"[NativeBindings] tcp_sender_create error: {ex.Message}"); return IntPtr.Zero; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_start", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int voidwarp_tcp_sender_start_native(IntPtr sender, string ipAddress, ushort port, string senderName);

        /// <summary>
        /// Start sending a file to the specified IP and port.
        /// Returns: 0=success, 1=rejected, 2=checksum_mismatch, 3=connection_failed, 4=timeout, 5=cancelled, 6=io_error
        /// </summary>
        public static int voidwarp_tcp_sender_start(IntPtr sender, string ipAddress, ushort port, string senderName)
        {
            if (sender == IntPtr.Zero) return -1;
            try { return voidwarp_tcp_sender_start_native(sender, ipAddress, port, senderName); }
            catch (Exception ex) { Debug.WriteLine($"[NativeBindings] tcp_sender_start error: {ex.Message}"); return -1; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_get_checksum")]
        private static partial IntPtr voidwarp_tcp_sender_get_checksum_native(IntPtr sender);

        public static IntPtr voidwarp_tcp_sender_get_checksum(IntPtr sender) =>
            sender != IntPtr.Zero ? voidwarp_tcp_sender_get_checksum_native(sender) : IntPtr.Zero;

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_get_file_size")]
        private static partial ulong voidwarp_tcp_sender_get_file_size_native(IntPtr sender);

        public static ulong voidwarp_tcp_sender_get_file_size(IntPtr sender) =>
            sender != IntPtr.Zero ? voidwarp_tcp_sender_get_file_size_native(sender) : 0;

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_get_progress")]
        private static partial float voidwarp_tcp_sender_get_progress_native(IntPtr sender);

        public static float voidwarp_tcp_sender_get_progress(IntPtr sender) =>
            sender != IntPtr.Zero ? voidwarp_tcp_sender_get_progress_native(sender) : 0f;

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_cancel")]
        private static partial void voidwarp_tcp_sender_cancel_native(IntPtr sender);

        public static void voidwarp_tcp_sender_cancel(IntPtr sender)
        {
            if (sender != IntPtr.Zero)
            {
                try { voidwarp_tcp_sender_cancel_native(sender); }
                catch { }
            }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_is_folder")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static partial bool voidwarp_tcp_sender_is_folder_native(IntPtr sender);

        public static bool voidwarp_tcp_sender_is_folder(IntPtr sender) =>
            sender != IntPtr.Zero && voidwarp_tcp_sender_is_folder_native(sender);

        [LibraryImport(DllName, EntryPoint = "voidwarp_tcp_sender_destroy")]
        private static partial void voidwarp_tcp_sender_destroy_native(IntPtr sender);

        public static void voidwarp_tcp_sender_destroy(IntPtr sender)
        {
            if (sender != IntPtr.Zero)
            {
                try { voidwarp_tcp_sender_destroy_native(sender); }
                catch { }
            }
        }

        #endregion

        #region File Receiver

        [LibraryImport(DllName, EntryPoint = "voidwarp_create_receiver")]
        private static partial IntPtr voidwarp_create_receiver_native();

        public static IntPtr voidwarp_create_receiver()
        {
            try { return voidwarp_create_receiver_native(); }
            catch (Exception ex) { Debug.WriteLine($"[NativeBindings] create_receiver error: {ex.Message}"); return IntPtr.Zero; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_port")]
        private static partial ushort voidwarp_receiver_get_port_native(IntPtr receiver);

        public static ushort voidwarp_receiver_get_port(IntPtr receiver) =>
            receiver != IntPtr.Zero ? voidwarp_receiver_get_port_native(receiver) : (ushort)0;

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_start")]
        private static partial void voidwarp_receiver_start_native(IntPtr receiver);

        public static void voidwarp_receiver_start(IntPtr receiver)
        {
            if (receiver != IntPtr.Zero)
            {
                try { voidwarp_receiver_start_native(receiver); }
                catch (Exception ex) { Debug.WriteLine($"[NativeBindings] receiver_start error: {ex.Message}"); }
            }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_stop")]
        private static partial void voidwarp_receiver_stop_native(IntPtr receiver);

        public static void voidwarp_receiver_stop(IntPtr receiver)
        {
            if (receiver != IntPtr.Zero)
            {
                try { voidwarp_receiver_stop_native(receiver); }
                catch { }
            }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_state")]
        private static partial int voidwarp_receiver_get_state_native(IntPtr receiver);

        /// <summary>
        /// Get receiver state: 0=Idle, 1=Listening, 2=AwaitingAccept, 3=Receiving, 4=Completed, 5=Error
        /// </summary>
        public static int voidwarp_receiver_get_state(IntPtr receiver) =>
            receiver != IntPtr.Zero ? voidwarp_receiver_get_state_native(receiver) : 0;

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_pending")]
        private static partial FfiPendingTransfer voidwarp_receiver_get_pending_native(IntPtr receiver);

        public static FfiPendingTransfer voidwarp_receiver_get_pending(IntPtr receiver)
        {
            if (receiver == IntPtr.Zero) return default;
            try { return voidwarp_receiver_get_pending_native(receiver); }
            catch { return default; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_free_pending_transfer")]
        private static partial void voidwarp_free_pending_transfer_native(FfiPendingTransfer transfer);

        public static void voidwarp_free_pending_transfer(FfiPendingTransfer transfer)
        {
            try { voidwarp_free_pending_transfer_native(transfer); }
            catch { }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_accept", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int voidwarp_receiver_accept_native(IntPtr receiver, string savePath);

        public static int voidwarp_receiver_accept(IntPtr receiver, string savePath)
        {
            if (receiver == IntPtr.Zero) return -1;
            try { return voidwarp_receiver_accept_native(receiver, savePath); }
            catch (Exception ex) { Debug.WriteLine($"[NativeBindings] receiver_accept error: {ex.Message}"); return -1; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_reject")]
        private static partial int voidwarp_receiver_reject_native(IntPtr receiver);

        public static int voidwarp_receiver_reject(IntPtr receiver)
        {
            if (receiver == IntPtr.Zero) return -1;
            try { return voidwarp_receiver_reject_native(receiver); }
            catch { return -1; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_progress")]
        private static partial float voidwarp_receiver_get_progress_native(IntPtr receiver);

        public static float voidwarp_receiver_get_progress(IntPtr receiver) =>
            receiver != IntPtr.Zero ? voidwarp_receiver_get_progress_native(receiver) : 0f;

        [LibraryImport(DllName, EntryPoint = "voidwarp_receiver_get_bytes_received")]
        private static partial ulong voidwarp_receiver_get_bytes_received_native(IntPtr receiver);

        public static ulong voidwarp_receiver_get_bytes_received(IntPtr receiver) =>
            receiver != IntPtr.Zero ? voidwarp_receiver_get_bytes_received_native(receiver) : 0;

        [LibraryImport(DllName, EntryPoint = "voidwarp_destroy_receiver")]
        private static partial void voidwarp_destroy_receiver_native(IntPtr receiver);

        public static void voidwarp_destroy_receiver(IntPtr receiver)
        {
            if (receiver != IntPtr.Zero)
            {
                try { voidwarp_destroy_receiver_native(receiver); }
                catch { }
            }
        }

        #endregion

        #region Transport Utilities

        [LibraryImport(DllName, EntryPoint = "voidwarp_transport_start_server")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static partial bool voidwarp_transport_start_server_native(ushort port);

        public static bool voidwarp_transport_start_server(ushort port)
        {
            try { return voidwarp_transport_start_server_native(port); }
            catch { return false; }
        }

        [LibraryImport(DllName, EntryPoint = "voidwarp_transport_ping", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static partial bool voidwarp_transport_ping_native(string ipAddress, ushort port);

        /// <summary>
        /// Test if a peer is reachable.
        /// </summary>
        public static bool voidwarp_transport_ping(string ipAddress, ushort port)
        {
            try { return voidwarp_transport_ping_native(ipAddress, port); }
            catch { return false; }
        }

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

        [StructLayout(LayoutKind.Sequential)]
        public struct FfiPendingTransfer
        {
            public IntPtr SenderName;
            public IntPtr SenderAddr;
            public IntPtr FileName;
            public ulong FileSize;
            [MarshalAs(UnmanagedType.I1)]
            public bool IsValid;
            [MarshalAs(UnmanagedType.I1)]
            public bool IsFolder;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Safely get a string from a native pointer and free it.
        /// </summary>
        public static string? GetStringAndFree(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            try
            {
                string? result = Marshal.PtrToStringUTF8(ptr);
                voidwarp_free_string(ptr);
                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Safely get a string from a native pointer WITHOUT freeing it.
        /// </summary>
        public static string? GetString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(ptr); }
            catch { return null; }
        }

        #endregion
    }
}
