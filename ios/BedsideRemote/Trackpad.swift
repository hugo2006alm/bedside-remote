import SwiftUI
import UIKit

struct Trackpad: UIViewRepresentable {
    let enabled: Bool
    let supportsDrag: Bool
    let sensitivity: Double
    let haptics: Bool
    let deviceID: String?
    let send: (String, [String: Any]) -> Void

    func makeUIView(context: Context) -> Surface { Surface() }

    func updateUIView(_ surface: Surface, context: Context) {
        if surface.deviceID != deviceID || !enabled || !supportsDrag {
            surface.endDrag(sendRelease: surface.deviceID == deviceID)
        }
        surface.deviceID = deviceID
        surface.send = send
        surface.isUserInteractionEnabled = enabled
        surface.supportsDrag = supportsDrag
        surface.sensitivity = sensitivity
        surface.haptics = haptics
    }

    static func dismantleUIView(_ uiView: Surface, coordinator: ()) {
        uiView.endDrag(sendRelease: true)
    }

    final class Surface: UIView {
        var send: (String, [String: Any]) -> Void = { _, _ in }
        var supportsDrag = false
        var sensitivity = 1.5
        var haptics = false
        var deviceID: String?
        private var touchesInContact = Set<UITouch>()
        private var origin = CGPoint.zero
        private var lastCenter = CGPoint.zero
        private var lastTap: (time: TimeInterval, point: CGPoint)?
        private var firstTouchTime: TimeInterval = 0
        private var maximumFingers = 0
        private var moved = false
        private var dragging = false
        private var wheelRemainder = 0.0
        private var keepAlive: Task<Void, Never>?

        init() {
            super.init(frame: .zero)
            isMultipleTouchEnabled = true
            isAccessibilityElement = true
            accessibilityLabel = "Computer trackpad"
            accessibilityHint = "Tap to click, two fingers to scroll or right-click, double-tap and hold to drag."
            accessibilityCustomActions = [
                UIAccessibilityCustomAction(name: "Right click") { [weak self] _ in self?.click(right: true) ?? false },
                UIAccessibilityCustomAction(name: "Scroll up") { [weak self] _ in self?.accessibleScroll(120) ?? false },
                UIAccessibilityCustomAction(name: "Scroll down") { [weak self] _ in self?.accessibleScroll(-120) ?? false }
            ]
        }

        required init?(coder: NSCoder) { nil }

        override func accessibilityActivate() -> Bool { click(right: false) }

        private func accessibleScroll(_ delta: Int) -> Bool {
            guard isUserInteractionEnabled else { return false }
            send("input.scroll", ["delta": delta])
            return true
        }

        private func click(right: Bool) -> Bool {
            guard isUserInteractionEnabled else { return false }
            send("input.click", ["button": right ? "right" : "left"])
            if haptics { UISelectionFeedbackGenerator().selectionChanged() }
            return true
        }

        private func center() -> CGPoint {
            let locations = touchesInContact.map { $0.location(in: self) }
            let count = CGFloat(locations.count)
            guard count > 0 else { return .zero }
            return CGPoint(x: locations.reduce(0) { $0 + $1.x } / count,
                           y: locations.reduce(0) { $0 + $1.y } / count)
        }

        override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
            let first = touchesInContact.isEmpty
            touchesInContact.formUnion(touches)
            lastCenter = center()
            if first {
                origin = lastCenter
                firstTouchTime = touches.first?.timestamp ?? 0
                maximumFingers = 0
                moved = false
                wheelRemainder = 0
            }
            maximumFingers = max(maximumFingers, touchesInContact.count)
            if maximumFingers > 1 {
                lastTap = nil
                endDrag(sendRelease: true)
            } else if let tap = lastTap, supportsDrag,
                      firstTouchTime - tap.time < 0.3,
                      hypot(origin.x - tap.point.x, origin.y - tap.point.y) < 36 {
                lastTap = nil
                dragging = true
                send("input.drag", ["phase": "start", "dx": 0.0, "dy": 0.0])
                keepAlive = Task { [weak self] in
                    while !Task.isCancelled {
                        try? await Task.sleep(for: .milliseconds(400))
                        guard let self, self.dragging, !Task.isCancelled else { return }
                        self.send("input.drag", ["phase": "move", "dx": 0.0, "dy": 0.0])
                    }
                }
            }
        }

        override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
            let point = center()
            let dx = Double(point.x - lastCenter.x)
            let dy = Double(point.y - lastCenter.y)
            lastCenter = point
            if hypot(point.x - origin.x, point.y - origin.y) > 7 { moved = true }
            guard moved || dragging else { return }
            if touchesInContact.count == 1 && maximumFingers == 1 {
                let movement: [String: Any] = ["dx": min(1000, max(-1000, dx * sensitivity)),
                                                "dy": min(1000, max(-1000, dy * sensitivity))]
                if dragging {
                    send("input.drag", movement.merging(["phase": "move"]) { first, _ in first })
                } else {
                    send("input.pointer", movement)
                }
            } else if touchesInContact.count == 2 {
                wheelRemainder -= dy * sensitivity * 5
                let delta = Int(wheelRemainder)
                wheelRemainder -= Double(delta)
                if delta != 0 { send("input.scroll", ["delta": min(1200, max(-1200, delta))]) }
            }
        }

        override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
            touchesInContact.subtract(touches)
            lastCenter = center()
            guard touchesInContact.isEmpty else { return }
            let end = touches.first?.timestamp ?? 0
            if dragging {
                endDrag(sendRelease: true)
            } else if !moved && end - firstTouchTime < 0.3 && maximumFingers <= 2 {
                _ = click(right: maximumFingers == 2)
                if maximumFingers == 1 { lastTap = (end, origin) }
            }
        }

        override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) {
            touchesInContact.removeAll()
            lastTap = nil
            endDrag(sendRelease: true)
        }

        func endDrag(sendRelease: Bool) {
            keepAlive?.cancel()
            keepAlive = nil
            guard dragging else { return }
            dragging = false
            if sendRelease { send("input.drag", ["phase": "end", "dx": 0.0, "dy": 0.0]) }
        }
    }
}
