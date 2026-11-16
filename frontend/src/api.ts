// Thin client for the Dragon&Fit API. In development Vite proxies /api to the .NET server.
const BASE = import.meta.env.VITE_API_BASE ?? ''

const TOKEN_KEY = 'df.tokens'

type Tokens = { accessToken: string; refreshToken: string; expiresAt: number }

function loadTokens(): Tokens | null {
  try {
    const raw = localStorage.getItem(TOKEN_KEY)
    return raw ? (JSON.parse(raw) as Tokens) : null
  } catch {
    return null
  }
}

function saveTokens(t: Tokens | null) {
  try {
    if (t) localStorage.setItem(TOKEN_KEY, JSON.stringify(t))
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    /* storage blocked: the session simply won't survive a reload */
  }
}

let tokens = loadTokens()

export class ApiError extends Error {
  status: number
  fields?: Record<string, string>
  constructor(status: number, message: string, fields?: Record<string, string>) {
    super(message)
    this.status = status
    this.fields = fields
  }
}

export const isLoggedIn = () => tokens !== null

export function logout() {
  tokens = null
  saveTokens(null)
}

function storeLogin(body: { accessToken: string; refreshToken: string; expiresIn: number }) {
  tokens = { accessToken: body.accessToken, refreshToken: body.refreshToken, expiresAt: Date.now() + body.expiresIn * 1000 }
  saveTokens(tokens)
}

async function refresh(): Promise<boolean> {
  if (!tokens) return false
  const res = await fetch(`${BASE}/api/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: tokens.refreshToken }),
  })
  if (!res.ok) {
    logout()
    return false
  }
  storeLogin(await res.json())
  return true
}

async function request<T>(method: string, path: string, body?: unknown, retry = true): Promise<T> {
  if (tokens && tokens.expiresAt - Date.now() < 30_000) await refresh()
  const headers: Record<string, string> = {}
  if (tokens) headers.Authorization = `Bearer ${tokens.accessToken}`
  let payload: BodyInit | undefined
  if (body instanceof FormData) payload = body
  else if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
    payload = JSON.stringify(body)
  }
  const res = await fetch(`${BASE}${path}`, { method, headers, body: payload })
  if (res.status === 401 && retry && (await refresh())) return request<T>(method, path, body, false)
  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  if (!res.ok) {
    if (res.status === 401) logout()
    if (res.status === 410) window.dispatchEvent(new Event('df:dead'))
    const msg = data?.error ?? (data?.errors ? Object.values(data.errors).flat().join(' ') : null) ?? `Lỗi ${res.status}`
    throw new ApiError(res.status, msg, data?.fields)
  }
  return data as T
}

export const api = {
  async register(email: string, password: string) {
    await request('POST', '/api/auth/register', { email, password })
  },
  async login(email: string, password: string) {
    storeLogin(await request('POST', '/api/auth/login', { email, password }))
  },
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  del: (path: string) => request<null>('DELETE', path),
  /** Fetch a private file (proof photo) with the bearer token and return an object URL. */
  async blobUrl(path: string) {
    const res = await fetch(`${BASE}${path}`, { headers: tokens ? { Authorization: `Bearer ${tokens.accessToken}` } : {} })
    if (!res.ok) throw new ApiError(res.status, 'Không tải được ảnh.')
    return URL.createObjectURL(await res.blob())
  },
}

/** Shrinks a photo in the browser to at most 1280 px and WebP before upload (plan B2: ~300 KB). */
export async function compressPhoto(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file)
  const scale = Math.min(1, 1280 / Math.max(bitmap.width, bitmap.height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(bitmap.width * scale)
  canvas.height = Math.round(bitmap.height * scale)
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  for (const quality of [0.75, 0.6, 0.45]) {
    const blob = await new Promise<Blob | null>(r => canvas.toBlob(r, 'image/webp', quality))
    if (blob && blob.size <= 300_000) return blob
    if (blob && quality === 0.45) return blob
  }
  throw new Error('Không nén được ảnh.')
}
