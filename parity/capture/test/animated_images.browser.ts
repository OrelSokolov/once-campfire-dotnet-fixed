// Real image-route lifetime holder, outside the host-only *.test.ts glob.
// The same fixture is copied onto the untouched baseline and the repaired source.
import assert from "node:assert/strict"
import fs from "node:fs"
import os from "node:os"
import path from "node:path"
import { fileURLToPath } from "node:url"
import { createServer } from "node:http"
import type { ServerResponse } from "node:http"
import { gzipSync } from "node:zlib"
import { spawn } from "node:child_process"
import { test } from "node:test"
import { chromium, firefox, webkit } from "playwright"
import { freezeAnimatedImages } from "../animated_images.ts"
import { captureCell } from "../capture.ts"
import { BrowserPool } from "../browsers.ts"
import { REPO_DIR, VIEWPORTS } from "../config.ts"
import type { Job } from "../inventory.ts"
import type { CaptureEnv } from "../capture.ts"

const child = process.env.PARITY_IMAGE_OWNER_CHILD
const root = process.env.PARITY_IMAGE_OWNER_RECEIPTS
const scenario = process.env.PARITY_IMAGE_OWNER_SCENARIO ?? "released"
const engine = process.env.PARITY_IMAGE_OWNER_ENGINE as "chromium" | "firefox" | "webkit"
const events: unknown[] = []
const receipt = (type: string, detail: unknown = {}) => events.push({ sequence: events.length + 1, type, at: Date.now(), detail })
const writeReceipt = (outcome: string, detail: unknown = {}) => {
  if (!root) throw new Error("Persistent owner receipt directory required")
  fs.mkdirSync(root, { recursive: true })
  fs.writeFileSync(path.join(root, `${engine}-${scenario}.json`), JSON.stringify({ outcome, detail, events }, null, 2) + "\n")
}
async function eventually(check: () => boolean, message: string) {
  const until = Date.now() + 5_000
  while (!check()) {
    assert.ok(Date.now() < until, message)
    await new Promise(resolve => setTimeout(resolve, 5))
  }
}
function chunks(body: Buffer) {
  assert.equal(body.toString("ascii", 0, 4), "RIFF")
  assert.equal(body.toString("ascii", 8, 12), "WEBP")
  assert.equal(body.readUInt32LE(4), body.length - 8)
  const entries: { kind: string; bytes: Buffer }[] = []
  let position = 12
  while (position < body.length) {
    const end = position + 8 + body.readUInt32LE(position + 4) + (body.readUInt32LE(position + 4) & 1)
    assert.ok(end <= body.length)
    entries.push({ kind: body.toString("ascii", position, position + 4), bytes: body.subarray(position, end) })
    position = end
  }
  return entries
}
async function runChild() {
  // This monitor retains the actual fatal boundary without handling/suppressing it.
  process.on("uncaughtExceptionMonitor", (error, origin) => writeReceipt("uncaught", { message: error.message, stack: error.stack, origin }))
  const image = fs.readFileSync(path.join(REPO_DIR, "reference/app/assets/images/sounds/56k.webp"))
  const compressed = gzipSync(image)
  const still = fs.readFileSync(path.join(REPO_DIR, "reference/app/assets/images/sounds/yay.webp"))
  let pending: ServerResponse | undefined
  let imageRequests = 0
  const server = createServer((request, response) => {
    if (request.url === "/held.webp") {
      response.writeHead(200, { "content-type": "image/webp", "content-encoding": "gzip", "content-length": compressed.length, "x-owner-image": "original" })
      response.write(compressed.subarray(0, Math.floor(compressed.length / 2)))
      const requestId = ++imageRequests
      pending ??= response // Release the first actual fetch, never a later bypass request.
      receipt("gzip200-first-chunk", { requestId, path: request.url, compressedBytes: compressed.length })
      if (scenario === "truncated" || scenario === "capture-truncated") {
        // Real short body/closed transport, not a mocked route rejection.
        const socket = response.socket
        response.end(() => socket?.destroy())
        receipt("truncated-stream")
      }
    } else if (request.url === "/still.webp") {
      response.writeHead(200, { "content-type": "image/webp", "content-encoding": "gzip", "x-owner-image": "original" }).end(gzipSync(still))
    } else if (scenario === "capture-released" && request.url === "/late-image.js") {
      receipt("late-image-script-request")
      // Server timing remains real while captureCell freezes browser timers. A complete
      // document is parsed/rendered in WebKit; actual script execution starts the image.
      const delayedImage = setTimeout(() => {
        if (!response.destroyed) {
          response.writeHead(200, { "content-type": "text/javascript" }).end('const image = new Image(); image.src = "/held.webp"; document.body.append(image)')
          receipt("late-image-script-completed")
        }
      }, 1_000)
      response.on("close", () => clearTimeout(delayedImage))
    } else if (scenario === "capture-released" && request.url === "/held-script.js") {
      response.writeHead(200, { "content-type": "text/javascript" })
      response.flushHeaders()
      receipt("held-script-headers") // Real load remains pending until context cleanup.
    } else if (scenario === "capture-released") {
      response.writeHead(200, { "content-type": "text/html; charset=utf-8" }).end('<!doctype html><html><head><title>real capture cleanup barrier</title></head><body><p>Complete visible owner document</p>' + '<p>ordinary document content</p>'.repeat(100) + '<script defer src="/late-image.js"></script><script defer src="/held-script.js"></script></body></html>')
      receipt("complete-main-document")
    } else {
      response.writeHead(200, { "content-type": "text/html" }).end(`<!doctype html><title>real image owner</title>${scenario === "capture-truncated" ? '<img src="/held.webp">' : ""}`)
    }
  })
  await new Promise<void>(resolve => server.listen(0, "127.0.0.1", resolve))
  const address = server.address()
  assert.ok(address && typeof address !== "string")
  const origin = `http://127.0.0.1:${address.port}`
  const browser = await ({ chromium, firefox, webkit }[engine]).launch()
  const context = await browser.newContext()
  const page = await context.newPage()
  let owner: Awaited<ReturnType<typeof freezeAnimatedImages>> | undefined
  let failure: unknown
  const pageErrors: string[] = []
  page.on("pageerror", error => pageErrors.push(error.message))
  context.on("close", () => receipt("context-closed"))
  try {
    if (scenario === "capture-truncated" || scenario === "capture-released") {
      // Actual production captureCell owns and persists this asynchronous callback failure.
      // A plain fixture has no Stimulus app, so any separate readiness failure stays visible.
      const pool = new BrowserPool()
      const outDir = path.join(root!, `${engine}-${scenario}-capture`)
      const job: Job = { state: { id: "image-owner", path: "/", seed: "image-owner-fixture", steps: [], clock: "advancing" }, cell: { engine, viewport: VIEWPORTS.desktop, scheme: "light" } }
      try {
        const warm = await pool.acquire(engine)
        const captureBrowser = warm.browser
        await warm.release()
        let completed = false
        const capture = captureCell(job, { name: "candidate", url: origin, origin }, { pool, outDir, time: "2026-10-06T00:00:00Z", timeoutMs: scenario === "capture-released" ? 2_000 : 600, seedDir: outDir } as CaptureEnv).then(meta => { completed = true; return meta })
        if (scenario === "capture-released") {
          await eventually(() => captureBrowser.contexts().length > 0, "actual capture context must start")
          const captureContext = captureBrowser.contexts()[0]
          assert.ok(captureContext)
          let closed = false
          captureContext.on("close", () => { closed = true; receipt("capture-context-closed") })
          const observedPages = new WeakSet<object>()
          const recordPage = (page: import("playwright").Page) => {
            if (observedPages.has(page)) return
            observedPages.add(page)
            page.on("request", request => receipt("capture-browser-request", { url: request.url(), resourceType: request.resourceType() }))
            page.on("response", response => receipt("capture-browser-response", { url: response.url(), status: response.status() }))
          }
          captureContext.on("page", recordPage)
          for (const page of captureContext.pages()) recordPage(page)
          await eventually(() => !!pending, "actual capture must receive held image headers")
          // Image starts a second after navigation. This crosses the navigation's 2s
          // deadline but stays inside that image's separate 2s request deadline.
          await new Promise(resolve => setTimeout(resolve, 1_500))
          assert.equal(completed, false, "real capture cleanup must be draining its image")
          assert.equal(closed, false, "actual capture context must stay alive during drain")
          assert.equal(imageRequests, 1, "registration removal must not bypass an owned image request")
          receipt("capture-body-release")
          pending!.end(compressed.subarray(Math.floor(compressed.length / 2)))
        }
        const meta = await capture
        receipt("capture-meta", meta)
        assert.ok(meta.error, "The original real navigation/readiness failure must stay observable")
        assert.deepEqual(meta.pageErrors, [], "The actual captured page must remain free of JavaScript errors")
        const errors = (meta as typeof meta & { imageRouteErrors?: { url?: string; stage: string; message: string }[] }).imageRouteErrors
        if (scenario === "capture-truncated") {
          assert.ok(errors?.some(error => error.url === `${origin}/held.webp` && error.stage === "fetch" && error.message.length > 0), "original image fetch failure must be retained in cell metadata")
        } else {
          assert.equal(errors, undefined)
          assert.match(meta.error, /page.goto: Timeout 2000ms exceeded/)
          const types = events.map((event: any) => event.type)
          assert.ok(types.includes("late-image-script-completed"), "real delayed script completion must be observed")
          assert.ok(types.includes("gzip200-first-chunk"), "real held image headers must be observed")
          assert.ok(types.indexOf("late-image-script-completed") < types.indexOf("gzip200-first-chunk"))
          assert.equal(imageRequests, 1)
          assert.ok(types.indexOf("capture-body-release") < types.indexOf("capture-context-closed"))
        }
        const saved = JSON.parse(fs.readFileSync(path.join(outDir, "candidate/image-owner", `${engine}-desktop-light.json`), "utf8"))
        assert.deepEqual(saved.imageRouteErrors, errors)
        assert.deepEqual(saved.pageErrors, meta.pageErrors)
        assert.equal(pool.version(engine) !== undefined, true)
      } finally { await pool.close() }
    } else {
      owner = await freezeAnimatedImages(context, origin, 1_000)
      await page.goto(origin)
      const received = page.waitForResponse(`${origin}/held.webp`).catch(error => { receipt("image-response-wait-failed", String(error)); return undefined })
      await page.evaluate(() => { const image = new Image(); image.src = "/held.webp"; document.body.append(image) })
      await eventually(() => !!pending, "real gzip response must reach its held body")
      if (!owner) {
        // Exact baseline cleanup: helper returns no holder, context closes with fetch in flight.
        receipt("baseline-context-close-with-live-fetch")
        await context.close()
        await new Promise(resolve => setTimeout(resolve, 250))
        assert.fail("Unowned live route callback did not expose its context-disposal failure")
      } else {
        receipt("retirement-start")
        let retired = false
        const retirement = owner.retire().then(() => { retired = true })
        if (scenario === "released") {
          // Two real event-loop turns while the server body is still held. Retirement cannot
          // complete or close the context before the holder has finished route.fetch/fulfill.
          await new Promise(resolve => setTimeout(resolve, 30))
          assert.equal(retired, false)
          assert.equal(context.pages().length, 1)
          assert.equal(pending!.writableEnded, false)
          assert.equal(imageRequests, 1, "retirement must not resume the intercepted browser request")
          receipt("body-release")
          pending!.end(compressed.subarray(Math.floor(compressed.length / 2)))
          await retirement
          receipt("retirement-complete")
          assert.deepEqual(owner.snapshot(), [])
          assert.equal(imageRequests, 1)
          const response = await received
          assert.ok(response)
          assert.equal(response.status(), 200)
          assert.equal(response.headers()["content-type"], "image/webp")
          assert.equal(response.headers()["x-owner-image"], "original")
          const actual = chunks(await response.body()), original = chunks(image)
          assert.equal(actual.filter(entry => entry.kind === "ANMF").length, 1)
          assert.deepEqual(actual.find(entry => entry.kind === "ANMF")!.bytes, original.find(entry => entry.kind === "ANMF")!.bytes)
          assert.deepEqual(actual.filter(entry => entry.kind !== "ANMF"), original.filter(entry => entry.kind !== "ANMF"))
          // A new normal still-image context exercises the unchanged passthrough bytes.
          const stillContext = await browser.newContext()
          const stillOwner = await freezeAnimatedImages(stillContext, origin, 1_000)
          try {
            const stillPage = await stillContext.newPage()
            const response = await stillPage.goto(`${origin}/still.webp`)
            assert.ok(response)
            assert.equal(response.status(), 200)
            assert.deepEqual(await response.body(), still)
            await stillOwner.retire()
          } finally { await stillContext.close(); await stillOwner.finish() }
          assert.deepEqual(stillOwner.snapshot(), [])
        } else {
          const began = Date.now()
          await retirement
          assert.ok(Date.now() - began < 4_000, "real failure/timeout must be bounded")
          await context.close()
          await owner.finish()
          const failures = owner.snapshot()
          receipt("route-failures", failures)
          assert.ok(failures.some(error => error.url === `${origin}/held.webp` && error.stage === "fetch" && error.message.length > 0))
          if (scenario === "held") assert.ok(failures.some(error => /timeout|timed out/i.test(error.message)))
          assert.equal(context.pages().length, 0)
          await received
        }
      }
    }
    assert.deepEqual(pageErrors, [], "No page-error suppression or new page errors")
  } catch (error) { failure = error; throw error }
  finally {
    await context.close()
    if (owner) await owner.finish()
    await browser.close()
    server.closeAllConnections()
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
    writeReceipt(failure ? "assertion-failed" : "complete", { failure: failure instanceof Error ? failure.stack : failure, routeErrors: owner?.snapshot(), pageErrors })
  }
}

if (child === "1") {
  await runChild()
} else {
  for (const engine of ["chromium", "firefox", "webkit"] as const) {
    for (const scenario of ["released", "capture-released", "held", "truncated", "capture-truncated"]) {
      test(`image route retirement ${scenario === "released" || scenario === "capture-released" ? "gzip barrier " + scenario : "failure " + scenario} ${engine}`, { timeout: 30_000 }, async () => {
        const directory = root ?? fs.mkdtempSync(path.join(os.tmpdir(), "image-route-owner-"))
        fs.mkdirSync(directory, { recursive: true })
        const child = spawn(process.execPath, [fileURLToPath(import.meta.url)], { env: { ...process.env, PARITY_IMAGE_OWNER_CHILD: "1", PARITY_IMAGE_OWNER_ENGINE: engine, PARITY_IMAGE_OWNER_SCENARIO: scenario, PARITY_IMAGE_OWNER_RECEIPTS: directory }, stdio: ["ignore", "pipe", "pipe"] })
        let stdout = "", stderr = ""
        child.stdout.on("data", data => { stdout += data })
        child.stderr.on("data", data => { stderr += data })
        const kill = setTimeout(() => child.kill("SIGTERM"), 25_000)
        let result: { code: number | null; signal: string | null }
        try { result = await new Promise((resolve, reject) => { child.once("error", reject); child.once("close", (code, signal) => resolve({ code, signal })) }) }
        finally { clearTimeout(kill) }
        fs.writeFileSync(path.join(directory, `${engine}-${scenario}.process.json`), JSON.stringify({ ...result, stdout, stderr }, null, 2) + "\n")
        assert.equal(result.signal, null, stderr)
        assert.equal(result.code, 0, stderr)
        const evidence = JSON.parse(fs.readFileSync(path.join(directory, `${engine}-${scenario}.json`), "utf8"))
        assert.equal(evidence.outcome, "complete")
        if (scenario === "released") {
          const types = evidence.events.map((event: { type: string }) => event.type)
          assert.ok(types.indexOf("retirement-start") < types.indexOf("body-release"))
          assert.ok(types.indexOf("body-release") < types.indexOf("retirement-complete"))
          assert.ok(types.indexOf("retirement-complete") < types.indexOf("context-closed"))
        }
      })
    }
  }
}