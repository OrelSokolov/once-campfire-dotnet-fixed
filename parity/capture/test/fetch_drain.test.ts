import { test } from "node:test"
import assert from "node:assert/strict"
import { setImmediate } from "node:timers/promises"
import { installErrorBodyDrain } from "../fetch_drain.ts"

test("error-body drain preserves the exact response, status, headers and original readable body", async () => {
  const response = new Response("error detail", { status: 404, headers: { "content-type": "text/html", "x-control": "retained" } })
  const scope = { fetch: (async () => response) as typeof fetch, __parityFetchDrainErrors: [] as string[] }
  installErrorBodyDrain(scope)
  const returned = await scope.fetch("http://localhost/removed")
  await setImmediate()
  assert.equal(returned, response)
  assert.equal(returned.status, 404)
  assert.equal(returned.headers.get("x-control"), "retained")
  assert.equal(returned.bodyUsed, false)
  assert.equal(await returned.text(), "error detail")
  assert.deepEqual(scope.__parityFetchDrainErrors, [])
})

test("successful bodies are not cloned and fetch rejection is not swallowed", async () => {
  const response = new Response("success")
  response.clone = () => assert.fail("successful response was cloned")
  const scope = { fetch: (async () => response) as typeof fetch }
  installErrorBodyDrain(scope)
  assert.equal(await scope.fetch("http://localhost/success"), response)
  assert.equal(await response.text(), "success")
  const failure = new Error("transport failure")
  const failed = { fetch: (async () => { throw failure }) as typeof fetch }
  installErrorBodyDrain(failed)
  await assert.rejects(failed.fetch("http://localhost/broken"), (error) => error === failure)
})

test("clone and streaming failures remain recorded readiness failures", async () => {
  for (const streamFailure of [false, true]) {
    const response = { ok: false, clone: () => {
      if (!streamFailure) throw new Error("clone failed")
      return { arrayBuffer: async () => { throw new Error("stream failed") } }
    } } as unknown as Response
    const scope = { fetch: (async () => response) as typeof fetch, __parityFetchDrainErrors: [] as string[] }
    installErrorBodyDrain(scope)
    assert.equal(await scope.fetch("http://localhost/error"), response)
    await setImmediate()
    assert.deepEqual(scope.__parityFetchDrainErrors, [streamFailure ? "Error: stream failed" : "Error: clone failed"])
  }
})
