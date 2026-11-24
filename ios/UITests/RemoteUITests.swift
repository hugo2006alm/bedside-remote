import XCTest

final class RemoteUITests: XCTestCase {
    func testRemoteOpensOnTrackpad() {
        let app = XCUIApplication()
        app.launch()
        XCTAssertTrue(app.descendants(matching: .any)["Computer trackpad"].firstMatch.waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["Pair a computer"].exists)
        XCTAssertTrue(app.tabBars.buttons["Queue"].exists)
        XCTAssertTrue(app.tabBars.buttons["Devices"].exists)
        XCTAssertFalse(app.tabBars.buttons["Touch"].exists)
    }
}
