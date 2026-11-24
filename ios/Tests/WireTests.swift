import XCTest
@testable import BedsideRemote

final class WireTests: XCTestCase {
    private struct URLCase: Decodable {
        let url: String
        let videoId: String?
    }

    func testSharedYouTubeCases() throws {
        let url = try XCTUnwrap(Bundle(for: Self.self).url(forResource: "youtube-url-cases", withExtension: "json"))
        let cases = try JSONDecoder().decode([URLCase].self, from: Data(contentsOf: url))
        for row in cases {
            XCTAssertEqual(youtubeURL(row.url), row.videoId.map { "https://www.youtube.com/watch?v=\($0)" }, row.url)
        }
    }

    func testEndpointAcceptsOnlyNumericLANAddresses() throws {
        XCTAssertNil(Endpoint("8.8.8.8", 8765))
        XCTAssertNil(Endpoint("127.0.0.1@host.example", 8765))
        XCTAssertNil(Endpoint("bedroom.local", 8765))
        XCTAssertEqual(Endpoint("192.168.1.23", 8765)?.url("wss", "/ws").absoluteString, "wss://192.168.1.23:8765/ws")
        XCTAssertEqual(Endpoint("fd00::1", 8765)?.url("https", "/pair").absoluteString, "https://[fd00::1]:8765/pair")
    }

    func testCertificateRequiresFullDERFingerprint() {
        let pin = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        XCTAssertTrue(PinnedDelegate.matches(Data("abc".utf8), pin: pin))
        XCTAssertFalse(PinnedDelegate.matches(Data("abd".utf8), pin: pin))
        XCTAssertFalse(PinnedDelegate.matches(Data("abc".utf8), pin: String(pin.dropLast())))
    }
}
