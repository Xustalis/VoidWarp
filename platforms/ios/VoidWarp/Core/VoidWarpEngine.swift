import Foundation
import Combine
import UIKit

// MARK: - Models

struct PeerItem: Identifiable, Equatable, Hashable {
    let id: String
    let name: String
    let ips: [String] // Robustness: Support multiple IPs
    let port: UInt16
    
    // Helper for display
    var displayIp: String { ips.first ?? "Unknown" }
}

enum TransferState: Equatable {
    case idle
    case awaitingAccept(fileName: String, sender: String, size: UInt64)
    case transferring(progress: Float, speed: String)
    case completed(success: Bool, message: String)
    case error(String)
}

// MARK: - Engine

@MainActor
class VoidWarpEngine: ObservableObject {
    static let shared = VoidWarpEngine()
    
    // Opaque Pointers (Handle with care)
    private var engineHandle: OpaquePointer?
    private var receiverHandle: OpaquePointer?
    
    // UI State
    @Published var deviceName: String = UIDevice.current.name
    @Published var deviceId: String = ""
    @Published var discoveredPeers: [PeerItem] = []
    @Published var transferState: TransferState = .idle
    @Published var isDiscovering: Bool = false
    
    // Background Tasks
    private var discoveryTask: Task<Void, Never>?
    private var receiverTask: Task<Void, Never>?
    
    private init() {
        setupEngine()
    }
    
    deinit {
        let e = engineHandle
        let r = receiverHandle
        Task.detached {
            if let r { voidwarp_destroy_receiver(r) }
            if let e { voidwarp_destroy(e) }
        }
    }
    
    // MARK: - Setup
    
    private func setupEngine() {
        print("🚀 Initializing VoidWarp Engine (Robust Mode)...")
        let namePtr = strdup(deviceName)
        engineHandle = voidwarp_init(namePtr)
        free(namePtr)
        
        if let handle = engineHandle {
            if let idPtr = voidwarp_get_device_id(handle) {
                deviceId = String(cString: idPtr)
                voidwarp_free_string(idPtr)
            }
        }
        
        receiverHandle = voidwarp_create_receiver()
        startReceiverLoop()
    }
    
    // MARK: - Discovery
    
    func startDiscovery(port: UInt16 = 42424) {
        guard let handle = engineHandle, !isDiscovering else { return }
        isDiscovering = true
        
        discoveryTask = Task.detached(priority: .userInitiated) { [weak self] in
            // Android Parity: Ensure we don't return partial failures on start
            let res = voidwarp_start_discovery(handle, port)
            if res != 0 {
                print("⚠️ Discovery started with warning code: \(res) (Manual mode active)")
            }
            
            while !Task.isCancelled {
                let rawList = voidwarp_get_peers(handle)
                var newPeers: [PeerItem] = []
                
                if rawList.count > 0 {
                    let buffer = UnsafeBufferPointer(start: rawList.peers, count: Int(rawList.count))
                    for ffiPeer in buffer {
                        if let id = ffiPeer.device_id, let name = ffiPeer.device_name, let ip = ffiPeer.ip_address {
                             // Robustness: Parse comma-separated IPs from FFI if supported, or single
                             let ipString = String(cString: ip)
                             let ips = ipString.components(separatedBy: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
                             
                             newPeers.append(PeerItem(
                                id: String(cString: id),
                                name: String(cString: name),
                                ips: ips,
                                port: ffiPeer.port
                            ))
                        }
                    }
                }
                voidwarp_free_peer_list(rawList)
                
                await self?.updatePeers(newPeers)
                try? await Task.sleep(nanoseconds: 1_000_000_000)
            }
            
            voidwarp_stop_discovery(handle)
        }
    }
    
    func stopDiscovery() {
        isDiscovering = false
        discoveryTask?.cancel()
        discoveryTask = nil
    }
    
    private func updatePeers(_ peers: [PeerItem]) {
        self.discoveredPeers = peers.filter { $0.id != self.deviceId }
    }
    
    // MARK: - Sending (Robustness Implementation)
    
    func sendFile(url: URL, to peer: PeerItem) {
        // Prevent concurrent transfers (Android Mutex equivalent)
        guard case .idle = transferState else {
            print("⚠️ Transfer already in progress")
            return
        }
        
        // Wake Lock: Prevent screen sleep during transfer
        UIApplication.shared.isIdleTimerDisabled = true
        
        guard url.startAccessingSecurityScopedResource() else {
            self.transferState = .error("Permission Denied: Unable to access file")
            return
        }
        
        let path = url.path
        let myName = self.deviceName
        let targetPort = peer.port
        let targetIps = peer.ips
        
        Task.detached(priority: .high) {
            defer { 
                url.stopAccessingSecurityScopedResource()
                await MainActor.run { 
                    UIApplication.shared.isIdleTimerDisabled = false 
                }
            }
            
            await MainActor.run { self.transferState = .transferring(progress: 0, speed: "Preparing...") }
            
            // 1. Connection Pre-Check (Ping)
            // Iterate IPs to find the best one before committing resources
            var bestIp: String? = nil
            for ip in targetIps {
                await MainActor.run { self.transferState = .transferring(progress: 0, speed: "Testing connection to \(ip)...") }
                if self.ping(ip: ip, port: targetPort) {
                    bestIp = ip
                    print("✅ Connection verified to \(ip)")
                    break
                }
            }
            
            guard let activeIp = bestIp else {
                await MainActor.run { self.transferState = .error("Connection Failed: Device unreachable") }
                return
            }
            
            // 2. Create Sender
            let pathC = strdup(path)
            guard let sender = voidwarp_tcp_sender_create(pathC) else {
                free(pathC)
                await MainActor.run { self.transferState = .error("Failed to read file for sending") }
                return
            }
            free(pathC)
            
            // 3. Optimization: Apply Streaming Mode
            let size = voidwarp_tcp_sender_get_file_size(sender)
            let optimalChunk: UInt
            if size < 32 * 1024 * 1024 { optimalChunk = UInt(size) }      
            else if size < 100 * 1024 * 1024 { optimalChunk = 1024 * 1024 } 
            else { optimalChunk = 4 * 1024 * 1024 }                         
            
            voidwarp_tcp_sender_set_chunk_size(sender, optimalChunk)
            
            // 4. Send with Retry Logic
            let maxRetries = 3
            var success = false
            var finalRes: Int32 = -1
            
            let ipC = strdup(activeIp)
            let nameC = strdup(myName)
            
            // Progress Monitor Task
            let progressTask = Task {
                while !Task.isCancelled {
                    let p = voidwarp_tcp_sender_get_progress(sender)
                    await MainActor.run {
                        if case .transferring(_, _) = self.transferState {
                            self.transferState = .transferring(progress: p, speed: "Sending...")
                        }
                    }
                    if p >= 100 { break }
                    try? await Task.sleep(nanoseconds: 200_000_000)
                }
            }
            
            for attempt in 1...maxRetries {
                print("🔄 Sending attempt \(attempt)/\(maxRetries) to \(activeIp)...")
                
                finalRes = voidwarp_tcp_sender_start(sender, ipC, targetPort, nameC)
                
                if finalRes == 0 {
                    success = true
                    break
                } else if finalRes == 6 { // IO Error (Socket closed)
                    print("⚠️ IO Error, retrying...")
                    try? await Task.sleep(nanoseconds: 500_000_000) // Backoff 0.5s
                    continue
                } else {
                    // Fatal error (Rejected, Checksum mismatch, etc) - Do not retry
                    break
                }
            }
            
            free(ipC); free(nameC)
            progressTask.cancel()
            voidwarp_tcp_sender_destroy(sender)
            
            await MainActor.run {
                if success {
                    self.transferState = .completed(success: true, message: "Sent Successfully")
                } else {
                    let errorMsg = self.mapErrorCode(finalRes)
                    self.transferState = .error(errorMsg)
                }
                
                // Reset state
                Task {
                    try? await Task.sleep(nanoseconds: 3_000_000_000)
                    if case .completed = self.transferState { self.transferState = .idle }
                    if case .error = self.transferState { self.transferState = .idle }
                }
            }
        }
    }
    
    // Connectivity Check Helper
    private func ping(ip: String, port: UInt16) -> Bool {
        let ipC = strdup(ip)
        defer { free(ipC) }
        return voidwarp_transport_ping(ipC, port)
    }
    
    private func mapErrorCode(_ code: Int32) -> String {
        switch code {
        case 1: return "Transfer Rejected by Peer"
        case 2: return "Checksum Mismatch (Corrupt)"
        case 3: return "Connection Failed"
        case 4: return "Timeout"
        case 5: return "Cancelled"
        case 6: return "Network I/O Error"
        default: return "Unknown Error (\(code))"
        }
    }
    
    // MARK: - Receiving
    
    private func startReceiverLoop() {
        guard let rx = receiverHandle else { return }
        voidwarp_receiver_start(rx)
        
        receiverTask = Task.detached(priority: .background) { [weak self] in
            while !Task.isCancelled {
                let state = voidwarp_receiver_get_state(rx)
                
                if state == 2 { // Awaiting Acceptance
                    let pending = voidwarp_receiver_get_pending(rx)
                    if pending.is_valid {
                        let name = String(cString: pending.file_name)
                        let sender = String(cString: pending.sender_name)
                        let size = pending.file_size
                        voidwarp_free_pending_transfer(pending)
                        
                        await self?.setTransferState(.awaitingAccept(fileName: name, sender: sender, size: size))
                        
                        // Wake lock for receiving
                        await MainActor.run { UIApplication.shared.isIdleTimerDisabled = true }
                        
                        // Wait for decision
                        while !Task.isCancelled {
                            try? await Task.sleep(nanoseconds: 500_000_000)
                            let current = await self?.transferState
                            if case .awaitingAccept = current { continue }
                            break
                        }
                    }
                } else if state == 3 { // Receiving
                    let p = voidwarp_receiver_get_progress(rx)
                    await self?.setTransferState(.transferring(progress: p, speed: "Receiving..."))
                } else if state == 4 { // Completed
                    await self?.setTransferState(.completed(success: true, message: "File Received"))
                    
                    // Release lock
                    await MainActor.run { UIApplication.shared.isIdleTimerDisabled = false }
                    
                    voidwarp_receiver_start(rx)
                    try? await Task.sleep(nanoseconds: 3_000_000_000)
                    await self?.setTransferState(.idle)
                }
                
                try? await Task.sleep(nanoseconds: 500_000_000)
            }
        }
    }
    
    private func setTransferState(_ state: TransferState) {
        self.transferState = state
    }
    
    func acceptTransfer() {
        guard let rx = receiverHandle else { return }
        let docs = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
        
        if case .awaitingAccept(let fileName, _, _) = transferState {
            let fullUrl = docs.appendingPathComponent(fileName)
            let pathC = strdup(fullUrl.path)
            
            Task.detached {
                voidwarp_receiver_accept(rx, pathC)
                free(pathC)
            }
            self.transferState = .transferring(progress: 0, speed: "Starting...")
        }
    }
    
    func rejectTransfer() {
        guard let rx = receiverHandle else { return }
        voidwarp_receiver_reject(rx)
        UIApplication.shared.isIdleTimerDisabled = false
        self.transferState = .idle
    }
    
    // Helper for C-Strings
    private func strdup(_ s: String) -> UnsafeMutablePointer<Int8> {
        return strdup(s.cString(using: .utf8))
    }
}
