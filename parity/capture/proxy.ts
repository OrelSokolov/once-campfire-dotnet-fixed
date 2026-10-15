// Both servers are shown to the browser under one shared origin (default http://localhost:3999),
// because join and transfer links, QR codes and bot curl commands embed the request's host
// (parity/seeds/README.md). Each target gets its own forward proxy; a browser context for that
// target uses it, and the proxy sends everything, including the Action Cable WebSocket (tunneled
// with CONNECT, or an absolute-URI upgrade), to the real server with the Host header untouched.
// Connections to the server go through forward.ts when the capture runs without a network.
import http from "node:http"
import net from "node:net"
import type { AddressInfo } from "node:net"
import { connectUpstream } from "./forward.ts"
import { WireReceipts } from "./wire_receipts.ts"

export const DEFAULT_ORIGIN = "http://localhost:3999"

export interface Proxy {
  server: string // http://127.0.0.1:<port>, for BrowserContextOptions.proxy
  wireHeaders: WireReceipts
  close: () => Promise<void>
}

const HOP_BY_HOP = new Set(["proxy-connection", "proxy-authorization", "keep-alive", "connection", "transfer-encoding", "te", "trailer", "upgrade"])

export async function startProxy(upstreamUrl: string): Promise<Proxy> {
  const upstream = new URL(upstreamUrl)
  const host = upstream.hostname
  const port = Number(upstream.port || 80)
  const agent = new http.Agent({ keepAlive: true, maxSockets: 64 })
  const wireHeaders = new WireReceipts()
  // Through forward.ts when the capture has no network of its own.
  ;(agent as any).createConnection = (_options: unknown, callback: (error: Error | null, socket: net.Socket) => void) => {
    const socket = connectUpstream(host, port, () => callback(null, socket))
    socket.once("error", (error) => callback(error, socket))
  }
  const sockets = new Set<net.Socket>()

  const server = http.createServer((req, res) => {
    const url = new URL(req.url ?? "/", "http://placeholder")
    const headers: http.OutgoingHttpHeaders = {}
    for (const [name, value] of Object.entries(req.headers)) if (!HOP_BY_HOP.has(name)) headers[name] = value
    const forward = http.request({ host, port, agent, method: req.method, path: url.pathname + url.search, headers }, (response) => {
      // PARITY PATCH: retain actual redirect headers when WebKit's protocol receipt omits them.
      if ((response.statusCode ?? 0) >= 300 && (response.statusCode ?? 0) < 400) wireHeaders.record({ method: req.method ?? "GET", url: url.href, status: response.statusCode!, location: response.headers.location ?? "" }, Object.fromEntries(Object.entries(response.headers).map(([name,value]) => [name, Array.isArray(value) ? value.join(", ") : value ?? ""])))
      const raw: string[] = []
      for (let i = 0; i < response.rawHeaders.length; i += 2) {
        if (!HOP_BY_HOP.has(response.rawHeaders[i].toLowerCase())) raw.push(response.rawHeaders[i], response.rawHeaders[i + 1])
      }
      res.writeHead(response.statusCode ?? 502, response.statusMessage, raw)
      // PARITY PATCH: destruction after a browser cancellation must not become an unhandled stream error.
      response.on("error", (error) => res.destroy(error))
      response.pipe(res)
    })
    // PARITY PATCH: abandoned bodies otherwise occupy the HTTP client's bounded upstream pool indefinitely.
    req.on("aborted", () => forward.destroy())
    res.on("close", () => {
      if (!res.writableFinished) forward.destroy()
    })
    forward.on("error", (error) => {
      if (res.destroyed) return // PARITY PATCH: the browser has already discarded this response.
      if (!res.headersSent) res.writeHead(502, { "content-type": "text/plain" })
      res.end(`parity proxy: ${error.message}`)
    })
    req.pipe(forward)
  })

  // CONNECT host:port — Chromium tunnels WebSockets through an HTTP proxy this way.
  server.on("connect", (req, client: net.Socket, head: Buffer) => {
    const socket = connectUpstream(host, port, () => {
      client.write("HTTP/1.1 200 Connection Established\r\n\r\n")
      if (head.length) socket.write(head)
      socket.pipe(client)
      client.pipe(socket)
    })
    track(socket, client)
  })

  // GET ws://… with Upgrade — how Firefox and WebKit may send WebSockets to a plain proxy.
  server.on("upgrade", (req, client: net.Socket, head: Buffer) => {
    const url = new URL(req.url ?? "/", "http://placeholder")
    const socket = connectUpstream(host, port, () => {
      const lines = [`${req.method} ${url.pathname}${url.search} HTTP/1.1`]
      for (let i = 0; i < req.rawHeaders.length; i += 2) {
        const name = req.rawHeaders[i].toLowerCase()
        if (name !== "proxy-connection" && name !== "proxy-authorization") lines.push(`${req.rawHeaders[i]}: ${req.rawHeaders[i + 1]}`)
      }
      socket.write(lines.join("\r\n") + "\r\n\r\n")
      if (head.length) socket.write(head)
      socket.pipe(client)
      client.pipe(socket)
    })
    track(socket, client)
  })

  function track(...pair: net.Socket[]) {
    for (const s of pair) {
      sockets.add(s)
      s.on("error", () => pair.forEach((p) => p.destroy()))
      s.on("close", () => {
        sockets.delete(s)
        pair.forEach((p) => p.destroy())
      })
    }
  }

  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve))
  const { port: proxyPort } = server.address() as AddressInfo
  return {
    server: `http://127.0.0.1:${proxyPort}`,
    wireHeaders,
    close: async () => {
      for (const s of sockets) s.destroy()
      agent.destroy()
      server.closeAllConnections()
      await new Promise<void>((resolve) => server.close(() => resolve()))
    },
  }
}
