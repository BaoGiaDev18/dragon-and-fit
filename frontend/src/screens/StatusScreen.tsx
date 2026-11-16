import { AvatarSprite } from '../components/AvatarSprite'
import { Bar, Icon, Window } from '../components/ui'
import type { Skill, Status } from '../types'

const fmt = (n: number | null | undefined, digits = 1) =>
  n === null || n === undefined ? '—' : Number(n).toLocaleString('vi-VN', { maximumFractionDigits: digits })

/** Status page in the reference layout: portrait + attributes · skill wheel · real-life numbers (plan B4). */
export function StatusScreen({ status, onGoQuests, onDialog }: { status: Status; onGoQuests: () => void; onDialog: (d: 'metrics' | 'rest') => void }) {
  const c = status.character
  const remaining = status.today.required - status.today.done
  const bestDelta = Math.max(...status.stats.map(s => s.delta))

  return (
    <Window title="Trạng thái" style={{ width: '100%', maxWidth: 1040 }}>
      <div style={{ display: 'grid', gridTemplateColumns: '300px 1fr 300px', gap: 28 }}>
        {/* Left: portrait, XP, attributes */}
        <div>
          <div style={{ display: 'flex', gap: 14, alignItems: 'flex-start' }}>
            <div style={{ border: '1px solid var(--border-bright)', background: 'var(--bg-inset)', padding: 3, flex: 'none' }}>
              {c.avatar && <AvatarSprite sheetUrl={c.avatar.spriteSheetUrl} stage={c.evolutionStage} scale={2} />}
            </div>
            <div style={{ minWidth: 0, flex: 1 }}>
              <div className="char-name" style={{ fontSize: 26, lineHeight: 1.1 }}>{c.name}</div>
              <div className="pixel accent" style={{ fontSize: 17 }}>{c.title}</div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 6 }}>
                <div style={{ flex: 1 }}><Bar kind="xp" value={c.xpIntoLevel} max={c.xpForNext} /></div>
                <span className="pixel" style={{ fontSize: 22 }}>{c.level}</span>
              </div>
              <div className="dim" style={{ fontSize: 12.5 }}>
                XP hiện tại: {c.xpIntoLevel}/{c.xpForNext || 'MAX'}
              </div>
            </div>
          </div>

          {remaining > 0 || status.today.required === 0
            ? <button onClick={onGoQuests} className="notice" style={{ background: 'none', border: 'none', padding: 0, margin: '18px 0 10px', cursor: 'pointer' }}>
                {status.today.required === 0 ? `Nhiệm vụ mở lúc ${status.today.questTime}` : `Còn ${remaining} nhiệm vụ hôm nay`}
              </button>
            : <p className="notice" style={{ margin: '18px 0 10px', color: 'var(--good)' }}>Đã xong mọi nhiệm vụ hôm nay</p>}

          <div>
            {status.stats.map(s => (
              <div key={s.key} title={s.hint} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '7px 0', cursor: 'help' }}>
                <span style={{ width: 34, height: 34, display: 'grid', placeItems: 'center', border: '1px solid #3b3e43', background: 'var(--bg-inset)' }}>
                  <Icon name={s.key} />
                </span>
                <span style={{ flex: 1 }}>
                  {s.name} <span className="dim pixel" style={{ fontSize: 15 }}>{s.key}</span>
                  {s.delta > 0 && s.delta === bestDelta && <span className="accent" title="Tăng nhiều nhất tuần này"> ★</span>}
                </span>
                <span className="pixel" style={{ fontSize: 24, minWidth: 30, textAlign: 'right' }}>{s.value}</span>
                <span className="pixel" style={{ fontSize: 17, width: 30, textAlign: 'right', color: s.delta > 0 ? 'var(--good)' : 'var(--text-dim)' }}>
                  {s.delta > 0 ? `+${s.delta}` : '·'}
                </span>
              </div>
            ))}
          </div>
        </div>

        {/* Centre: skill wheel */}
        <div style={{ textAlign: 'center' }}>
          <h3>Sách Kỹ năng</h3>
          <div className="dim" style={{ fontSize: 13 }}>Bậc tiến hóa · Lv {c.nextEvolutionLevel || '—'} mở bậc {c.evolutionStage + 1}</div>
          <SkillWheel skills={status.skills} />
          <div style={{ display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
            <button className="btn" onClick={onGoQuests}>Nhận nhiệm vụ</button>
            <button className="btn ghost small" onClick={() => onDialog('metrics')}>
              {status.today.measured ? 'Sửa số đo' : 'Ghi số đo'}
            </button>
            <button className="btn ghost small" onClick={() => onDialog('rest')}>Dưỡng thương</button>
          </div>
          {!status.today.measured && <p className="notice" style={{ justifyContent: 'center' }}>Chưa ghi số đo hôm nay</p>}
        </div>

        {/* Right: real numbers */}
        <div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginBottom: 14 }}>
            <div className={c.hpState !== 'normal' ? 'hp-danger' : undefined}>
              <Bar kind="hp" label={c.hpState === 'dying' ? 'HP · Hấp hối' : c.hpState === 'critical' ? 'HP · Nguy kịch' : 'HP'} value={c.hp} max={c.maxHp} />
            </div>
            <Bar kind="mp" label="MP" value={c.mp} max={c.maxMp} />
          </div>
          {c.hpState !== 'normal' && (
            <p className="notice" style={{ color: '#ff7a6e', marginTop: 0 }}>
              {c.hpState === 'dying' ? 'Cảnh báo cuối: một ngày tệ nữa là tử trận.' : 'Máu thấp: nhiệm vụ đã được giảm độ khó để dễ có ngày hoàn hảo.'}
            </p>
          )}
          {c.restModeUntil && <p className="notice" style={{ marginTop: 0 }}>Đang dưỡng thương đến {new Date(c.restModeUntil).toLocaleDateString('vi-VN')}</p>}
          {[
            ['gem', 'Gem', fmt(status.gems, 0)],
            ['streak', 'Chuỗi ngày', `${c.streakDays}`],
            ['weight', 'Cân nặng', `${fmt(status.body.weightKg)} kg`],
            ['waist', 'Vòng bụng', `${fmt(status.body.waistCm)} cm`],
            ['steps', 'Bước/ngày (đầu vào)', fmt(status.targets.steps, 0)],
            ['kcal', 'Mục tiêu năng lượng', `${fmt(status.targets.kcal, 0)} kcal`],
            ['protein', 'Mục tiêu protein', `${status.targets.proteinG} g`],
          ].map(([icon, k, v]) => (
            <div key={k} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '6px 0' }}>
              <span style={{ color: 'var(--text)' }}><Icon name={icon} size={20} /></span>
              <span style={{ flex: 1 }}>{k}</span>
              <span className="pixel" style={{ fontSize: 21 }}>{v}</span>
            </div>
          ))}
        </div>
      </div>
    </Window>
  )
}

/** Six exercise groups around a hexagon; groups trained in the last 7 days glow amber. */
function SkillWheel({ skills }: { skills: Skill[] }) {
  const R = 110, C = 160
  const nodes = skills.map((s, i) => {
    const a = (-90 + i * 60) * (Math.PI / 180)
    return { ...s, x: C + R * Math.cos(a), y: C + R * Math.sin(a) }
  })
  const hex = Array.from({ length: 6 }, (_, i) => {
    const a = (-90 + i * 60) * (Math.PI / 180)
    return `${C + 38 * Math.cos(a)},${C + 38 * Math.sin(a)}`
  }).join(' ')

  return (
    <svg viewBox="0 0 320 320" style={{ width: '100%', maxWidth: 320, margin: '6px auto 10px', display: 'block' }} role="img" aria-label="Vòng kỹ năng">
      <defs>
        <filter id="glow" filterUnits="userSpaceOnUse" x="0" y="0" width="320" height="320">
          <feGaussianBlur stdDeviation="4" result="b" />
          <feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge>
        </filter>
      </defs>
      <circle cx={C} cy={C} r={R} fill="none" stroke="#3b3e43" strokeWidth="10" />
      <circle cx={C} cy={C} r={R} fill="none" stroke="#24262a" strokeWidth="6" />
      {nodes.map(n => (
        <line key={`l-${n.group}`} x1={C} y1={C} x2={n.x} y2={n.y}
          stroke={n.active ? 'var(--accent)' : '#2c2e33'} strokeWidth={n.active ? 3 : 2} filter={n.active ? 'url(#glow)' : undefined} />
      ))}
      <polygon points={hex} fill="var(--bg-inset)" stroke="var(--border-bright)" strokeWidth="2" />
      <path d={`M${C} ${C - 14}l11 5v8c0 8-5 12-11 15-6-3-11-7-11-15v-8z`} fill="none" stroke="var(--text)" strokeWidth="2" />
      {nodes.map(n => (
        <g key={n.group}>
          <circle cx={n.x} cy={n.y} r="30" fill="var(--bg-inset)"
            stroke={n.active ? 'var(--glow)' : '#4a4d52'} strokeWidth={n.active ? 3 : 2} filter={n.active ? 'url(#glow)' : undefined} />
          <text x={n.x} y={n.y - 2} textAnchor="middle" fontFamily="var(--font-body)" fontSize="12"
            fill={n.active ? 'var(--text)' : 'var(--text-dim)'}>{n.name}</text>
          <text x={n.x} y={n.y + 15} textAnchor="middle" fontFamily="var(--font-pixel)" fontSize="16"
            fill={n.active ? 'var(--glow)' : 'var(--locked)'}>{n.tier}/{n.maxTier}</text>
          {/* tier pips under the node, like the reference's small bars */}
          {Array.from({ length: n.maxTier }, (_, i) => (
            <rect key={i} x={n.x - 15 + i * 6.5} y={n.y + 34} width="5" height="3"
              fill={i < n.tier ? (n.active ? 'var(--accent)' : 'var(--border)') : '#2c2e33'} />
          ))}
        </g>
      ))}
    </svg>
  )
}
