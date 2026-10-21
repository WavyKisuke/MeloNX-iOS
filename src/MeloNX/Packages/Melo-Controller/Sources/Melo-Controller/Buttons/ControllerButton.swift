//
//  ControllerButton.swift
//  Melo-Controller
//
//  Created by Stossy11 on 10/1/2026.
//

import UIKit
import SwiftUI


final class ControllerTouchRouter {
    static let shared = ControllerTouchRouter()
    private init() {}
    
    private final class WeakTarget {
        weak var view: ControllerTouchView?
        init(_ view: ControllerTouchView) { self.view = view }
    }
    
    private var targets: [ObjectIdentifier: WeakTarget] = [:]
    
    /// Buttons each touch is holding. With "Press Both From the Gap" a touch between two buttons holds both.
    private var owners: [ObjectIdentifier: [ControllerTouchView]] = [:]
    
    private var tracked: Set<ObjectIdentifier> = []
    
    // MARK: Gap press
    
    /// Hit area, as a multiple of a button's radius, while gap press is on.
    static let gapReach: CGFloat = 1.35
    /// Touches closer to a button's centre than this press only that button.
    private static let gapCore: CGFloat = 0.85
    
    static var gapPressEnabled: Bool {
        UserDefaults.standard.bool(forKey: "gapPress")
    }
    
    // MARK: Registration
    
    func register(_ view: ControllerTouchView) {
        targets[ObjectIdentifier(view)] = WeakTarget(view)
    }
    
    func unregister(_ view: ControllerTouchView, cancellingPress: Bool = true) {
        targets[ObjectIdentifier(view)] = nil
        for (touch, held) in owners where held.contains(where: { $0 === view }) {
            owners[touch] = held.filter { $0 !== view }
        }
        if cancellingPress { view.cancelPress() }
    }
    
    // MARK: Touch handling
    
    func touchBegan(_ touch: UITouch, on view: ControllerTouchView) {
        let key = ObjectIdentifier(touch)
        owners.removeValue(forKey: key)?.forEach { $0.endPress() }
        
        var pressed = [view]
        
        if Self.gapPressEnabled, view.acceptsSlide {
            let found = pressTargets(at: touch.location(in: nil), in: touch.window)
            if !found.isEmpty { pressed = found }
        }
        
        owners[key] = pressed
        tracked.insert(key)
        pressed.forEach { $0.beginPress() }
    }
    
    func touchMoved(_ touch: UITouch, on view: ControllerTouchView) {
        let key = ObjectIdentifier(touch)
        guard view.slideEnabled, tracked.contains(key) else { return }
        let current = owners[key] ?? []
        
        let next = pressTargets(at: touch.location(in: nil), in: touch.window)
        guard !Self.sameViews(current, next) else { return }
        
        for old in current where !next.contains(where: { $0 === old }) {
            old.endPress()
        }
        for new in next where !current.contains(where: { $0 === new }) {
            new.beginPress()
        }
        owners[key] = next
    }
    
    func touchEnded(_ touch: UITouch, on view: ControllerTouchView) {
        let key = ObjectIdentifier(touch)
        tracked.remove(key)
        owners.removeValue(forKey: key)?.forEach { $0.endPress() }
    }
    
    private static func sameViews(_ a: [ControllerTouchView], _ b: [ControllerTouchView]) -> Bool {
        a.count == b.count && a.allSatisfy { view in b.contains(where: { $0 === view }) }
    }
    
    /// The buttons a touch at `point` presses: the nearest one, plus its neighbour when the touch sits
    /// in the gap between them and gap press is on.
    private func pressTargets(at point: CGPoint, in window: UIWindow?) -> [ControllerTouchView] {
        let gapPress = Self.gapPressEnabled
        let reach = gapPress ? Self.gapReach : 1
        var candidates: [(view: ControllerTouchView, score: CGFloat)] = []
        var stale: [ObjectIdentifier] = []
        
        for (key, box) in targets {
            guard let candidate = box.view else {
                stale.append(key)
                continue
            }
            
            guard let candidateWindow = candidate.window,
                  window == nil || candidateWindow === window,
                  !candidate.isHidden,
                  candidate.isUserInteractionEnabled,
                  candidate.acceptsSlide,
                  candidate.alpha > 0.01 else { continue }
            
            let frame = candidate.convert(candidate.bounds, to: nil)
            guard frame.width > 0, frame.height > 0 else { continue }
            
            
            let nx = (point.x - frame.midX) / (frame.width / 2)
            let ny = (point.y - frame.midY) / (frame.height / 2)
            
            let score = candidate.isCircular ? hypot(nx, ny) : max(abs(nx), abs(ny))
            
            guard score <= reach else { continue }
            candidates.append((candidate, score))
        }
        
        for key in stale { targets[key] = nil }
        
        candidates.sort { $0.score < $1.score }
        
        guard let best = candidates.first else { return [] }
        
        if gapPress, best.score > Self.gapCore, candidates.count > 1 {
            return [best.view, candidates[1].view]
        }
        
        return [best.view]
    }
}

final class ControllerTouchView: UIView {
    var onPress: (() -> Void)?
    var onRelease: (() -> Void)?
    
    var isCircular = true
    
    var slideEnabled = true
    var acceptsSlide = true
    
    
    private var pressCount = 0
    
    override init(frame: CGRect) {
        super.init(frame: frame)
        commonInit()
    }
    
    required init?(coder: NSCoder) {
        super.init(coder: coder)
        commonInit()
    }
    
    private func commonInit() {
        backgroundColor = .clear
        isUserInteractionEnabled = true
        isMultipleTouchEnabled = true
    }
    
    deinit {
        ControllerTouchRouter.shared.unregister(self, cancellingPress: false)
    }
    
    override func didMoveToWindow() {
        super.didMoveToWindow()
        if window == nil {
            ControllerTouchRouter.shared.unregister(self)
        } else {
            ControllerTouchRouter.shared.register(self)
        }
    }
    
    override func point(inside point: CGPoint, with event: UIEvent?) -> Bool {
        guard bounds.width > 0, bounds.height > 0 else { return false }
        let reach = acceptsSlide && ControllerTouchRouter.gapPressEnabled ? ControllerTouchRouter.gapReach : 1
        let nx = (point.x - bounds.midX) / (bounds.width / 2)
        let ny = (point.y - bounds.midY) / (bounds.height / 2)
        return (isCircular ? hypot(nx, ny) : max(abs(nx), abs(ny))) <= reach
    }
    
    // MARK: Press state
    
    func beginPress() {
        pressCount += 1
        if pressCount == 1 { onPress?() }
    }
    
    func endPress() {
        guard pressCount > 0 else { return }
        pressCount -= 1
        if pressCount == 0 { onRelease?() }
    }
    
    func cancelPress() {
        guard pressCount > 0 else { return }
        pressCount = 0
        onRelease?()
    }
    
    // MARK: Touches
    
    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        for touch in touches {
            ControllerTouchRouter.shared.touchBegan(touch, on: self)
        }
    }
    
    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        for touch in touches {
            ControllerTouchRouter.shared.touchMoved(touch, on: self)
        }
    }
    
    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        for touch in touches {
            ControllerTouchRouter.shared.touchEnded(touch, on: self)
        }
    }
    
    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) {
        for touch in touches {
            ControllerTouchRouter.shared.touchEnded(touch, on: self)
        }
    }
}

struct ControllerUIButtonViewRepresentable: UIViewRepresentable {
    let onPress: () -> Void
    let onRelease: () -> Void
    var isCircular: Bool = true
    var slideEnabled: Bool = true
    var acceptsSlide: Bool = true
    
    func makeUIView(context: Context) -> ControllerTouchView {
        let view = ControllerTouchView(frame: .zero)
        apply(to: view)
        return view
    }
    
    func updateUIView(_ uiView: ControllerTouchView, context: Context) {
        apply(to: uiView)
    }
    
    private func apply(to view: ControllerTouchView) {
        view.onPress = onPress
        view.onRelease = onRelease
        view.isCircular = isCircular
        view.slideEnabled = slideEnabled
        view.acceptsSlide = acceptsSlide
    }
}
