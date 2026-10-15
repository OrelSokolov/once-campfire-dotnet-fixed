// Browser-only harness hook. Chromium can postpone requestfinished when a non-2xx fetch body is
// never consumed. That would freeze our clock even after the wire response completed. Draining a
// clone lets the transport settle without changing the Response the application receives.
export function installErrorBodyDrain(scope: { fetch: typeof fetch; __parityFetchDrainErrors?: string[] } = globalThis) {
  const originalFetch = scope.fetch
  const errors: string[] = []
  scope.__parityFetchDrainErrors = errors
  scope.fetch = async (...args) => {
    const response = await originalFetch.apply(scope, args)
    if (!response.ok) {
      try {
        void response.clone().arrayBuffer().catch((error) => errors.push(String(error)))
      } catch (error) {
        errors.push(String(error))
      }
    }
    return response
  }
}
