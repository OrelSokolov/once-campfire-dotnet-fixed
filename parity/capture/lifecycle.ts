// Observer-only host events. Full URLs/stacks may contain protected tokens: write this
// receipt to diagnostic output, never to the normalized comparison or public reports.
import { performance } from "node:perf_hooks"
import { createHash } from "node:crypto"
import { TextDecoder } from "node:util"
import type { Frame, Page, Request, Response, WebSocket } from "playwright"

const MAX_EVENTS = 20_000
const MAX_TEXT_LENGTH = 8_192
const MAX_ACTIVE_SOCKETS = 32
const MAX_CONTROL_BYTES = 4_096
const CONTROL_DECODER = new TextDecoder("utf-8", { fatal: true })
const CONTROL_TYPES = new Set(["welcome", "confirm_subscription", "reject_subscription", "disconnect"])

interface SocketObserver {
  receive: (data: { payload: string | Buffer }) => void
  error: (message: string) => void
  close: () => void
}

interface FrameIdentity { frameId: number; navigationSequence: number }
interface RequestIdentity {
  requestId: number
  url: string
  method: string
  resourceType: string
  navigationRequest: boolean
  frameId?: number
  navigationSequence?: number
  frameUnavailable?: boolean
  redirectedFromId?: number
}

export interface LifecycleEvent {
  sequence: number
  hostTimeMs: number
  hostMonotonicMs: number
  type: string
  [field: string]: unknown
}

/** Request identity and event order are observed; a nearby pageerror does not
 * identify its failed request or prove a navigation caused cancellation. No page
 * script, HTTP body, header, cookie or request payload is inspected. WebSocket
 * receipts retain only bounded control hashes, never application HTML bodies. */
export class RequestLifecycleRecorder {
  private readonly events: LifecycleEvent[] = []
  private readonly requests = new WeakMap<Request, RequestIdentity>()
  private readonly frames = new WeakMap<Frame, FrameIdentity>()
  private readonly sockets = new Map<WebSocket, SocketObserver>()
  private nextRequestId = 0
  private nextFrameId = 0
  private nextSocketId = 0
  private observedEvents = 0
  private droppedEvents = 0
  private truncatedFields = 0
  private unobservedSockets = 0
  private controlFrames = 0
  private ignoredSocketFrames = 0
  private oversizedSocketFrames = 0
  private malformedSocketFrames = 0
  private stopped = false
  private readonly page: Page
  private readonly maxEvents: number

  constructor(page: Page, maxEvents = MAX_EVENTS) {
    if (!Number.isSafeInteger(maxEvents) || maxEvents < 1 || maxEvents > MAX_EVENTS) throw new RangeError("Invalid lifecycle event limit")
    this.page = page
    this.maxEvents = maxEvents
    page.on("request", this.onRequest)
    page.on("response", this.onResponse)
    page.on("requestfinished", this.onFinished)
    page.on("requestfailed", this.onFailed)
    page.on("framenavigated", this.onNavigation)
    page.on("pageerror", this.onPageError)
    page.on("close", this.onClose)
    page.on("websocket", this.onWebSocket)
  }

  private frame(frame: Frame): FrameIdentity {
    let identity = this.frames.get(frame)
    if (!identity) {
      identity = { frameId: ++this.nextFrameId, navigationSequence: 0 }
      this.frames.set(frame, identity)
    }
    return identity
  }

  private request(request: Request): RequestIdentity {
    let identity = this.requests.get(request)
    if (identity) return identity
    identity = {
      requestId: ++this.nextRequestId, url: request.url(), method: request.method(),
      resourceType: request.resourceType(), navigationRequest: request.isNavigationRequest(),
    }
    // Playwright explicitly permits a service-worker request to have no frame.
    try { Object.assign(identity, this.frame(request.frame())) }
    catch { identity.frameUnavailable = true }
    const previous = request.redirectedFrom()
    if (previous) identity.redirectedFromId = this.request(previous).requestId
    this.requests.set(request, identity)
    return identity
  }

  private append(type: string, fields: Record<string, unknown>) {
    if (this.stopped) return
    const sequence = ++this.observedEvents
    if (this.events.length >= this.maxEvents) { this.droppedEvents++; return }
    const truncated: string[] = []
    for (const [key, value] of Object.entries(fields)) {
      if (typeof value === "string" && value.length > MAX_TEXT_LENGTH) {
        fields[key] = value.slice(0, MAX_TEXT_LENGTH)
        truncated.push(key)
        this.truncatedFields++
      }
    }
    this.events.push({ ...fields, ...(truncated.length ? { truncated } : {}),
      sequence, hostTimeMs: Date.now(), hostMonotonicMs: performance.now(), type })
  }

  private onRequest = (request: Request) => this.append("request", { ...this.request(request) })
  private onResponse = (response: Response) => this.append("response", { ...this.request(response.request()), status: response.status() })
  private onFinished = (request: Request) => this.append("finished", { ...this.request(request) })
  private onFailed = (request: Request) => this.append("failed", { ...this.request(request), failure: request.failure()?.errorText ?? null })
  private onNavigation = (frame: Frame) => {
    const identity = this.frame(frame)
    identity.navigationSequence++
    this.append("navigation", { ...identity, url: frame.url(), mainFrame: frame === this.page.mainFrame() })
  }
  private onPageError = (error: Error) => {
    const frame = this.frame(this.page.mainFrame())
    this.append("pageerror", {
      name: error.name, message: error.message, stack: error.stack ?? null,
      mainFrameUrl: this.page.url(), mainFrameId: frame.frameId,
      mainFrameNavigationSequence: frame.navigationSequence,
    })
  }
  private onClose = () => this.append("close", {})

  private mainFrameContext() {
    const frame = this.frame(this.page.mainFrame())
    return { observedMainFrameId: frame.frameId, observedMainFrameNavigationSequence: frame.navigationSequence }
  }

  private onWebSocket = (socket: WebSocket) => {
    if (this.stopped) return
    const socketId = ++this.nextSocketId
    const observed = this.sockets.size < MAX_ACTIVE_SOCKETS
    // Playwright emits this when the request is sent, not when OPEN is reached.
    this.append("websocket-requested", {
      socketId, urlSha256: createHash("sha256").update(socket.url()).digest("hex"), observed, ...this.mainFrameContext(),
    })
    if (!observed) { this.unobservedSockets++; return }
    const observer: SocketObserver = {
      receive: ({ payload }) => {
        if (this.stopped) return
        const payloadRepresentation = typeof payload === "string" ? "text" : "binary"
        const payloadUtf8Bytes = typeof payload === "string" ? Buffer.byteLength(payload) : payload.length
        if (payloadUtf8Bytes > MAX_CONTROL_BYTES) { this.oversizedSocketFrames++; return }
        let text: string
        try { text = typeof payload === "string" ? payload : CONTROL_DECODER.decode(payload) }
        catch { this.malformedSocketFrames++; return }
        let control: unknown
        try { control = JSON.parse(text) }
        catch { this.malformedSocketFrames++; return }
        if (!control || typeof control !== "object" || Array.isArray(control)) { this.ignoredSocketFrames++; return }
        const message = control as Record<string, unknown>
        if (typeof message.type !== "string" || !CONTROL_TYPES.has(message.type) || "message" in message) {
          this.ignoredSocketFrames++
          return
        }
        this.controlFrames++
        this.append("websocket-control-received", {
          socketId, controlType: message.type, payloadUtf8Bytes, payloadRepresentation,
          payloadSha256: createHash("sha256").update(payload).digest("hex"),
          ...(typeof message.identifier === "string" ? { identifierSha256: createHash("sha256").update(message.identifier).digest("hex") } : {}),
          ...this.mainFrameContext(),
        })
      },
      error: (message) => this.append("websocket-error", {
        socketId, errorSha256: createHash("sha256").update(message).digest("hex"),
        errorUtf8Bytes: Buffer.byteLength(message), ...this.mainFrameContext(),
      }),
      close: () => {
        this.append("websocket-closed", { socketId, ...this.mainFrameContext() })
        this.unwatchSocket(socket)
      },
    }
    this.sockets.set(socket, observer)
    socket.on("framereceived", observer.receive)
    socket.on("socketerror", observer.error)
    socket.on("close", observer.close)
  }

  private unwatchSocket(socket: WebSocket) {
    const observer = this.sockets.get(socket)
    if (!observer) return
    socket.off("framereceived", observer.receive)
    socket.off("socketerror", observer.error)
    socket.off("close", observer.close)
    this.sockets.delete(socket)
  }

  snapshot() {
    return {
      schemaVersion: 2, observer: "Playwright host events", maxEvents: this.maxEvents,
      maxTextLength: MAX_TEXT_LENGTH, observedEvents: this.observedEvents,
      droppedEvents: this.droppedEvents, truncatedFields: this.truncatedFields,
      maxActiveSockets: MAX_ACTIVE_SOCKETS, maxControlBytes: MAX_CONTROL_BYTES,
      activeSocketObservers: this.sockets.size, unobservedSockets: this.unobservedSockets,
      controlFrames: this.controlFrames, ignoredSocketFrames: this.ignoredSocketFrames, oversizedSocketFrames: this.oversizedSocketFrames, malformedSocketFrames: this.malformedSocketFrames,
      attribution: "Request IDs join transport events. Navigation/pageerror proximity is correlation, not proven cancellation cause. Frame navigationSequence includes same-document navigation. WebSocket contexts describe the observed main frame, not the originating frame/document. Requested is not OPEN; text hashes describe Playwright text re-encoded as UTF-8 and binary hashes describe observed Buffer bytes. Neither representation proves a wire opcode or independently verifies wire bytes.",
      events: this.events.slice(),
    }
  }

  stop() {
    if (this.stopped) return
    this.stopped = true
    this.page.off("request", this.onRequest)
    this.page.off("response", this.onResponse)
    this.page.off("requestfinished", this.onFinished)
    this.page.off("requestfailed", this.onFailed)
    this.page.off("framenavigated", this.onNavigation)
    this.page.off("pageerror", this.onPageError)
    this.page.off("close", this.onClose)
    this.page.off("websocket", this.onWebSocket)
    for (const socket of this.sockets.keys()) this.unwatchSocket(socket)
  }
}
