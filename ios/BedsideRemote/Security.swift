import CryptoKit
import Foundation
import Network
import Security

enum RemoteError: LocalizedError, Equatable {
    case invalidDetails, invalidResponse, rejected, storage, disconnected, revoked

    var errorDescription: String? {
        switch self {
        case .invalidDetails: "Enter a private IP, valid port, Windows fingerprint and six-digit code."
        case .invalidResponse: "Windows sent an invalid response."
        case .rejected: "Windows rejected the request. Check pairing approval and the temporary code."
        case .storage: "Could not access the saved pairings."
        case .disconnected: "Computer disconnected. Reconnect before retrying."
        case .revoked: "Authorization revoked. Pair this iPhone again."
        }
    }
}

enum CertificateFingerprint {
    static func sha256(_ certificate: Data) -> String {
        SHA256.hash(data: certificate).map { String(format: "%02x", $0) }.joined()
    }

    static func candidate(for endpoint: Endpoint) async -> String? {
        guard let port = NWEndpoint.Port(rawValue: endpoint.port) else { return nil }
        let queue = DispatchQueue(label: "com.bedside.remote.fingerprint")
        let tls = NWProtocolTLS.Options()
        let (results, continuation) = AsyncStream<String>.makeStream(bufferingPolicy: .bufferingNewest(1))
        sec_protocol_options_set_verify_block(tls.securityProtocolOptions, { _, peerTrust, complete in
            let trust = sec_trust_copy_ref(peerTrust).takeRetainedValue()
            if let leaf = (SecTrustCopyCertificateChain(trust) as? [SecCertificate])?.first {
                continuation.yield(sha256(SecCertificateCopyData(leaf) as Data))
            }
            // The candidate is for comparison only. Never establish a trusted connection here.
            complete(false)
            continuation.finish()
        }, queue)
        let connection = NWConnection(host: NWEndpoint.Host(endpoint.host), port: port,
                                      using: NWParameters(tls: tls))
        connection.stateUpdateHandler = { state in
            if case .failed = state { continuation.finish() }
        }
        let deadline = DispatchWorkItem { continuation.finish() }
        queue.asyncAfter(deadline: .now() + 4, execute: deadline)
        connection.start(queue: queue)
        defer {
            deadline.cancel()
            connection.cancel()
        }
        for await fingerprint in results {
            return fingerprint
        }
        return nil
    }
}

final class PinnedDelegate: NSObject, URLSessionDelegate, URLSessionTaskDelegate, Sendable {
    let pin: String

    init(pin: String) {
        self.pin = pin
    }

    func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge,
                    completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              let trust = challenge.protectionSpace.serverTrust,
              let leaf = (SecTrustCopyCertificateChain(trust) as? [SecCertificate])?.first,
              Self.matches(SecCertificateCopyData(leaf) as Data, pin: pin) else {
            completionHandler(.cancelAuthenticationChallenge, nil)
            return
        }
        completionHandler(.useCredential, URLCredential(trust: trust))
    }

    static func matches(_ certificate: Data, pin: String) -> Bool {
        CertificateFingerprint.sha256(certificate) == pin
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }

    static func session(pin: String, resourceTimeout: TimeInterval? = nil) -> URLSession {
        let config = URLSessionConfiguration.ephemeral
        config.connectionProxyDictionary = [:]
        config.httpShouldSetCookies = false
        config.urlCredentialStorage = nil
        config.urlCache = nil
        config.timeoutIntervalForRequest = 30
        if let resourceTimeout { config.timeoutIntervalForResource = resourceTimeout }
        return URLSession(configuration: config, delegate: PinnedDelegate(pin: pin), delegateQueue: nil)
    }
}

enum PairingStore {
    private static let query: [String: Any] = [
        kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: "com.bedside.remote",
        kSecAttrAccount as String: "pairings-v1"
    ]

    static func load() throws -> [Computer] {
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query.merging([kSecReturnData as String: true]) { _, value in value } as CFDictionary, &result)
        if status == errSecItemNotFound { return [] }
        guard status == errSecSuccess, let data = result as? Data else { throw RemoteError.storage }
        return try JSONDecoder().decode([Computer].self, from: data)
    }

    static func save(_ computers: [Computer]) throws {
        let data = try JSONEncoder().encode(computers)
        let updates = [kSecValueData as String: data]
        var status = SecItemUpdate(query as CFDictionary, updates as CFDictionary)
        if status == errSecItemNotFound {
            var entry = query
            entry[kSecValueData as String] = data
            entry[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
            status = SecItemAdd(entry as CFDictionary, nil)
        }
        guard status == errSecSuccess else { throw RemoteError.storage }
    }
}
