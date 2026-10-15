// Animated images (the /play sounds' WebPs in reference/app/assets/images/sounds) advance with
// wall-clock time, which no Web Animations API pauses and no engine setting freezes in all three
// browsers. The harness serves them reduced to their first frame, identically for both servers,
// so the capture shows a deterministic frame. Their bytes are compared elsewhere (assets).
import type { BrowserContext, Route } from "playwright"

export async function freezeAnimatedImages(context: BrowserContext, origin: string, timeoutMs = 30_000) {
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new Error("Image route timeout must be positive and finite")
  type Failure = { id?: number; url?: string; stage: string; message: string; stack?: string }
  const failures: Failure[] = []
  const active = new Map<number, { url: string; stage: string; done: Promise<void> }>()
  const patterns = [`${origin}/**/*.webp`, `${origin}/**/*.gif`]
  let sequence = 0
  let retirement: Promise<void> | undefined
  const record = (stage: string, error: unknown, id?: number, url?: string) => {
    failures.push({ id, url, stage, message: error instanceof Error ? error.message : String(error), stack: error instanceof Error ? error.stack : undefined })
  }
  const handler = async (route: Route) => {
    const id = ++sequence, url = route.request().url()
    let resolve!: () => void
    const operation = { url, stage: "fetch", done: new Promise<void>(done => { resolve = done }) }
    active.set(id, operation)
    try {
      const response = await route.fetch({ timeout: timeoutMs })
      operation.stage = "body"
      const body = await response.body()
      operation.stage = "freeze"
      const frozen = firstFrameWebp(body) ?? firstFrameGif(body)
      operation.stage = "fulfill"
      await route.fulfill({ response, body: frozen ?? body })
    } catch (error) {
      // This callback's rejection otherwise escapes captureCell's awaited catch. Transfer
      // the actual failure to its capture holder; do not mark the image/cell successful.
      record(operation.stage, error, id, url)
      try { await route.abort("failed") }
      catch (abortError) { record("abort-after-failure", abortError, id, url) }
    } finally {
      active.delete(id)
      resolve()
    }
  }
  const bounded = async (done: Promise<void>, stage: string) => {
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      const completed = await Promise.race([
        done.then(() => true),
        new Promise<boolean>(resolve => { timer = setTimeout(() => resolve(false), timeoutMs) }),
      ])
      if (!completed) record(stage, new Error(`Image route ${stage} timed out after ${timeoutMs}ms; pending ${JSON.stringify([...active].map(([id, op]) => ({ id, url: op.url, stage: op.stage })))}`))
    } finally { if (timer !== undefined) clearTimeout(timer) }
  }
  await context.route(patterns[0], handler)
  await context.route(patterns[1], handler)
  return {
    retire: async () => {
      retirement ??= (async () => {
        for (const pattern of patterns) {
          // Removing the final registration makes Playwright resume paused browser
          // requests. Keep interception alive until owned fetch/body/fulfill work ends.
          // Loop rather than taking one snapshot: new callbacks admitted while draining
          // belong to this capture too. No await separates the empty check from unroute.
          while (active.size) await Promise.all([...active.values()].map(operation => operation.done))
          try { await context.unroute(pattern, handler) }
          catch (error) { record("unregister", error, undefined, pattern) }
        }
        // Calls admitted during the first registration's removal remain tracked.
        while (active.size) await Promise.all([...active.values()].map(operation => operation.done))
      })()
      await bounded(retirement, "retirement")
    },
    finish: async () => {
      // Only after context.close: cancelled operations are still owned, including their
      // abort failure. An unresolved callback remains an explicit failure, never a pass.
      await bounded(Promise.all([retirement ?? Promise.resolve(), ...[...active.values()].map(operation => operation.done)]).then(() => {}), "post-close-drain")
    },
    snapshot: () => failures.map(failure => ({ ...failure })),
  }
}

// RIFF WebP: keep every chunk except ANMF frames after the first, and fix the RIFF size.
export function firstFrameWebp(buf: Buffer): Buffer | undefined {
  if (buf.length < 12 || buf.toString("ascii", 0, 4) !== "RIFF" || buf.toString("ascii", 8, 12) !== "WEBP") return
  const chunks: Buffer[] = []
  let frames = 0
  for (let offset = 12; offset + 8 <= buf.length;) {
    const size = buf.readUInt32LE(offset + 4)
    const end = offset + 8 + size + (size & 1)
    const fourcc = buf.toString("ascii", offset, offset + 4)
    if (fourcc === "ANMF") frames++
    if (fourcc !== "ANMF" || frames === 1) chunks.push(buf.subarray(offset, Math.min(end, buf.length)))
    offset = end
  }
  if (frames < 2) return
  const payload = Buffer.concat(chunks)
  const header = Buffer.alloc(12)
  header.write("RIFF", 0, "ascii")
  header.writeUInt32LE(payload.length + 4, 4)
  header.write("WEBP", 8, "ascii")
  return Buffer.concat([header, payload])
}

// GIF89a: keep everything up to the end of the first image, then the trailer.
export function firstFrameGif(buf: Buffer): Buffer | undefined {
  if (buf.length < 13 || !/^GIF8[79]a$/.test(buf.toString("ascii", 0, 6))) return
  const packed = buf[10]
  let offset = 13 + (packed & 0x80 ? 3 * (1 << ((packed & 0x07) + 1)) : 0)
  const skipSubBlocks = () => {
    while (offset < buf.length && buf[offset] !== 0) offset += buf[offset] + 1
    offset++
  }
  let images = 0
  let firstEnd = 0
  while (offset < buf.length) {
    const introducer = buf[offset]
    if (introducer === 0x21) { // extension: label, sub-blocks
      offset += 2
      skipSubBlocks()
    } else if (introducer === 0x2c) { // image descriptor, local color table, LZW min size, data
      const localPacked = buf[offset + 9]
      offset += 10 + (localPacked & 0x80 ? 3 * (1 << ((localPacked & 0x07) + 1)) : 0) + 1
      skipSubBlocks()
      images++
      if (images === 1) firstEnd = offset
    } else {
      break // 0x3b trailer, or garbage
    }
  }
  if (images < 2) return
  return Buffer.concat([buf.subarray(0, firstEnd), Buffer.from([0x3b])])
}
