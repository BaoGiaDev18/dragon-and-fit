import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { AvatarPicker } from '../components/AvatarPicker'
import { AvatarSprite } from '../components/AvatarSprite'
import { Window } from '../components/ui'
import { sfx } from '../sound'
import type { AvatarInfo } from '../types'

type Forge = {
  avatar: string; activeTitle: string | null; evolutionStage: number; level: number; customSprite: string | null
  avatars: AvatarInfo[]
  titles: { code: string; name: string; source: string; minLevel: number | null }[]
  lockedTitles: { name: string; minLevel: number }[]
  template: { width: number; height: number; frame: number; columns: number; rows: number }
}

const STAGE_LEVELS = [1, 20, 40, 60, 80, 99]

/** Draws the blank 256×384 template: 4 idle frames per row, one row per evolution stage. */
function downloadTemplate() {
  const c = document.createElement('canvas')
  c.width = 256
  c.height = 384
  const g = c.getContext('2d')!
  g.strokeStyle = 'rgba(240,163,58,0.55)'
  g.fillStyle = 'rgba(240,163,58,0.55)'
  g.font = '9px monospace'
  for (let r = 0; r < 6; r++) {
    for (let col = 0; col < 4; col++) {
      g.strokeRect(col * 64 + 0.5, r * 64 + 0.5, 63, 63)
      g.fillText(`Lv${STAGE_LEVELS[r]} f${col + 1}`, col * 64 + 3, r * 64 + 10)
    }
  }
  c.toBlob(b => {
    if (!b) return
    const a = document.createElement('a')
    a.href = URL.createObjectURL(b)
    a.download = 'dragon-fit-sprite-template.png'
    a.click()
    URL.revokeObjectURL(a.href)
  }, 'image/png')
}

/** Character forge (plan B4): avatar, title and a custom sprite sheet. */
export function ForgeScreen({ onChanged, toast }: { onChanged: () => void; toast: (t: string, err?: boolean) => void }) {
  const [forge, setForge] = useState<Forge | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => api.get<Forge>('/api/forge').then(setForge), [])
  useEffect(() => { load().catch(e => toast(e.message, true)) }, [load, toast])

  async function save(patch: { avatar?: string; titleCode?: string }) {
    setBusy(true)
    try {
      await api.post('/api/forge', patch)
      sfx('confirm')
      await load()
      onChanged()
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      setBusy(false)
    }
  }

  async function upload(file: File | undefined) {
    if (!file) return
    setError(null)
    // Same check as the server, before uploading.
    const img = await createImageBitmap(file).catch(() => null)
    if (!img || file.type !== 'image/png') return setError('File phải là ảnh PNG.')
    if (img.width !== 256 || img.height !== 384) return setError(`Ảnh phải đúng 256 × 384 px. Ảnh của ngươi là ${img.width} × ${img.height}.`)
    if (file.size > 1_000_000) return setError('File quá lớn (tối đa 1 MB).')
    setBusy(true)
    const form = new FormData()
    form.append('sprite', file, 'sprite.png')
    try {
      await api.post('/api/forge/sprite', form)
      sfx('levelup')
      toast('Đã rèn hình hài mới')
      await load()
      onChanged()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Có lỗi xảy ra.')
    } finally {
      setBusy(false)
    }
  }

  async function removeSprite() {
    setBusy(true)
    try {
      await api.del('/api/forge/sprite')
      await load()
      onChanged()
    } finally {
      setBusy(false)
    }
  }

  if (!forge) return <p className="pixel dim" style={{ fontSize: 24 }}>Đang nhóm lửa lò rèn…</p>
  const sheet = forge.customSprite ?? forge.avatars.find(a => a.code === forge.avatar)?.spriteSheetUrl ?? ''

  return (
    <Window title="Lò rèn nhân vật" style={{ width: '100%', maxWidth: 1000 }}>
      <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr', gap: 26 }}>
        <section>
          <h3 style={{ marginBottom: 8 }}>Hình hài</h3>
          {forge.customSprite && <p className="notice">Đang dùng sprite tự tải lên. Gỡ nó ra để dùng hình hài có sẵn.</p>}
          <AvatarPicker avatars={forge.avatars} value={forge.avatar} onChange={code => { if (!busy && code !== forge.avatar) void save({ avatar: code }) }} />

          <h3 style={{ margin: '18px 0 8px' }}>Sprite của riêng ngươi</h3>
          <div className="inset">
            <p style={{ marginTop: 0 }}>
              PNG nền trong suốt, đúng <b>256 × 384 px</b>: 4 khung đứng yên mỗi hàng × 6 hàng cho 6 bậc tiến hóa
              (Lv 1, 20, 40, 60, 80, 99), mỗi khung 64 × 64. Tối đa 1 MB. Bậc nào chưa vẽ thì để trống.
            </p>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
              <button className="btn ghost small" onClick={downloadTemplate}>Tải khung mẫu</button>
              <a className="btn ghost small" href="/avatars/ember_knight.png" download="dragon-fit-example-knight.png" style={{ textDecoration: 'none' }}>Tải ví dụ</a>
              <label className="btn small" style={{ cursor: 'pointer' }}>
                Tải lên
                <input type="file" accept="image/png" hidden onChange={e => { void upload(e.target.files?.[0]); e.target.value = '' }} />
              </label>
              {forge.customSprite && <button className="btn ghost small" disabled={busy} onClick={removeSprite}>Gỡ sprite</button>}
            </div>
            {error && <p className="field err" role="alert">{error}</p>}
            <p className="dim" style={{ fontSize: 12.5, marginBottom: 0 }}>Sprite sẽ hiện trên bảng xếp hạng, nên đừng dùng hình ảnh của người khác hoặc nhân vật có bản quyền.</p>
          </div>
        </section>

        <section>
          <h3 style={{ marginBottom: 8 }}>Xem trước</h3>
          <div className="inset" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10 }}>
            {sheet && <AvatarSprite sheetUrl={sheet} stage={forge.evolutionStage} scale={3} />}
            <div className="pixel dim" style={{ fontSize: 17 }}>Bậc {forge.evolutionStage} · Lv {forge.level}</div>
          </div>

          <h3 style={{ margin: '18px 0 8px' }}>Danh hiệu</h3>
          <div className="choices">
            {forge.titles.map(t => (
              <button key={t.code} className={`choice ${forge.activeTitle === t.code ? 'on' : ''}`} disabled={busy}
                onClick={() => save({ titleCode: t.code })}>
                <span className="pixel" style={{ fontSize: 18 }}>{t.name}</span>
              </button>
            ))}
          </div>
          {forge.lockedTitles.length > 0 && (
            <div className="dim" style={{ fontSize: 13, marginTop: 10 }}>
              Sắp mở: {forge.lockedTitles.slice(0, 3).map(t => `${t.name} (Lv ${t.minLevel})`).join(' · ')}
            </div>
          )}
        </section>
      </div>
    </Window>
  )
}
