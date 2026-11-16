import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { sfx } from '../sound'
import type { Food, MenuDraw, MenuItem, MenuToday, Reward } from '../types'

const SLOT_LABEL: Record<string, string> = { breakfast: 'Sáng', lunch: 'Trưa', dinner: 'Tối', snack: 'Ăn vặt' }
const RARITY_LABEL: Record<string, string> = { Common: 'THƯỜNG', Rare: 'HIẾM', Epic: 'SỬ THI' }
const SERVINGS = [0.5, 1, 1.5, 2, 3]
const fmt = (n: number) => Math.round(n).toLocaleString('vi-VN')

/**
 * Daily menu gacha (plan B3): one free roll each morning, rerolls cost Gems. Before confirming,
 * the player tunes the menu on the nutrition scale; after confirming, each dish is logged with one tap.
 */
export function GachaPanel({ foods, onLogged, toast }: { foods: Food[]; onLogged: (r: Reward | null) => void; toast: (t: string, err?: boolean) => void }) {
  const [menu, setMenu] = useState<MenuToday | null>(null)
  const [spinning, setSpinning] = useState(false)
  const [busy, setBusy] = useState(false)
  const [adding, setAdding] = useState('')

  const load = useCallback(() => api.get<MenuToday>('/api/menu/today').then(setMenu), [])
  useEffect(() => { load().catch(e => toast(e.message, true)) }, [load, toast])

  async function roll() {
    setBusy(true)
    setSpinning(true)
    const ticks = window.setInterval(() => sfx('click'), 110)
    try {
      const [res] = await Promise.all([api.post<MenuToday>('/api/menu/roll'), new Promise(r => setTimeout(r, 1100))])
      setMenu(res)
      const epic = res.draw?.items.some(i => i.rarity === 'Epic')
      sfx(epic ? 'levelup' : 'gem')
    } catch (e) {
      sfx('error')
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      window.clearInterval(ticks)
      setSpinning(false)
      setBusy(false)
    }
  }

  async function update(draw: MenuDraw, items: { foodId: number; servings: number; slot: string }[]) {
    setBusy(true)
    try {
      const d = await api.put<MenuDraw>(`/api/menu/${draw.id}/items`, items)
      setMenu(m => (m ? { ...m, draw: d } : m))
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      setBusy(false)
    }
  }

  const asInput = (items: MenuItem[]) => items.map(i => ({ foodId: i.food.id, servings: i.servings, slot: i.slot }))

  async function confirm(draw: MenuDraw) {
    setBusy(true)
    try {
      const d = await api.post<MenuDraw>(`/api/menu/${draw.id}/confirm`)
      sfx('confirm')
      setMenu(m => (m ? { ...m, draw: d } : m))
    } finally {
      setBusy(false)
    }
  }

  async function log(item: MenuItem) {
    setBusy(true)
    try {
      const res = await api.post<{ reward: Reward | null }>(`/api/menu/items/${item.id}/log`)
      await load()
      onLogged(res.reward)
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      setBusy(false)
    }
  }

  if (!menu) return null
  const d = menu.draw
  const editable = d?.status === 'Rolled'
  const kcalOff = d ? d.totals.kcal - menu.targets.kcal : 0

  return (
    <section className="inset" style={{ marginBottom: 18 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: d ? 12 : 0 }}>
        <div style={{ flex: 1 }}>
          <h3 style={{ color: 'var(--text)' }}>Vòng quay thực đơn</h3>
          <div className="dim" style={{ fontSize: 13 }}>
            Bốc thực đơn cả ngày quanh mục tiêu {fmt(menu.targets.kcal)} kcal và {menu.targets.proteinG} g đạm. Món càng giàu đạm càng hiếm.
          </div>
        </div>
        <button className="btn" disabled={busy || d?.status === 'Confirmed'} onClick={roll}>
          {menu.freeRoll ? 'Quay miễn phí' : `Quay lại · ◆ ${menu.rerollCost}`}
        </button>
      </div>

      {d && (
        <div className={spinning ? 'gacha-spin' : ''}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 8 }}>
            {d.items.map(item => (
              <div key={item.id} className={`gacha-card ${item.rarity}`}>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span className="pixel dim" style={{ fontSize: 15 }}>{SLOT_LABEL[item.slot]}</span>
                  <span className="rarity">{RARITY_LABEL[item.rarity]}</span>
                </div>
                <div style={{ fontWeight: 500 }}>{spinning ? '· · ·' : item.food.nameVi}</div>
                <div className="dim" style={{ fontSize: 12.5 }}>{fmt(item.kcal)} kcal · {item.proteinG} g đạm</div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 6 }}>
                  {editable ? (
                    <>
                      <select className="input" style={{ padding: '2px 6px' }} value={item.servings} aria-label="Số phần"
                        onChange={e => update(d, asInput(d.items).map((x, j) => (d.items[j].id === item.id ? { ...x, servings: Number(e.target.value) } : x)))}>
                        {SERVINGS.map(n => <option key={n} value={n}>× {String(n).replace('.', ',')}</option>)}
                      </select>
                      <span style={{ flex: 1 }} />
                      <button className="btn ghost small" style={{ padding: '0 8px' }} disabled={busy} aria-label="Bỏ món"
                        onClick={() => update(d, asInput(d.items.filter(x => x.id !== item.id)))}>✕</button>
                    </>
                  ) : item.logged ? (
                    <span className="accent" style={{ fontSize: 13 }}>✔ Đã ăn</span>
                  ) : (
                    <button className="btn small" disabled={busy} onClick={() => log(item)}>Đã ăn</button>
                  )}
                </div>
              </div>
            ))}
          </div>

          {/* Nutrition scale: totals against the target, same ±10% rule as the settlement */}
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 14, alignItems: 'center', marginTop: 12 }}>
            <span className="pixel" style={{ fontSize: 20, color: d.onTarget ? 'var(--good)' : '#ff7a6e' }}>
              {fmt(d.totals.kcal)} / {fmt(menu.targets.kcal)} kcal
            </span>
            <span className="pixel" style={{ fontSize: 20, color: d.proteinOk ? 'var(--good)' : 'var(--accent)' }}>
              {Math.round(d.totals.proteinG)} / {menu.targets.proteinG} g đạm
            </span>
            <span className="dim" style={{ fontSize: 13 }}>≈ {fmt(d.totals.priceVnd)} đ</span>
            {!d.onTarget && <span className="notice" style={{ color: '#ff7a6e' }}>{kcalOff > 0 ? 'Vượt' : 'Thiếu'} quá 10% mục tiêu: ăn đúng thực đơn này sẽ mất máu</span>}
            {editable && (
              <>
                <span style={{ flex: 1 }} />
                <select className="input" style={{ maxWidth: 220 }} value={adding} onChange={e => setAdding(e.target.value)} aria-label="Thêm món">
                  <option value="">+ Thêm món…</option>
                  {foods.map(f => <option key={f.id} value={f.id}>{f.nameVi} ({fmt(f.kcal)} kcal)</option>)}
                </select>
                <select className="input" id="add-slot" defaultValue="snack" aria-label="Vào bữa">
                  {Object.entries(SLOT_LABEL).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                </select>
                <button className="btn ghost small" disabled={!adding || busy} onClick={() => {
                  const slot = (document.getElementById('add-slot') as HTMLSelectElement).value
                  void update(d, [...asInput(d.items), { foodId: Number(adding), servings: 1, slot }])
                  setAdding('')
                }}>Thêm</button>
                <button className="btn small" disabled={busy || d.items.length === 0} onClick={() => confirm(d)}>Chốt thực đơn</button>
              </>
            )}
          </div>
        </div>
      )}
    </section>
  )
}
