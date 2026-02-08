import SwiftUI
import PhotosUI

struct ContentView: View {
    @EnvironmentObject var engine: VoidWarpEngine
    @State private var selectedPeer: PeerItem?
    @State private var showFilePicker = false
    @State private var showPhotoPicker = false
    @State private var selectedPhotoItem: PhotosPickerItem?
    @State private var pulse = false
    
    var body: some View {
        ZStack {
            // Background
            Color("VoidBlack").ignoresSafeArea()
            
            // Animated Background Glow
            GeometryReader { geo in
                Circle()
                    .fill(Color.purple.opacity(pulse ? 0.15 : 0.05))
                    .frame(width: geo.size.width * 1.5, height: geo.size.width * 1.5)
                    .position(x: geo.size.width / 2, y: -geo.size.height * 0.2)
                    .blur(radius: 60)
                    .animation(.easeInOut(duration: 4).repeatForever(autoreverses: true), value: pulse)
            }
            
            VStack(spacing: 0) {
                // MARK: - Header
                HStack {
                    VStack(alignment: .leading) {
                        Text("VoidWarp")
                            .font(.system(size: 34, weight: .bold, design: .rounded))
                            .foregroundStyle(
                                LinearGradient(colors: [.purple, .blue], startPoint: .topLeading, endPoint: .bottomTrailing)
                            )
                        Text(engine.deviceName)
                            .font(.subheadline)
                            .foregroundStyle(.gray)
                    }
                    Spacer()
                    
                    Button(action: { 
                        let impactMed = UIImpactFeedbackGenerator(style: .medium)
                        impactMed.impactOccurred()
                        engine.isDiscovering ? engine.stopDiscovery() : engine.startDiscovery()
                    }) {
                        Image(systemName: engine.isDiscovering ? "antenna.radiowaves.left.and.right" : "antenna.radiowaves.left.and.right.slash")
                            .font(.system(size: 20))
                            .padding(10)
                            .background(engine.isDiscovering ? Color.purple.opacity(0.2) : Color.gray.opacity(0.1))
                            .clipShape(Circle())
                            .foregroundStyle(engine.isDiscovering ? .purple : .gray)
                    }
                }
                .padding(.horizontal)
                .padding(.top, 20)
                .padding(.bottom, 10)
                
                // MARK: - Radar Area
                ZStack {
                    if engine.discoveredPeers.isEmpty {
                        VStack(spacing: 15) {
                            ZStack {
                                Circle()
                                    .stroke(Color.white.opacity(0.05), lineWidth: 1)
                                    .frame(width: 200, height: 200)
                                
                                if engine.isDiscovering {
                                    Circle()
                                        .stroke(Color.cyan.opacity(0.3), lineWidth: 2)
                                        .frame(width: 200, height: 200)
                                        .scaleEffect(pulse ? 1.3 : 0.8)
                                        .opacity(pulse ? 0 : 1)
                                        .animation(.easeOut(duration: 2).repeatForever(autoreverses: false), value: pulse)
                                }
                                
                                Image(systemName: "wifi")
                                    .font(.system(size: 40))
                                    .foregroundStyle(engine.isDiscovering ? .white : .gray)
                                    .opacity(0.5)
                            }
                            
                            if engine.isDiscovering {
                                Text("Scanning for devices...")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            } else {
                                Text("Discovery Paused")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                        }
                        .frame(maxWidth: .infinity, maxHeight: 300)
                    } else {
                        // MARK: - Peer List
                        ScrollView {
                            LazyVStack(spacing: 16) {
                                ForEach(engine.discoveredPeers) { peer in
                                    PeerRow(peer: peer) {
                                        selectedPeer = peer
                                        let impact = UIImpactFeedbackGenerator(style: .light)
                                        impact.impactOccurred()
                                        showFilePicker = true
                                    }
                                    .transition(.scale.combined(with: .opacity))
                                }
                            }
                            .padding()
                        }
                    }
                }
                .frame(maxHeight: .infinity)
                
                // MARK: - Transfer Overlay
                if case .transferring(let progress, let speed) = engine.transferState {
                    TransferStatusCard(title: "Transferring...", progress: progress, status: speed, color: .purple)
                        .padding()
                        .transition(.move(edge: .bottom))
                }
                
                if case .completed(let success, let msg) = engine.transferState {
                    TransferStatusCard(title: success ? "Success" : "Failed", progress: 100, status: msg, color: success ? .green : .red)
                        .padding()
                        .transition(.move(edge: .bottom))
                }
                
                if case .error(let msg) = engine.transferState {
                     TransferStatusCard(title: "Error", progress: 0, status: msg, color: .red)
                        .padding()
                        .transition(.move(edge: .bottom))
                }
            }
        }
        .task {
            engine.startDiscovery()
            withAnimation { pulse = true }
        }
        .onDisappear {
            engine.stopDiscovery()
        }
        // File Pickers
        .fileImporter(isPresented: $showFilePicker, allowedContentTypes: [.item]) { result in
            if let url = try? result.get(), let peer = selectedPeer {
                engine.sendFile(url: url, to: peer)
            }
        }
        .photosPicker(isPresented: $showPhotoPicker, selection: $selectedPhotoItem)
        // Incoming Transfer Alert
        .alert(item: bindingIncomingTransfer) { transfer in
            Alert(
                title: Text("Received File"),
                message: Text("Accept '\(transfer.fileName)' from \(transfer.sender)?"),
                primaryButton: .default(Text("Accept")) { engine.acceptTransfer() },
                secondaryButton: .cancel(Text("Reject")) { engine.rejectTransfer() }
            )
        }
        .animation(.spring(), value: engine.discoveredPeers)
        .animation(.spring(), value: engine.transferState)
    }
    
    // Binding helper
    var bindingIncomingTransfer: Binding<IncomingTransfer?> {
        Binding {
            if case .awaitingAccept(let fileName, let sender, _) = engine.transferState {
                return IncomingTransfer(fileName: fileName, sender: sender)
            }
            return nil
        } set: { _ in }
    }
}

// MARK: - Subviews

struct PeerRow: View {
    let peer: PeerItem
    let action: () -> Void
    
    var body: some View {
        Button(action: action) {
            HStack(spacing: 15) {
                ZStack {
                    Circle()
                        .fill(LinearGradient(colors: [.blue, .purple], startPoint: .topLeading, endPoint: .bottomTrailing))
                        .frame(width: 50, height: 50)
                    
                    Image(systemName: "laptopcomputer")
                        .foregroundStyle(.white)
                        .font(.system(size: 20))
                }
                
                VStack(alignment: .leading, spacing: 4) {
                    Text(peer.name)
                        .font(.headline)
                        .foregroundStyle(.white)
                    // Robustness: Show IP count if multiple
                    if peer.ips.count > 1 {
                        Text("\(peer.displayIp) +\(peer.ips.count-1) more")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                    } else {
                        Text(peer.displayIp)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                    }
                }
                
                Spacer()
                
                Image(systemName: "arrow.up.circle.fill")
                    .font(.system(size: 28))
                    .foregroundStyle(.white.opacity(0.8))
            }
            .padding(16)
            .background(Color(uiColor: .secondarySystemBackground).opacity(0.1))
            .background(.thinMaterial)
            .clipShape(RoundedRectangle(cornerRadius: 16))
            .overlay(
                RoundedRectangle(cornerRadius: 16)
                    .stroke(Color.white.opacity(0.1), lineWidth: 1)
            )
        }
        .buttonStyle(ScaleButtonStyle())
    }
}

struct TransferStatusCard: View {
    let title: String
    let progress: Float
    let status: String
    let color: Color
    
    var body: some View {
        VStack(spacing: 12) {
            HStack {
                Text(title)
                    .font(.headline)
                Spacer()
                Text("\(Int(progress))%")
                    .font(.subheadline)
                    .monospacedDigit()
            }
            .foregroundStyle(.white)
            
            ProgressView(value: progress, total: 100)
                .tint(color)
            
            Text(status)
                .font(.caption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(20)
        .background(Color.black.opacity(0.8))
        .background(.ultraThinMaterial)
        .cornerRadius(20)
        .shadow(color: color.opacity(0.3), radius: 20, x: 0, y: 10)
        .overlay(
            RoundedRectangle(cornerRadius: 20)
                .stroke(Color.white.opacity(0.1), lineWidth: 1)
        )
    }
}

// Bouncy button effect
struct ScaleButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .animation(.spring(response: 0.3, dampingFraction: 0.6), value: configuration.isPressed)
    }
}

struct IncomingTransfer: Identifiable {
    let id = UUID()
    let fileName: String
    let sender: String
}
