import { useEffect, useRef } from 'react'
import { AnimatedSprite, Application, Assets, Rectangle, Texture } from 'pixi.js'

const FRAME = 64
const FRAMES = 4

/**
 * Plays one evolution stage (one row) of a 64×64 sprite sheet with PixiJS.
 * Integer scaling with nearest-neighbour keeps the pixels crisp (plan B4).
 */
export function AvatarSprite({ sheetUrl, stage = 1, scale = 3, silhouette = false, animate = true }: {
  sheetUrl: string; stage?: number; scale?: number; silhouette?: boolean; animate?: boolean
}) {
  const host = useRef<HTMLDivElement>(null)
  const size = FRAME * scale
  const still = !animate

  useEffect(() => {
    if (still) return
    let cancelled = false
    let app: Application | null = null
    ;(async () => {
      const a = new Application()
      await a.init({ width: size, height: size, backgroundAlpha: 0, antialias: false, preference: 'webgl' })
      if (cancelled) { a.destroy(true); return }
      app = a
      host.current?.appendChild(a.canvas)

      const base = await Assets.load<Texture>(sheetUrl)
      if (cancelled) return
      base.source.scaleMode = 'nearest'
      const row = Math.max(0, Math.min(5, stage - 1))
      const frames = Array.from({ length: FRAMES }, (_, i) =>
        new Texture({ source: base.source, frame: new Rectangle(i * FRAME, row * FRAME, FRAME, FRAME) }))
      const sprite = new AnimatedSprite(frames)
      sprite.scale.set(scale)
      sprite.animationSpeed = 0.07
      if (silhouette) { sprite.tint = 0x000000; sprite.alpha = 0.9 }
      if (animate && !silhouette) sprite.play()
      a.stage.addChild(sprite)
    })()
    return () => {
      cancelled = true
      app?.destroy(true)
    }
  }, [sheetUrl, stage, scale, silhouette, animate, size, still])

  // A still frame needs no WebGL: plain CSS keeps lists (leaderboard, evolution preview) light,
  // since browsers only allow a limited number of WebGL canvases at once.
  if (still) {
    const row = Math.max(0, Math.min(5, stage - 1))
    return (
      <div aria-label="Avatar" style={{
        width: size, height: size, imageRendering: 'pixelated', backgroundImage: `url(${sheetUrl})`,
        backgroundSize: `${4 * size}px ${6 * size}px`, backgroundPosition: `0 ${-row * size}px`,
        filter: silhouette ? 'brightness(0)' : undefined, opacity: silhouette ? 0.9 : 1,
      }} />
    )
  }

  return <div ref={host} style={{ width: size, height: size, imageRendering: 'pixelated' }} aria-label="Avatar" />
}
