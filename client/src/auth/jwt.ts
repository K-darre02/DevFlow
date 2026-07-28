// Minimal, dependency-free JWT payload decode — just base64url + JSON, no
// signature verification (the server already verified it; this is purely
// for the client to read its own `exp` claim, not a trust boundary).

interface JwtPayload {
  exp?: number
}

function decodePayload(token: string): JwtPayload | null {
  try {
    const [, payload] = token.split('.')
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/')
    return JSON.parse(atob(base64)) as JwtPayload
  } catch {
    return null
  }
}

export function isTokenExpired(token: string): boolean {
  const payload = decodePayload(token)
  if (!payload?.exp) {
    return true
  }
  return Date.now() >= payload.exp * 1000
}
