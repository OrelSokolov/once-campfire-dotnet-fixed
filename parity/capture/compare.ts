// Compares two captures of the same jobs: pixels exactly, then normalized server HTML, live DOM
// and the accessibility tree as line diffs.
import fs from "node:fs"
import { parse } from "parse5"
import path from "node:path"
import type { Allowlist, AllowEntry, Layer } from "./allowlist.ts"
import { comparePngs, diffText } from "./diff.ts"
import type { PixelResult, TextDiff } from "./diff.ts"
import { artifactBase } from "./capture.ts"
import type { CellMeta } from "./capture.ts"
import { cellId } from "./inventory.ts"
import type { Job } from "./inventory.ts"
import { maskText, normalizeNativeContractHtml } from "./normalize.ts"
import { normalizeNativeNetworkPair, verifyNativeContractReceipts } from "./native_network_contracts.ts"
import type { DocumentOwner, NativeNetworkPair, NativeNetworkProof, NetworkContractReceipt } from "./native_network_contracts.ts"

export type Status = "pass" | "fail" | "allowed" | "error"

export interface LayerResult {
  layer: Layer
  equal: boolean
  allowed?: AllowEntry
  pixels?: PixelResult
  text?: TextDiff
  nativeContract?: string // named contract allowance; never pair semantic equality
}

export interface CellComparison {
  state: string
  cell: string
  status: Status
  error?: string
  layers: LayerResult[]
  expected: { base: string; meta?: CellMeta }
  actual: { base: string; meta?: CellMeta }
  diffImage?: string
  // Pixel flake policy (run.ts): the earlier captures of a cell whose server output matched but
  // whose pixels didn't, and whether a later capture then matched.
  flaky?: boolean
  attempts?: Attempt[]
  masks?: string[] // the state's masks, as the report lists them
  networkContract?: Pick<NativeNetworkPair, "applied" | "unproven">
  nativeContractUnproven?: string[]
}

export interface Attempt {
  attempt: number
  status: Status
  differentPixels?: number
  sizeMismatch?: string
  images?: { expected: string; actual: string; diff?: string }
}

export const SERVER_OUTPUT_LAYERS: Layer[] = ["server", "live", "aria", "network", "cable"]

// A failure that re-capturing may clear: only the pixels differ (not allowed), and every
// server-output layer is identical. Anything else the servers sent differently is never retried.
export function pixelOnlyFailure(result: CellComparison): boolean {
  if (result.status !== "fail") return false
  const failing = result.layers.filter((l) => !l.equal && !l.allowed)
  return failing.length > 0 && failing.every((l) => l.layer === "pixels") && result.layers.filter((l) => SERVER_OUTPUT_LAYERS.includes(l.layer)).every((l) => l.equal)
}

export function describeMasks(masks: Job["state"]["masks"]): string[] | undefined {
  if (!masks) return undefined
  const out = [
    ...Object.entries(masks.values ?? {}).map(([name, m]) => `«${name}»: ${m.selector}${m.attribute ? ` [${m.attribute}]` : ""}${m.match ? ` ~ /${m.match}/` : ""}`),
    ...(masks.pixels ?? []).map((selector) => `pixels: ${selector}`),
  ]
  return out.length ? out : undefined
}

const TEXT_LAYERS: [Layer, string][] = [
  ["server", ".server.norm.html"], ["live", ".live.norm.html"], ["aria", ".aria.yml"], ["network", ".network.txt"], ["cable", ".cable.txt"],
]

export interface CompareOptions {
  nativeComparison?: boolean
}

export function compareJob(job: Job, runDir: string, expectedName: string, actualName: string, allowlist: Allowlist, options: CompareOptions = {}): CellComparison {
  const cell = cellId(job.cell)
  const expectedBase = artifactBase(runDir, expectedName, job)
  const actualBase = artifactBase(runDir, actualName, job)
  const result: CellComparison = {
    state: job.state.id,
    cell,
    status: "pass",
    layers: [],
    expected: { base: expectedBase, meta: readMeta(expectedBase) },
    actual: { base: actualBase, meta: readMeta(actualBase) },
    masks: describeMasks(job.state.masks),
  }
  const errors = [
    !result.expected.meta && `${expectedName}: not captured`,
    result.expected.meta?.error && `${expectedName}: ${result.expected.meta.error}`,
    !result.actual.meta && `${actualName}: not captured`,
    result.actual.meta?.error && `${actualName}: ${result.actual.meta.error}`,
  ].filter(Boolean)
  if (errors.length) {
    result.status = "error"
    result.error = errors.join("\n")
    return result
  }
  if (result.expected.meta!.kind === "fragment") {
    // A response, not a rendering: status, content type and normalized body are one text layer.
    const text = diffText(htmlText(expectedBase + ".server.norm.html", options), htmlText(actualBase + ".server.norm.html", options))
    result.layers.push({ layer: "server", equal: text.equal, text })
    return finish(result, allowlist)
  }
  if (result.expected.meta!.status !== result.actual.meta!.status) {
    result.layers.push({
      layer: "server",
      equal: false,
      text: diffText(`HTTP ${result.expected.meta!.status}`, `HTTP ${result.actual.meta!.status}`),
    })
  }

  const diffImage = path.join(runDir, "diff", actualName, job.state.id, `${cell}.png`)
  fs.rmSync(diffImage, { force: true })
  fs.mkdirSync(path.dirname(diffImage), { recursive: true })
  const pixels = comparePngs(expectedBase + ".png", actualBase + ".png", diffImage)
  if (fs.existsSync(diffImage)) result.diffImage = diffImage
  result.layers.push({ layer: "pixels", equal: pixels.equal, pixels })

  const signedOutOwner = options.nativeComparison ? signedOutRedirectOwner(expectedBase, actualBase, result.expected.meta!, result.actual.meta!, job) : undefined

  for (const [layer, ext] of TEXT_LAYERS) {
    const expected = layer === "server" || layer === "live" ? htmlText(expectedBase + ext, options) : readText(expectedBase + ext)
    let actual = layer === "server" || layer === "live" ? htmlText(actualBase + ext, options) : readText(actualBase + ext)
    if (signedOutOwner && (layer === "server" || layer === "live")) actual = signedOutDocument(expected, actual, signedOutOwner.protectedPath, signedOutOwner.actualUrl)
    let text = diffText(expected, actual)
    if (layer === "network" && options.nativeComparison) {
      try {
        const semanticEqual = ["server", "live"].every((owner) => result.layers.some((layer) => layer.layer === owner && layer.equal))
          && [expectedBase, actualBase].every((base) => [".server.norm.html", ".live.norm.html"].every((ext) => fs.existsSync(base + ext)))
        const flashOwnerVerified = semanticEqual && result.layers.some((layer) => layer.layer === "aria" && layer.equal)
        const pair = networkPair(expected, actual, expectedBase, actualBase, result.expected.meta!, result.actual.meta!, job, semanticEqual, flashOwnerVerified, signedOutOwner)
        result.networkContract = { applied: pair.applied, unproven: pair.unproven }
        text = diffText(pair.expected, pair.actual)
        if (pair.unproven.length) text = { ...text, equal: false, hunks: text.hunks + "\nUnproven native contracts:\n" + pair.unproven.map((item) => `${item.side} ${item.request}: ${item.rule}`).join("\n") }
      } catch (error) {
        text = { ...text, equal: false, hunks: text.hunks + `\nNative network contract rejected: ${String(error)}` }
      }
    }
    result.layers.push({ layer, equal: text.equal, text })
  }

  if (options.nativeComparison && ["auth/incompatible_browser", "auth/incompatible_browser/apple_messages"].includes(job.state.id)) {
    applyRetiredBrowserContract(result, job, runDir, actualName, options)
  }

  return finish(result, allowlist)
}

function finish(result: CellComparison, allowlist: Allowlist): CellComparison {
  for (const layer of result.layers) {
    if (!layer.equal) layer.allowed ??= allowlist.match(result.state, result.cell, layer.layer)
  }
  const failing = result.layers.filter((l) => !l.equal)
  result.status = !failing.length ? "pass" : failing.every((l) => l.allowed) ? "allowed" : "fail"
  // A JavaScript page error is independent of visual/server equality or layer allowances.
  // Keep every comparison layer for diagnosis; never retry or normalize the error away.
  const errors = (["expected", "actual"] as const).flatMap(side =>
    (result[side].meta?.pageErrors ?? []).map((error, index) => `${side} page error ${index + 1}: ${JSON.stringify(error)}`))
  if (errors.length) {
    result.status = "error"
    result.error = [result.error, ...errors].filter(value => value !== undefined).join("\n")
  }
  return result
}

function htmlText(file: string, options: CompareOptions): string {
  return normalizeNativeContractHtml(readText(file), options)
}

function applyRetiredBrowserContract(result: CellComparison, job: Job, runDir: string, actualName: string, options: CompareOptions) {
  // Native supports these user agents with the ordinary sign-in page. The independent same-cell
  // capture must exist and match every native rendering/response layer; a state glob is not proof.
  const controlJob = { ...job, state: { ...job.state, id: "auth/sign_in" } }
  const control = artifactBase(runDir, actualName, controlJob)
  const meta = readMeta(control)
  const actual = result.actual.base
  if (!meta || meta.error || meta.pageErrors.length || result.actual.meta!.pageErrors.length
    || result.expected.meta!.pageErrors.length || meta.kind !== "page" || result.actual.meta!.kind !== "page"
    || meta.status !== 200 || result.actual.meta!.status !== 200 || result.expected.meta!.status !== 200) return
  const files = [".png", ...TEXT_LAYERS.map(([, ext]) => ext)]
  if (files.some((ext) => !fs.existsSync(control + ext) || !fs.existsSync(actual + ext))) return
  for (const [layer, ext] of TEXT_LAYERS) {
    const expected = layer === "server" || layer === "live" ? htmlText(control + ext, options) : readText(control + ext)
    const observed = layer === "server" || layer === "live" ? htmlText(actual + ext, options) : readText(actual + ext)
    if (expected !== observed) return
  }
  // Raw security/cookie receipts are mandatory for retirement; old captures cannot manufacture
  // them from header-name-only text. Equality to a second unverified capture is insufficient.
  try {
    const controlReceipts = readReceipts(control)
    const actualReceipts = readReceipts(actual)
    if (!controlReceipts?.length || !actualReceipts?.length) {
      result.nativeContractUnproven = ["native-supported-browser-sign-in: missing native/control raw network contract receipts"]
      return
    }
    const verify = (base: string, receipts: ActorReceipt[]) => [...networkActors(readText(base + ".network.txt"))].flatMap(([actor, text]) =>
      verifyNativeContractReceipts(text, receipts.filter((receipt) => (receipt.actor ?? "main") === actor)))
    if (verify(control, controlReceipts).length || verify(actual, actualReceipts).length) return
    // The layer guard above requires exact native/control network text, and both raw
    // receipt sets were independently verified here. Rails/native representation rules
    // are asymmetric and must not be reapplied to these two native captures.
    if (!nativeSignInControlPolicy(controlReceipts) || !nativeSignInControlPolicy(actualReceipts)) return
  } catch {
    return
  }
  const diffFile = path.join(runDir, "diff", actualName, job.state.id, `${cellId(job.cell)}.sign-in-control.png`)
  fs.rmSync(diffFile, { force: true })
  if (!comparePngs(control + ".png", actual + ".png", diffFile).equal) return
  const reason = "Native retires the incompatible-browser page; status, HTML, ARIA, pixels, network and Cable match the independent same-cell native sign-in control."
  for (const layer of result.layers) {
    if (["server", "live", "aria", "pixels", "network"].includes(layer.layer) && !layer.equal) {
      layer.nativeContract = "native-supported-browser-sign-in"
      layer.allowed = { state: job.state.id, cells: result.cell, layers: [layer.layer], reason, owner: "native-contract" }
    }
  }
}

function nativeSignInControlPolicy(receipts: ActorReceipt[]): boolean {
  // This approved retirement concerns one ordinary sign-in navigation. An identical
  // mistake in two controls must not stand in for the native cache policy.
  const navigations = receipts.filter(raw => raw.request === "GET /session/new (navigation)")
  if (navigations.length !== 1) return false
  const raw = navigations[0]
  const block = raw.originalBlock
  if (raw.status !== 200 || !block || !block.startsWith("GET /session/new (navigation) → 200\n")) return false
  const value = (name: string) => block.split("\n").filter(line => line.startsWith(`  ${name}: `)).map(line => line.slice(name.length + 4))
  const exact = (name: string, required: string) => raw.headers[name] === required && JSON.stringify(value(name)) === JSON.stringify([required])
  const directives = (input: string) => input.split(",").map(value => value.trim()).sort().join(", ")
  const cache = value("cache-control")
  return exact("pragma", "no-cache") && exact("content-type", "text/html; charset=utf-8")
    && cache.length === 1 && directives(cache[0]) === "max-age=0, must-revalidate, private"
    && directives(raw.headers["cache-control"] ?? "") === "max-age=0, must-revalidate, private"
}

type ActorReceipt = NetworkContractReceipt & { actor?: string }
function readReceipts(base: string): ActorReceipt[] | undefined {
  if (!fs.existsSync(base + ".network.contracts.json")) return
  const value = JSON.parse(fs.readFileSync(base + ".network.contracts.json", "utf8"))
  if (!Array.isArray(value)) throw new Error("Network contract sidecar must contain an array")
  return value
}

function networkActors(text: string): Map<string, string> {
  if (!text.startsWith("## ")) return new Map([["main", text]])
  const actors = new Map<string, string>()
  for (const match of text.matchAll(/^## ([^\n]+)\n([\s\S]*?)(?=^## |$(?![\s\S]))/gm)) actors.set(match[1], match[2])
  return actors
}

function networkPair(expected: string, actual: string, expectedBase: string, actualBase: string, expectedMeta: CellMeta, actualMeta: CellMeta, job: Job, semanticEqual: boolean, flashOwnerVerified: boolean, signedOutOwner?: NativeNetworkProof["signedOutRedirectOwner"]): NativeNetworkPair {
  const left = networkActors(expected), right = networkActors(actual)
  const expectedReceipts = readReceipts(expectedBase), actualReceipts = readReceipts(actualBase)
  const result: NativeNetworkPair = { expected, actual, applied: [], unproven: [] }
  const multi = expected.startsWith("## ") || actual.startsWith("## ")
  for (const [actor, text] of left) {
    const observed = right.get(actor)
    if (observed === undefined) continue
    const receipts = (values?: ActorReceipt[]) => values?.filter((receipt) => (receipt.actor ?? "main") === actor)
    const pair = normalizeNativeNetworkPair(text, observed, {
      expectedReceipts: receipts(expectedReceipts), actualReceipts: receipts(actualReceipts),
      documentOwners: actor === (job.state.actors ? job.state.capture : "main") ? documentOwners(text, observed, expectedMeta, actualMeta, semanticEqual) : [],
      flashOwnerVerified,
      accountPostOwner: actor === "main" ? accountPostOwner(expectedBase, actualBase, expectedMeta, actualMeta, job, flashOwnerVerified) : undefined,
      signedOutRedirectOwner: actor === "main" && signedOutOwner ? { ...signedOutOwner, semanticEqual: flashOwnerVerified } : undefined,
    })
    left.set(actor, pair.expected); right.set(actor, pair.actual)
    result.applied.push(...pair.applied.map((receipt) => ({ ...receipt, request: multi ? `${actor}: ${receipt.request}` : receipt.request })))
    result.unproven.push(...pair.unproven.map((receipt) => ({ ...receipt, request: multi ? `${actor}: ${receipt.request}` : receipt.request })))
  }
  const render = (values: Map<string, string>) => [...values].map(([actor, text]) => (multi ? `## ${actor}\n` : "") + text).join(multi ? "\n" : "")
  result.expected = render(left); result.actual = render(right)
  return result
}

function signedOutRedirectOwner(expectedBase: string, actualBase: string, expectedMeta: CellMeta, actualMeta: CellMeta, job: Job): NativeNetworkProof["signedOutRedirectOwner"] {
  if (job.state.id !== "auth/signed_out_redirect" || expectedMeta.status !== 200 || actualMeta.status !== 200
    || expectedMeta.pageErrors.length || actualMeta.pageErrors.length || !expectedMeta.finalUrl || !actualMeta.finalUrl) return
  const expectedUrl = contractUrl(expectedMeta.finalUrl), actualUrl = contractUrl(actualMeta.finalUrl)
  if (!expectedUrl || !actualUrl) return
  const protectedPath = actualUrl.searchParams.get("returnUrl") ?? ""
  if (!/^\/rooms\/[1-9][0-9]*$/.test(protectedPath) || expectedUrl.origin !== actualUrl.origin || expectedUrl.pathname !== "/session/new" || expectedUrl.search || expectedUrl.hash
    || actualUrl.pathname !== expectedUrl.pathname || actualUrl.hash || [...actualUrl.searchParams].length !== 1) return
  if (typeof job.state.path !== "string" || ![protectedPath, "/rooms/{{rooms.designers}}"].includes(job.state.path)) return
  const protectedRequest = `GET ${protectedPath} (navigation)`
  const receipts = [readReceipts(expectedBase), readReceipts(actualBase)]
  if (receipts.some(values => values?.filter(raw => (raw.actor ?? "main") === "main" && raw.request === protectedRequest && raw.status === 302).length !== 1)) return
  const expectedFinal = receipts[0]?.filter(raw => (raw.actor ?? "main") === "main" && raw.request === "GET /session/new (navigation)" && raw.status === 200)
  const actualFinal = receipts[1]?.filter(raw => (raw.actor ?? "main") === "main" && raw.request === `GET ${actualUrl.pathname + actualUrl.search} (navigation)` && raw.status === 200)
  if (expectedFinal?.length !== 1 || actualFinal?.length !== 1 || !expectedFinal[0].bodyHash || !actualFinal[0].bodyHash) return
  return { state: "auth/signed_out_redirect", protectedPath, expectedUrl: expectedMeta.finalUrl, actualUrl: actualMeta.finalUrl, semanticEqual: false,
    expectedBodyHash: expectedFinal[0].bodyHash, actualBodyHash: actualFinal[0].bodyHash }
}

function signedOutDocument(expected: string, actual: string, protectedPath: string, finalUrl: string): string {
  if (expected.includes('name="returnUrl"')) return actual
  const lines = actual.split("\n")
  let signInForm = false
  const fields: number[] = []
  for (const [index, line] of lines.entries()) {
    if (/^\s*<form(?: |>)/.test(line)) {
      const action = / action="([^"]+)"/.exec(line)?.[1]
      const parsed = action ? contractUrl(action, finalUrl) : undefined
      signInForm = / method="post"(?: |>)/.test(line) && parsed?.pathname === "/session" && parsed.origin === contractUrl(finalUrl)?.origin && !parsed.search && !parsed.hash
    }
    if (/^\s*<\/form>/.test(line)) signInForm = false
    if (line.includes('name="returnUrl"')) {
      if (!signInForm || line.trim() !== `<input id="returnUrl" name="returnUrl" type="hidden" value="${protectedPath}">`
        && line.trim() !== `<input id="returnUrl" name="returnUrl" style type="hidden" value="${protectedPath}">`) return actual
      fields.push(index)
    }
  }
  return fields.length === 1 ? lines.filter((_line, index) => index !== fields[0]).join("\n") : actual
}

function contractUrl(value: string, base?: string): URL | undefined {
  try { return new URL(value, base) } catch { return }
}

function accountPostOwner(expectedBase: string, actualBase: string, expectedMeta: CellMeta, actualMeta: CellMeta, job: Job, semanticOwners: boolean): { expectedRequest: string; actualRequest: string; location: string } | undefined {
  if (!semanticOwners || job.state.id !== "account/edit/saved" || expectedMeta.pageErrors.length || actualMeta.pageErrors.length
    || expectedMeta.status !== 200 || actualMeta.status !== 200 || !expectedMeta.finalUrl || !actualMeta.finalUrl) return
  const paths = [expectedMeta.finalUrl, actualMeta.finalUrl].map(value => new URL(value))
  if (paths.some(value => value.pathname !== "/account/edit" || value.search || value.hash) || paths[0].href !== paths[1].href) return
  const files = [expectedBase, actualBase].map(base => base + ".server.html")
  if (files.some(file => !fs.existsSync(file))) return
  const actions = files.map(file => {
    const found: { action: string; method: string; nameFields: number }[] = []
    const walk = (node: any) => {
      if (node.tagName === "form") {
        const attrs: Record<string,string> = Object.fromEntries(node.attrs.map((attr: {name: string; value: string}) => [attr.name, attr.value]))
        if (/^\/account(?:\.[1-9][0-9]*)?$/.test(attrs.action ?? "")) {
          const methods: string[] = []
          let nameFields = 0
          const inputs = (child: any) => {
            if (child.tagName === "input") {
              const values: Record<string,string> = Object.fromEntries(child.attrs.map((attr: {name: string; value: string}) => [attr.name, attr.value]))
              if (values.name === "_method") methods.push(values.type === "hidden" ? values.value : "invalid")
              if (values.id === "account_name" && values.name === "account[name]" && values.type === "text") nameFields++
            }
            for (const nested of child.childNodes ?? []) inputs(nested)
          }
          for (const child of node.childNodes ?? []) inputs(child)
          if (attrs.method !== "post" || attrs["data-controller"] !== "form" || methods.length !== 1 || !["patch", "put"].includes(methods[0])) { found.push({ action: "invalid", method: "invalid", nameFields }); return }
          found.push({ action: attrs.action, method: methods[0], nameFields })
        }
      }
      for (const child of node.childNodes ?? []) walk(child)
    }
    walk(parse(fs.readFileSync(file, "utf8")))
    return found
  })
  const expected = actions[0], actual = actions[1]
  if (!expected.length || expected.length !== actual.length || !/^\/account\.[1-9][0-9]*$/.test(expected[0].action)
    || !expected.every(form => form.action === expected[0].action) || !actual.every(form => form.action === "/account")
    || !expected.every((form, index) => form.method === actual[index].method && form.nameFields === actual[index].nameFields)
    || expected.reduce((total, form) => total + form.nameFields, 0) !== 1
    || !expected.some(form => form.nameFields === 1 && form.method === "patch")) return
  return { expectedRequest: "POST " + expected[0].action, actualRequest: "POST /account", location: paths[0].href }
}

function documentOwners(expected: string, actual: string, expectedMeta: CellMeta, actualMeta: CellMeta, semanticEqual: boolean): DocumentOwner[] {
  if (!semanticEqual || expectedMeta.status !== actualMeta.status || !expectedMeta.finalUrl || !actualMeta.finalUrl) return []
  const finalPath = (url: string) => { const parsed = new URL(url); return maskText(parsed.pathname + parsed.search, {}) }
  if (finalPath(expectedMeta.finalUrl) !== finalPath(actualMeta.finalUrl)) return []
  const final = finalPath(expectedMeta.finalUrl)
  const candidates = (text: string) => [...text.matchAll(/^([A-Z]+ [^\n]+ \(navigation\)) → (\d{3})\n([\s\S]*?)(?=^[A-Z]+ [^\n]+ → |^## |$(?![\s\S]))/gm)]
    .filter((match) => match[1].replace(/^[A-Z]+ /, "").replace(/ \(navigation\)$/, "") === final && Number(match[2]) === expectedMeta.status)
  const left = candidates(expected), right = candidates(actual)
  if (left.length !== 1 || right.length !== 1 || left[0][1] !== right[0][1]) return []
  const hash = (body: string) => /^  body: sha256:([0-9a-f]{16})$/m.exec(body)?.[1]
  const expectedBodyHash = hash(left[0][3]), actualBodyHash = hash(right[0][3])
  return expectedBodyHash && actualBodyHash ? [{ request: left[0][1], status: expectedMeta.status!, semanticEqual: true, expectedBodyHash, actualBodyHash }] : []
}

function readMeta(base: string): CellMeta | undefined {
  try {
    return JSON.parse(fs.readFileSync(base + ".json", "utf8"))
  } catch {
    return undefined
  }
}

function readText(file: string): string {
  try {
    return fs.readFileSync(file, "utf8")
  } catch {
    return "(missing)\n"
  }
}
