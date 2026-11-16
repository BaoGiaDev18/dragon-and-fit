import { useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import { AvatarPicker } from '../components/AvatarPicker'
import { AvatarSprite } from '../components/AvatarSprite'
import { Window } from '../components/ui'
import type { Death, OnboardingState } from '../types'

const NAME_PATTERN = /^[A-Za-z](?:[A-Za-z0-9_'-]| (?! ))*$/

/** HP reached 0: a summary of the journey, then rebirth with a new name and avatar (plan B10). */
export function DeathScreen({ death, onReborn, onLogout }: { death: Death; onReborn: () => void; onLogout: () => void }) {
  const [state, setState] = useState<OnboardingState | null>(null)
  const [rebirth, setRebirth] = useState(false)
  const [name, setName] = useState('')
  const [avatar, setAvatar] = useState<string | undefined>()
  const [errors, setErrors] = useState<Record<string, string>>({})

  useEffect(() => {
    api.get<OnboardingState>('/api/onboarding').then(s => {
      setState(s)
      setAvatar(s.avatars.find(a => a.isDefault)?.code)
    })
  }, [])

  async function reborn() {
    try {
      await api.post('/api/character/rebirth', { name, avatar })
      onReborn()
    } catch (e) {
      if (e instanceof ApiError && e.fields) setErrors(e.fields)
      else setErrors({ _: e instanceof Error ? e.message : 'Có lỗi xảy ra.' })
    }
  }

  const nameError = name.trim() && !NAME_PATTERN.test(name.trim())
    ? "Tên chỉ dùng chữ cái không dấu (A–Z), số, khoảng trắng đơn và - _ '." : null

  return (
    <div className="scene" style={{ justifyContent: 'center', background: 'radial-gradient(ellipse at 50% 40%, rgba(196,66,59,0.16), transparent 60%), #08090a' }}>
      {!rebirth ? (
        <Window title="Tử trận" style={{ width: 520, textAlign: 'center' }}>
          <div style={{ display: 'flex', justifyContent: 'center', margin: '4px 0 6px', filter: 'grayscale(1) brightness(0.7)' }}>
            {death.avatar && <AvatarSprite sheetUrl={death.avatar} stage={death.evolutionStage} scale={3} animate={false} />}
          </div>
          <div className="char-name" style={{ fontSize: 30 }}>{death.name}</div>
          <div className="pixel dim" style={{ fontSize: 19 }}>{death.title} · Lv {death.level}</div>
          <p style={{ fontStyle: 'italic' }} className="dim">
            Ngọn lửa trong lò rèn đã tắt. Hành trình của {death.name} kết thúc sau {death.daysAlive} ngày, với {death.questsDone} nhiệm vụ hoàn thành.
          </p>
          <div className="inset" style={{ textAlign: 'left', marginBottom: 14 }}>
            <div className="pixel accent" style={{ fontSize: 18 }}>Những ngày cuối</div>
            {death.lastDays.map(d => (
              <div key={d.date} style={{ display: 'flex', gap: 10, fontSize: 13.5, padding: '3px 0' }}>
                <span style={{ width: 70 }}>{new Date(d.date).toLocaleDateString('vi-VN', { day: 'numeric', month: 'numeric' })}</span>
                <span style={{ flex: 1 }} className="dim">
                  {d.mealsLogged === 0 ? 'không ghi bữa ăn' : `${d.actualKcal.toLocaleString('vi-VN')}/${d.targetKcal.toLocaleString('vi-VN')} kcal`} · nhiệm vụ {Math.round(d.questCompletion * 100)}%
                </span>
                <span className="pixel" style={{ fontSize: 17, color: '#ff7a6e' }}>−{d.damage}</span>
                <span className="pixel" style={{ fontSize: 17, width: 54, textAlign: 'right' }}>HP {d.hpAfter}</span>
              </div>
            ))}
          </div>
          <p className="dim" style={{ fontSize: 12.5 }}>
            Level, Gem, danh hiệu và vật phẩm đã mất. Số đo và nhật ký ăn uống, tập luyện vẫn được giữ, nên các chỉ số STR, VIT… của nhân vật mới vẫn tính từ sức khỏe thật của ngươi.
          </p>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <button className="btn ghost small" onClick={onLogout}>Thoát</button>
            <button className="btn" onClick={() => setRebirth(true)}>Tái sinh</button>
          </div>
        </Window>
      ) : (
        <Window title="Tái sinh" style={{ width: 640 }}>
          <div className="field">
            <label htmlFor="rb-name">Tên mới</label>
            <input id="rb-name" className="input char-name" value={name} maxLength={16} autoComplete="off" spellCheck={false}
              onChange={e => setName(e.target.value)} />
            {(errors.char_name ?? nameError) && <span className="err">{errors.char_name ?? nameError}</span>}
          </div>
          {state && (
            <div className="field">
              <label>Hình hài</label>
              <AvatarPicker avatars={state.avatars} value={avatar} onChange={setAvatar} />
            </div>
          )}
          {errors._ && <p className="field err">{errors._}</p>}
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <button className="btn ghost small" onClick={() => setRebirth(false)}>Quay lại</button>
            <button className="btn" disabled={!name.trim() || !!nameError} onClick={reborn}>Bắt đầu lại</button>
          </div>
        </Window>
      )}
    </div>
  )
}
