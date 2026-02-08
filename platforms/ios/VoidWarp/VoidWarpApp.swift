import SwiftUI

@main
struct VoidWarpApp: App {
    @StateObject private var engine = VoidWarpEngine.shared
    
    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(engine)
                .preferredColorScheme(.dark) // Cyberpunk is dark only
        }
    }
}
