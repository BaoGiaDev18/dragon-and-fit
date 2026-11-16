import { useState } from 'react'
import { api } from '../api'
import type { SettlementReport, Status } from '../types'
import { Keys, Window, useKey } from './ui'

function Modal({ title, width = 440, onClose, children }: { title: string; width?: number; onClose: () => void; children: React.ReactNode }) {
  useKey('Escape', onClose)
  return (
    <div className="overlay" onClick={onClose}>
      <div onClick={e => e.stopPropagation()}>
        <Window title={title} style={{ width }}>{children}</Window>
      </div>
    </div>
  )
}

/** Daily weigh-in: weight and waist feed VIT and the kcal target, sleep feeds healing, energy is MP. */
export function BodyMetricDialog({ status, onClose, onSaved }: { status: Status; onClose: () => void; onSaved: () => void }) {
  const [v, setV] = useState({
    weightKg: status.body.weightKg?.toString() ?? '', waistCm: status.body.waistCm?.toString() ?? '', sleepHours: '', energyScore: 0,
  })
  const [error, setError] = useState<string | null>(null)
  const num = (s: string) => (s.trim() === '' ? null : Number(s.replace(',', '.')))

  async function save() {
    try {
      await api.post('/api/body-metrics', {
        weightKg: num(v.weightKg), waistCm: num(v.waistCm), sleepHours: num(v.sleepHours), energyScore: v.energyScore || null,
      })
      onSaved()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Có lỗi xảy ra.')
    }
  }

  return (
    <Modal title="Ghi số đo hôm nay" onClose={onClose}>
      {([['weightKg', 'Cân nặng (buổi sáng)', 'kg'], ['waistCm', 'Vòng bụng, ngang rốn', 'cm'], ['sleepHours', 'Ngủ đêm qua', 'giờ']] as const).map(([k, label, unit]) => (
        <div className="field" key={k}>
          <label htmlFor={k}>{label}</label>
          <div className="input-unit">
            <input id={k} className="input" inputMode="decimal" value={v[k]} onChange={e => setV({ ...v, [k]: e.target.value })} />
            <span className="dim">{unit}</span>
          </div>
        </div>
      ))}
      <div className="field">
        <label>Năng lượng sáng nay (MP)</label>
        <div className="choices">
          {[1, 2, 3, 4, 5].map(n => (
            <button key={n} className={`choice ${v.energyScore === n ? 'on' : ''}`} onClick={() => setV({ ...v, energyScore: n })}>{n}</button>
          ))}
        </div>
        <span className="help">Ngủ từ 7 giờ trở lên giúp hồi thêm 2 máu vào ngày hoàn hảo. Vòng bụng lấy trung vị 3 lần đo gần nhất.</span>
      </div>
      {error && <p className="field err" role="alert">{error}</p>}
      <div style={{ display: 'flex', justifyContent: 'space-between' }}>
        <button className="btn ghost small" onClick={onClose}>Hủy</button>
        <button className="btn" onClick={save}>Lưu</button>
      </div>
    </Modal>
  )
}

/** Illness or injury: no damage, no healing, at most 7 days a month. */
export function RestModeDialog({ status, onClose, onSaved }: { status: Status; onClose: () => void; onSaved: () => void }) {
  const c = status.character
  const [days, setDays] = useState(Math.min(3, c.restDaysLeft) || 1)
  const [error, setError] = useState<string | null>(null)
  async function save(d: number) {
    try {
      await api.post('/api/character/rest-mode', { days: d })
      onSaved()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Có lỗi xảy ra.')
    }
  }
  return (
    <Modal title="Dưỡng thương" onClose={onClose}>
      <p>Khi ốm hoặc chấn thương: không mất máu, cũng không hồi máu, chuỗi ngày hoàn hảo được giữ nguyên. Bắt đầu từ hôm nay.</p>
      {c.restModeUntil ? (
        <>
          <p className="notice">Đang dưỡng thương đến hết ngày {new Date(c.restModeUntil).toLocaleDateString('vi-VN')}.</p>
          <button className="btn" onClick={() => save(0)}>Kết thúc sớm</button>
        </>
      ) : c.restDaysLeft === 0 ? (
        <p className="notice">Tháng này đã dùng hết 7 ngày dưỡng thương.</p>
      ) : (
        <>
          <div className="field">
            <label>Số ngày (còn {c.restDaysLeft} ngày trong tháng)</label>
            <div className="choices">
              {Array.from({ length: c.restDaysLeft }, (_, i) => i + 1).map(n => (
                <button key={n} className={`choice ${days === n ? 'on' : ''}`} onClick={() => setDays(n)}>{n}</button>
              ))}
            </div>
          </div>
          <p className="dim" style={{ fontSize: 13 }}>Nếu đau hoặc chóng mặt khi tập, hãy dừng lại và đi khám bác sĩ.</p>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <button className="btn ghost small" onClick={onClose}>Hủy</button>
            <button className="btn" onClick={() => save(days)}>Bắt đầu</button>
          </div>
        </>
      )}
      {error && <p className="field err" role="alert">{error}</p>}
    </Modal>
  )
}

const REASON_ICON: Record<string, string> = {
  NoFoodLog: '🍽', Overeat: '▲', Undereat: '▼', QuestMissed: '✖', QuestPartial: '◐', DailyCap: '⛨', PerfectDay: '✦', Sleep: '☾', RestMode: '✚',
}

/** The 4:00 settlement, shown once when the player comes back. */
export function MorningReport({ report, onClose }: { report: SettlementReport; onClose: () => void }) {
  useKey('Enter', onClose)
  const net = report.hpAfter - report.hpBefore
  const title = report.isRestMode ? 'Dưỡng thương' : report.isPerfect ? 'Ngày hoàn hảo' : net < 0 ? 'Báo cáo chiến trận' : 'Báo cáo đêm qua'
  return (
    <Modal title={title} width={480} onClose={onClose}>
      <div className="dim" style={{ textAlign: 'center' }}>
        Ngày {new Date(report.date).toLocaleDateString('vi-VN', { weekday: 'long', day: 'numeric', month: 'numeric' })}
        {report.unseenDays > 1 && ` · cùng ${report.unseenDays - 1} ngày trước đó chưa xem`}
      </div>
      <div className="pixel" style={{ textAlign: 'center', fontSize: 48, margin: '4px 0', color: net < 0 ? '#ff7a6e' : net > 0 ? 'var(--good)' : 'var(--text)' }}>
        HP {report.hpBefore} → {report.hpAfter}
      </div>
      <div className="inset" style={{ marginBottom: 12 }}>
        {report.lines.map((l, i) => (
          <div key={i} style={{ display: 'flex', gap: 10, padding: '4px 0' }}>
            <span style={{ width: 18, textAlign: 'center' }}>{REASON_ICON[l.reason] ?? '·'}</span>
            <span style={{ flex: 1 }}>{l.detail}</span>
            <span className="pixel" style={{ fontSize: 19, color: l.amount < 0 ? '#ff7a6e' : l.amount > 0 ? 'var(--good)' : 'var(--text-dim)' }}>
              {l.amount > 0 ? `+${l.amount}` : l.amount === 0 ? '·' : l.amount}
            </span>
          </div>
        ))}
        {report.lines.length === 0 && <div className="dim">Không mất máu, nhưng cũng chưa đủ điều kiện hồi máu.</div>}
      </div>
      <div className="dim" style={{ fontSize: 13, display: 'flex', justifyContent: 'space-between' }}>
        <span>Ăn {report.actualKcal.toLocaleString('vi-VN')} / {report.targetKcal.toLocaleString('vi-VN')} kcal</span>
        <span>Đạm {report.proteinG}/{report.proteinTargetG} g</span>
        <span>Nhiệm vụ {Math.round(report.questCompletion * 100)}%</span>
      </div>
      {!report.isPerfect && !report.isRestMode && (
        <p className="notice">Hồi máu cần ngày hoàn hảo: xong mọi nhiệm vụ bắt buộc, có ghi bữa ăn và ăn trong khoảng ±10% mục tiêu.</p>
      )}
      <div style={{ textAlign: 'center', marginTop: 12 }}><button className="btn" onClick={onClose}>Tiếp tục</button></div>
      <Keys hints={[['Enter', 'Tiếp tục']]} />
    </Modal>
  )
}
