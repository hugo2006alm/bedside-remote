import Foundation
import Darwin

struct Endpoint: Codable, Equatable {
    let host: String
    let port: UInt16

    init?(_ host: String, _ port: UInt16) {
        guard port != 0 else { return nil }
        var v4 = in_addr()
        var v6 = in6_addr()
        if inet_pton(AF_INET, host, &v4) == 1 {
            let ip = UInt32(bigEndian: v4.s_addr)
            guard ip >> 24 == 10 || ip >> 24 == 127 || ip >> 20 == 0xac1 || ip >> 16 == 0xc0a8 else { return nil }
        } else if inet_pton(AF_INET6, host, &v6) == 1 {
            let bytes = withUnsafeBytes(of: v6) { Array($0) }
            guard host == "::1" || bytes[0] & 0xfe == 0xfc || (bytes[0] == 0xfe && bytes[1] & 0xc0 == 0x80) else { return nil }
        } else {
            return nil
        }
        self.host = host
        self.port = port
    }

    func url(_ scheme: String, _ path: String) -> URL {
        var parts = URLComponents()
        parts.scheme = scheme
        parts.host = host.contains(":") ? "[\(host)]" : host
        parts.port = Int(port)
        parts.path = path
        return parts.url!
    }
}

struct Computer: Codable, Identifiable {
    let id: String
    let name: String
    let endpoint: Endpoint
    let pin: String
    let token: String
}

struct PairResponse: Decodable {
    let version: Int
    let deviceId: String
    let token: String
    let deviceName: String
}

struct Snapshot: Decodable {
    struct Session: Decodable, Identifiable {
        let sessionId: String
        let title: String
        var id: String { sessionId }
    }

    struct Playback: Decodable {
        let platform: String
        let title: String
        let paused: Bool
        let position: Double
        let duration: Double
        let capabilities: [String]
        let error: String?
    }

    struct QueueItem: Decodable, Identifiable {
        let entryId: String
        let title: String
        var id: String { entryId }
    }

    let version: Int
    let type: String
    let activeSessionId: String?
    let sessions: [Session]
    let playback: Playback?
    let queue: [QueueItem]
    let currentEntryId: String?
}

struct Catalog: Decodable {
    struct App: Decodable, Identifiable {
        let appId: String
        let name: String
        var id: String { appId }
    }
    let version: Int
    let controls: [String]
    let apps: [App]
}

struct Acknowledgement: Decodable {
    let id: String
    let ok: Bool
    let error: String?
}

func youtubeURL(_ raw: String) -> String? {
    guard raw.utf8.count <= 4096,
          let url = URLComponents(string: raw.trimmingCharacters(in: .whitespacesAndNewlines)),
          url.scheme?.lowercased() == "https", url.user == nil, url.password == nil,
          url.port == nil || url.port == 443, let host = url.host?.lowercased() else { return nil }
    let segments = url.path.split(separator: "/", omittingEmptySubsequences: false)
    let id: String?
    if host == "youtu.be" && segments.count == 2 {
        id = String(segments[1])
    } else if ["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com"].contains(host) {
        if url.path == "/watch" {
            id = url.queryItems?.first(where: { $0.name == "v" })?.value
        } else if segments.count == 3 && ["shorts", "live", "embed"].contains(String(segments[1])) {
            id = String(segments[2])
        } else {
            id = nil
        }
    } else {
        return nil
    }
    guard let id, id.utf8.count == 11, id.range(of: "^[A-Za-z0-9_-]{11}$", options: .regularExpression) != nil else { return nil }
    return "https://www.youtube.com/watch?v=\(id)"
}
