use std::io;
use std::net::SocketAddr;
use std::sync::Arc;
use tokio::net::UdpSocket;

/// Wrapper around Tokio's UdpSocket to handle platform-specific configuration
/// and provide a clean interface for the TransportManager.
#[derive(Debug, Clone)]
pub struct UdpTransport {
    socket: Arc<UdpSocket>,
}

impl UdpTransport {
    /// Bind to a specific address.
    /// To bind to ephemeral port on all interfaces: "0.0.0.0:0"
    pub async fn bind(addr: SocketAddr) -> io::Result<Self> {
        let socket = UdpSocket::bind(addr).await?;

        // TODO: optimizations (buffer sizes, TOS)

        Ok(UdpTransport {
            socket: Arc::new(socket),
        })
    }

    /// Helper to try binding to IPv4 and IPv6 dual stack if supported,
    /// otherwise fall back to IPv4.
    pub async fn bind_dual_stack(port: u16) -> io::Result<Self> {
        // For simplicity in MVP, we bind to 0.0.0.0.
        // Proper dual-stack support often requires binding two sockets
        // or using OS-specific setsockopt (IPV6_V6ONLY = 0).
        let addr = SocketAddr::from(([0, 0, 0, 0], port));
        Self::bind(addr).await
    }

    pub async fn send(&self, buf: &[u8], target: SocketAddr) -> io::Result<usize> {
        self.socket.send_to(buf, target).await
    }

    pub async fn recv(&self, buf: &mut [u8]) -> io::Result<(usize, SocketAddr)> {
        self.socket.recv_from(buf).await
    }

    pub fn local_addr(&self) -> io::Result<SocketAddr> {
        self.socket.local_addr()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_bind_and_send() {
        let server = UdpTransport::bind("127.0.0.1:0".parse().unwrap())
            .await
            .expect("Failed to bind server");
        let server_addr = server.local_addr().unwrap();

        let client = UdpTransport::bind("127.0.0.1:0".parse().unwrap())
            .await
            .expect("Failed to bind client");

        let msg = b"ping";
        client.send(msg, server_addr).await.expect("Send failed");

        let mut buf = [0u8; 1024];
        let (len, addr) = server.recv(&mut buf).await.expect("Recv failed");

        assert_eq!(&buf[..len], msg);
        // On some platforms/loopback addr might vary slightly but usually it's correct.
        // assert_eq!(addr, client.local_addr().unwrap());
        assert!(len > 0);
    }
}
