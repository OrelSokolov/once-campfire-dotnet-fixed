// Optional fixture decoder. The capture never guesses an opaque token's record from
// its position in the page. Unknown or tampered tokens stay visible in comparison output.
import http from "node:http"
import { connectUpstream } from "./forward.ts"

export function protectedTokenResolver(endpoint: string, target: string, descriptions: Map<string,string>): (text: string) => Promise<void> {
  const url = new URL(endpoint)
  if (url.protocol !== "http:" || !["localhost", "127.0.0.1"].includes(url.hostname)) throw new Error("Native token resolver must be a private loopback HTTP service")
  const attempted = new Set<string>()
  return async (text) => {
    const decodedQr = [...text.matchAll(/\/qr_code\/([A-Za-z0-9_=-]{8,})/g)].map((match) => Buffer.from(match[1], "base64url").toString("utf8")).join("\n")
    const tokens = [...new Set((text + "\n" + decodedQr).match(/\bCfDJ8[A-Za-z0-9_-]{40,}/g) ?? [])].filter((token) => !attempted.has(token))
    if (!tokens.length) return
    const payload = JSON.stringify({ port: new URL(target).port, tokens })
    const result = await new Promise<Record<string,string>>((resolve, reject) => {
      const request = http.request({ hostname: url.hostname, port: Number(url.port), path: url.pathname, method: "POST", headers: { "content-type": "application/json", "content-length": Buffer.byteLength(payload) }, createConnection: (_options, connected) => {
        // Give the Unix forwarder its destination before HTTP can write request bytes. Returning
        // the connecting socket immediately lets http.request flush before that framing line.
        const socket = connectUpstream(url.hostname, Number(url.port), () => {
          socket.off("error", failed)
          connected!(null, socket)
        })
        const failed = (error: Error) => connected!(error, socket)
        socket.once("error", failed)
        return undefined as unknown as ReturnType<typeof connectUpstream>
      } }, (response) => {
        const parts: Buffer[] = []
        response.on("data", (part) => parts.push(part))
        response.on("error", reject)
        response.on("end", () => {
          if (response.statusCode !== 200) return reject(new Error(`Private token resolver returned ${response.statusCode}`))
          try { resolve(JSON.parse(Buffer.concat(parts).toString("utf8"))) } catch (error) { reject(error) }
        })
      })
      request.setTimeout(30_000, () => request.destroy(new Error("Private token resolver timed out")))
      request.on("error", reject)
      request.end(payload)
    })
    for (const token of tokens) {
      attempted.add(token)
      const description = result[token]
      if (description !== undefined) {
        if (typeof description !== "string" || !/^«(?:signed_id|sgid|stream):.+»$/.test(description)) throw new Error("Private token resolver returned an invalid semantic description")
        descriptions.set(token, description)
      }
    }
  }
}
