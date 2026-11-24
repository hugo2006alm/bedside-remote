import AppIntents
import Foundation

struct PrepareYouTubeLink: AppIntent {
    static let title: LocalizedStringResource = "Prepare a YouTube link"
    static let description = IntentDescription("Open a validated YouTube link in the queue for review.")
    static let openAppWhenRun = true

    @Parameter(title: "YouTube URL")
    var url: URL

    func perform() async throws -> some IntentResult {
        guard let canonical = youtubeURL(url.absoluteString) else { throw InvalidYouTubeLink() }
        await MainActor.run {
            UserDefaults.standard.set(canonical, forKey: "pendingYouTubeLink")
        }
        return .result()
    }
}

private struct InvalidYouTubeLink: LocalizedError {
    var errorDescription: String? { "Choose an official YouTube video URL." }
}

struct BedsideShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: PrepareYouTubeLink(),
            phrases: ["Prepare a YouTube link in \(.applicationName)"],
            shortTitle: "Prepare a YouTube link",
            systemImageName: "play.rectangle"
        )
    }
}
