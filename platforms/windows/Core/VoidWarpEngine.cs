using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VoidWarp.Windows.Native;

namespace VoidWarp.Windows.Core
{
    /// <summary>
    /// High-level wrapper around the VoidWarp native library
    /// </summary>
    public sealed class VoidWarpEngine : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public string DeviceId { get; private set; } = string.Empty;
        public bool IsDiscovering { get; private set; } = false;

        public VoidWarpEngine(string deviceName = "Windows Device")
        {
            _handle = NativeBindings.voidwarp_init(deviceName);
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to initialize VoidWarp engine");
            }

            DeviceId = NativeBindings.GetStringAndFree(
                NativeBindings.voidwarp_get_device_id(_handle)
            ) ?? "unknown";
        }

        /// <summary>
        /// Generate a new 6-digit pairing code
        /// </summary>
        public static string GeneratePairingCode()
        {
            return NativeBindings.GetStringAndFree(
                NativeBindings.voidwarp_generate_pairing_code()
            ) ?? "000-000";
        }

        /// <summary>
        /// Start mDNS discovery on the specified port
        /// </summary>
        public bool StartDiscovery(ushort port = 42424)
        {
            int result = NativeBindings.voidwarp_start_discovery(_handle, port);
            IsDiscovering = result == 0;
            return IsDiscovering;
        }

        /// <summary>
        /// Stop mDNS discovery
        /// </summary>
        public void StopDiscovery()
        {
            NativeBindings.voidwarp_stop_discovery(_handle);
            IsDiscovering = false;
        }

        /// <summary>
        /// Get list of discovered peers
        /// </summary>
        public List<DiscoveredPeer> GetPeers()
        {
            var result = new List<DiscoveredPeer>();
            var list = NativeBindings.voidwarp_get_peers(_handle);

            if (list.Peers != IntPtr.Zero && list.Count > 0)
            {
                int structSize = Marshal.SizeOf<NativeBindings.FfiPeer>();
                for (nuint i = 0; i < list.Count; i++)
                {
                    IntPtr peerPtr = list.Peers + (int)(i * (nuint)structSize);
                    var ffiPeer = Marshal.PtrToStructure<NativeBindings.FfiPeer>(peerPtr);

                    result.Add(new DiscoveredPeer
                    {
                        DeviceId = Marshal.PtrToStringUTF8(ffiPeer.DeviceId) ?? "",
                        DeviceName = Marshal.PtrToStringUTF8(ffiPeer.DeviceName) ?? "",
                        IpAddress = Marshal.PtrToStringUTF8(ffiPeer.IpAddress) ?? "",
                        Port = ffiPeer.Port
                    });
                }

                NativeBindings.voidwarp_free_peer_list(list);
            }

            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBindings.voidwarp_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a discovered peer on the network
    /// </summary>
    public class DiscoveredPeer
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public ushort Port { get; set; }

        public override string ToString() => $"{DeviceName} ({IpAddress}:{Port})";
    }
}
