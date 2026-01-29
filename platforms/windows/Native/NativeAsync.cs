using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoidWarp.Windows.Native
{
    /// <summary>
    /// Async wrappers for blocking native calls so they don't block the UI thread.
    /// </summary>
    public static class NativeAsync
    {
        /// <summary>
        /// Start the receiver asynchronously (wraps voidwarp_receiver_start in Task.Run).
        /// </summary>
        public static Task StartReceiverAsync(IntPtr receiverHandle)
        {
            return Task.Run(() => NativeBindings.voidwarp_receiver_start(receiverHandle));
        }

        /// <summary>
        /// Start TCP sender and wait for result asynchronously (wraps voidwarp_tcp_sender_start in Task.Run).
        /// Returns: 0=success, 1=rejected, 2=checksum mismatch, 3=connection failed, 4=timeout, 5=cancelled.
        /// </summary>
        public static Task<int> StartTcpSenderAsync(
            IntPtr senderHandle,
            string ip,
            ushort port,
            string senderName,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return NativeBindings.voidwarp_tcp_sender_start(senderHandle, ip, port, senderName);
            }, cancellationToken);
        }
    }
}
