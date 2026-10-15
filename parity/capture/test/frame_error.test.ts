// Transport holder: exercise PageTracker's real frame/error listeners, not a decoder replica.
import assert from "node:assert/strict"
import { EventEmitter } from "node:events"
import { test } from "node:test"
import type { Page, WebSocket } from "playwright"
import { CableLog } from "../network.ts"
import { PageTracker } from "../readiness.ts"

function fixture(engine: string) {
  const page = Object.assign(new EventEmitter(), {
    context: () => ({ browser: () => ({ browserType: () => ({ name: () => engine }) }) }),
  })
  const tracker = new PageTracker(page as unknown as Page)
  const cable = new CableLog({}, true)
  tracker.cable = cable
  cable.recording = true
  const socket = new EventEmitter()
  page.emit("websocket", socket as unknown as WebSocket)
  return { page, socket, tracker, cable }
}

// The pinned Firefox Juggler text event passes the ACString payload bytes as a
// byte-string; the other engines pass decoded text. No production decoder is
// used to make these inputs or the expected values.
const firefoxWire = (text: string) => Buffer.from(text, "utf8").toString("latin1")
const identifier = JSON.stringify({ channel: "RoomChannel", room_id: "room-👍-𠮷" })
const received = (text: string) => JSON.stringify({ identifier, message: text })

test("Firefox ASCII and UTF8-encoded Latin1 characters retain their actual text", () => {
  for (const text of ["plain ASCII", "café £"]) {
    const { socket, cable, tracker } = fixture("firefox")
    socket.emit("framereceived", { payload: firefoxWire(received(text)) })
    assert.ok(cable.text().includes(`  < ${text}\n`))
    assert.deepEqual(tracker.errors, [])
  }
})

test("Firefox UTF8 byte-string frames retain non-BMP text in both Cable directions and readiness identities", () => {
  const { socket, tracker, cable } = fixture("firefox")
  socket.emit("framesent", { payload: firefoxWire(JSON.stringify({ command: "subscribe", identifier })) })
  socket.emit("framereceived", { payload: firefoxWire(JSON.stringify({ type: "confirm_subscription", identifier })) })
  socket.emit("framesent", { payload: firefoxWire(JSON.stringify({ command: "message", identifier, data: "boost 👍 𠮷 café" })) })
  socket.emit("framereceived", { payload: firefoxWire(received("<p>boost 👍 𠮷 café</p>")) })
  assert.equal(tracker.outstanding.get(identifier), 0)
  assert.equal(tracker.confirmed.has(identifier), true)
  assert.match(cable.text(), /boost 👍 𠮷 café/)
  assert.doesNotMatch(cable.text(), /ð|\u009f/)
  assert.deepEqual(tracker.errors, [])
})

test("Chromium and WebKit preserve already decoded text, including legitimate mojibake-looking text", () => {
  for (const engine of ["chromium", "webkit"]) {
    const { socket, cable, tracker } = fixture(engine)
    socket.emit("framereceived", { payload: received("👍 𠮷 ð\u009f\u0091\u008d") })
    assert.match(cable.text(), /👍 𠮷 ð\u009f\u0091\u008d/)
    assert.deepEqual(tracker.errors, [])
  }
})

test("different emoji remain different after Firefox byte decoding, rather than receiving an alias", () => {
  const results: string[] = []
  for (const emoji of ["👍", "👎"]) {
    const { socket, cable } = fixture("firefox")
    socket.emit("framereceived", { payload: firefoxWire(received(emoji)) })
    const text = cable.text()
    assert.ok(text.includes(`  < ${emoji}\n`))
    results.push(text)
  }
  assert.notEqual(results[0], results[1])
})

test("malformed Firefox bytes and non-byte strings fail visibly; binary frames remain excluded", () => {
  for (const payload of ["\xc3(", "\xf0\x9f", received("👍")]) {
    const { socket, cable, tracker } = fixture("firefox")
    socket.emit("framereceived", { payload })
    assert.equal(tracker.errors.length, 1)
    assert.match(tracker.errors[0], /Firefox WebSocket text decode/)
    assert.equal(cable.text(), "(connection)\n")
  }
  const { socket, cable, tracker } = fixture("firefox")
  socket.emit("framereceived", { payload: Buffer.from(received("👎")) })
  assert.equal(cable.text(), "(connection)\n")
  assert.deepEqual(tracker.errors, [])
})

test("a page error with an empty stack retains its name, message and string value", () => {
  const { page, tracker } = fixture("chromium")
  const error = new Error("")
  error.name = "AbortError"
  error.stack = ""
  page.emit("pageerror", error)
  assert.equal(tracker.errors.length, 1)
  assert.deepEqual(JSON.parse(tracker.errors[0]), { name: "AbortError", message: "", stack: "", value: "AbortError" })
  error.name = ""
  page.emit("pageerror", error)
  assert.equal(tracker.errors.length, 2)
  assert.deepEqual(JSON.parse(tracker.errors[1]), { name: "", message: "", stack: "", value: "" })
})

test("a populated page error preserves its genuine stack without suppression", () => {
  const { page, tracker } = fixture("chromium")
  const error = new Error("real failure")
  error.stack = "Error: real failure\n    at owner (actual.js:8:2)"
  page.emit("pageerror", error)
  assert.deepEqual(JSON.parse(tracker.errors[0]), { name: "Error", message: "real failure", stack: error.stack, value: "Error: real failure" })
})
