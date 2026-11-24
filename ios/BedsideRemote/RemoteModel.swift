import Foundation
import Observation
import UIKit

@MainActor
@Observable
final class RemoteModel {
    private(set) var computers: [Computer] = []
    private(set) var snapshot: Snapshot?
    private(set) var catalog: Catalog?
    private(set) var connected = false
    private(set) var pairing = false
    private(set) var selectedID: String?
    var message: String?
    private(set) var pendingActions: Set<String> = []

    @ObservationIgnored private var active = false
    @ObservationIgnored private var connection: Task<Void, Never>?
    @ObservationIgnored private var pairingTask: Task<Void, Never>?
    @ObservationIgnored private var socket: URLSessionWebSocketTask?
    @ObservationIgnored private var generation = UUID()
    @ObservationIgnored private var sequence = 0
    @ObservationIgnored private var sending = false
    @ObservationIgnored private var outgoing: [Outgoing] = []
    @ObservationIgnored private var pending: [String: Pending] = [:]

    private struct Outgoing {
        var action: String
        var payload: [String: Any]
        var id: String
    }

    private struct Pending {
        let action: String
        let timeout: Task<Void, Never>
        let accepted: (() -> Void)?
    }

    init() {
        #if DEBUG && targetEnvironment(simulator)
        if let index = CommandLine.arguments.firstIndex(of: "--preview-state"),
           CommandLine.arguments.indices.contains(index + 1) {
            configurePreview(CommandLine.arguments[index + 1])
            return
        }
        #endif
        do {
            computers = try PairingStore.load()
            selectedID = UserDefaults.standard.string(forKey: "selectedComputer")
            if !computers.contains(where: { $0.id == selectedID }) { selectedID = computers.first?.id }
        } catch {
            message = RemoteError.storage.localizedDescription
        }
    }

    var current: Computer? { computers.first { $0.id == selectedID } }
    var supportsDrag: Bool { catalog?.controls.contains("drag") == true }

    #if DEBUG && targetEnvironment(simulator)
    private var preview = false

    private func configurePreview(_ mode: String) {
        preview = true
        if mode == "disconnected" { return }
        connected = true
        selectedID = "preview"
        computers = [Computer(id: "preview", name: "Bedroom PC", endpoint: Endpoint("127.0.0.1", 8765)!, pin: "", token: "")]
        if mode == "idle" { return }
        let youtube = mode == "youtube"
        let platform = youtube ? "youtube" : "netflix"
        var capabilities = ["playPause", "seek", "fullscreen"]
        if mode == "netflix-actions" {
            capabilities += ["skipIntro", "nextEpisode", "audio"]
        }
        let title = youtube ? "A quiet evening" : "Evening series · Episode 3"
        let player = Snapshot.Playback(platform: platform, title: title, paused: false,
                                       position: 318, duration: 1200, capabilities: capabilities, error: nil)
        let queue = youtube ? [Snapshot.QueueItem(entryId: "next", title: "Night walks")] : []
        snapshot = Snapshot(version: 1, type: "state", activeSessionId: "preview:1",
                            sessions: [.init(sessionId: "preview:1", title: title)],
                            playback: player, queue: queue, currentEntryId: nil)
    }
    #endif

    func setActive(_ value: Bool) {
        #if DEBUG && targetEnvironment(simulator)
        if preview { return }
        #endif
        guard value != active else { return }
        active = value
        if !value { cancelPairing() }
        reconnect()
    }

    func select(_ id: String?) {
        selectedID = id
        UserDefaults.standard.set(id, forKey: "selectedComputer")
        reconnect()
    }

    func forget(_ id: String) {
        let remaining = computers.filter { $0.id != id }
        do {
            try PairingStore.save(remaining)
            computers = remaining
            if id == selectedID { select(remaining.first?.id) }
        } catch {
            message = error.localizedDescription
        }
    }

    func cancelPairing() {
        pairingTask?.cancel()
    }

    func pair(host: String, port: String, fingerprint: String, code: String, confirmed: Bool) {
        let pin = fingerprint.filter { !$0.isWhitespace && $0 != ":" }.lowercased()
        guard !pairing, confirmed, let port = UInt16(port),
              let endpoint = Endpoint(host.trimmingCharacters(in: .whitespacesAndNewlines), port),
              pin.range(of: "^[0-9a-f]{64}$", options: .regularExpression) != nil, pin.utf8.count == 64,
              code.range(of: "^[0-9]{6}$", options: .regularExpression) != nil, code.utf8.count == 6 else {
            message = RemoteError.invalidDetails.localizedDescription
            return
        }
        pairing = true
        message = nil
        pairingTask = Task {
            defer { pairing = false }
            let session = PinnedDelegate.session(pin: pin)
            defer { session.invalidateAndCancel() }
            do {
                var request = URLRequest(url: endpoint.url("https", "/pair"), timeoutInterval: 310)
                request.httpMethod = "POST"
                request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                request.httpBody = try JSONSerialization.data(withJSONObject: ["version": 1, "deviceName": "iPhone", "code": code])
                let (data, response) = try await session.data(for: request)
                try Task.checkCancellation()
                guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw RemoteError.rejected }
                let result = try JSONDecoder().decode(PairResponse.self, from: data)
                guard result.version == 1, !result.deviceId.isEmpty,
                      Data(base64Encoded: result.token)?.count == 32 else { throw RemoteError.invalidResponse }
                let computer = Computer(id: result.deviceId, name: result.deviceName, endpoint: endpoint, pin: pin, token: result.token)
                let updated = computers.filter { $0.id != computer.id } + [computer]
                try PairingStore.save(updated)
                computers = updated
                select(computer.id)
            } catch {
                if !Task.isCancelled { message = Self.connectionMessage(error) }
            }
        }
    }

    func reconnect() {
        #if DEBUG && targetEnvironment(simulator)
        if preview { return }
        #endif
        connection?.cancel()
        disconnect()
        message = nil
        guard active, let computer = current else { return }
        connection = Task {
            var delay = 1
            while !Task.isCancelled {
                let id = generation
                do {
                    try await connect(computer, generation: id)
                } catch {
                    guard !Task.isCancelled, generation == id else { return }
                    if connected { delay = 1 }
                    disconnect()
                    message = Self.connectionMessage(error)
                    if (error as? RemoteError) == .revoked { return }
                }
                try? await Task.sleep(for: .seconds(delay))
                delay = min(30, delay * 2)
            }
        }
    }

    private static func connectionMessage(_ error: Error) -> String {
        let failure = error as NSError
        if failure.domain == NSURLErrorDomain && [NSURLErrorServerCertificateUntrusted,
            NSURLErrorSecureConnectionFailed, NSURLErrorCancelled].contains(failure.code) {
            return "Could not verify the Windows certificate. Compare its full fingerprint before pairing again."
        }
        return error.localizedDescription
    }

    private func connect(_ computer: Computer, generation id: UUID) async throws {
        let session = PinnedDelegate.session(pin: computer.pin)
        defer { session.invalidateAndCancel() }
        var apps = URLRequest(url: computer.endpoint.url("https", "/apps"), timeoutInterval: 5)
        apps.setValue("Bearer \(computer.token)", forHTTPHeaderField: "Authorization")
        let (data, response) = try await session.data(for: apps)
        guard generation == id else { throw CancellationError() }
        switch (response as? HTTPURLResponse)?.statusCode {
        case 401: throw RemoteError.revoked
        case 404: catalog = nil
        case 200:
            let result = try JSONDecoder().decode(Catalog.self, from: data)
            guard result.version == 1 else { throw RemoteError.invalidResponse }
            catalog = result
        default: throw RemoteError.invalidResponse
        }

        var request = URLRequest(url: computer.endpoint.url("wss", "/ws"))
        request.setValue("Bearer \(computer.token)", forHTTPHeaderField: "Authorization")
        let webSocket = session.webSocketTask(with: request)
        webSocket.maximumMessageSize = 64 * 1024
        socket = webSocket
        webSocket.resume()
        while !Task.isCancelled {
            let incoming: URLSessionWebSocketTask.Message
            do {
                incoming = try await webSocket.receive()
            } catch {
                if (webSocket.response as? HTTPURLResponse)?.statusCode == 401 { throw RemoteError.revoked }
                throw error
            }
            guard generation == id else { throw CancellationError() }
            guard case let .string(text) = incoming, let data = text.data(using: .utf8),
                  let fields = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let version = fields["version"] as? Int, version == 1,
                  let type = fields["type"] as? String else { throw RemoteError.invalidResponse }
            switch type {
            case "state":
                guard Set(fields.keys) == ["version", "type", "deviceName", "extensionConnected", "activeSessionId", "sessions", "playback", "queue", "currentEntryId"] else { throw RemoteError.invalidResponse }
                snapshot = try JSONDecoder().decode(Snapshot.self, from: data)
                if !connected { message = nil }
                connected = true
            case "ack":
                guard Set(fields.keys).isSubset(of: ["version", "type", "id", "ok", "error"]) else { throw RemoteError.invalidResponse }
                let ack = try JSONDecoder().decode(Acknowledgement.self, from: data)
                finish(ack.id, error: ack.ok ? nil : ack.error ?? "Windows rejected the command.")
            default: throw RemoteError.invalidResponse
            }
        }
    }

    private func disconnect() {
        generation = UUID()
        socket?.cancel(with: .goingAway, reason: nil)
        socket = nil
        connected = false
        snapshot = nil
        catalog = nil
        sequence = 0
        sending = false
        outgoing.removeAll()
        pending.values.forEach { $0.timeout.cancel() }
        pending.removeAll()
        pendingActions.removeAll()
    }

    func send(_ action: String, _ payload: [String: Any] = [:], accepted: (() -> Void)? = nil) {
        guard connected, socket != nil else { return }
        let id = UUID().uuidString.lowercased()
        let isMotion = action == "input.pointer" || action == "input.scroll" ||
            action == "input.drag" && payload["phase"] as? String == "move"
        if isMotion, let previous = outgoing.last, previous.action == action,
           action != "input.drag" || previous.payload["phase"] as? String == "move" {
            var combined = payload
            if action == "input.scroll" {
                let total = (previous.payload["delta"] as? Int ?? 0) + (payload["delta"] as? Int ?? 0)
                combined["delta"] = min(1200, max(-1200, total))
            } else {
                for key in ["dx", "dy"] {
                    let total = (previous.payload[key] as? Double ?? 0) + (payload[key] as? Double ?? 0)
                    combined[key] = min(1000.0, max(-1000.0, total))
                }
            }
            outgoing[outgoing.count - 1].payload = combined
            return
        }
        let dragEnd = action == "input.drag" && payload["phase"] as? String == "end"
        guard outgoing.count < 64 || dragEnd else { return }
        if !isMotion {
            if accepted != nil && pendingActions.contains(action) { return }
            if accepted != nil { pendingActions.insert(action) }
            let seconds = action == "input.text" ? 315 : 25
            let timeout = Task {
                try? await Task.sleep(for: .seconds(seconds))
                if !Task.isCancelled { finish(id, error: "Windows did not acknowledge the command.") }
            }
            pending[id] = Pending(action: action, timeout: timeout, accepted: accepted)
        }
        outgoing.append(Outgoing(action: action, payload: payload, id: id))
        guard !sending else { return }
        sending = true
        let currentGeneration = generation
        Task { await flush(generation: currentGeneration) }
    }

    private func flush(generation id: UUID) async {
        while generation == id, let socket, !outgoing.isEmpty {
            let command = outgoing.removeFirst()
            sequence += 1
            do {
                let data = try JSONSerialization.data(withJSONObject: ["version": 1, "type": "command",
                    "id": command.id, "sequence": sequence, "action": command.action, "payload": command.payload])
                try await socket.send(.string(String(decoding: data, as: UTF8.self)))
            } catch {
                if generation == id {
                    reconnect()
                    message = RemoteError.disconnected.localizedDescription
                }
                return
            }
        }
        if generation == id { sending = false }
    }

    private func finish(_ id: String, error: String?) {
        guard let item = pending.removeValue(forKey: id) else { return }
        item.timeout.cancel()
        if item.accepted != nil { pendingActions.remove(item.action) }
        if let error { message = error } else { item.accepted?() }
    }

    func browser(_ command: String, position: Double? = nil) {
        guard snapshot?.playback?.capabilities.contains(command) == true else { return }
        var payload: [String: Any] = ["command": command]
        if let position { payload["position"] = position }
        send("browser.command", payload)
    }
}
