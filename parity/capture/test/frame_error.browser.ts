// Pinned real-Firefox transport holder, outside the host-only *.test.ts glob.
// PARITY_FRAME_OWNER_RECEIPTS must point to a writable diagnostic receipt mount.
import assert from "node:assert/strict"
import fs from "node:fs"
import path from "node:path"
import { createHash } from "node:crypto"
import { createServer } from "node:http"
import type { Duplex } from "node:stream"
import { test } from "node:test"
import { firefox } from "playwright"
import { CableLog } from "../network.ts"
import { PageTracker } from "../readiness.ts"

test("real Firefox text transport preserves actual UTF8 bytes in Cable and readiness", { timeout: 20_000 }, async () => {
  const root = process.env.PARITY_FRAME_OWNER_RECEIPTS
  assert.ok(root && path.isAbsolute(root), "a private absolute receipt directory is required")
  const identifier = JSON.stringify({ channel: "RoomChannel", room_id: "room-👍-𠮷" })
  const sent = [JSON.stringify({ command: "subscribe", identifier }), JSON.stringify({ command: "message", identifier, data: "client 👍 𠮷 café" })]
  const replies = [JSON.stringify({ type: "confirm_subscription", identifier }), ...["ASCII", "👍 𠮷 café", "👎"].map(message => JSON.stringify({ identifier, message }))]
  const events: unknown[] = []
  const connections = new Set<Duplex>()
  const serverFrames: string[] = []
  const protocolFailures: string[] = []
  const record = (type: string, details: unknown) => events.push({ sequence: events.length + 1, type, details })
  const server = createServer((_request, response) => response.end("<!doctype html><title>UTF8 frame owner</title>"))
  server.on("upgrade", (request, socket) => {
    connections.add(socket)
    socket.on("error", error => protocolFailures.push(String(error)))
    const key = request.headers["sec-websocket-key"]
    if (typeof key !== "string") { protocolFailures.push("missing handshake key"); socket.destroy(); return }
    const accept = createHash("sha1").update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`).digest("base64")
    socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`)
    let buffered = Buffer.alloc(0)
    socket.on("data", (data: Buffer) => {
      buffered = Buffer.concat([buffered, data])
      while (buffered.length >= 2) {
        let length = buffered[1] & 127
        let position = 2
        if (length === 127) { protocolFailures.push("unexpected large frame"); socket.destroy(); return }
        if (length === 126) {
          if (buffered.length < 4) return
          length = buffered.readUInt16BE(2)
          position = 4
        }
        if (!(buffered[1] & 128)) { protocolFailures.push("client frame must be masked"); socket.destroy(); return }
        if (buffered.length < position + 4 + length) return
        const opcode = buffered[0] & 15
        const fin = !!(buffered[0] & 128)
        const mask = buffered.subarray(position, position + 4)
        position += 4
        const bytes = Buffer.from(buffered.subarray(position, position + length))
        for (let index = 0; index < bytes.length; index++) bytes[index] ^= mask[index % 4]
        buffered = buffered.subarray(position + length)
        if (opcode === 8) { socket.end(Buffer.from([0x88, 0])); return }
        if (!fin || opcode !== 1) { protocolFailures.push("unexpected fragmented/non-text fixture frame"); socket.destroy(); return }
        const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes)
        record("server-received", { opcode, bytesBase64: bytes.toString("base64"), text })
        serverFrames.push(text)
        if (serverFrames.length === 1) for (const reply of replies) {
          // Independently serialize raw wire UTF8, rather than fabricating a
          // Playwright event or using the harness's conversion under test.
          const body = Buffer.from(reply, "utf8")
          const header = Buffer.alloc(body.length < 126 ? 2 : 4)
          header[0] = 0x81
          header[1] = body.length < 126 ? body.length : 126
          if (header.length === 4) header.writeUInt16BE(body.length, 2)
          record("server-sent", { opcode: 1, bytesBase64: body.toString("base64"), text: reply })
          socket.write(Buffer.concat([header, body]))
        }
      }
    })
  })
  await new Promise<void>(resolve => server.listen(0, "127.0.0.1", resolve))
  const address = server.address()
  assert.ok(address && typeof address !== "string")
  const origin = `http://127.0.0.1:${address.port}`
  const browser = await firefox.launch()
  const page = await browser.newPage()
  const tracker = new PageTracker(page)
  const cable = new CableLog({}, true)
  tracker.cable = cable
  cable.recording = true
  let browserMessages: string[] = []
  let outcome = "failed"
  page.on("websocket", socket => {
    const observed = (event: string) => ({ payload }: { payload: string | Buffer }) => record(`playwright-${event}`, {
      representation: typeof payload === "string" ? "string" : "Buffer",
      payload: typeof payload === "string" ? payload : payload.toString("base64"),
      codeUnits: typeof payload === "string" ? Array.from({ length: payload.length }, (_, index) => payload.charCodeAt(index)) : undefined,
    })
    socket.on("framesent", observed("framesent"))
    socket.on("framereceived", observed("framereceived"))
  })
  try {
    await page.goto(origin)
    // goto resets recording, as it does for ordinary production captures.
    cable.recording = true
    browserMessages = await page.evaluate(({ url, sent }) => new Promise<string[]>((resolve, reject) => {
      const messages: string[] = []
      const socket = new WebSocket(url)
      const deadline = setTimeout(() => reject(new Error("fixture WebSocket receive deadline")), 5_000)
      socket.onopen = () => { for (const text of sent) socket.send(text) }
      socket.onerror = () => { clearTimeout(deadline); reject(new Error("fixture WebSocket transport error")) }
      socket.onmessage = event => {
        messages.push(event.data)
        if (messages.length === 4) { clearTimeout(deadline); resolve(messages) }
      }
      ;(window as any).ownerSocket = socket
    }), { url: origin.replace("http:", "ws:") + "/cable", sent })
    const deadline = Date.now() + 5_000
    while (serverFrames.length < 2 || events.filter((event: any) => event.type === "playwright-framereceived").length < 4) {
      assert.ok(Date.now() < deadline, "both actual send/receive transports must finish")
      await new Promise(resolve => setTimeout(resolve, 10))
    }
    assert.deepEqual(protocolFailures, [])
    assert.deepEqual(serverFrames, sent, "the actual server must receive the original caller UTF8 text")
    assert.deepEqual(browserMessages, replies, "Firefox's application must receive the original server UTF8 text")
    assert.equal(tracker.confirmed.has(identifier), true, "actual Firefox UTF8 subscription identity must match the original Unicode identifier")
    assert.equal(tracker.outstanding.get(identifier), 0)
    assert.ok(cable.text().includes("  < ASCII\n"))
    assert.ok(cable.text().includes("  < 👍 𠮷 café\n"))
    assert.ok(cable.text().includes("  < 👎\n"), "different emoji must remain independently visible")
    assert.ok(cable.text().includes("  > client 👍 𠮷 café\n"))
    assert.deepEqual(tracker.errors, [])
    outcome = "passed"
  } finally {
    // Persist actual frame representations and raw bytes even on a baseline
    // assertion failure. This fixture does not intercept page errors.
    try {
      fs.mkdirSync(root, { recursive: true })
      fs.writeFileSync(path.join(root, "firefox-utf8-transport.private.json"), JSON.stringify({ outcome, browserMessages, serverFrames, protocolFailures, events, trackerErrors: tracker.errors, cable: cable.text(), confirmed: [...tracker.confirmed], outstanding: [...tracker.outstanding] }, null, 2) + "\n")
    } finally {
      await browser.close()
      for (const socket of connections) socket.destroy()
      server.closeAllConnections()
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
    }
  }
})
