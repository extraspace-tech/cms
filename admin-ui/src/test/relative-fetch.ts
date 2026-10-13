// Node's Request/fetch only accept absolute URLs, but jsdom has a base URL
// (location.href). Resolve relative URLs against it so `new Request('/x')`
// (what openapi-fetch does with baseUrl '/') works in tests like in a browser.
const NativeRequest = globalThis.Request

class RelativeRequest extends NativeRequest {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    super(typeof input === 'string' ? new URL(input, globalThis.location.href).href : input, init)
  }
}

globalThis.Request = RelativeRequest as typeof Request
