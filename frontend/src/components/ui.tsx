import { useEffect, useState, type CSSProperties, type ReactNode } from 'react'

export function Window({ title, children, className = '', style }: { title?: string; children: ReactNode; className?: string; style?: CSSProperties }) {
  return (
    <section className={`window ${className}`} style={style}>
      {title && <div className="window-title">{title}</div>}
      {children}
      <span className="corner-b" aria-hidden />
    </section>
  )
}

export function Bar({ kind, value, max, label }: { kind: 'xp' | 'hp' | 'mp'; value: number; max: number; label?: string }) {
  const pct = max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 100
  return (
    <div>
      {label && (
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12.5 }} className="dim">
          <span>{label}</span>
          <span className="pixel" style={{ fontSize: 17, color: 'var(--text)' }}>{max > 0 ? `${value} / ${max}` : 'MAX'}</span>
        </div>
      )}
      <div className={`bar ${kind}`} role="progressbar" aria-valuenow={value} aria-valuemax={max}>
        <span style={{ width: `${pct}%` }} />
      </div>
    </div>
  )
}

export function Keys({ hints }: { hints: [string, string][] }) {
  return (
    <div className="keys">
      {hints.map(([k, label]) => (
        <span key={k}><span className="kbd">{k}</span>{label}</span>
      ))}
    </div>
  )
}

export type ToastMsg = { text: string; error?: boolean } | null

export function Toast({ msg, onDone }: { msg: ToastMsg; onDone: () => void }) {
  useEffect(() => {
    if (!msg) return
    const t = setTimeout(onDone, 3200)
    return () => clearTimeout(t)
  }, [msg, onDone])
  if (!msg) return null
  return <div className={`toast ${msg.error ? 'error' : ''}`} role="status">{msg.text}</div>
}

/** Simple line icons drawn in-house (white strokes, like the reference's attribute icons). */
export function Icon({ name, size = 22 }: { name: string; size?: number }) {
  const p: Record<string, ReactNode> = {
    STR: <path d="M6 14l4-4 2 2 4-4 2 2-6 6-2-2-2 2zM4 18h16" />,
    VIT: <path d="M12 20s-7-4.5-7-10a4 4 0 0 1 7-2.5A4 4 0 0 1 19 10c0 5.5-7 10-7 10z" />,
    END: <path d="M13 3L6 13h5l-1 8 7-10h-5z" />,
    DIS: <path d="M12 3l7 3v5c0 5-3.5 8-7 10-3.5-2-7-5-7-10V6z M9 12l2 2 4-4" />,
    WIS: <path d="M4 6h7v13H4zM13 6h7v13h-7zM11 8h2" />,
    hp: <path d="M12 20s-7-4.5-7-10a4 4 0 0 1 7-2.5A4 4 0 0 1 19 10c0 5.5-7 10-7 10z" />,
    gem: <path d="M7 4h10l4 5-9 11L3 9zM3 9h18M9 4l3 16M15 4l-3 16" />,
    weight: <path d="M6 7h12l2 13H4zM9 7a3 3 0 0 1 6 0" />,
    waist: <path d="M4 12h16M7 8l-3 4 3 4M17 8l3 4-3 4" />,
    steps: <path d="M8 4c2 0 3 2 3 5s-1 5-3 5-3-2-3-5 1-5 3-5zM16 10c2 0 3 2 3 5s-1 5-3 5-3-2-3-5 1-5 3-5z" />,
    kcal: <path d="M12 3c3 4 6 6 6 11a6 6 0 0 1-12 0c0-3 2-5 3-7 1 2 2 3 3 3 0-3-1-5 0-7z" />,
    protein: <path d="M6 18l12-12M14 4l6 6M4 14l6 6" />,
    streak: <path d="M5 12h4l2-6 2 12 2-6h4" />,
  }
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      {p[name] ?? <circle cx="12" cy="12" r="6" />}
    </svg>
  )
}

export function useKey(key: string, handler: () => void, enabled = true) {
  useEffect(() => {
    if (!enabled) return
    const fn = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement)?.tagName
      if (key !== 'Escape' && (tag === 'INPUT' || tag === 'TEXTAREA')) return
      if (e.key === key) { e.preventDefault(); handler() }
    }
    window.addEventListener('keydown', fn)
    return () => window.removeEventListener('keydown', fn)
  }, [key, handler, enabled])
}

export function useToast() {
  const [msg, setMsg] = useState<ToastMsg>(null)
  return { msg, show: (text: string, error = false) => setMsg({ text, error }), clear: () => setMsg(null) }
}
