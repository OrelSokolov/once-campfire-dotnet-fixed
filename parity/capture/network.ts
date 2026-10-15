// Server-output layers of a page capture (https://github.com/basecamp/once-campfire-rust/blob/3fe9f61dcd6930b597170e1ea2c7b7604c3570ce/plans/rust-conversion.md, decision 4): every response
// the server sent the page, and every Action Cable frame, normalized so that two servers that
// sent the same bytes (up to the typed placeholders of normalize.ts) produce the same text.
import { createHash } from "node:crypto"
import type { Page, Request, Response } from "playwright"
import { maskText, normalizeFragment, normalizeResponse } from "./normalize.ts"
import type { NormalizeOptions } from "./normalize.ts"
import type { WireVerifier } from "./wire_receipts.ts"
import { cookieContractReceipt, cookieShape } from "./native_network_contracts.ts"
import type { NetworkContractReceipt } from "./native_network_contracts.ts"

// Headers whose presence says nothing about the app (transport, timing, per-request ids).
const TRANSPORT_HEADERS = new Set([
  "connection", "content-length", "date", "keep-alive", "server", "server-timing", "transfer-encoding", "x-request-id", "x-runtime",
])
const NATIVE_HEADER_DIFFERENCES = new Set(["referrer-policy", "x-content-type-options", "x-frame-options", "x-permitted-cross-domain-policies", "x-xss-protection", "x-rev", "x-version"])
const REQUIRED_NATIVE_HEADERS: Record<string, string> = { "x-content-type-options": "nosniff", "x-frame-options": "SAMEORIGIN", "referrer-policy": "strict-origin-when-cross-origin" }
// Headers whose value is part of the shape: what the response is, where it sends you, how it may be
// cached, which cookies it sets or clears (https://github.com/basecamp/once-campfire-rust/blob/3fe9f61dcd6930b597170e1ea2c7b7604c3570ce/plans/rust-conversion.md, decision 3).
const VALUE_HEADERS = new Set(["content-type", "location", "cache-control", "content-disposition", "vary"])

// Records every response from the page's origin: method, path, status, header shape and a hash of
// the normalized body. Requests the harness answered itself (external fixtures) or never let
// through (held requests) aren't the server's output and are left out.
export class NetworkLog {
  // per request line (method, path): the latest response's description
  // PARITY PATCH: handle body-read rejection immediately, retaining it for text() to report.
  private pending: { key: string; entry: Promise<Description> }[] = []
  private origin: string
  private options: NormalizeOptions
  private page: Page
  private wireHeaders?: WireVerifier
  private capturedContracts?: NetworkContractReceipt[]

  constructor(page: Page, origin: string, options: NormalizeOptions, wireHeaders?: WireVerifier) {
    this.page = page
    this.wireHeaders = wireHeaders
    this.origin = origin
    this.options = options
    // PARITY PATCH: navigation cleanup may close the context before the network log is drained.
    page.on("response", (response) => this.pending.push({
      // A synthetic receipt must never replace a real HTTP response for the same URL.
      key: `${response.request().method()} ${response.url()}${response.status() === 0 ? ` synthetic-${this.pending.length}` : ""}`,
      entry: this.describe(response).then(
        (entry): Description => ({ ok: true, entry }),
        (error): Description => ({ ok: false, error }),
      ),
    }))
  }

  private async describe(response: Response): Promise<Entry | undefined> {
    const request = response.request()
    const url = new URL(response.url())
    if (!/^https?:$/.test(url.protocol) || url.origin !== this.origin || request.resourceType() === "websocket") return
    const headers = await response.allHeaders().catch(() => response.headers())
    const status = response.status()
    if (status === 0) {
      // WebKit emits an empty non-HTTP preview receipt for a video it intentionally did not load.
      // Keep real media requests, failures and every unmatched status-zero receipt observable.
      const timing = request.timing()
      const preview = this.page.context().browser()?.browserType().name() === "webkit" && request.method() === "GET" && request.resourceType() === "other" && !request.redirectedFrom() && !request.failure() && !Object.keys(request.headers()).length && !Object.keys(headers).length && timing.requestStart === -1
      if (preview && await this.page.evaluate((url) => [...document.querySelectorAll("video")].some((video) => video.src === url && video.preload === "none" && video.paused && video.error === null && video.readyState === 0 && video.networkState <= 1), response.url())) return
      throw new Error("Unmatched non-HTTP response receipt")
    }
    let verifiedHeaders: Readonly<Record<string,string>> = headers
    if (this.options.nativeTarget) {
      let securityHeaders: Readonly<Record<string,string>> = headers
      if (status >= 300 && status < 400 && this.page.context().browser()?.browserType().name() === "webkit" && Object.entries(REQUIRED_NATIVE_HEADERS).some(([name,expected]) => headers[name] !== expected) && this.wireHeaders) securityHeaders = this.wireHeaders({ method: request.method(), url: response.url(), status, location: headers.location ?? "" })
      for (const [name, expected] of Object.entries(REQUIRED_NATIVE_HEADERS)) {
        if ((headers[name] !== undefined && headers[name] !== expected) || securityHeaders[name] !== expected) throw new Error(`Native response ${url.pathname} ${status}: ${name} must be ${expected}`)
      }
      verifiedHeaders = securityHeaders
    }
    const shape = comparisonHeaderNames(headers, status, this.options.nativeComparison)
    const values = shape.filter((name) => VALUE_HEADERS.has(name) || (this.options.nativeComparison && ["content-encoding", "pragma", "expires"].includes(name))).map((name) => `${name}: ${maskText(normalizeHeaderValue(name, headers[name], headers, status, this.options.nativeComparison), this.options)}`)
    const issuedCookies = (await response.headersArray().catch(() => [])).filter((h) => h.name.toLowerCase() === "set-cookie")
    const cookies = issuedCookies.map((h) => cookieShape(h.value, this.options.nativeComparison)).sort()
    let body: string
    let normalized: string | undefined
    if (status >= 300 && status < 400) {
      body = "no body (redirect)"
    } else if (status === 206 || request.resourceType() === "media") {
      // Media is fetched in ranges whose sizes are the engine's choice; the total is the server's.
      body = `range of ${headers["content-range"]?.split("/")[1] ?? "?"} bytes`
    } else if (DIGESTED_ASSET.test(url.pathname)) {
      // Propshaft names an asset after its content's digest, and the path keeps that digest here.
      body = "named by its digest"
    } else if (request.method() !== "GET") {
      // A form submission's response is gone as soon as the page navigates, which may be before
      // it can be read; what it did shows in the pages and frames that follow.
      body = "not compared (not a GET)"
    } else {
      const buffer = await this.bodyOf(response)
      await this.options.resolveProtectedTokens?.(buffer.toString("utf8"))
      normalized = normalizeResponse(buffer, headers["content-type"] ?? "", this.options)
      body = buffer.length ? "" : "empty "
    }
    const head = [
      `${describeRequest(request, this.origin, this.options)} → ${status}`,
      `  headers: ${shape.join(" ")}`,
      ...values.map((v) => `  ${v}`),
      ...cookies.map((c) => `  set-cookie: ${c}`),
    ].join("\n")
    await this.options.resolveProtectedTokens?.(request.url() + "\n" + head)
    return { head, body, normalized, receipt: {
      request: describeRequest(request, this.origin, this.options), status, protocol: url.protocol as "http:" | "https:",
      // Independently joined WebKit wire evidence may fill omitted allowlisted raw values,
      // but never replaces a present browser policy value or reconstructs Set-Cookie values.
      headers: Object.fromEntries(["date", "pragma", "expires", "location", "cache-control", "vary", "content-type", "content-encoding", ...Object.keys(REQUIRED_NATIVE_HEADERS)].filter(name => headers[name] !== undefined || verifiedHeaders[name] !== undefined).map(name => [name, headers[name] ?? verifiedHeaders[name]])),
      cookies: issuedCookies.map(cookie => cookieContractReceipt(cookie.value)),
    } }
  }

  // The browser drops a response's body when its document goes away, sometimes before it could be
  // read; a GET is then asked for again, with the page's cookies.
  private async bodyOf(response: Response): Promise<Buffer> {
    try {
      const body = await response.body()
      if (body.length || response.status() !== 200 || !/^image\//i.test(response.headers()["content-type"] ?? "")) return body
    } catch {
      // A discarded body is refetched below.
    }
    const again = await this.page.context().request.get(response.url(), { maxRedirects: 0, headers: { accept: response.request().headers()["accept"] ?? "*/*" } })
    if (again.status() !== response.status()) throw new Error(`Refetched response changed status for ${new URL(response.url()).pathname}`)
    const body = await again.body()
    if (!body.length && response.status() === 200 && /^image\//i.test(response.headers()["content-type"] ?? "")) throw new Error(`Empty image body after retry for ${new URL(response.url()).pathname}`)
    return body
  }

  // The log as sorted text: requests run in parallel, so arrival order isn't the server's. `mask`
  // replaces a state's page-derived values (masks.values in screens.yml) before bodies are hashed.
  async text(mask: (text: string) => string = (t) => t): Promise<string> {
    // The ready capture has one bounded snapshot. Late responses belong to a later drain.
    const pending = [...this.pending]
    // A later successful request must never hide an earlier security/body capture failure.
    for (const description of await Promise.all(pending.map(({ entry }) => entry))) {
      if (!description.ok) throw new Error("Could not capture response body", { cause: description.error })
    }
    // Whether a resource is requested once or twice (the memory cache, a preload) is the browser's
    // business, and so is what an earlier request for it got: a frame loaded while the page was
    // still marking the room read on another connection (the sidebar) may come back either way.
    // The latest response to each request counts.
    const latest = new Map<string, Promise<Description>>() // PARITY PATCH: retain failures until comparison.
    for (const { key, entry } of pending) latest.set(key, entry)
    const render = ({ head, body, normalized }: Entry) =>
      `${mask(maskText(head, this.options))}\n  body: ${normalized === undefined ? body : `${body}sha256:${createHash("sha256").update(mask(maskText(normalized, this.options))).digest("hex").slice(0, 16)}`}`
    // PARITY PATCH: a failed retained response still fails capture; it is never omitted from comparison.
    const entries = (await Promise.all(latest.values())).map((description) => {
      if (!description.ok) throw new Error("Could not capture response body", { cause: description.error })
      return description.entry
    })
    const rendered = entries.filter((entry): entry is Entry => !!entry).map(entry => ({ entry, block: maskBlobKeys(render(entry)) }))
    this.capturedContracts = rendered.map(({ entry, block }) => ({ ...entry.receipt,
      request: maskBlobKeys(mask(maskText(entry.receipt.request, this.options))),
      originalBlock: block,
      ...(entry.normalized === undefined ? {} : { bodyHash: createHash("sha256").update(mask(maskText(entry.normalized, this.options))).digest("hex").slice(0, 16) }),
    }))
    // Keep every raw policy variant; only the public rendered text deduplicates blocks.
    const lines = new Set(rendered.map(({ block }) => block))
    return [...lines].sort().join("\n") + "\n"
  }

  // Same bounded snapshot as text(), with cookie values excluded. Saved alongside diagnostic captures.
  contractReceipts(): NetworkContractReceipt[] {
    if (!this.capturedContracts) throw new Error("Network contract receipts require a completed text snapshot")
    return structuredClone(this.capturedContracts)
  }
}

// PARITY PATCH: settled descriptions prevent unrelated browser workers from aborting on cleanup.
type Description = { ok: true; entry: Entry | undefined } | { ok: false; error: unknown }

// A response's description: the request line and header shape, and the body as a note (redirect,
// media range, digest-named asset) or as normalized text, hashed when the log is written.
interface Entry {
  head: string
  body: string
  normalized?: string
  receipt: NetworkContractReceipt
}

// Active Storage names a blob's file with a random key (ActiveStorage::Blob.generate_unique_secure_token),
// which shows in the disk service URLs that representation redirects point to. It's random by
// design, in the port too, so it's a placeholder here; the blob itself is compared by its signed
// id (in the pages) and its bytes (the body hash).
// It also names the file Users::AvatarsController sends (send_file of the variant's path).
function maskBlobKeys(text: string): string {
  return text
    .replace(/"key":"[a-z0-9]{28}"/g, '"key":"«blob-key»"')
    .replace(/filename="[a-z0-9]{28}"; filename\*=UTF-8''[a-z0-9]{28}/g, "filename=\"«blob-key»\"; filename*=UTF-8''«blob-key»")
}

const DIGESTED_ASSET = /^\/assets\/.+-[0-9a-f]{8,}\.[a-z0-9]+$/

function describeRequest(request: Request, origin: string, options: NormalizeOptions): string {
  const url = new URL(request.url())
  const nav = request.isNavigationRequest() ? " (navigation)" : ""
  const target = DIGESTED_ASSET.test(url.pathname) ? url.pathname + url.search : maskText(url.pathname + url.search, options)
  return `${request.method()} ${target}${nav}`.replace(origin, "")
}

function comparisonHeaderNames(headers: Record<string,string>, status: number, nativeComparison?: boolean): string[] {
  const compressionDifference = nativeComparison && nativeCompressionDifference(headers, status)
  return Object.keys(headers).filter((name) => !TRANSPORT_HEADERS.has(name) && !(nativeComparison && NATIVE_HEADER_DIFFERENCES.has(name)) && !(compressionDifference && name === "content-encoding") && !(compressionDifference && name === "vary" && normalizeHeaderValue(name, headers[name], headers, status, nativeComparison) === "")).sort()
}

// The documented representation difference is gzip versus identity, not arbitrary encodings.
// Browser response bodies are decoded; status, content type and their body digest stay compared.
function nativeCompressionDifference(headers: Record<string,string>, status: number): boolean {
  const encoding = headers["content-encoding"]?.trim().toLowerCase() ?? "identity"
  return ["gzip", "identity"].includes(encoding) && (/^image\//i.test(headers["content-type"] ?? "") || status < 200 || status >= 300)
}

function normalizeHeaderValue(name: string, value: string, headers: Record<string,string>, status: number, nativeComparison?: boolean): string {
  if (name === "cache-control" || name === "vary") {
    const values = value.split(",").map((s) => s.trim().toLowerCase()).filter((s) => !(name === "vary" && nativeComparison && nativeCompressionDifference(headers, status) && s === "accept-encoding"))
    return (name === "vary" ? [...new Set(values)] : values).sort().join(", ")
  }
  if (name === "content-encoding") return value.trim().toLowerCase()
  return value
}

// Action Cable frames per subscription. Frames of different subscriptions interleave in real
// time, and how often a subscription is dropped and made again is the client's business (Turbo
// replaces the sidebar's stream sources as frames load), so each subscription is summarized: how
// it ended (confirmed, rejected), the distinct actions the page sent on it, and the distinct
// messages the server sent on it, each normalized. Pings are left out.
//
// Messages count only while the page is settled (recording: from its first readiness until it
// navigates, and again from the next readiness): while a room page loads, its own
// PresenceChannel subscription broadcasts a read to the user's ReadRoomsChannel stream, which the
// page receives or not depending on whether that subscription's Redis SUBSCRIBE got in before the
// PUBLISH, a race inside the reference server. What the page's steps set off is all recorded.
//
// And only on a server the capture has to itself (a `mutates: true` state): on a shared server,
// every other capture signed in as the same user broadcasts to that user's streams too (each room
// page a read on ReadRoomsChannel), so what arrives there depends on what else is running. On a
// shared server the layer is how each subscription ended.
export class CableLog {
  recording = false
  private messages: boolean
  private subscriptions = new Map<string, { outcome: string; sent: Set<string>; received: Set<string> }>()
  private connection = new Set<string>()
  private options: NormalizeOptions

  constructor(options: NormalizeOptions, messages: boolean) {
    this.options = options
    this.messages = messages
  }

  sent(payload: string) {
    const message = parse(payload)
    if (!message?.identifier) return this.connection.add(`> ${this.mask(payload)}`)
    const subscription = this.subscription(message.identifier)
    if (message.command === "message" && this.recording && this.messages) subscription.sent.add(this.mask(typeof message.data === "string" ? message.data : JSON.stringify(message.data)))
  }

  received(payload: string) {
    const message = parse(payload)
    if (!message) return this.connection.add(`< ${this.mask(payload)}`)
    if (message.type === "ping") return
    if (!message.identifier) {
      this.connection.add(`< ${message.type}${message.reason ? ` ${message.reason}` : ""}${message.reconnect !== undefined ? ` reconnect=${message.reconnect}` : ""}`)
      return
    }
    const subscription = this.subscription(message.identifier)
    if (message.type === "confirm_subscription") subscription.outcome = "confirmed"
    else if (message.type === "reject_subscription") subscription.outcome = "rejected"
    else if (message.message !== undefined && this.recording && this.messages) {
      subscription.received.add(typeof message.message === "string" ? message.message : JSON.stringify(message.message))
    }
  }

  private subscription(identifier: string) {
    const key = this.mask(identifier)
    if (!this.subscriptions.has(key)) this.subscriptions.set(key, { outcome: "unconfirmed", sent: new Set(), received: new Set() })
    return this.subscriptions.get(key)!
  }

  private mask(text: string): string {
    return maskText(text, this.options)
  }

  text(mask: (text: string) => string = (t) => t): string {
    const sorted = (lines: Iterable<string>) => [...new Set([...lines].map(mask))].sort()
    const out = [`(connection)`, ...sorted(this.connection).map((l) => `  ${l}`)]
    const subscriptions = [...this.subscriptions.entries()].map(([id, s]) => [mask(id), s] as const).sort(([a], [b]) => a.localeCompare(b))
    for (const [id, s] of subscriptions) {
      out.push(`${this.mask(id)}: ${s.outcome}`, ...sorted(s.sent).map((l) => `  > ${this.mask(l)}`), ...sorted([...s.received].map((value) => /^\s*</.test(value) ? normalizeFragment(value, this.options).trim() : this.mask(value))).map((l) => `  < ${l}`))
    }
    return out.join("\n") + "\n"
  }

  async resolveProtectedTokens(): Promise<void> {
    for (const [identifier, subscription] of this.subscriptions) await this.options.resolveProtectedTokens?.([identifier, ...subscription.sent, ...subscription.received].join("\n"))
  }
}

function parse(payload: string): any {
  try {
    return JSON.parse(payload)
  } catch {
    return undefined
  }
}
