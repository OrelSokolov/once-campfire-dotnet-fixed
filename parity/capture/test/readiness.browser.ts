// Real-browser holder regression, deliberately outside the host-only *.test.ts glob.
// Run in the pinned parity Playwright image; see docs/development.md.
import assert from "node:assert/strict"
import { after, before, describe, test } from "node:test"
import { chromium, firefox, webkit, type Browser } from "playwright"
import { PageTracker, READINESS_SCRIPT, settle } from "../readiness.ts"

const TIME = Date.UTC(2024, 0, 1)
const ORIGIN = "https://readiness.test"

async function fixture(browser: Browser, options: { register?: boolean; busy?: boolean; pending?: boolean } = {}) {
  const context = await browser.newContext()
  await context.clock.install({ time: new Date(TIME - 1000) })
  await context.clock.pauseAt(TIME)
  await context.addInitScript({ path: READINESS_SCRIPT })
  const page = await context.newPage()
  const tracker = new PageTracker(page)
  let releasePending!: () => void
  const pending = new Promise<void>((resolve) => { releasePending = resolve })
  await page.route(`${ORIGIN}/**`, async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname === "/pending") {
      await pending
      await route.fulfill({ status: 200, contentType: "text/plain", body: "finished" })
      return
    }
    const first = url.pathname === "/first"
    await route.fulfill({ status: 200, contentType: "text/html", body: first
      ? "<!doctype html><html><body>first document</body></html>"
      : `<!doctype html><html${options.busy ? ' aria-busy="true"' : ""}><body>
          <delayed-editor></delayed-editor>
          <script>
            ${options.pending ? 'fetch("/pending")' : ""}
          </script>
        </body></html>` })
  })
  // A real full navigation creates a new document while the context clock is paused.
  await page.goto(`${ORIGIN}/first`)
  const request = options.pending ? page.waitForRequest(`${ORIGIN}/pending`) : undefined
  await page.goto(`${ORIGIN}/second`)
  if (request) await request
  // Re-pause this document explicitly before its deferred module startup. A
  // context clock's navigation replay alone can follow real time after goto.
  await page.clock.pauseAt(await page.evaluate(() => Date.now()) + 1000)
  if (options.register !== false) {
    await page.evaluate(() => {
      setTimeout(() => {
        customElements.define("delayed-editor", class extends HTMLElement {})
        document.documentElement.removeAttribute("aria-busy")
      }, 0)
    })
  }
  await new Promise((resolve) => setTimeout(resolve, 150))
  assert.equal(await page.evaluate(() => !!customElements.get("delayed-editor")), false,
    "the document's zero-delay registration must initially be waiting on its frozen clock")
  return {
    page, tracker,
    async close() {
      const response = options.pending ? page.waitForResponse(`${ORIGIN}/pending`) : undefined
      releasePending()
      if (response) await (await response).finished()
      await context.close()
    },
  }
}

for (const [name, engine] of Object.entries({ chromium, firefox, webkit })) {
  describe(`${name} frozen-clock readiness owner`, () => {
    let browser: Browser
    before(async () => { browser = await engine.launch() })
    after(async () => { await browser?.close() })

    for (const busy of [false, true]) {
      test(`settled navigation releases zero-delay custom-element registration${busy ? " alongside Turbo busy" : ""}`, async () => {
        const { page, tracker, close } = await fixture(browser, { busy })
        try {
          await settle(tracker, 4000, TIME)
          assert.equal(await page.evaluate(() => !!customElements.get("delayed-editor")), true)
          const state = await page.evaluate(async () => (window as any).__parity.snapshot(false))
          assert.deepEqual(state.undefinedElements, [])
          assert.deepEqual(state.turboBusy, [])
          assert.deepEqual(tracker.errors, [])
          assert.deepEqual(tracker.networkErrors, [])
        } finally {
          await close()
        }
      })
    }

    test("absent registration remains a custom-element readiness timeout", async () => {
      const { page, tracker, close } = await fixture(browser, { register: false })
      try {
        await assert.rejects(settle(tracker, 1200, TIME), /not ready.*custom elements not defined: delayed-editor/)
        assert.equal(await page.evaluate(() => !!customElements.get("delayed-editor")), false)
        assert.deepEqual(tracker.errors, [])
        assert.deepEqual(tracker.networkErrors, [])
      } finally {
        await close()
      }
    })

    test("an unfinished real fetch prevents custom-element clock advancement", async () => {
      const { page, tracker, close } = await fixture(browser, { pending: true })
      try {
        await assert.rejects(settle(tracker, 1200, TIME), /not ready.*network: 1 in flight/)
        assert.equal(await page.evaluate(() => !!customElements.get("delayed-editor")), false)
        assert.equal(tracker.inflight.size, 1)
        assert.deepEqual(tracker.errors, [])
        assert.deepEqual(tracker.networkErrors, [])
      } finally {
        await close()
      }
    })
  })
}
