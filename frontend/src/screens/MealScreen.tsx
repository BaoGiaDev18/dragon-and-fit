import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api'
import { GachaPanel } from '../components/GachaPanel'
import { Window } from '../components/ui'
import type { Food, MealDay, Reward } from '../types'

const SLOTS = [
  { value: 'breakfast', label: 'Sáng' },
  { value: 'lunch', label: 'Trưa' },
  { value: 'dinner', label: 'Tối' },
  { value: 'snack', label: 'Ăn vặt' },
]

const TAGS = [
  { value: '', label: 'Tất cả' },
  { value: 'protein', label: 'Giàu đạm' },
  { value: 'meal', label: 'Món chính' },
  { value: 'canteen', label: 'Căng tin' },
  { value: 'veg', label: 'Rau' },
  { value: 'snack', label: 'Ăn vặt' },
  { value: 'drink', label: 'Đồ uống' },
]

function defaultSlot() {
  const h = new Date().getHours()
  return h < 10 ? 'breakfast' : h < 15 ? 'lunch' : h < 21 ? 'dinner' : 'snack'
}

const fmt = (n: number) => Math.round(n).toLocaleString('vi-VN')

/** Meal log (Phase 2): every logged dish feeds the 4:00 settlement, WIS, and the protein quest. */
export function MealScreen({ onReward, toast }: { onReward: (r: Reward) => void; toast: (t: string, err?: boolean) => void }) {
  const [day, setDay] = useState<MealDay | null>(null)
  const [foods, setFoods] = useState<Food[]>([])
  const [query, setQuery] = useState('')
  const [tag, setTag] = useState('')
  const [slot, setSlot] = useState(defaultSlot)
  const [servings, setServings] = useState<Record<number, number>>({})
  const [busy, setBusy] = useState(false)

  const loadDay = useCallback(() => api.get<MealDay>('/api/meals/day').then(setDay), [])
  useEffect(() => {
    loadDay().catch(e => toast(e.message, true))
    // Sort on the client with Vietnamese collation (the database sorts by bytes, so "Táo" lands after "Trứng").
    api.get<Food[]>('/api/foods').then(f => setFoods([...f].sort((x, y) => x.nameVi.localeCompare(y.nameVi, 'vi')))).catch(e => toast(e.message, true))
  }, [loadDay, toast])

  const shown = useMemo(() => {
    const q = query.trim().toLowerCase()
    return foods.filter(f => (!tag || f.tags.includes(tag)) && (!q || f.nameVi.toLowerCase().includes(q)))
  }, [foods, query, tag])

  async function add(f: Food) {
    setBusy(true)
    try {
      const res = await api.post<{ reward: Reward | null }>('/api/meals', { foodId: f.id, servings: servings[f.id] ?? 1, slot })
      await loadDay()
      if (res.reward) onReward(res.reward)
      else toast(`Đã ghi: ${f.nameVi}`)
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      setBusy(false)
    }
  }

  async function remove(id: number) {
    setBusy(true)
    try {
      await fetchDelete(id)
      await loadDay()
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      setBusy(false)
    }
  }

  if (!day) return <p className="pixel dim" style={{ fontSize: 24 }}>Đang mở kho lương thực…</p>

  const over = day.totals.kcal > day.targets.kcal * 1.1
  const under = day.logs.length > 0 && day.totals.kcal < day.targets.kcal * 0.9

  return (
    <Window title="Thực đơn hôm nay" style={{ width: '100%', maxWidth: 1040 }}>
      {/* Nutrition scale: the green zone is ±10% of the target, the same rule the settlement uses. */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 28, marginBottom: 18 }}>
        <Scale label="Năng lượng" value={day.totals.kcal} target={day.targets.kcal} unit="kcal" zone />
        <Scale label="Protein" value={day.totals.proteinG} target={day.targets.proteinG} unit="g" />
      </div>
      {over && <p className="notice" style={{ color: '#ff7a6e' }}>Vượt quá mục tiêu hơn 10%: ăn đúng như vậy thì sáng mai sẽ mất máu.</p>}
      {under && <p className="notice">Còn thiếu khá nhiều so với mục tiêu. Ăn quá ít cũng bị trừ máu.</p>}
      {day.logs.length === 0 && <p className="notice">Chưa ghi bữa nào. Cả ngày không ghi bữa nào sẽ mất 10 máu.</p>}

      {day.open && <GachaPanel foods={foods} toast={toast} onLogged={async r => { await loadDay(); if (r) onReward(r); else toast('Đã ghi vào nhật ký ăn uống') }} />}

      <div style={{ display: 'grid', gridTemplateColumns: '1.25fr 1fr', gap: 24, marginTop: 10 }}>
        <section>
          <h3 style={{ marginBottom: 8 }}>Kho món ăn</h3>
          <input className="input" style={{ width: '100%', marginBottom: 8 }} placeholder="Tìm món: phở, ức gà, trứng…"
            value={query} onChange={e => setQuery(e.target.value)} />
          <div className="choices" style={{ marginBottom: 10 }}>
            {TAGS.map(t => (
              <button key={t.value} className={`choice ${tag === t.value ? 'on' : ''}`} style={{ padding: '4px 10px', fontSize: 13 }}
                onClick={() => setTag(t.value)}>{t.label}</button>
            ))}
          </div>
          <div className="inset" style={{ maxHeight: 430, overflowY: 'auto', padding: 0 }}>
            {shown.map(f => (
              <div key={f.id} style={{ display: 'grid', gridTemplateColumns: '1fr auto auto', gap: 10, alignItems: 'center', padding: '9px 12px', borderBottom: '1px solid #1f2125' }}>
                <div style={{ minWidth: 0 }}>
                  <div>{f.nameVi}</div>
                  <div className="dim" style={{ fontSize: 12.5 }}>
                    {f.serving} · {fmt(f.kcal)} kcal · <span style={{ color: f.proteinG >= 20 ? 'var(--accent)' : undefined }}>{f.proteinG} g đạm</span>
                  </div>
                </div>
                <select className="input" style={{ padding: '4px 6px' }} aria-label="Số phần" value={servings[f.id] ?? 1}
                  onChange={e => setServings(s => ({ ...s, [f.id]: Number(e.target.value) }))}>
                  {[0.5, 1, 1.5, 2, 3].map(n => <option key={n} value={n}>× {String(n).replace('.', ',')}</option>)}
                </select>
                <button className="btn small" disabled={busy || !day.open} onClick={() => add(f)}>Ghi</button>
              </div>
            ))}
            {shown.length === 0 && <p className="dim" style={{ padding: 14 }}>Không có món nào khớp.</p>}
          </div>
          <p className="dim" style={{ fontSize: 12 }}>Số liệu kcal và đạm là ước tính cho một phần ăn thông thường.</p>
        </section>

        <section>
          <h3 style={{ marginBottom: 8 }}>Đã ăn</h3>
          <div className="choices" style={{ marginBottom: 10 }}>
            <span className="dim" style={{ alignSelf: 'center', fontSize: 13 }}>Ghi vào bữa:</span>
            {SLOTS.map(s => (
              <button key={s.value} className={`choice ${slot === s.value ? 'on' : ''}`} style={{ padding: '4px 10px', fontSize: 13 }}
                onClick={() => setSlot(s.value)}>{s.label}</button>
            ))}
          </div>
          {SLOTS.map(s => {
            const items = day.logs.filter(l => l.slot === s.value)
            if (items.length === 0) return null
            return (
              <div key={s.value} style={{ marginBottom: 12 }}>
                <div className="pixel accent" style={{ fontSize: 18 }}>{s.label}</div>
                {items.map(l => (
                  <div key={l.id} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0', borderBottom: '1px solid #1f2125' }}>
                    <span style={{ flex: 1 }}>{l.food.nameVi} <span className="dim">× {String(l.servings).replace('.', ',')}</span></span>
                    <span className="pixel" style={{ fontSize: 17 }}>{fmt(l.kcal)} kcal · {l.proteinG} g</span>
                    {day.open && <button className="btn ghost small" style={{ padding: '0 8px' }} disabled={busy} onClick={() => remove(l.id)} aria-label="Xóa">✕</button>}
                  </div>
                ))}
              </div>
            )
          })}
          {day.logs.length === 0 && <p className="dim">Bấm "Ghi" ở món bên trái để thêm vào bữa đang chọn.</p>}
          <div className="dim" style={{ fontSize: 12.5, marginTop: 10 }}>
            Mục tiêu đã cộng thêm {fmt(day.targets.burnedKcal / 2)} kcal (một nửa năng lượng ước tính từ buổi tập hôm nay).
            Có thể sửa đến 4:00 sáng mai, lúc ngày được chốt sổ.
          </div>
        </section>
      </div>
    </Window>
  )
}

function Scale({ label, value, target, unit, zone }: { label: string; value: number; target: number; unit: string; zone?: boolean }) {
  const max = target * 1.4
  const pct = (v: number) => `${Math.min(100, (v / max) * 100)}%`
  const ok = zone ? Math.abs(value - target) <= target * 0.1 : value >= target * 0.9
  const color = ok ? 'var(--good)' : zone && value > target * 1.1 ? 'var(--hp)' : 'var(--accent)'
  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <span>{label}</span>
        <span className="pixel" style={{ fontSize: 24, color }}>{fmt(value)} <span className="dim" style={{ fontSize: 18 }}>/ {fmt(target)} {unit}</span></span>
      </div>
      <div className="bar" style={{ height: 14 }}>
        {zone && <span style={{ left: pct(target * 0.9), width: `calc(${pct(target * 1.1)} - ${pct(target * 0.9)})`, background: 'rgba(127,191,90,0.18)' }} />}
        <span style={{ width: pct(value), background: color, opacity: 0.85 }} />
        <span style={{ left: pct(target), width: 2, background: 'var(--text)' }} />
      </div>
    </div>
  )
}

async function fetchDelete(id: number) {
  await api.del(`/api/meals/${id}`)
}
