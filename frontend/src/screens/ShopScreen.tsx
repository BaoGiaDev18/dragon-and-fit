import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { Icon, Window } from '../components/ui'
import { sfx } from '../sound'

type Item = { id: number; code: string | null; name: string; description: string | null; kind: string; size: string | null; price: number; timesBought: number; lastBought: string | null }
type Shop = { gems: number; restPasses: number; restPassToday: boolean; items: Item[]; history: { id: number; name: string; price: number; purchasedAt: string; usedForDate: string | null }[] }

const SIZE = { small: 'Nhỏ', medium: 'Vừa', large: 'Lớn' } as Record<string, string>

/** Town shop (plan B2): Gems buy rest passes and the real-life rewards the player set at character creation. */
export function ShopScreen({ onChanged, toast }: { onChanged: () => void; toast: (t: string, err?: boolean) => void }) {
  const [shop, setShop] = useState<Shop | null>(null)
  const [confirm, setConfirm] = useState<Item | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => api.get<Shop>('/api/shop').then(setShop), [])
  useEffect(() => { load().catch(e => toast(e.message, true)) }, [load, toast])

  async function buy(item: Item) {
    setBusy(true)
    try {
      await api.post(`/api/shop/${item.id}/buy`)
      sfx('gem')
      toast(item.kind === 'personal' ? `Đã đổi "${item.name}". Hãy tự thưởng cho mình!` : `Đã mua ${item.name}`)
      setConfirm(null)
      await load()
      onChanged()
    } catch (e) {
      sfx('error')
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      setBusy(false)
    }
  }

  async function usePass() {
    setBusy(true)
    try {
      await api.post('/api/shop/rest-pass/use')
      sfx('heal')
      toast('Đã dùng vé nghỉ cho hôm nay')
      await load()
      onChanged()
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      setBusy(false)
    }
  }

  if (!shop) return <p className="pixel dim" style={{ fontSize: 24 }}>Đang mở cửa hàng…</p>

  const groups: [string, string][] = [['utility', 'Vật phẩm'], ['personal', 'Phần thưởng đời thực']]
  return (
    <Window title="Cửa hàng" style={{ width: '100%', maxWidth: 900 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
        <span className="dim">Gem kiếm từ nhiệm vụ. Phần thưởng đời thực do chính ngươi đặt khi tạo nhân vật.</span>
        <span className="pixel" style={{ fontSize: 26 }}><span className="accent">◆</span> {shop.gems.toLocaleString('vi-VN')}</span>
      </div>

      <div className="inset" style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 16 }}>
        <Icon name="DIS" />
        <span style={{ flex: 1 }}>
          Vé nghỉ trong túi: <b className="pixel" style={{ fontSize: 20 }}>{shop.restPasses}</b>
          {shop.restPassToday && <span className="accent"> · đang dùng cho hôm nay</span>}
        </span>
        <button className="btn small" disabled={busy || shop.restPasses === 0 || shop.restPassToday} onClick={usePass}>Dùng hôm nay</button>
      </div>

      {groups.map(([kind, label]) => {
        const items = shop.items.filter(i => i.kind === kind)
        if (items.length === 0) return null
        return (
          <section key={kind} style={{ marginBottom: 16 }}>
            <h3 style={{ marginBottom: 8 }}>{label}</h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 10 }}>
              {items.map(i => {
                const affordable = shop.gems >= i.price
                return (
                  <article key={i.id} className="inset" style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                      <span style={{ fontWeight: 500 }}>{i.name}</span>
                      {i.size && <span className="dim pixel" style={{ fontSize: 15 }}>{SIZE[i.size]}</span>}
                    </div>
                    {i.description && <span className="dim" style={{ fontSize: 13 }}>{i.description}</span>}
                    {i.timesBought > 0 && <span className="dim" style={{ fontSize: 12.5 }}>Đã {kind === 'personal' ? 'đổi' : 'mua'} {i.timesBought} lần</span>}
                    {!affordable && (
                      <div className="bar xp" title={`${shop.gems}/${i.price} Gem`}>
                        <span style={{ width: `${Math.min(100, (shop.gems / i.price) * 100)}%` }} />
                      </div>
                    )}
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 'auto' }}>
                      <span className="pixel" style={{ fontSize: 22, color: affordable ? 'var(--accent)' : 'var(--text-dim)' }}>◆ {i.price.toLocaleString('vi-VN')}</span>
                      <button className="btn small" disabled={!affordable || busy} onClick={() => setConfirm(i)}>{kind === 'personal' ? 'Đổi' : 'Mua'}</button>
                    </div>
                  </article>
                )
              })}
            </div>
          </section>
        )
      })}

      {shop.history.length > 0 && (
        <details>
          <summary className="dim" style={{ cursor: 'pointer' }}>Lịch sử mua</summary>
          {shop.history.map(h => (
            <div key={h.id} style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13.5, padding: '3px 0' }}>
              <span>{h.name}{h.usedForDate && <span className="dim"> · đã dùng {new Date(h.usedForDate).toLocaleDateString('vi-VN')}</span>}</span>
              <span className="dim">{new Date(h.purchasedAt).toLocaleDateString('vi-VN')} · −{h.price.toLocaleString('vi-VN')}</span>
            </div>
          ))}
        </details>
      )}

      {confirm && (
        <div className="overlay" onClick={() => setConfirm(null)}>
          <div onClick={e => e.stopPropagation()}>
            <Window title="Xác nhận" style={{ width: 400, textAlign: 'center' }}>
              <p>{confirm.kind === 'personal' ? 'Đổi' : 'Mua'} <b>{confirm.name}</b> với giá <span className="accent pixel" style={{ fontSize: 20 }}>◆ {confirm.price.toLocaleString('vi-VN')}</span>?</p>
              <p className="dim" style={{ fontSize: 13 }}>Còn lại {(shop.gems - confirm.price).toLocaleString('vi-VN')} Gem.</p>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <button className="btn ghost small" onClick={() => setConfirm(null)}>Hủy</button>
                <button className="btn" disabled={busy} onClick={() => buy(confirm)}>Xác nhận</button>
              </div>
            </Window>
          </div>
        </div>
      )}
    </Window>
  )
}
