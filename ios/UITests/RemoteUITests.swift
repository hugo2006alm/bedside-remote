import XCTest

final class RemoteUITests: XCTestCase {
    func testMultipleAvailableComputersRequireAChoice() {
        let app = XCUIApplication()
        app.launchArguments = ["--preview-state", "computer-choice"]
        app.launch()
        XCTAssertTrue(app.staticTexts["Choose which computer to control."].waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["paired-computer-office"].exists)
        XCTAssertTrue(app.buttons["paired-computer-bedroom"].exists)
        XCTAssertFalse(app.buttons["Reconnect"].exists)
    }

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
