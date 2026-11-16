import { useState, type FormEvent } from 'react'
import { api, ApiError } from '../api'
import { Keys, Window } from '../components/ui'

export function Login({ onDone }: { onDone: () => void }) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      if (mode === 'register') await api.register(email, password)
      await api.login(email, password)
      onDone()
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401
        ? 'Email hoặc mật khẩu không đúng.'
        : err instanceof Error ? err.message : 'Có lỗi xảy ra.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="scene" style={{ justifyContent: 'center' }}>
      <div style={{ textAlign: 'center', marginBottom: 34 }}>
        <div className="pixel" style={{ fontSize: 72, lineHeight: 1, color: 'var(--accent)', textShadow: '0 0 24px rgba(240,163,58,0.35)' }}>
          Dragon<span style={{ color: 'var(--text)' }}>&amp;</span>Fit
        </div>
        <div className="dim" style={{ marginTop: 8 }}>Mỗi buổi tập là một trận chiến. Mỗi ngày là một chương phiêu lưu.</div>
      </div>

      <Window title={mode === 'login' ? 'Tiếp tục hành trình' : 'Ghi danh chiến binh'} style={{ width: 400 }}>
        <form onSubmit={submit}>
          <div className="field">
            <label htmlFor="email">Email</label>
            <input id="email" className="input" type="email" autoComplete="email" required value={email} onChange={e => setEmail(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="pw">Mật khẩu</label>
            <input id="pw" className="input" type="password" minLength={8} required value={password}
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'} onChange={e => setPassword(e.target.value)} />
            {mode === 'register' && <span className="help">Ít nhất 8 ký tự, có cả chữ thường và số.</span>}
          </div>
          {error && <p className="field err" role="alert">{error}</p>}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginTop: 6 }}>
            <button className="btn" disabled={busy}>{mode === 'login' ? 'Vào game' : 'Tạo tài khoản'}</button>
            <button type="button" className="btn ghost small" disabled title="Cần Google Client ID — sẽ bật ở bước cấu hình">Đăng nhập bằng Google (sắp có)</button>
          </div>
          <p style={{ textAlign: 'center', marginTop: 18, marginBottom: 0 }} className="dim">
            {mode === 'login' ? 'Chưa có tài khoản? ' : 'Đã có tài khoản? '}
            <button type="button" onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); setError(null) }}
              style={{ background: 'none', border: 'none', color: 'var(--accent)', cursor: 'pointer', padding: 0 }}>
              {mode === 'login' ? 'Ghi danh' : 'Đăng nhập'}
            </button>
          </p>
        </form>
      </Window>
      <Keys hints={[['Enter', 'Xác nhận'], ['Tab', 'Chuyển ô']]} />
    </div>
  )
}
