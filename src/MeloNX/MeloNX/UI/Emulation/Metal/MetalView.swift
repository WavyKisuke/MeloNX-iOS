//
//  MetalView.swift
//  MeloNX
//
//  Created by Stossy11 on 20/4/2026.
//

import SwiftUI
import MetalKit

// IDs belong to a contact's lifetime, rather than its position in an array.
struct TouchTracker<Contact: Hashable> {
    static var maxContacts: Int { 16 }

    private var contacts: [Contact: Int] = [:]
    private var nextID = 0

    mutating func begin(_ contact: Contact) -> Int? {
        if let id = contacts[contact] { return id }
        guard contacts.count < Self.maxContacts else { return nil }
        while contacts.values.contains(nextID) {
            nextID = nextID == Int(Int32.max) ? 0 : nextID + 1
        }
        let id = nextID
        nextID = nextID == Int(Int32.max) ? 0 : nextID + 1
        contacts[contact] = id
        return id
    }

    func id(for contact: Contact) -> Int? { contacts[contact] }

    mutating func end(_ contact: Contact) -> Int? {
        contacts.removeValue(forKey: contact)
    }

    mutating func reset() -> [Int] {
        let ids = Array(contacts.values)
        contacts.removeAll(keepingCapacity: true)
        return ids
    }
}

class MetalView: MTKView {
    @ObservedObject var menuViewShower: InGameConfigView_MenuView = .shared
    private var activeTouches = TouchTracker<UITouch>()

    func setAspectRatio() {
        Ryujinx.setViewSize(self.bounds)
        updateUnboundedPresentTargetFps()
    }

    private func updateUnboundedPresentTargetFps() {
        Ryujinx.setUnboundedPresentTargetFps(for: window?.screen)
    }

    override func didMoveToWindow() {
        super.didMoveToWindow()
        isMultipleTouchEnabled = true
        if window == nil { resetTouchTracking() }
        updateUnboundedPresentTargetFps()
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        updateUnboundedPresentTargetFps()
    }

    private var touchDisabled: Bool {
        UserDefaults.standard.bool(forKey: "disableTouch")
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        super.touchesBegan(touches, with: event)
        
        Task.detached {
            await self.menuViewShower.triggerButton()
        }
        
        guard !touchDisabled else {
            resetTouchTracking()
            return
        }
        
        setAspectRatio()
        
        for touch in touches {
            guard let index = activeTouches.begin(touch) else { continue }
            Ryujinx.touchBegan(touch.location(in: self), index: index)
        }
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        super.touchesMoved(touches, with: event)
        
        Task.detached {
            await self.menuViewShower.triggerButton()
        }
        
        guard !touchDisabled else {
            resetTouchTracking()
            return
        }
        
        setAspectRatio()
        
        for touch in touches {
            guard let index = activeTouches.id(for: touch) else { continue }
            Ryujinx.touchMoved(touch.location(in: self), index: index)
        }
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        super.touchesEnded(touches, with: event)
        endTouches(touches)
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) {
        super.touchesCancelled(touches, with: event)
        endTouches(touches)
    }

    private func endTouches(_ touches: Set<UITouch>) {
        // Always release contacts, including when touch input was disabled mid-gesture.
        for touch in touches {
            if let index = activeTouches.end(touch) {
                Ryujinx.touchEnded(index: index)
            }
        }
    }

    func resetTouchTracking() {
        for index in activeTouches.reset() {
            Ryujinx.touchEnded(index: index)
        }
    }
}



struct MetalViewRepresentable: UIViewRepresentable {
    var showView: Bool
    
    init(showView: Bool = true) {
        self.showView = showView
    }
    
    func makeUIView(context: Context) -> UIView {
        if showView {
            return Self.createView()
        } else { return MetalView() }
    }
    
    func updateUIView(_ uiView: UIView, context: Context) {
        
    }
    
    @discardableResult
    static func createView() -> UIView {
        if Ryujinx.emulationView == nil {
            let view = MetalView()
            
            guard let metalLayer = view.layer as? CAMetalLayer else {
                fatalError("[Swift] Error: MTKView's layer is not a CAMetalLayer")
            }
            
            UIApplication.shared.isIdleTimerDisabled = true
            
            //metalLayer.presentsWithTransaction = false
            //metalLayer.allowsNextDrawableTimeout = false
            
            if metalLayer.device == nil {
                metalLayer.device = MTLCreateSystemDefaultDevice()
            }
            
            view.framebufferOnly = false

            if #available(iOS 16.0, *) {
                MetalFxBridge.registerCallbacks()
            }
            
            let layerPtr = Unmanaged.passUnretained(metalLayer).toOpaque()
            
            Ryujinx.setNativeWindow(layerPtr)
            Ryujinx.setUnboundedPresentTargetFps(for: view.window?.screen)
            
            Ryujinx.emulationView = view
            return view
        }
        
        return Ryujinx.emulationView!
    }
}
