import AppKit

enum Client: String, CaseIterable, Identifiable {
    case chatgpt, grok, muse, claude
    var id: String { rawValue }
    var name: String { switch self { case .chatgpt: return "ChatGPT"; case .grok: return "Grok Bot"; case .muse: return "Muse"; case .claude: return "Claude" } }
    var bundleID: String { switch self { case .chatgpt: return "com.openai.codex"; case .grok: return "com.anysphere.sand"; case .muse: return "com.meta.endo"; case .claude: return "com.anthropic.claudefordesktop" } }
    var spriteName: String { self == .grok ? "pet-grok-glossy" : "pet-\(rawValue)" }
    var applicationURL: URL? { NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) }
}
