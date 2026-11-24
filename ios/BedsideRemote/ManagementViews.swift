import SwiftUI

struct DevicesView: View {
    let model: RemoteModel
    @State private var discovery = Discovery()
    @State private var host = ""
    @State private var port = "8765"
    @State private var pin = ""
    @State private var code = ""
    @State private var confirmed = false
    @State private var probeFailed = false

    private var pairingEndpoint: Endpoint? {
        guard let port = UInt16(port) else { return nil }
        return Endpoint(host.trimmingCharacters(in: .whitespacesAndNewlines), port)
    }

    var body: some View {
        Form {
            Section("Paired computers") {
                if model.computers.count > 1 && !model.connected {
                    if model.checkingComputers {
                        ProgressView("Checking paired computers…")
                    } else {
                        Text(model.reachableIDs?.isEmpty == true && model.current == nil
                             ? "No paired computers responded. Check the network or try again."
                             : "Choose which computer to control.")
                        Button("Check availability") { model.checkComputers() }
                    }
                }
                ForEach(sortedComputers) { computer in
                    Button {
                        model.select(computer.id)
                    } label: {
                        HStack {
                            VStack(alignment: .leading) {
                                Text(computer.name)
                                Text(computer.endpoint.host).font(.caption).foregroundStyle(.secondary)
                                if model.reachableIDs?.contains(computer.id) == true {
                                    Text("Available on this network").font(.caption).foregroundStyle(.secondary)
                                }
                            }
                            Spacer()
                            if computer.id == model.selectedID {
                                Image(systemName: "checkmark").accessibilityLabel("Selected")
                            }
                        }
                    }
                    .accessibilityIdentifier("paired-computer-\(computer.id)")
                    .swipeActions {
                        Button("Forget", role: .destructive) { model.forget(computer.id) }
                    }
                }
                if model.current != nil { Button("Reconnect") { model.reconnect() } }
                Text("Revoke access in the Windows agent. Forget removes credentials from this iPhone.")
                    .font(.footnote).foregroundStyle(.secondary)
            }
            Section("Discovered computers") {
                ForEach(discovery.computers) { computer in
                    Button(computer.name) {
                        host = computer.endpoint.host
                        port = String(computer.endpoint.port)
                    }
                }
                if discovery.computers.isEmpty {
                    Text("No computers found. Allow local network access or enter the address shown on Windows.")
                        .font(.footnote).foregroundStyle(.secondary)
                }
            }
            Section("Pair a computer") {
                TextField("Computer IP address", text: $host)
                    .keyboardType(.numbersAndPunctuation).textInputAutocapitalization(.never).autocorrectionDisabled()
                TextField("Port", text: $port).keyboardType(.numberPad)
                TextField("SHA-256 fingerprint", text: $pin, axis: .vertical)
                    .textInputAutocapitalization(.never).autocorrectionDisabled()
                if probeFailed {
                    Text("Could not read the fingerprint. Check the address or enter it from Windows.")
                        .font(.footnote).foregroundStyle(.secondary)
                }
                Toggle("I compared all 64 fingerprint digits with Windows", isOn: $confirmed)
                SecureField("Six-digit pairing code", text: $code).keyboardType(.numberPad)
                Button(model.pairing ? "Cancel pairing" : "Request Windows approval") {
                    if model.pairing { model.cancelPairing() }
                    else { model.pair(host: host, port: port, fingerprint: pin, code: code, confirmed: confirmed) }
                }
                .disabled(!model.pairing && (!confirmed || code.count != 6))
                Text("Open pairing in the Windows agent, compare the full fingerprint and approve this iPhone on the computer.")
                    .font(.footnote).foregroundStyle(.secondary)
            }
        }
        .navigationTitle("Devices")
        .onAppear { discovery.start() }
        .onDisappear { discovery.stop() }
        .onChange(of: pairingEndpoint) { _, _ in
            confirmed = false
            pin = ""
            probeFailed = false
        }
        .onChange(of: pin) { _, _ in confirmed = false }
        .task(id: pairingEndpoint) { [endpoint = pairingEndpoint] in
            guard let endpoint else { return }
            // Let manual address entry settle before starting a probe.
            try? await Task.sleep(for: .milliseconds(350))
            guard !Task.isCancelled, pairingEndpoint == endpoint, pin.isEmpty else { return }
            let candidate = await CertificateFingerprint.candidate(for: endpoint)
            guard !Task.isCancelled, pairingEndpoint == endpoint, pin.isEmpty else { return }
            if let candidate {
                confirmed = false
                pin = candidate.uppercased()
            } else {
                probeFailed = true
            }
        }
        .onChange(of: model.computers.count) { _, _ in
            if model.pairing {
                code = ""
                pin = ""
                confirmed = false
            }
        }
    }
    private var sortedComputers: [Computer] {
        guard let available = model.reachableIDs else { return model.computers }
        return model.computers.filter { available.contains($0.id) } +
            model.computers.filter { !available.contains($0.id) }
    }

}

struct QueueView: View {
    let model: RemoteModel
    @Binding var draft: String
    @State private var clear = false

    var body: some View {
        List {
            Section("Add a YouTube video") {
                TextField("YouTube URL", text: $draft)
                    .keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
                Button("Play now") { enqueue("now") }.disabled(!canEnqueue)
                Button("Play next") { enqueue("next") }.disabled(!canEnqueue)
                Button("Add to queue") { enqueue("last") }.disabled(!canEnqueue)
            }
            Section("Playback queue") {
                if let state = model.snapshot {
                    ForEach(Array(state.queue.enumerated()), id: \.element.id) { index, item in
                        Label(item.title, systemImage: state.currentEntryId == item.id ? "play.circle" : "film")
                            .swipeActions {
                                Button("Remove", role: .destructive) { model.send("queue.remove", ["entryId": item.id]) }
                            }
                            .contextMenu {
                                if index > 0 { Button("Move up") { move(item.id, to: index - 1) } }
                                if index + 1 < state.queue.count { Button("Move down") { move(item.id, to: index + 1) } }
                            }
                    }
                    if !state.queue.isEmpty {
                        Button("Previous video") { model.send("queue.previous") }
                        Button("Next video") { model.send("queue.next") }
                        Button("Clear queue", role: .destructive) { clear = true }
                    }
                } else {
                    Text("Connect to Windows to see the queue.")
                }
            }
        }
        .navigationTitle("Queue")
        .confirmationDialog("Clear the playback queue?", isPresented: $clear, titleVisibility: .visible) {
            Button("Clear queue", role: .destructive) { model.send("queue.clear") }
        }
    }

    private var canEnqueue: Bool {
        model.connected && youtubeURL(draft) != nil && !model.pendingActions.contains("youtube.enqueue")
    }

    private func enqueue(_ mode: String) {
        let submitted = draft
        guard let url = youtubeURL(submitted) else { return }
        model.send("youtube.enqueue", ["url": url, "mode": mode]) {
            if draft == submitted { draft = "" }
        }
    }

    private func move(_ id: String, to index: Int) {
        model.send("queue.move", ["entryId": id, "index": index])
    }
}

struct SettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @AppStorage("theme") private var theme = "system"
    @AppStorage("sensitivity") private var sensitivity = 1.5
    @AppStorage("haptics") private var haptics = false
    @State private var release: Release?
    @State private var status: String?
    @State private var checking = false

    var body: some View {
        NavigationStack {
            Form {
                Picker("Appearance", selection: $theme) {
                    Text("System").tag("system")
                    Text("Light").tag("light")
                    Text("Dark").tag("dark")
                }
                Slider(value: $sensitivity, in: 0.5...3) { Text("Pointer sensitivity") }
                Toggle("Haptic feedback", isOn: $haptics)
                Section("Updates") {
                    Button(checking ? "Checking…" : "Check for updates") {
                        Task { await checkUpdates() }
                    }.disabled(checking)
                    if let status { Text(status).font(.footnote) }
                    if let release, let url = URL(string: release.html_url) {
                        Link("View \(release.tag_name) for SideStore", destination: url)
                    }
                }
            }
            .navigationTitle("Settings")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } } }
        }
    }

    private struct Release: Decodable {
        struct Asset: Decodable { let name: String }
        let tag_name: String
        let html_url: String
        let draft: Bool
        let assets: [Asset]
    }

    private func checkUpdates() async {
        checking = true
        defer { checking = false }
        release = nil
        do {
            let url = URL(string: "https://api.github.com/repos/hugo2006alm/bedside-remote/releases?per_page=25")!
            let (data, response) = try await URLSession.shared.data(from: url)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw RemoteError.invalidResponse }
            let releases = try JSONDecoder().decode([Release].self, from: data)
            let matching = releases.filter { release in
                !release.draft && release.assets.contains { $0.name == "bedside-remote-ios-\(release.tag_name)-arm64-sideload.ipa" }
            }
            guard let latest = matching.max(by: { versionIsNewer($1.tag_name, than: $0.tag_name) }) else {
                status = "No published iOS package found."
                return
            }
            let version = Bundle.main.object(forInfoDictionaryKey: "BedsideVersion") as? String ?? ""
            if !versionIsNewer(latest.tag_name, than: version) {
                status = "This iPhone has the latest published iOS package."
            } else {
                status = "Published iOS package \(latest.tag_name) is available."
                release = latest
            }
        } catch {
            status = "Could not check GitHub releases."
        }
    }
}
