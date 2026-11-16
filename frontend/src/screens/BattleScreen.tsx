import { useCallback, useEffect, useRef, useState } from 'react'
import { api, compressPhoto } from '../api'
import { AvatarSprite } from '../components/AvatarSprite'
import { Keys, Window, useKey } from '../components/ui'
import { sfx } from '../sound'
import type { Quest, Reward } from '../types'

const ENEMIES: Record<string, { name: string; sprite: string; flip?: boolean; line: string }> = {
  push: { name: 'Golem Đá', sprite: '/enemies/golem.png', line: 'Golem Đá chặn cổng thành. Đẩy lùi nó!' },
  legs: { name: 'Sói Bóng Đêm', sprite: '/enemies/wolf.png', flip: true, line: 'Sói Bóng Đêm lao ra từ rừng. Đứng vững trên đôi chân!' },
  pull: { name: 'Kraken Đầm Lầy', sprite: '/enemies/kraken.png', line: 'Kraken quấn lấy đồng đội. Kéo họ ra!' },
  core: { name: 'Hồn Ma Bão', sprite: '/enemies/ghost.png', line: 'Hồn Ma Bão gào thét. Giữ thân mình như đá!' },
  mobility: { name: 'Hồn Ma Bão', sprite: '/enemies/ghost.png', line: 'Gió dịu lại. Thả lỏng cơ thể.' },
}

const REST_SECONDS: Record<string, number> = { push: 60, pull: 60, legs: 75, core: 45, mobility: 0 }

type Phase = 'fight' | 'rest' | 'victory'
type Float = { id: number; text: string }

/**
 * Battle workout (plan B4): each set is an attack, the enemy's HP is the total reps of the quest.
 * Reps per set are sent with the report and saved as a workout session.
 */
export function BattleScreen({ quest, sheetUrl, stage, proofRequired, onClose, onDone }: {
  quest: Quest; sheetUrl: string; stage: number; proofRequired: boolean
  onClose: () => void; onDone: (r: Reward) => void
}) {
  const group = quest.exercise?.group ?? 'push'
  const enemy = ENEMIES[group] ?? ENEMIES.push
  const seconds = quest.unit === 'seconds'
  const maxHp = quest.sets * quest.targetValue
  const [sets, setSets] = useState<number[]>([])
  const [value, setValue] = useState(String(quest.targetValue))
  const [phase, setPhase] = useState<Phase>('fight')
  const [hurt, setHurt] = useState(false)
  const [lunge, setLunge] = useState(false)
  const [floats, setFloats] = useState<Float[]>([])
  const [rest, setRest] = useState(0)
  const [watch, setWatch] = useState<number | null>(null) // stopwatch start for holds
  const [now, setNow] = useState(Date.now())
  const [photo, setPhoto] = useState<Blob | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const floatId = useRef(0)

  const dealt = sets.reduce((a, b) => a + b, 0)
  const hp = Math.max(0, maxHp - dealt)
  const setNo = sets.length + 1

  // rest countdown and stopwatch tick
  useEffect(() => {
    if (phase !== 'rest' && watch === null) return
    const t = window.setInterval(() => {
      setNow(Date.now())
      if (phase === 'rest') setRest(r => Math.max(0, r - 1))
    }, phase === 'rest' ? 1000 : 200)
    return () => window.clearInterval(t)
  }, [phase, watch])

  useEffect(() => {
    if (phase === 'rest' && rest === 0) { sfx('confirm'); setPhase('fight') }
  }, [phase, rest])

  const attack = useCallback(() => {
    const n = Number(value)
    if (!Number.isInteger(n) || n < 0 || n > 500) return setError('Nhập số nguyên từ 0.')
    setError(null)
    const next = [...sets, n]
    setSets(next)
    sfx('hit')
    setLunge(true); setHurt(true)
    window.setTimeout(() => setLunge(false), 220)
    window.setTimeout(() => setHurt(false), 380)
    const id = ++floatId.current
    setFloats(f => [...f, { id, text: `−${n}` }])
    window.setTimeout(() => setFloats(f => f.filter(x => x.id !== id)), 900)
    setValue(String(quest.targetValue))
    setWatch(null)
    if (next.length >= quest.sets) {
      window.setTimeout(() => { sfx('victory'); setPhase('victory') }, 600)
    } else if ((REST_SECONDS[group] ?? 60) > 0) {
      setRest(REST_SECONDS[group] ?? 60)
      window.setTimeout(() => setPhase('rest'), 500)
    }
  }, [value, sets, quest, group])

  useKey('Enter', () => { if (phase === 'fight') attack() }, phase === 'fight')
  // Escape flees mid-fight; after the last set the result must be claimed or the quest stays open.
  useKey('Escape', onClose, phase !== 'victory')

  async function pick(file: File | undefined) {
    if (!file) return
    try {
      const blob = await compressPhoto(file)
      setPhoto(blob)
      if (preview) URL.revokeObjectURL(preview)
      setPreview(URL.createObjectURL(blob))
    } catch {
      setError('Không đọc được ảnh này.')
    }
  }

  async function submit() {
    if (proofRequired && !photo) return setError('Cần ảnh chụp làm bằng chứng.')
    setBusy(true)
    const form = new FormData()
    form.append('doneValue', String(Math.floor(dealt / quest.sets)))
    form.append('sets', sets.join(','))
    if (photo) form.append('photo', photo, 'proof.webp')
    try {
      onDone(await api.post<Reward>(`/api/quests/${quest.id}/complete`, form))
    } catch (e) {
      sfx('error')
      setError(e instanceof Error ? e.message : 'Có lỗi xảy ra.')
      setBusy(false)
    }
  }

  const unit = seconds ? 'giây' : 'rep'
  const elapsed = watch === null ? 0 : Math.floor((now - watch) / 1000)
  const won = dealt >= maxHp

  return (
    <div className="overlay">
      <Window title="Trận chiến" style={{ width: 760 }}>
        {/* Arena */}
        <div className="inset battle-arena">
          <div className="dim" style={{ textAlign: 'center', fontStyle: 'italic', fontSize: 13.5 }}>{enemy.line}</div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', alignItems: 'end', padding: '10px 30px 0' }}>
            <div className={lunge ? 'lunge' : ''} style={{ justifySelf: 'start' }}>
              <AvatarSprite sheetUrl={sheetUrl} stage={stage} scale={3} />
            </div>
            <div style={{ justifySelf: 'end', textAlign: 'center', position: 'relative' }}>
              <div className="pixel" style={{ fontSize: 20 }}>{enemy.name}</div>
              <div className="bar hp" style={{ width: 200, marginBottom: 6 }}>
                <span style={{ width: `${(hp / maxHp) * 100}%` }} />
              </div>
              <div className="pixel dim" style={{ fontSize: 16 }}>HP {hp}/{maxHp}</div>
              <div className={`enemy ${hurt ? 'hurt' : ''} ${phase === 'victory' && won ? 'defeated' : ''}`}
                style={{ backgroundImage: `url(${enemy.sprite})`, transform: enemy.flip ? 'scaleX(-1)' : undefined }} />
              {floats.map(f => <span key={f.id} className="dmg-float pixel">{f.text}</span>)}
            </div>
          </div>
        </div>

        {/* Command window */}
        <div className="inset" style={{ marginTop: 12 }}>
          <div style={{ display: 'flex', gap: 14, marginBottom: 10, flexWrap: 'wrap' }}>
            {Array.from({ length: quest.sets }, (_, i) => (
              <span key={i} className="pixel" style={{ fontSize: 18, color: i < sets.length ? (sets[i] >= quest.targetValue ? 'var(--good)' : 'var(--accent)') : i === sets.length ? 'var(--text)' : 'var(--text-dim)' }}>
                {i < sets.length ? '◆' : i === sets.length && phase !== 'victory' ? '▶' : '◇'} Set {i + 1}{i < sets.length ? `: ${sets[i]} ${unit}` : ''}
              </span>
            ))}
          </div>

          {phase === 'fight' && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <span>Set {setNo}/{quest.sets} · {quest.exercise?.nameVi} · mục tiêu <b>{quest.targetValue} {unit}</b></span>
              <div style={{ flex: 1 }} />
              {seconds && (
                watch === null
                  ? <button className="btn ghost small" onClick={() => { sfx('click'); setWatch(Date.now()); setNow(Date.now()) }}>Bấm giờ</button>
                  : <button className="btn ghost small" onClick={() => { sfx('click'); setValue(String(elapsed)); setWatch(null) }}>Dừng · {elapsed}s</button>
              )}
              <button className="btn ghost small" onClick={() => setValue(v => String(Math.max(0, Number(v) - 1)))} aria-label="Bớt 1">−</button>
              <input className="input pixel" style={{ width: 70, fontSize: 22, textAlign: 'center' }} inputMode="numeric" value={value}
                onChange={e => setValue(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') attack() }} aria-label={`Số ${unit} đã làm`} />
              <button className="btn ghost small" onClick={() => setValue(v => String(Number(v) + 1))} aria-label="Thêm 1">+</button>
              <button className="btn" onClick={attack}>Tấn công</button>
            </div>
          )}

          {phase === 'rest' && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
              <span className="pixel accent" style={{ fontSize: 34 }}>{rest}s</span>
              <span style={{ flex: 1 }}>Nghỉ giữa hiệp. Hít thở sâu, uống nước.</span>
              <button className="btn ghost small" onClick={() => setRest(r => r + 15)}>+15s</button>
              <button className="btn small" onClick={() => setRest(0)}>Đánh tiếp</button>
            </div>
          )}

          {phase === 'victory' && (
            <div>
              <div className="pixel" style={{ fontSize: 30, color: won ? 'var(--glow)' : 'var(--accent)' }}>
                {won ? `Hạ gục ${enemy.name}!` : `${enemy.name} rút lui (${dealt}/${maxHp})`}
              </div>
              <p className="dim" style={{ margin: '4px 0 10px' }}>
                {won ? 'Gửi ảnh bằng chứng để nhận phần thưởng.' : 'Làm thiếu vẫn nhận thưởng theo tỉ lệ, và mất ít máu hơn là bỏ hẳn.'}
              </p>
              <div className="field">
                <label htmlFor="battle-photo">Ảnh bằng chứng{proofRequired && <span className="accent"> *</span>}</label>
                <input id="battle-photo" type="file" accept="image/*" capture="environment" onChange={e => pick(e.target.files?.[0])} />
                {preview && <img src={preview} alt="Ảnh bằng chứng" style={{ maxHeight: 120, objectFit: 'contain', border: '1px solid #3b3e43', marginTop: 6, alignSelf: 'flex-start' }} />}
              </div>
              <button className="btn" disabled={busy} onClick={submit}>Nhận thưởng</button>
            </div>
          )}
          {error && <p className="field err" role="alert">{error}</p>}
        </div>
        {phase === 'fight' && <Keys hints={[['Enter', 'Tấn công'], ['Esc', 'Rút lui']]} />}
        {phase === 'rest' && <Keys hints={[['Esc', 'Rút lui']]} />}
      </Window>
    </div>
  )
}
