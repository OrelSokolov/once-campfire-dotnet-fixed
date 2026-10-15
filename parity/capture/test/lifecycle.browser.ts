// Real transport/lifecycle holder proof, outside the host-only *.test.ts glob.
import assert from "node:assert/strict"
import { createServer } from "node:http"
import { createHash } from "node:crypto"
import { test } from "node:test"
import { chromium } from "playwright"
import { RequestLifecycleRecorder } from "../lifecycle.ts"

async function eventually(predicate: () => boolean, message: string) {
  const deadline = Date.now() + 5_000
  while (!predicate()) {
    assert.ok(Date.now() < deadline, message)
    await new Promise(resolve => setTimeout(resolve, 10))
  }
}

function serverTextFrame(text: string) {
  return serverFrame(Buffer.from(text), 0x81)
}

function serverFrame(body: Buffer, opcode: number) {
  assert.ok(body.length <= 65_535)
  const header = Buffer.alloc(body.length < 126 ? 2 : 4)
  header[0] = opcode
  header[1] = body.length < 126 ? body.length : 126
  if (header.length === 4) header.writeUInt16BE(body.length, 2)
  return Buffer.concat([header, body])
}

test("lifecycle receipt joins distinct cancelled requests and survives navigation without consuming page errors", async () => {
  const server = createServer((request, response) => {
    if (request.url === "/slow") {
      response.writeHead(200, { "content-type": "text/plain" })
      response.flushHeaders()
    } else if (request.url === "/redirect") {
      response.writeHead(302, { location: "/ok" }).end()
    } else {
      response.writeHead(200, { "content-type": request.url === "/ok" ? "text/plain" : "text/html" })
      response.end(request.url === "/ok" ? "complete" : "<!doctype html><title>lifecycle fixture</title>")
    }
  })
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve))
  const address = server.address()
  assert.ok(address && typeof address !== "string")
  const origin = `http://127.0.0.1:${address.port}`
  const browser = await chromium.launch()
  const page = await browser.newPage()
  const recorder = new RequestLifecycleRecorder(page)
  const independentErrors: Error[] = []
  page.on("pageerror", error => independentErrors.push(error))
  try {
    await page.goto(origin)
    await page.evaluate(() => {
      ;(window as any).controllers = [new AbortController(), new AbortController()]
      for (const controller of (window as any).controllers) fetch("/slow", { signal: controller.signal }).then(response => response.text())
    })
    // Wait for actual received headers on both streams before cancellation.
    const waitForEvents = async (type: string, count: number) => {
      const deadline = Date.now() + 5_000
      while (recorder.snapshot().events.filter(event => event.type === type).length < count) {
        assert.ok(Date.now() < deadline, `timed out waiting for ${count} ${type} events`)
        await new Promise(resolve => setTimeout(resolve, 10))
      }
    }
    // The two main-document events are not part of the /slow header assertion.
    const deadline = Date.now() + 5_000
    while (recorder.snapshot().events.filter(event => event.type === "response" && event.url === `${origin}/slow`).length < 2) {
      assert.ok(Date.now() < deadline, "both slow responses must reach headers")
      await new Promise(resolve => setTimeout(resolve, 10))
    }
    await page.evaluate(() => { for (const controller of (window as any).controllers) controller.abort() })
    await waitForEvents("failed", 2)
    await waitForEvents("pageerror", 2)
    assert.equal(await page.evaluate(() => fetch("/redirect").then(response => response.text())), "complete")
    await page.goto(`${origin}/next`)

    const receipt = recorder.snapshot()
    const failed = receipt.events.filter(event => event.type === "failed")
    assert.equal(failed.length, 2)
    assert.equal(new Set(failed.map(event => event.requestId)).size, 2)
    for (const failure of failed) {
      assert.equal(failure.url, `${origin}/slow`)
      assert.equal(failure.method, "GET")
      assert.equal(failure.resourceType, "fetch")
      assert.match(String(failure.failure), /abort/i)
      const joined = receipt.events.filter(event => event.requestId === failure.requestId)
      assert.deepEqual(joined.map(event => event.type), ["request", "response", "failed"])
      assert.equal(joined[1].status, 200)
    }
    const finished = receipt.events.find(event => event.type === "finished" && event.url === `${origin}/ok`)
    assert.ok(finished)
    const redirected = receipt.events.find(event => event.type === "request" && event.requestId === finished.requestId)
    assert.ok(redirected?.redirectedFromId)
    const navigations = receipt.events.filter(event => event.type === "navigation" && event.mainFrame)
    assert.deepEqual(navigations.map(event => event.url), [`${origin}/`, `${origin}/next`])
    assert.ok(Number(navigations[1].navigationSequence) > Number(navigations[0].navigationSequence))
    assert.equal(independentErrors.length, 2, "the observer must not consume page errors")
    assert.equal(receipt.events.filter(event => event.type === "pageerror").length, 2)
    assert.ok(receipt.events.every(event => Number.isFinite(event.hostTimeMs) && Number.isFinite(event.hostMonotonicMs)))
    assert.equal(receipt.droppedEvents, 0)

    const bounded = new RequestLifecycleRecorder(page, 3)
    const longError = page.waitForEvent("pageerror")
    await page.evaluate(() => { setTimeout(() => { throw new Error("x".repeat(9_000)) }, 0) })
    assert.equal((await longError).message.length, 9_000, "the original error must stay intact")
    assert.equal(bounded.snapshot().events[0].message, "x".repeat(8_192))
    assert.ok(bounded.snapshot().truncatedFields > 0, "bounded text must advertise truncation")
    await page.evaluate(async () => { for (let index = 0; index < 4; index++) await fetch(`/ok?${index}`).then(response => response.text()) })
    assert.equal(bounded.snapshot().events.length, 3)
    assert.ok(bounded.snapshot().droppedEvents > 0, "a partial receipt must advertise dropped events")
    bounded.stop()
    const before = bounded.snapshot()
    await page.goto(`${origin}/after-stop`)
    assert.deepEqual(bounded.snapshot(), before, "stop must remove listeners without affecting the page")
  } finally {
    recorder.stop()
    await browser.close()
    server.closeAllConnections()
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
  }
})

test("socket receipts keep control identity across navigation without retaining protected text or changing delivery", async () => {
  const identifier = JSON.stringify({ channel: "Turbo::StreamsChannel", signed_stream_name: "CfDJ8_PRIVATE_FIXTURE_IDENTIFIER" })
  const controls = [
    JSON.stringify({ type: "welcome" }),
    JSON.stringify({ type: "confirm_subscription", identifier }),
    JSON.stringify({ type: "reject_subscription", identifier }),
    JSON.stringify({ type: "disconnect", reason: "fixture", reconnect: false }),
  ]
  const application = JSON.stringify({ identifier, message: "<turbo-stream>PRIVATE_HTML_SENTINEL</turbo-stream>" })
  const misleadingControl = JSON.stringify({ type: "confirm_subscription", identifier, message: "PRIVATE_HTML_SENTINEL" })
  const oversized = JSON.stringify({ type: "confirm_subscription", identifier: "x".repeat(5_000) })
  const binaryControl = Buffer.from(controls[1])
  const malformedUtf8 = Buffer.concat([Buffer.from('{"type":"confirm_subscription","identifier":"'), Buffer.from([0xff]), Buffer.from('"}')])
  const binaryApplication = Buffer.from(application)
  const server = createServer((request, response) => {
    if (request.url === "/emit") for (const socket of connections) socket.write(serverTextFrame(controls[0]))
    response.end("<!doctype html><title>socket lifecycle fixture</title>")
  })
  const connections = new Set<import("node:stream").Duplex>()
  server.on("upgrade", (request, socket) => {
    connections.add(socket)
    socket.on("close", () => connections.delete(socket))
    if (request.url === "/fail") {
      socket.end("HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n")
      return
    }
    const key = request.headers["sec-websocket-key"]
    assert.equal(typeof key, "string")
    const accept = createHash("sha1").update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`).digest("base64")
    socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`)
    for (const text of [controls[0], controls[1], controls[2], application, misleadingControl, oversized]) socket.write(serverTextFrame(text))
    for (const bytes of [binaryControl, malformedUtf8, binaryApplication]) socket.write(serverFrame(bytes, 0x82))
    socket.write(serverTextFrame(controls[3]))
    // Browser navigation/close sends a close frame; reply without changing any data frame.
    socket.on("data", data => { if ((data[0] & 0x0f) === 8) socket.end(Buffer.from([0x88, 0])) })
  })
  await new Promise<void>(resolve => server.listen(0, "127.0.0.1", resolve))
  const address = server.address()
  assert.ok(address && typeof address !== "string")
  const origin = `http://127.0.0.1:${address.port}`
  const socketUrl = origin.replace("http:", "ws:") + "/cable"
  const browser = await chromium.launch()
  const page = await browser.newPage()
  const recorder = new RequestLifecycleRecorder(page)
  const independentFrames: string[] = []
  const independentBinary: Buffer[] = []
  const independentErrors: string[] = []
  page.on("websocket", socket => {
    socket.on("framereceived", ({ payload }) => {
      if (typeof payload === "string") independentFrames.push(payload)
      else independentBinary.push(payload)
    })
    socket.on("socketerror", message => independentErrors.push(message))
  })
  const open = async (count: number, url = socketUrl) => page.evaluate(async ({ count, url }) => {
    const sockets: WebSocket[] = (window as any).fixtureSockets ??= []
    await Promise.all(Array.from({ length: count }, () => new Promise<void>((resolve, reject) => {
      const socket = new WebSocket(url)
      sockets.push(socket)
      socket.onerror = () => url.endsWith("/fail") ? resolve() : reject(new Error("unexpected fixture socket error"))
      socket.onmessage = event => { if (typeof event.data === "string" && JSON.parse(event.data).type === "disconnect") resolve() }
    })))
  }, { count, url })
  try {
    await page.goto(origin)
    await open(2)
    await eventually(() => recorder.snapshot().controlFrames === 10, "both real sockets must deliver text and binary controls")
    const first = recorder.snapshot()
    const requested = first.events.filter(event => event.type === "websocket-requested")
    assert.equal(requested.length, 2)
    assert.equal(new Set(requested.map(event => event.socketId)).size, 2)
    assert.ok(requested.every(event => event.urlSha256 === createHash("sha256").update(socketUrl).digest("hex")))
    for (const request of requested) {
      const received = first.events.filter(event => event.type === "websocket-control-received" && event.socketId === request.socketId)
      const expectedPayloads = [controls[0], controls[1], controls[2], binaryControl, controls[3]]
      assert.deepEqual(received.map(event => event.controlType), ["welcome", "confirm_subscription", "reject_subscription", "confirm_subscription", "disconnect"])
      assert.deepEqual(received.map(event => event.payloadRepresentation), ["text", "text", "text", "binary", "text"])
      assert.deepEqual(received.map(event => event.payloadSha256), expectedPayloads.map(payload => createHash("sha256").update(payload).digest("hex")))
      assert.deepEqual(received.map(event => event.payloadUtf8Bytes), expectedPayloads.map(payload => Buffer.byteLength(payload)))
      assert.equal(received[1].identifierSha256, createHash("sha256").update(identifier).digest("hex"))
      assert.equal(received[3].identifierSha256, received[1].identifierSha256)
      assert.ok(received.every(event => event.observedMainFrameNavigationSequence === request.observedMainFrameNavigationSequence))
    }
    assert.equal(independentFrames.length, 14, "observer must leave application and oversized frames intact for other listeners")
    assert.equal(independentBinary.length, 6)
    for (const expected of [binaryControl, malformedUtf8, binaryApplication]) {
      assert.equal(independentBinary.filter(payload => payload.equals(expected)).length, 2, "observer must leave binary controls, invalid UTF-8 and HTML bytes intact")
    }
    assert.equal(first.ignoredSocketFrames, 6)
    assert.equal(first.malformedSocketFrames, 2)
    assert.equal(first.oversizedSocketFrames, 2)
    assert.doesNotMatch(JSON.stringify(first), /CfDJ8_PRIVATE_FIXTURE_IDENTIFIER|PRIVATE_HTML_SENTINEL/)

    // Same-document navigation leaves the sockets alive. Receipt context must
    // advance without inventing a new socket identity or an originating document.
    await page.goto(`${origin}/#after-controls`)
    await page.evaluate(() => fetch("/emit").then(response => response.text()))
    await eventually(() => recorder.snapshot().controlFrames === 12, "existing sockets must retain controls observed after same-document navigation")
    const sameDocument = recorder.snapshot()
    for (const request of requested) {
      const received = sameDocument.events.filter(event => event.type === "websocket-control-received" && event.socketId === request.socketId)
      assert.equal(received.length, 6)
      assert.ok(Number(received.at(-1)!.observedMainFrameNavigationSequence) > Number(request.observedMainFrameNavigationSequence))
    }
    assert.equal(sameDocument.events.filter(event => event.type === "websocket-requested").length, 2)

    // Navigation can retain a page/socket rather than close it immediately. Test
    // close ownership with actual browser closes, then establish the next page.
    await page.evaluate(() => { for (const socket of (window as any).fixtureSockets) socket.close() })
    await eventually(() => recorder.snapshot().events.filter(event => event.type === "websocket-closed").length === 2, "old sockets must retain their own close identities")
    await page.goto(`${origin}/next`)
    await open(1)
    await eventually(() => recorder.snapshot().controlFrames === 17, "new document control history must be retained alongside the old one")
    const afterNavigation = recorder.snapshot()
    const latest = afterNavigation.events.filter(event => event.type === "websocket-requested").at(-1)!
    assert.ok(Number(latest.observedMainFrameNavigationSequence) > Number(requested[0].observedMainFrameNavigationSequence))
    assert.ok(!requested.some(event => event.socketId === latest.socketId))
    assert.equal(afterNavigation.events.filter(event => event.type === "websocket-control-received").length, 17)
    assert.deepEqual(new Set(afterNavigation.events.filter(event => event.type === "websocket-closed").map(event => event.socketId)), new Set(requested.map(event => event.socketId)))

    await open(1, origin.replace("http:", "ws:") + "/fail")
    await eventually(() => recorder.snapshot().events.some(event => event.type === "websocket-error"), "actual rejected handshake must be observed")
    const failure = recorder.snapshot().events.find(event => event.type === "websocket-error")!
    assert.ok(independentErrors.length > 0)
    assert.ok(independentErrors.some(message => createHash("sha256").update(message).digest("hex") === failure.errorSha256))

    await open(recorder.snapshot().maxActiveSockets + 1)
    assert.equal(recorder.snapshot().activeSocketObservers, recorder.snapshot().maxActiveSockets)
    assert.ok(recorder.snapshot().unobservedSockets > 0, "excess sockets must be marked rather than retained without bound")

    const bounded = new RequestLifecycleRecorder(page, 3)
    await open(2)
    await eventually(() => bounded.snapshot().droppedEvents > 0, "socket receipts must share the event cap")
    assert.equal(bounded.snapshot().events.length, 3)
    bounded.stop()
    assert.equal(bounded.snapshot().activeSocketObservers, 0)
    const stopped = bounded.snapshot()
    const independentBefore = independentFrames.length
    await page.evaluate(() => fetch("/emit").then(response => response.text()))
    await eventually(() => independentFrames.length > independentBefore, "existing live socket listeners must still receive after observer stop")
    assert.deepEqual(bounded.snapshot(), stopped, "stop must remove listeners from previously tracked sockets")
    const afterExisting = independentFrames.length
    await open(1)
    assert.ok(independentFrames.length > afterExisting, "stopping the observer must leave new sockets usable")
    assert.deepEqual(bounded.snapshot(), stopped)
  } finally {
    recorder.stop()
    await browser.close()
    for (const socket of connections) socket.destroy()
    server.closeAllConnections()
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
  }
})
