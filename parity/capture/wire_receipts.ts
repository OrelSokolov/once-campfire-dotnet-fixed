// WebKit omits some wire headers from redirect receipts. Keep the proxy's actual headers
// as independent evidence, scoped to this target and to responses after a capture starts.
export interface WireRequest { method: string; url: string; status: number; location: string }
export type WireVerifier = (request: WireRequest) => Readonly<Record<string, string>>

export class WireReceipts {
  private sequence = 0
  private receipts: { sequence: number; request: WireRequest; headers: Readonly<Record<string,string>> }[] = []

  record(request: WireRequest, headers: Record<string,string>): void {
    this.receipts.push({ sequence: ++this.sequence, request: { ...request }, headers: Object.freeze({ ...headers }) })
    if (this.receipts.length > 4096) this.receipts.shift()
  }

  verifier(): WireVerifier {
    const watermark = this.sequence
    const claimed = new Set<number>()
    return (request) => {
      const matches = this.receipts.filter((receipt) => receipt.sequence > watermark && !claimed.has(receipt.sequence) && Object.entries(request).every(([key,value]) => receipt.request[key as keyof WireRequest] === value))
      if (matches.length !== 1) throw new Error(`Redirect wire evidence must have one matching receipt, found ${matches.length}`)
      claimed.add(matches[0].sequence)
      return matches[0].headers
    }
  }
}
