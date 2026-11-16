import { useEffect, useState } from 'react'
import { api } from '../api'
import { AvatarSprite } from '../components/AvatarSprite'
import { Window } from '../components/ui'
import type { Leaderboard, LeaderRow } from '../types'

const BOARDS = [
  { value: 'week', label: 'XP tuần này', unit: 'XP' },
  { value: 'level', label: 'Level', unit: 'XP' },
  { value: 'streak', label: 'Chuỗi ngày', unit: 'ngày' },
]

/** Hall of fame (plan B2). Shows name, avatar, level and title only — never body data. */
export function LeaderboardScreen({ toast }: { toast: (t: string, err?: boolean) => void }) {
  const [board, setBoard] = useState('week')
  const [data, setData] = useState<Leaderboard | null>(null)

  useEffect(() => {
    api.get<Leaderboard>(`/api/leaderboard?board=${board}`).then(setData).catch(e => toast(e.message, true))
  }, [board, toast])

  const unit = BOARDS.find(b => b.value === board)!.unit
  const value = (r: LeaderRow) => (board === 'level' ? `Lv ${r.level}` : `${r.score.toLocaleString('vi-VN')} ${unit}`)

  return (
    <Window title="Bảng vinh danh" style={{ width: '100%', maxWidth: 820 }}>
      <div className="choices" style={{ marginBottom: 14 }}>
        {BOARDS.map(b => (
          <button key={b.value} className={`choice ${board === b.value ? 'on' : ''}`} onClick={() => setBoard(b.value)}>{b.label}</button>
        ))}
        <span className="dim" style={{ alignSelf: 'center', marginLeft: 'auto', fontSize: 13 }}>
          {data && `${data.players} chiến binh`}{board === 'week' && data && ` · từ thứ Hai ${new Date(data.weekStart).toLocaleDateString('vi-VN')}`}
        </span>
      </div>
      {data && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {data.top.map(r => <Row key={`${r.rank}-${r.name}`} r={r} value={value(r)} />)}
          {data.me && data.me.rank > data.top.length && (
            <>
              <div className="dim" style={{ textAlign: 'center' }}>· · ·</div>
              <Row r={data.me} value={value(data.me)} />
            </>
          )}
        </div>
      )}
      <p className="dim" style={{ fontSize: 12.5, marginBottom: 0 }}>Bảng này chỉ hiện tên, hình hài, level và danh hiệu. Số đo cơ thể và bữa ăn luôn là bí mật của riêng ngươi.</p>
    </Window>
  )
}

function Row({ r, value }: { r: LeaderRow; value: string }) {
  const medal = r.rank === 1 ? 'var(--glow)' : r.rank === 2 ? 'var(--border-bright)' : r.rank === 3 ? '#c27a45' : 'var(--text-dim)'
  return (
    <div className="inset" style={{
      display: 'grid', gridTemplateColumns: '48px 72px 1fr auto', alignItems: 'center', gap: 12, padding: '6px 12px',
      borderColor: r.isMe ? 'var(--accent)' : undefined,
    }}>
      <span className="pixel" style={{ fontSize: 30, color: medal, textAlign: 'center' }}>{r.rank}</span>
      {r.sprite ? <AvatarSprite sheetUrl={r.sprite} stage={r.stage} scale={1} animate={false} /> : <span />}
      <div style={{ minWidth: 0 }}>
        <div className="char-name" style={{ fontSize: 20 }}>{r.name}{r.isMe && <span className="accent" style={{ fontFamily: 'var(--font-body)', fontSize: 13 }}> · ngươi</span>}</div>
        <div className="pixel accent" style={{ fontSize: 15 }}>{r.title} · Lv {r.level}</div>
      </div>
      <span className="pixel" style={{ fontSize: 24 }}>{value}</span>
    </div>
  )
}
