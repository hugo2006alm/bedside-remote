import Foundation
import Darwin
import Observation

@MainActor
@Observable
final class Discovery: NSObject, @preconcurrency NetServiceBrowserDelegate, @preconcurrency NetServiceDelegate {
    struct Computer: Identifiable {
        let id: String
        let name: String
        let endpoint: Endpoint
    }

    private(set) var computers: [Computer] = []
    @ObservationIgnored private let browser = NetServiceBrowser()
    @ObservationIgnored private var services: [String: NetService] = [:]
    @ObservationIgnored private var browsing = false

    func start() {
        guard !browsing else { return }
        browsing = true
        browser.delegate = self
        browser.searchForServices(ofType: "_bedside-remote._tcp.", inDomain: "local.")
    }

    func stop() {
        browsing = false
        browser.stop()
        services.values.forEach { $0.stop() }
        services.removeAll()
        computers.removeAll()
    }

    func netServiceBrowser(_ browser: NetServiceBrowser, didFind service: NetService, moreComing: Bool) {
        guard browsing else { return }
        services[service.name] = service
        service.delegate = self
        service.resolve(withTimeout: 6)
    }

    func netServiceBrowser(_ browser: NetServiceBrowser, didRemove service: NetService, moreComing: Bool) {
        services.removeValue(forKey: service.name)?.stop()
        computers.removeAll { $0.id == service.name }
    }

    func netServiceDidResolveAddress(_ sender: NetService) {
        guard services[sender.name] === sender, let port = UInt16(exactly: sender.port) else { return }
        let txt = sender.txtRecordData().map(NetService.dictionary(fromTXTRecord:))
        guard txt?["version"] == Data("1".utf8) else { return }
        let endpoints = (sender.addresses ?? []).compactMap { address -> Endpoint? in
            guard address.count >= MemoryLayout<sockaddr>.size else { return nil }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            let result = address.withUnsafeBytes { bytes in
                guard let base = bytes.baseAddress else { return EAI_FAIL }
                return getnameinfo(base.assumingMemoryBound(to: sockaddr.self), socklen_t(bytes.count),
                                   &host, socklen_t(host.count), nil, 0, NI_NUMERICHOST)
            }
            guard result == 0 else { return nil }
            return Endpoint(String(cString: host), port)
        }
        guard let endpoint = endpoints.first(where: { !$0.host.contains(":") }) ?? endpoints.first else { return }
        computers.removeAll { $0.id == sender.name }
        computers.append(Computer(id: sender.name, name: sender.name, endpoint: endpoint))
    }
}
