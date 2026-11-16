import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import { AvatarSprite } from '../components/AvatarSprite'
import { AvatarPicker } from '../components/AvatarPicker'
import { Keys, Window, useKey } from '../components/ui'
import type { AvatarInfo, OnboardingState, Question, Step } from '../types'

type Answers = Record<string, unknown>

/** Character creation (plan B8): 7 steps narrated by the Dungeon Master, then a review card. */
export function Onboarding({ onDone }: { onDone: () => void }) {
  const [state, setState] = useState<OnboardingState | null>(null)
  const [index, setIndex] = useState(0)
  const [answers, setAnswers] = useState<Answers>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [review, setReview] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    api.get<OnboardingState>('/api/onboarding').then(s => {
      setState(s)
      const defaults: Answers = { avatar: s.avatars.find(a => a.isDefault)?.code, quest_time: '06:00' }
      setAnswers({ ...defaults, ...s.answers })
      const i = s.steps.findIndex(st => st.code === s.currentStep)
      setIndex(Math.max(0, i))
      // Coming back after every step was saved: go straight to the review card.
      if (s.status === 'InProgress' && i === s.steps.length - 1 && s.steps[i].questions.every(q => !q.isRequired || s.answers[q.code] !== undefined)) setReview(true)
    }).catch(e => setLoadError(e.message))
  }, [])

  const step: Step | undefined = state?.steps[index]
  const set = (code: string, v: unknown) => setAnswers(a => ({ ...a, [code]: v }))

  const save = useCallback(async (skip = false) => {
    if (!state || !step) return
    setBusy(true)
    setErrors({})
    try {
      const payload = skip ? {} : Object.fromEntries(step.questions.map(q => [q.code, answers[q.code] ?? null]))
      await api.post(`/api/onboarding/steps/${step.code}`, { answers: payload, skip })
      if (index + 1 < state.steps.length) setIndex(index + 1)
      else setReview(true)
    } catch (e) {
      if (e instanceof ApiError && e.fields) setErrors(e.fields)
      else setErrors({ _: e instanceof Error ? e.message : 'Có lỗi xảy ra.' })
    } finally {
      setBusy(false)
    }
  }, [state, step, answers, index])

  const complete = useCallback(async () => {
    setBusy(true)
    try {
      await api.post('/api/onboarding/complete', { timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone })
      onDone()
    } catch (e) {
      if (e instanceof ApiError && e.fields) {
        // A required answer is missing: jump back to the first step that holds it.
        const code = Object.keys(e.fields)[0]
        const i = state!.steps.findIndex(s => s.questions.some(q => q.code === code))
        setReview(false)
        if (i >= 0) setIndex(i)
        setErrors(e.fields)
      } else setErrors({ _: e instanceof Error ? e.message : 'Có lỗi xảy ra.' })
    } finally {
      setBusy(false)
    }
  }, [onDone, state])

  useKey('Enter', () => { if (!busy) void (review ? complete() : save()) }, !!state)
  useKey('Escape', () => { if (review) setReview(false); else if (index > 0) setIndex(index - 1) }, !!state)

  if (loadError) return <div className="scene"><Window title="Lỗi">{loadError}</Window></div>
  if (!state || !step) return <div className="scene"><p className="pixel dim" style={{ fontSize: 24 }}>Đang mở lò rèn…</p></div>

  return (
    <div className="scene">
      <div style={{ display: 'grid', gridTemplateColumns: '220px minmax(0, 720px)', gap: 28, width: '100%', maxWidth: 980 }}>
        <Window title="Hành trình" style={{ alignSelf: 'start' }}>
          <ol style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {state.steps.map((s, i) => {
              const current = !review && i === index
              const done = review || i < index
              return (
                <li key={s.code}>
                  <button
                    onClick={() => { if (done || i <= index) { setReview(false); setIndex(i); setErrors({}) } }}
                    style={{
                      display: 'flex', gap: 10, alignItems: 'center', width: '100%', textAlign: 'left',
                      background: 'none', border: 'none', padding: '7px 0', cursor: done ? 'pointer' : 'default',
                      color: current ? 'var(--glow)' : done ? 'var(--text)' : 'var(--text-dim)',
                    }}>
                    <span className="pixel" style={{ width: 18, fontSize: 20 }}>{current ? '▶' : done ? '◆' : '◇'}</span>
                    <span>{s.title}</span>
                  </button>
                </li>
              )
            })}
            <li>
              <div style={{ display: 'flex', gap: 10, padding: '7px 0', color: review ? 'var(--glow)' : 'var(--text-dim)' }}>
                <span className="pixel" style={{ width: 18, fontSize: 20 }}>{review ? '▶' : '◇'}</span>
                <span>Xác nhận</span>
              </div>
            </li>
          </ol>
        </Window>

        {review
          ? <ReviewCard state={state} answers={answers} busy={busy} error={errors._} onConfirm={complete} onBack={() => setReview(false)} />
          : (
            <Window title={step.title}>
              <Narration text={step.narration} />
              {errors._ && <p className="field err" role="alert">{errors._}</p>}
              <div style={{ marginTop: 18 }}>
                {step.questions.map(q => (
                  <QuestionField key={q.code} q={q} value={answers[q.code]} error={errors[q.code]} avatars={state.avatars}
                    onChange={v => set(q.code, v)} />
                ))}
              </div>
              <div style={{ display: 'flex', gap: 10, justifyContent: 'space-between', marginTop: 8 }}>
                <button className="btn ghost small" disabled={index === 0 || busy} onClick={() => setIndex(index - 1)}>Quay lại</button>
                <div style={{ display: 'flex', gap: 10 }}>
                  {step.isSkippable && <button className="btn ghost small" disabled={busy} onClick={() => save(true)}>Làm sau</button>}
                  <button className="btn" disabled={busy} onClick={() => save()}>Tiếp tục</button>
                </div>
              </div>
              <Keys hints={[['Enter', 'Tiếp tục'], ['Esc', 'Quay lại']]} />
            </Window>
          )}
      </div>
    </div>
  )
}

function Narration({ text }: { text: string }) {
  return (
    <div className="inset" style={{ display: 'flex', gap: 14, alignItems: 'flex-start' }}>
      <div aria-hidden className="pixel" style={{
        width: 44, height: 44, flex: 'none', display: 'grid', placeItems: 'center', fontSize: 30,
        border: '1px solid var(--border)', color: 'var(--accent)', background: '#1b1c20',
      }}>☗</div>
      <div>
        <div className="pixel accent" style={{ fontSize: 18 }}>Người Quản Trò</div>
        <div style={{ fontStyle: 'italic' }}>{text}</div>
      </div>
    </div>
  )
}

function QuestionField({ q, value, error, avatars, onChange }: {
  q: Question; value: unknown; error?: string; avatars: AvatarInfo[]; onChange: (v: unknown) => void
}) {
  const id = `q-${q.code}`
  return (
    <div className="field">
      <label htmlFor={id}>
        {q.prompt}{q.isRequired && <span className="accent"> *</span>}
        {q.isSensitive && <span className="dim" style={{ fontSize: 12, marginLeft: 8 }}>· chỉ mình bạn thấy</span>}
      </label>
      <Input q={q} id={id} value={value} avatars={avatars} onChange={onChange} />
      {q.helpText && <span className="help">{q.helpText}</span>}
      {(error ?? patternError(q, value)) && <span className="err" role="alert">{error ?? patternError(q, value)}</span>}
    </div>
  )
}

/** Same rule the server enforces, shown while typing so the player isn't surprised on Continue. */
function patternError(q: Question, value: unknown): string | null {
  if (!q.pattern || typeof value !== 'string' || value.trim() === '') return null
  try {
    return new RegExp(q.pattern).test(value.trim()) ? null : q.patternMessage
  } catch {
    return null
  }
}

function Input({ q, id, value, avatars, onChange }: { q: Question; id: string; value: unknown; avatars: AvatarInfo[]; onChange: (v: unknown) => void }) {
  switch (q.inputType) {
    case 'text':
      return (
        <input id={id} className={`input ${q.pattern ? 'char-name' : ''}`} value={(value as string) ?? ''} maxLength={q.max ?? undefined}
          spellCheck={false} autoComplete="off" onChange={e => onChange(e.target.value)} />
      )
    case 'number':
      return (
        <div className="input-unit">
          <input id={id} className="input" inputMode="decimal" value={(value as string | number | null) ?? ''}
            onChange={e => onChange(e.target.value === '' ? null : e.target.value)} />
          {q.unit && <span className="dim">{q.unit}</span>}
        </div>
      )
    case 'single_choice':
      return (
        <div className="choices" role="radiogroup" id={id}>
          {q.options.map(o => (
            <button key={o.value} type="button" role="radio" aria-checked={value === o.value}
              className={`choice ${value === o.value ? 'on' : ''}`} onClick={() => onChange(o.value)}>{o.label}</button>
          ))}
        </div>
      )
    case 'multi_choice': {
      const picked = (value as string[]) ?? []
      const toggle = (v: string, exclusive: boolean) => {
        if (picked.includes(v)) return onChange(picked.filter(p => p !== v))
        if (exclusive) return onChange([v])
        const exclusives = q.options.filter(o => o.isExclusive).map(o => o.value)
        onChange([...picked.filter(p => !exclusives.includes(p)), v])
      }
      return (
        <div className="choices" id={id}>
          {q.options.map(o => (
            <button key={o.value} type="button" aria-pressed={picked.includes(o.value)}
              className={`choice ${picked.includes(o.value) ? 'on' : ''}`} onClick={() => toggle(o.value, o.isExclusive)}>{o.label}</button>
          ))}
        </div>
      )
    }
    case 'time':
      return <input id={id} type="time" className="input" style={{ width: 140 }} value={(value as string) ?? ''} onChange={e => onChange(e.target.value)} />
    case 'time_range': {
      const r = (value as { start: string; end: string }) ?? { start: '', end: '' }
      const upd = (k: 'start' | 'end', v: string) => {
        const next = { ...r, [k]: v }
        onChange(next.start || next.end ? next : null)
      }
      return (
        <div className="input-unit" id={id}>
          <input type="time" className="input" value={r.start} onChange={e => upd('start', e.target.value)} aria-label="Bắt đầu" />
          <span className="dim">đến</span>
          <input type="time" className="input" value={r.end} onChange={e => upd('end', e.target.value)} aria-label="Kết thúc" />
        </div>
      )
    }
    case 'avatar_picker':
      return <AvatarPicker avatars={avatars} value={value as string} onChange={onChange} />
    case 'reward_list':
      return <RewardList value={(value as { name: string; size: string }[]) ?? []} onChange={onChange} />
  }
}

const SIZES = [
  { value: 'small', label: 'Nhỏ · 1.500 Gem' },
  { value: 'medium', label: 'Vừa · 3.000 Gem' },
  { value: 'large', label: 'Lớn · 6.000 Gem' },
]

function RewardList({ value, onChange }: { value: { name: string; size: string }[]; onChange: (v: unknown) => void }) {
  const rows = value.length ? value : [{ name: '', size: 'small' }]
  const upd = (i: number, patch: Partial<{ name: string; size: string }>) =>
    onChange(rows.map((r, j) => (j === i ? { ...r, ...patch } : r)).filter(r => r.name.trim() || rows.length === 1))
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {rows.map((r, i) => (
        <div key={i} style={{ display: 'flex', gap: 8 }}>
          <input className="input" style={{ flex: 1 }} placeholder="Ví dụ: đôi giày chạy mới" value={r.name} maxLength={60}
            onChange={e => upd(i, { name: e.target.value })} />
          <select className="input" value={r.size} onChange={e => upd(i, { size: e.target.value })}>
            {SIZES.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
          {rows.length > 1 && <button type="button" className="btn ghost small" onClick={() => onChange(rows.filter((_, j) => j !== i))} aria-label="Xóa">✕</button>}
        </div>
      ))}
      {rows.length < 3 && rows[rows.length - 1].name.trim() && (
        <button type="button" className="btn ghost small" style={{ alignSelf: 'flex-start' }}
          onClick={() => onChange([...rows, { name: '', size: 'medium' }])}>+ Thêm phần thưởng</button>
      )}
    </div>
  )
}

function ReviewCard({ state, answers, busy, error, onConfirm, onBack }: {
  state: OnboardingState; answers: Answers; busy: boolean; error?: string; onConfirm: () => void; onBack: () => void
}) {
  const avatar = state.avatars.find(a => a.code === answers.avatar) ?? state.avatars[0]
  const label = useMemo(() => {
    const byCode = new Map(state.steps.flatMap(s => s.questions).map(q => [q.code, q]))
    return (code: string) => {
      const v = answers[code]
      const q = byCode.get(code)
      if (v === undefined || v === null || v === '') return '—'
      if (q && (q.inputType === 'single_choice' || q.inputType === 'multi_choice')) {
        const vals = Array.isArray(v) ? v : [v]
        return vals.map(x => q.options.find(o => o.value === x)?.label ?? x).join(', ')
      }
      return `${v}${q?.unit ? ` ${q.unit}` : ''}`
    }
  }, [state, answers])
  const rewards = (answers.rewards as { name: string }[] | undefined) ?? []

  return (
    <Window title="Thẻ nhân vật">
      <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: 24, alignItems: 'start' }}>
        <div style={{ textAlign: 'center' }}>
          <div style={{ border: '1px solid var(--border-bright)', background: 'var(--bg-inset)', padding: 6 }}>
            {avatar && <AvatarSprite sheetUrl={avatar.spriteSheetUrl} scale={3} />}
          </div>
          <div className="char-name" style={{ fontSize: 26, marginTop: 8 }}>{String(answers.char_name ?? '')}</div>
          <div className="accent pixel" style={{ fontSize: 18 }}>Wandering Squire · Lv 1</div>
        </div>
        <div>
          {[
            ['Mục tiêu', `${label('goal')} · ${label('goal_weeks')}`],
            ['Mục tiêu cụ thể', label('goal_target')],
            ['Số đo', `${label('height_cm')} · ${label('weight_kg')} · vòng bụng ${label('waist_cm')}`],
            ['Lịch tập', `${label('train_days')} · ${label('session_min')} · ${label('train_time')}`],
            ['Giờ nhận nhiệm vụ', label('quest_time')],
            ['Dụng cụ', label('equipment')],
            ['Chỗ đau cần tránh', label('avoid_parts')],
            ['Phần thưởng', rewards.map(r => r.name).join(', ') || '—'],
          ].map(([k, v]) => (
            <div key={k} style={{ display: 'flex', justifyContent: 'space-between', gap: 16, padding: '6px 0', borderBottom: '1px solid #26282c' }}>
              <span className="dim">{k}</span><span style={{ textAlign: 'right' }}>{v}</span>
            </div>
          ))}
          <p className="notice" style={{ marginTop: 14 }}>Nhận 100 Gem, danh hiệu Wandering Squire và bộ nhiệm vụ đầu tiên</p>
        </div>
      </div>
      {error && <p className="field err" role="alert">{error}</p>}
      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 18 }}>
        <button className="btn ghost small" onClick={onBack} disabled={busy}>Sửa lại</button>
        <button className="btn" onClick={onConfirm} disabled={busy}>Xác nhận</button>
      </div>
      <Keys hints={[['Enter', 'Xác nhận'], ['Esc', 'Sửa lại']]} />
    </Window>
  )
}
