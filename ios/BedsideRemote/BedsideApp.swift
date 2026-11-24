import SwiftUI

@main
struct BedsideApp: App {
    @State private var model = RemoteModel()
    @AppStorage("theme") private var theme = "system"
    @Environment(\.scenePhase) private var phase

    var body: some Scene {
        WindowGroup {
            RootView(model: model)
                .preferredColorScheme(theme == "light" ? .light : theme == "dark" ? .dark : nil)
                .tint(Color(red: 0.67, green: 0.49, blue: 0.24))
                .onChange(of: phase, initial: true) { _, value in model.setActive(value != .background) }
        }
    }
}

struct RootView: View {
    let model: RemoteModel
    @State private var tab = "remote"
    @State private var settings = false
    @State private var draft = ""
    @AppStorage("pendingYouTubeLink") private var pendingYouTubeLink = ""

    var body: some View {
        TabView(selection: $tab) {
            NavigationStack {
                RemoteView(model: model, devices: { tab = "devices" })
                    .navigationTitle("Bedside Remote")
                    .navigationBarTitleDisplayMode(.inline)
                    .toolbar {
                        ToolbarItem(placement: .topBarTrailing) {
                            Button("Settings", systemImage: "gearshape") { settings = true }
                        }
                    }
            }
            .tabItem { Label("Remote", systemImage: "cursorarrow") }
            .tag("remote")

            NavigationStack { QueueView(model: model, draft: $draft) }
                .tabItem { Label("Queue", systemImage: "list.bullet") }
                .tag("queue")

            NavigationStack { DevicesView(model: model) }
                .tabItem { Label("Devices", systemImage: "desktopcomputer") }
                .tag("devices")
        }
        .sheet(isPresented: $settings) { SettingsView() }
        .onChange(of: pendingYouTubeLink, initial: true) { _, value in
            guard let canonical = youtubeURL(value) else { return }
            pendingYouTubeLink = ""
            draft = canonical
            tab = "queue"
        }
        .onOpenURL { url in
            guard url.scheme == "bedsideremote", url.host == "youtube",
                  url.path.isEmpty, url.user == nil, url.password == nil, url.port == nil,
                  let parts = URLComponents(url: url, resolvingAgainstBaseURL: false),
                  let query = parts.queryItems, query.count == 1, query[0].name == "url",
                  let raw = query[0].value, let canonical = youtubeURL(raw) else { return }
            draft = canonical
            tab = "queue"
        }
        .safeAreaInset(edge: .top, spacing: 0) {
            if let message = model.message {
                HStack {
                    Text(message).font(.footnote)
                    Spacer()
                    Button("Dismiss", systemImage: "xmark") { model.message = nil }
                        .labelStyle(.iconOnly)
                        .frame(minWidth: 44, minHeight: 44)
                }
                .padding(.horizontal)
                .background(.thinMaterial)
            }
        }
    }
}

private struct RemoteView: View {
    let model: RemoteModel
    let devices: () -> Void
    @Environment(\.dynamicTypeSize) private var textSize
    @Environment(\.scenePhase) private var phase
    @AppStorage("sensitivity") private var sensitivity = 1.5
    @AppStorage("haptics") private var haptics = false
    @State private var sheet: Sheet?
    @State private var textDraft = ""

    private enum Sheet: String, Identifiable {
        case keyboard, windows, playback
        var id: String { rawValue }
    }

    var body: some View {
        GeometryReader { size in
            VStack(spacing: 8) {
                if !model.connected {
                    Button(model.current == nil ? "Pair a computer" : "Reconnect to \(model.current?.name ?? "Windows")", action: devices)
                        .frame(minHeight: 44)
                } else if let name = model.current?.name {
                    Text(name).font(.footnote).lineLimit(1)
                        .foregroundStyle(.secondary)
                }

                Trackpad(enabled: model.connected && sheet == nil && phase == .active, supportsDrag: model.supportsDrag,
                         sensitivity: min(3, max(0.5, sensitivity)), haptics: haptics, deviceID: model.selectedID) {
                    model.send($0, $1)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 26))
                .overlay {
                    Image(systemName: "hand.point.up.left")
                        .font(.largeTitle).foregroundStyle(.tertiary)
                        .accessibilityHidden(true).allowsHitTesting(false)
                }

                HStack {
                    icon("Keyboard", "keyboard") { sheet = .keyboard }
                    icon("Windows Escape", "escape") { model.send("input.key", ["key": "escape"]) }
                    Menu("Open media apps", systemImage: "square.grid.2x2") {
                        ForEach(model.catalog?.apps ?? []) { app in
                            Button(app.name) { model.send("app.launch", ["appId": app.appId]) }
                        }
                    }
                    .labelStyle(.iconOnly)
                    .frame(maxWidth: .infinity, minHeight: 44)
                    .disabled(model.catalog?.apps.isEmpty != false)
                    icon("Windows controls", "speaker.wave.2") { sheet = .windows }
                }
                .disabled(!model.connected)

                if model.snapshot?.playback != nil || model.snapshot?.sessions.isEmpty == false {
                    if size.size.height < 430 || textSize.isAccessibilitySize {
                        Button("Browser playback") { sheet = .playback }.frame(minHeight: 44)
                    } else {
                        PlaybackView(model: model)
                    }
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 6)
        }
        .sheet(item: $sheet) { value in
            NavigationStack {
                Group {
                    switch value {
                    case .keyboard: keyboard
                    case .windows: windows
                    case .playback: ScrollView { PlaybackView(model: model).padding() }
                    }
                }
                .toolbar {
                    ToolbarItem(placement: .confirmationAction) { Button("Done") { sheet = nil } }
                }
            }
        }
    }

    private func icon(_ title: String, _ symbol: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Label(title, systemImage: symbol).labelStyle(.iconOnly)
                .frame(maxWidth: .infinity, minHeight: 44)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private var keyboard: some View {
        Form {
            TextField("Text to send", text: $textDraft, axis: .vertical)
                .lineLimit(2...6)
            Button("Send text") {
                let sent = textDraft
                model.send("input.text", ["text": sent]) {
                    if textDraft == sent { textDraft = "" }
                }
            }
            .disabled(!model.connected || textDraft.isEmpty || textDraft.utf16.count > 4096 || model.pendingActions.contains("input.text"))
            ForEach(["enter", "backspace", "tab", "space"], id: \.self) { key in
                Button(key.capitalized) { model.send("input.key", ["key": key]) }
                    .disabled(!model.connected)
            }
        }
        .navigationTitle("Keyboard")
    }

    private var windows: some View {
        List {
            Section("Windows media") {
                Button("Play or pause") { media("playPause") }
                Button("Previous") { media("previous") }
                Button("Next") { media("next") }
                Button("Seek back") { media("seekBackward") }
                Button("Seek forward") { media("seekForward") }
            }
            Section("Volume") {
                Button("Raise volume") { media("volumeUp") }
                Button("Lower volume") { media("volumeDown") }
                Button("Mute or unmute") { media("mute") }
            }
            Section("Windows navigation") {
                Button("Start") { model.send("input.key", ["key": "home"]) }
                directions(action: model.catalog?.controls.contains("desktopNavigation") == true ? "input.navigate" : "input.key")
            }
            if model.snapshot?.activeSessionId != nil {
                Section("Browser navigation") { directions(action: "browser.navigate") }
            }
        }
        .disabled(!model.connected)
        .navigationTitle("Windows controls")
    }

    private func media(_ command: String) { model.send("media.command", ["command": command]) }

    private func directions(action: String) -> some View {
        ForEach(["up", "left", "enter", "right", "down", "escape"], id: \.self) { key in
            Button(key.capitalized) { model.send(action, ["key": key]) }
        }
    }
}

private struct PlaybackView: View {
    let model: RemoteModel
    @State private var seek: Double?

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                if let player = model.snapshot?.playback {
                    Text("\(player.platform.capitalized) · \(player.title)").font(.subheadline.bold()).lineLimit(1)
                } else {
                    Text("Browser playback").font(.subheadline.bold())
                }
                Spacer()
                if let state = model.snapshot, state.sessions.count > 1 || state.activeSessionId == nil {
                    Menu("Choose browser tab", systemImage: "macwindow.on.rectangle") {
                        ForEach(state.sessions) { session in
                            Button(session.title) { model.send("browser.select", ["sessionId": session.id]) }
                        }
                    }
                    .labelStyle(.iconOnly).frame(minWidth: 44, minHeight: 44)
                }
            }
            if let player = model.snapshot?.playback {
                controls(player)
            }
        }
        .padding(8)
        .background(Color(uiColor: .secondarySystemBackground), in: RoundedRectangle(cornerRadius: 18))
        .onChange(of: model.snapshot?.activeSessionId) { _, _ in seek = nil }
    }

    private func controls(_ player: Snapshot.Playback) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            if player.duration.isFinite && player.duration > 0 {
                Slider(value: Binding(get: { min(player.duration, max(0, seek ?? player.position)) },
                                      set: { seek = $0 }), in: 0...player.duration) { editing in
                    if !editing, let position = seek {
                        model.browser("seek", position: position)
                        seek = nil
                    }
                }
                .accessibilityLabel("Playback position")
                .disabled(!player.capabilities.contains("seek"))
                HStack {
                    Text(time(seek ?? player.position))
                    Spacer()
                    Text(time(player.duration))
                }
                .font(.caption.monospacedDigit()).foregroundStyle(.secondary)
            }

            HStack(spacing: 10) {
                if player.capabilities.contains("seek") {
                    control("Back ten seconds", "gobackward.10") {
                        model.browser("seek", position: max(0, player.position - 10))
                    }
                }
                if player.capabilities.contains("playPause") {
                    control(player.paused ? "Play" : "Pause", player.paused ? "play.fill" : "pause.fill") {
                        model.browser("playPause")
                    }
                }
                if player.capabilities.contains("seek") {
                    control("Forward ten seconds", "goforward.10") {
                        model.browser("seek", position: min(player.duration, player.position + 10))
                    }
                }
                if player.capabilities.contains("fullscreen") {
                    control("Toggle fullscreen", "arrow.down.right.and.arrow.up.left") { model.browser("fullscreen") }
                }
            }
            let actions = contextual(player)
            if let first = actions.first {
                HStack {
                    Button(first.label) { model.browser(first.command) }.frame(minHeight: 44)
                    if actions.count > 1 {
                        Menu("More") {
                            ForEach(actions.dropFirst(), id: \.command) { action in
                                Button(action.label) { model.browser(action.command) }
                            }
                        }
                        .frame(minHeight: 44)
                    }
                }
                .font(.subheadline)
            }
            if let error = player.error { Text(error).font(.caption).foregroundStyle(.red).lineLimit(1) }
            if player.platform == "youtube", let state = model.snapshot {
                let position = state.queue.firstIndex { $0.id == state.currentEntryId }
                if let item = state.queue.dropFirst(position.map { $0 + 1 } ?? 0).first {
                    Text("Up next · \(item.title)").font(.caption).lineLimit(1)
                }
            }
        }
    }

    private struct Action {
        let command: String
        let label: String
    }

    private func contextual(_ player: Snapshot.Playback) -> [Action] {
        var actions = [Action(command: "skipIntro", label: "Skip intro"), Action(command: "skipRecap", label: "Skip recap"),
                       Action(command: "nextEpisode", label: "Next episode")]
        if player.platform == "netflix" {
            actions.append(Action(command: player.capabilities.contains("audio") ? "audio" : "subtitles", label: "Audio & subtitles"))
        } else {
            actions.append(Action(command: "subtitles", label: "Subtitles"))
            actions.append(Action(command: "audio", label: "Audio"))
        }
        return actions.filter { player.capabilities.contains($0.command) }
    }

    private func control(_ label: String, _ icon: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Label(label, systemImage: icon).labelStyle(.iconOnly)
                .frame(maxWidth: .infinity, minHeight: 44)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private func time(_ seconds: Double) -> String {
        let whole = Int(min(359999, max(0, seconds)))
        return whole >= 3600
            ? String(format: "%d:%02d:%02d", whole / 3600, whole / 60 % 60, whole % 60)
            : String(format: "%d:%02d", whole / 60, whole % 60)
    }
}
