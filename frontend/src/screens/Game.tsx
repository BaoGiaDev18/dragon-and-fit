import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { AvatarSprite } from '../components/AvatarSprite'
import { Keys, Toast, Window, useKey, useToast } from '../components/ui'
import type { QuestBoard as Board, Reward, Status } from '../types'
import { QuestBoard } from './QuestBoard'
import { StatusScreen } from './StatusScreen'

type Tab = 'status' | 'quests'

export function Game({ onLogout }: { onLogout: () => void }) {
  const [tab, setTab] = useState<Tab>('status')
  const [status, setStatus] = useState<Status | null>(null)
  const [board, setBoard] = useState<Board | null>(null)
  const [levelUp, setLevelUp] = useState<Reward | null>(null)
  const toast = useToast()

  const load = useCallback(async () => {
    // Board first: opening it generates today's quests, which the status counts.
    const b = await api.get<Board>('/api/quests/today')
    const s = await api.get<Status>('/api/status')
    setStatus(s)
    setBoard(b)
  }, [])

  useEffect(() => { load().catch(e => toast.show(e.message, true)) }, [load])

  useKey('1', () => setTab('status'))
  useKey('2', () => setTab('quests'))

  async function changed(reward?: Reward) {
    if (reward) {
      if (reward.levelAfter > reward.levelBefore || reward.evolvedToStage) setLevelUp(reward)
      else toast.show(`+${reward.xpGained} XP · +${reward.gemsGained} Gem${reward.questStatus === 'Partial' ? ' (hoàn thành một phần)' : ''}`)
    }
    await load()
  }

  if (!status || !board) return <div className="scene"><p className="pixel dim" style={{ fontSize: 24 }}>Đang mở cửa doanh trại…</p></div>

  const tabs: [Tab, string, string][] = [['status', 'Trạng thái', '1'], ['quests', 'Nhiệm vụ', '2']]
  return (
    <div className="scene" style={{ paddingTop: 26 }}>
      <header style={{ width: '100%', maxWidth: 1040, display: 'flex', alignItems: 'center', gap: 18, marginBottom: 40 }}>
        <span className="pixel accent" style={{ fontSize: 34 }}>Dragon<span style={{ color: 'var(--text)' }}>&amp;</span>Fit</span>
        <nav style={{ display: 'flex', gap: 8, flex: 1 }}>
          {tabs.map(([t, label, key]) => (
            <button key={t} className={`choice ${tab === t ? 'on' : ''}`} onClick={() => setTab(t)}>
              <span className="kbd" style={{ fontSize: 14 }}>{key}</span>{label}
            </button>
          ))}
          {['Thực đơn', 'Cửa hàng', 'Xếp hạng'].map(l => (
            <button key={l} className="choice" disabled style={{ opacity: 0.4, cursor: 'not-allowed' }} title="Mở ở giai đoạn sau">{l} 🔒</button>
          ))}
        </nav>
        <span className="pixel" style={{ fontSize: 22 }}><span className="accent">◆</span> {status.gems.toLocaleString('vi-VN')}</span>
        <button className="btn ghost small" onClick={onLogout}>Thoát</button>
      </header>

      {tab === 'status'
        ? <StatusScreen status={status} onGoQuests={() => setTab('quests')} />
        : <QuestBoard board={board} gems={status.gems} onChanged={changed} toast={toast.show} />}

      <Keys hints={[['1', 'Trạng thái'], ['2', 'Nhiệm vụ'], ['Esc', 'Đóng']]} />
      <Toast msg={toast.msg} onDone={toast.clear} />
      {levelUp && <LevelUp reward={levelUp} status={status} onClose={() => setLevelUp(null)} />}
    </div>
  )
}

function LevelUp({ reward, status, onClose }: { reward: Reward; status: Status; onClose: () => void }) {
  useKey('Escape', onClose)
  useKey('Enter', onClose)
  const c = status.character
  return (
    <div className="overlay" onClick={onClose}>
      <div onClick={e => e.stopPropagation()}>
        <Window title={reward.evolvedToStage ? 'Tiến hóa' : 'Level Up'} style={{ width: 420, textAlign: 'center' }}>
          <div style={{ display: 'flex', justifyContent: 'center', margin: '6px 0 10px' }}>
            {c.avatar && <AvatarSprite sheetUrl={c.avatar.spriteSheetUrl} stage={reward.evolvedToStage ?? c.evolutionStage} scale={3} />}
          </div>
          <div className="pixel" style={{ fontSize: 44, color: 'var(--glow)', textShadow: '0 0 18px rgba(255,182,77,0.5)' }}>
            Lv {reward.levelBefore} → {reward.levelAfter}
          </div>
          <div className="pixel" style={{ fontSize: 20 }}>+{reward.xpGained} XP · +{reward.gemsGained} Gem</div>
          {reward.newTitles.map(t => <p key={t} className="notice" style={{ justifyContent: 'center' }}>Danh hiệu mới: {t}</p>)}
          {reward.evolvedToStage && <p className="notice" style={{ justifyContent: 'center' }}>Hình hài tiến hóa lên bậc {reward.evolvedToStage}</p>}
          <button className="btn" onClick={onClose} style={{ marginTop: 10 }}>Tiếp tục</button>
        </Window>
      </div>
    </div>
  )
}
