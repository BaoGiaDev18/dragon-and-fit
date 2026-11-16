import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { AvatarSprite } from '../components/AvatarSprite'
import { BodyMetricDialog, MorningReport, RestModeDialog } from '../components/Dialogs'
import { Keys, Toast, Window, useKey, useToast } from '../components/ui'
import { getSound, onSoundChange, resumeMusicIfEnabled, setSound, sfx } from '../sound'
import type { Quest, QuestBoard as Board, Reward, Status } from '../types'
import { BattleScreen } from './BattleScreen'
import { ForgeScreen } from './ForgeScreen'
import { MealScreen } from './MealScreen'
import { QuestBoard } from './QuestBoard'
import { LeaderboardScreen } from './LeaderboardScreen'
import { ShopScreen } from './ShopScreen'
import { StatusScreen } from './StatusScreen'

type Tab = 'status' | 'quests' | 'meals' | 'shop' | 'forge' | 'ranks'
type DialogName = 'metrics' | 'rest' | null

export function Game({ onLogout }: { onLogout: () => void }) {
  const [tab, setTab] = useState<Tab>('status')
  const [status, setStatus] = useState<Status | null>(null)
  const [board, setBoard] = useState<Board | null>(null)
  const [levelUp, setLevelUp] = useState<Reward | null>(null)
  const [dialog, setDialog] = useState<DialogName>(null)
  const [reportClosed, setReportClosed] = useState(false)
  const [battle, setBattle] = useState<Quest | null>(null)
  const [sound, setSoundState] = useState(getSound())
  useEffect(() => onSoundChange(() => setSoundState(getSound())), [])
  // Browsers block audio until the first click; start the music then if it was switched on.
  useEffect(() => {
    const once = () => { resumeMusicIfEnabled(); window.removeEventListener('pointerdown', once) }
    window.addEventListener('pointerdown', once)
    return () => window.removeEventListener('pointerdown', once)
  }, [])
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
  useKey('3', () => setTab('meals'))
  useKey('4', () => setTab('shop'))
  useKey('5', () => setTab('forge'))
  useKey('6', () => setTab('ranks'))

  async function closeReport() {
    setReportClosed(true)
    await api.post('/api/character/report/seen').catch(() => undefined)
  }

  async function changed(reward?: Reward) {
    if (reward) {
      if (reward.levelAfter > reward.levelBefore || reward.evolvedToStage || reward.newTitles.length > 0) { sfx('levelup'); setLevelUp(reward) }
      else if (reward.gemsGained > 0) sfx('gem')
      if (reward.bonus && !(reward.levelAfter > reward.levelBefore || reward.evolvedToStage || reward.newTitles.length > 0)) window.setTimeout(() => toast.show(reward.bonus!), 3400)
      if (!(reward.levelAfter > reward.levelBefore || reward.evolvedToStage || reward.newTitles.length > 0)) toast.show(`+${reward.xpGained} XP · +${reward.gemsGained} Gem${reward.questStatus === 'Partial' ? ' (hoàn thành một phần)' : ''}`)
    }
    await load()
  }

  if (!status || !board) return <div className="scene"><p className="pixel dim" style={{ fontSize: 24 }}>Đang mở cửa doanh trại…</p></div>

  const tabs: [Tab, string, string][] = [['status', 'Trạng thái', '1'], ['quests', 'Nhiệm vụ', '2'], ['meals', 'Thực đơn', '3'], ['shop', 'Cửa hàng', '4'], ['forge', 'Lò rèn', '5'], ['ranks', 'Xếp hạng', '6']]
  return (
    <div className="scene" style={{ paddingTop: 26 }}>
      <header style={{ width: '100%', maxWidth: 1040, marginBottom: 40 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
          <span className="pixel accent" style={{ fontSize: 34, flex: 1 }}>Dragon<span style={{ color: 'var(--text)' }}>&amp;</span>Fit</span>
          <span className="pixel" style={{ fontSize: 22, marginRight: 8 }}><span className="accent">◆</span> {status.gems.toLocaleString('vi-VN')}</span>
          <button className="btn ghost small" title="Hiệu ứng âm thanh" aria-pressed={sound.sfx}
            onClick={() => setSound({ sfx: !sound.sfx })}>{sound.sfx ? '♪ Tiếng: bật' : '♪ Tiếng: tắt'}</button>
          <button className="btn ghost small" title="Nhạc nền" aria-pressed={sound.music}
            onClick={() => setSound({ music: !sound.music })}>{sound.music ? '♫ Nhạc: bật' : '♫ Nhạc: tắt'}</button>
          <button className="btn ghost small" onClick={onLogout}>Thoát</button>
        </div>
        <nav style={{ display: 'flex', gap: 8 }}>
          {tabs.map(([t, label, key]) => (
            <button key={t} className={`choice ${tab === t ? 'on' : ''}`} style={{ whiteSpace: 'nowrap' }} onClick={() => { sfx('click'); setTab(t) }}>
              <span className="kbd" style={{ fontSize: 14 }}>{key}</span>{label}
            </button>
          ))}
        </nav>
      </header>

      {tab === 'status' && <StatusScreen status={status} onGoQuests={() => setTab('quests')} onDialog={setDialog} />}
      {tab === 'quests' && <QuestBoard board={board} gems={status.gems} onChanged={changed} toast={toast.show} onGoMeals={() => setTab('meals')} onBattle={setBattle} />}
      {tab === 'shop' && <ShopScreen onChanged={() => void load()} toast={toast.show} />}
      {tab === 'forge' && <ForgeScreen onChanged={() => void load()} toast={toast.show} />}
      {tab === 'ranks' && <LeaderboardScreen toast={toast.show} />}
      {tab === 'meals' && <MealScreen onReward={r => changed(r)} toast={toast.show} />}

      <Keys hints={[['1–6', 'Chuyển màn hình'], ['Enter', 'Xác nhận'], ['Esc', 'Đóng']]} />
      {battle && status.character.avatar && (
        <BattleScreen quest={battle} sheetUrl={status.character.avatar.spriteSheetUrl} stage={status.character.evolutionStage}
          proofRequired={board.proofRequired ?? true} onClose={() => setBattle(null)} onDone={r => { setBattle(null); void changed(r) }} />
      )}
      <Toast msg={toast.msg} onDone={toast.clear} />
      {levelUp && <LevelUp reward={levelUp} status={status} onClose={() => setLevelUp(null)} />}
      {status.report && !reportClosed && !levelUp && <MorningReport report={status.report} onClose={closeReport} />}
      {dialog === 'metrics' && <BodyMetricDialog status={status} onClose={() => setDialog(null)} onSaved={async () => { setDialog(null); toast.show('Đã lưu số đo hôm nay'); await load() }} />}
      {dialog === 'rest' && <RestModeDialog status={status} onClose={() => setDialog(null)} onSaved={async () => { setDialog(null); await load() }} />}
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
        <Window title={reward.evolvedToStage ? 'Tiến hóa' : reward.levelAfter > reward.levelBefore ? 'Level Up' : 'Danh hiệu mới'} style={{ width: 420, textAlign: 'center' }}>
          <div style={{ display: 'flex', justifyContent: 'center', margin: '6px 0 10px' }}>
            {c.avatar && <AvatarSprite sheetUrl={c.avatar.spriteSheetUrl} stage={reward.evolvedToStage ?? c.evolutionStage} scale={3} />}
          </div>
          <div className="pixel" style={{ fontSize: 44, color: 'var(--glow)', textShadow: '0 0 18px rgba(255,182,77,0.5)' }}>
            {reward.levelAfter > reward.levelBefore ? `Lv ${reward.levelBefore} → ${reward.levelAfter}` : reward.newTitles[0]}
          </div>
          <div className="pixel" style={{ fontSize: 20 }}>+{reward.xpGained} XP · +{reward.gemsGained} Gem</div>
          {reward.levelAfter > reward.levelBefore && reward.newTitles.map(t => <p key={t} className="notice" style={{ justifyContent: 'center' }}>Danh hiệu mới: {t}</p>)}
          {reward.bonus && <p className="notice" style={{ justifyContent: 'center' }}>{reward.bonus}</p>}
          {reward.evolvedToStage && <p className="notice" style={{ justifyContent: 'center' }}>Hình hài tiến hóa lên bậc {reward.evolvedToStage}</p>}
          <button className="btn" onClick={onClose} style={{ marginTop: 10 }}>Tiếp tục</button>
        </Window>
      </div>
    </div>
  )
}
