import { useCallback, useState } from 'react'
import { api, compressPhoto } from '../api'
import { Keys, Window, useKey } from '../components/ui'
import type { Quest, QuestBoard as Board, Reward } from '../types'

const unitLabel = (u: string) => ({ reps: 'rep', seconds: 'giây', steps: 'bước', g: 'g' }[u] ?? u)

export function QuestBoard({ board, gems, onChanged, toast }: {
  board: Board; gems: number; onChanged: (reward?: Reward) => void; toast: (t: string, err?: boolean) => void
}) {
  const [reporting, setReporting] = useState<Quest | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  async function reroll(q: Quest) {
    setBusy(q.id)
    try {
      await api.post(`/api/quests/${q.id}/reroll`)
      toast(`Đã đổi nhiệm vụ (−${board.rerollCost} Gem)`)
      onChanged()
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Có lỗi xảy ra.', true)
    } finally {
      setBusy(null)
    }
  }

  const done = board.quests.filter(q => q.status === 'Completed').length

  return (
    <Window title="Bảng nhiệm vụ" style={{ width: '100%', maxWidth: 820 }}>
      {board.locked ? (
        <div className="inset" style={{ textAlign: 'center', padding: 34 }}>
          <div className="pixel" style={{ fontSize: 26 }}>Bảng nhiệm vụ còn đóng</div>
          <div className="dim">Người Quản Trò sẽ giao nhiệm vụ lúc <span className="accent">{board.unlockAt}</span> sáng nay.</div>
        </div>
      ) : (
        <>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 12 }}>
            <span className="dim">Hôm nay · {new Date(board.date).toLocaleDateString('vi-VN', { weekday: 'long', day: 'numeric', month: 'numeric' })}</span>
            <span className="pixel" style={{ fontSize: 22 }}>{done}/{board.quests.length} hoàn thành</span>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {board.quests.map(q => {
              const finished = q.status === 'Completed'
              return (
                <article key={q.id} className="inset" style={{
                  display: 'grid', gridTemplateColumns: '1fr auto', gap: 14, alignItems: 'center',
                  borderColor: finished ? 'rgba(127,191,90,0.45)' : q.status === 'Partial' ? 'rgba(240,163,58,0.45)' : undefined,
                  opacity: finished ? 0.75 : 1,
                }}>
                  <div style={{ minWidth: 0 }}>
                    <div style={{ display: 'flex', gap: 8, alignItems: 'baseline' }}>
                      <span className="pixel" style={{ fontSize: 22, color: finished ? 'var(--good)' : 'var(--text)' }}>
                        {finished ? '✔ ' : q.status === 'Partial' ? '◐ ' : '◇ '}{q.title}
                      </span>
                      {q.exercise && <span className="dim pixel" style={{ fontSize: 15 }}>Bậc {q.exercise.tier}</span>}
                    </div>
                    <div className="dim" style={{ fontStyle: 'italic', fontSize: 13.5 }}>{q.narration}</div>
                    {q.exercise?.formTips && <div style={{ fontSize: 12.5, marginTop: 3 }}>Mẹo: {q.exercise.formTips}</div>}
                    {q.status === 'Partial' && <div className="accent" style={{ fontSize: 12.5 }}>Đã báo {q.doneValue}/{q.targetValue} {unitLabel(q.unit)}</div>}
                  </div>
                  <div style={{ textAlign: 'right' }}>
                    <div className="pixel" style={{ fontSize: 19 }}>
                      <span style={{ color: '#3fb3a8' }}>+{q.xp} XP</span> · <span className="accent">+{q.gems} Gem</span>
                    </div>
                    {!finished && (
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', marginTop: 6 }}>
                        {q.canReroll && (
                          <button className="btn ghost small" disabled={busy === q.id || gems < (board.rerollCost ?? 20)}
                            title={`Đổi bài khác cùng nhóm (${board.rerollCost} Gem)`} onClick={() => reroll(q)}>Đổi</button>
                        )}
                        <button className="btn small" onClick={() => setReporting(q)}>Báo cáo</button>
                      </div>
                    )}
                  </div>
                </article>
              )
            })}
          </div>
        </>
      )}
      {reporting && (
        <ReportDialog quest={reporting} proofRequired={board.proofRequired ?? true} onClose={() => setReporting(null)}
          onDone={r => { setReporting(null); onChanged(r) }} />
      )}
    </Window>
  )
}

function ReportDialog({ quest, proofRequired, onClose, onDone }: {
  quest: Quest; proofRequired: boolean; onClose: () => void; onDone: (r: Reward) => void
}) {
  const [value, setValue] = useState(String(quest.targetValue))
  const [photo, setPhoto] = useState<Blob | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useKey('Escape', onClose)

  async function pick(file: File | undefined) {
    if (!file) return
    setError(null)
    try {
      const blob = await compressPhoto(file)
      setPhoto(blob)
      if (preview) URL.revokeObjectURL(preview)
      setPreview(URL.createObjectURL(blob))
    } catch {
      setError('Không đọc được ảnh này.')
    }
  }

  const submit = useCallback(async () => {
    const n = Number(value)
    if (!Number.isInteger(n) || n <= 0) return setError('Nhập số nguyên lớn hơn 0.')
    if (proofRequired && !photo) return setError('Cần ảnh chụp làm bằng chứng.')
    setBusy(true)
    setError(null)
    const form = new FormData()
    form.append('doneValue', String(n))
    if (photo) form.append('photo', photo, 'proof.webp')
    try {
      onDone(await api.post<Reward>(`/api/quests/${quest.id}/complete`, form))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Có lỗi xảy ra.')
      setBusy(false)
    }
  }, [value, photo, proofRequired, quest.id, onDone])

  return (
    <div className="overlay" onClick={onClose}>
      <div onClick={e => e.stopPropagation()}>
        <Window title="Báo cáo nhiệm vụ" style={{ width: 460 }}>
          <div className="pixel" style={{ fontSize: 22, marginBottom: 12 }}>{quest.title}</div>
          <div className="field">
            <label htmlFor="done">
              {quest.unit === 'reps' || quest.unit === 'seconds' ? `Số ${unitLabel(quest.unit)} mỗi set đã làm` : `Đã đạt bao nhiêu ${unitLabel(quest.unit)}`}
            </label>
            <div className="input-unit">
              <input id="done" className="input" inputMode="numeric" autoFocus value={value} onChange={e => setValue(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') submit() }} />
              <span className="dim">/ {quest.targetValue} {unitLabel(quest.unit)}</span>
            </div>
            <span className="help">Làm thiếu vẫn báo được: phần thưởng tính theo tỉ lệ, báo lại khi làm thêm.</span>
          </div>
          <div className="field">
            <label htmlFor="photo">Ảnh bằng chứng{proofRequired && <span className="accent"> *</span>}</label>
            <input id="photo" type="file" accept="image/*" capture="environment" onChange={e => pick(e.target.files?.[0])} />
            <span className="help">Ảnh được nén nhỏ trước khi gửi và chỉ mình bạn xem được.</span>
            {preview && <img src={preview} alt="Ảnh bằng chứng" style={{ maxHeight: 160, objectFit: 'contain', border: '1px solid #3b3e43', marginTop: 6 }} />}
          </div>
          {error && <p className="field err" role="alert">{error}</p>}
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <button className="btn ghost small" onClick={onClose}>Hủy</button>
            <button className="btn" disabled={busy} onClick={submit}>Xác nhận</button>
          </div>
          <Keys hints={[['Enter', 'Xác nhận'], ['Esc', 'Đóng']]} />
        </Window>
      </div>
    </div>
  )
}
