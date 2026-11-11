import CoreGraphics
import Foundation

private let mouseTapCallback: CGEventTapCallBack = { _, type, event, info in
    guard let info else { return Unmanaged.passUnretained(event) }
    let me = Unmanaged<MouseTap>.fromOpaque(info).takeUnretainedValue()
    if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
        if let tap = me.tap { CGEvent.tapEnable(tap: tap, enable: true) }
        return Unmanaged.passUnretained(event)
    }
    let button = event.getIntegerValueField(.mouseEventButtonNumber)
    let id: String
    switch button {
    case 2: id = "SYS:MIDDLE"
    case 3: id = "SYS:BACK"
    case 4: id = "SYS:FORWARD"
    default: return Unmanaged.passUnretained(event)
    }
    let action = me.resolve(id)
    if action == "native" { return Unmanaged.passUnretained(event) }
    if type == .otherMouseDown { me.fire(action) }
    return nil // swallow both down and up of a mapped button
}

/// Optional fallback: catches back/forward/middle on every mouse (not per device).
final class MouseTap {
    fileprivate var tap: CFMachPort?
    private var source: CFRunLoopSource?
    fileprivate let resolve: (String) -> String
    fileprivate let fire: (String) -> Void

    init?(resolve: @escaping (String) -> String, fire: @escaping (String) -> Void) {
        self.resolve = resolve
        self.fire = fire
        let mask = (CGEventMask(1) << CGEventType.otherMouseDown.rawValue) |
            (CGEventMask(1) << CGEventType.otherMouseUp.rawValue)
        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
            eventsOfInterest: mask, callback: mouseTapCallback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()) else { return nil }
        self.tap = tap
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        self.source = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }

    func invalidate() {
        if let tap {
            CGEvent.tapEnable(tap: tap, enable: false)
            CFMachPortInvalidate(tap)
        }
        if let source { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        tap = nil
        source = nil
    }

    deinit { invalidate() }
}
