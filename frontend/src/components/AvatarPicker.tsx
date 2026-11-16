import { AvatarSprite } from './AvatarSprite'
import type { AvatarInfo } from '../types'

/** Avatar grid plus a preview of the six evolution stages (locked ones as silhouettes). */
export function AvatarPicker({ avatars, value, onChange }: { avatars: AvatarInfo[]; value?: string; onChange: (v: string) => void }) {
  const selected = avatars.find(a => a.code === value) ?? avatars[0]
  return (
    <div>
      <div className="choices">
        {avatars.map(a => (
          <button key={a.code} type="button" onClick={() => onChange(a.code)} aria-pressed={a.code === selected?.code}
            className={`choice ${a.code === selected?.code ? 'on' : ''}`}
            style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4, padding: 10 }}>
            <AvatarSprite sheetUrl={a.spriteSheetUrl} scale={2} />
            <span>{a.name}</span>
          </button>
        ))}
      </div>
      {selected && (
        <div className="inset" style={{ marginTop: 12 }}>
          <div className="dim" style={{ fontSize: 13, marginBottom: 8 }}>Các bậc tiến hóa — mở khóa khi lên level</div>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
            {selected.evolutions.map(e => (
              <div key={e.stage} style={{ textAlign: 'center' }}>
                <AvatarSprite sheetUrl={selected.spriteSheetUrl} stage={e.stage} scale={1} silhouette={e.stage > 1} animate={false} />
                <div className="pixel" style={{ fontSize: 16, color: e.stage === 1 ? 'var(--accent)' : 'var(--text-dim)' }}>Lv {e.minLevel}</div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
