// PARITY PATCH: cancelled browser responses must release upstream connections.
import { test } from "node:test"
import assert from "node:assert/strict"
import http from "node:http"
import { once } from "node:events"
import type { AddressInfo } from "node:net"
import { startProxy } from "../proxy.ts"

test("closing an unfinished browser response closes its upstream response and leaves the proxy usable", async (t) => {
  let streaming!: http.ServerResponse
  const upstream = http.createServer((request, response) => {
    if (request.url === "/stream") {
      streaming = response
      response.writeHead(200, { "content-type": "text/plain" })
      response.write("first")
    } else {
      response.end("complete")
    }
  })
  upstream.listen(0, "127.0.0.1")
  await once(upstream, "listening")
  const proxy = await startProxy(`http://127.0.0.1:${(upstream.address() as AddressInfo).port}`)
  t.after(async () => {
    await proxy.close()
    upstream.closeAllConnections()
    await new Promise<void>((resolve) => upstream.close(() => resolve()))
  })

  const browser = await requestThrough(proxy.server, "/stream")
  await once(browser, "data")
  const upstreamClosed = once(streaming, "close", { signal: AbortSignal.timeout(2000) })
  browser.destroy()
  await upstreamClosed

  const next = await requestThrough(proxy.server, "/complete")
  const chunks: Buffer[] = []
  for await (const chunk of next) chunks.push(chunk)
  assert.equal(Buffer.concat(chunks).toString(), "complete")
})

function requestThrough(proxy: string, path: string): Promise<http.IncomingMessage> {
  return new Promise((resolve, reject) => {
    const request = http.get(proxy, { path: `http://localhost:3999${path}` }, resolve)
    request.on("error", reject)
  })
}
