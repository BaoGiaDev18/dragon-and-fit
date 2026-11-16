// All sound is synthesized in the browser with the Web Audio API: original chiptune tones,
// no audio files, so there is nothing to license (plan B4: own or licensed music only).

const KEY = 'df.sound'
type Settings = { sfx: boolean; music: boolean; volume: number }

function load(): Settings {
  try {
    return { sfx: true, music: false, volume: 0.5, ...JSON.parse(localStorage.getItem(KEY) ?? '{}') }
  } catch {
    return { sfx: true, music: false, volume: 0.5 }
  }
}

let settings = load()
let ctx: AudioContext | null = null
let master: GainNode | null = null
const listeners = new Set<() => void>()

function audio() {
  if (!ctx) {
    ctx = new AudioContext()
    master = ctx.createGain()
    master.gain.value = settings.volume
    master.connect(ctx.destination)
  }
  if (ctx.state === 'suspended') void ctx.resume()
  return { ctx, master: master! }
}

export function getSound() { return settings }

export function setSound(patch: Partial<Settings>) {
  settings = { ...settings, ...patch }
  try { localStorage.setItem(KEY, JSON.stringify(settings)) } catch { /* not saved, still applied */ }
  if (master) master.gain.value = settings.volume
  if (settings.music) startMusic()
  else stopMusic()
  listeners.forEach(l => l())
}

export function onSoundChange(l: () => void) {
  listeners.add(l)
  return () => { listeners.delete(l) }
}

function tone(freq: number, start: number, dur: number, type: OscillatorType = 'square', gain = 0.18, slideTo?: number) {
  const { ctx, master } = audio()
  const o = ctx.createOscillator()
  const g = ctx.createGain()
  o.type = type
  o.frequency.setValueAtTime(freq, ctx.currentTime + start)
  if (slideTo) o.frequency.exponentialRampToValueAtTime(slideTo, ctx.currentTime + start + dur)
  g.gain.setValueAtTime(gain, ctx.currentTime + start)
  g.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + start + dur)
  o.connect(g).connect(master)
  o.start(ctx.currentTime + start)
  o.stop(ctx.currentTime + start + dur + 0.02)
}

function noise(start: number, dur: number, gain = 0.25) {
  const { ctx, master } = audio()
  const buf = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * dur), ctx.sampleRate)
  const data = buf.getChannelData(0)
  for (let i = 0; i < data.length; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / data.length)
  const src = ctx.createBufferSource()
  const g = ctx.createGain()
  g.gain.value = gain
  src.buffer = buf
  src.connect(g).connect(master)
  src.start(ctx.currentTime + start)
}

const SFX: Record<string, () => void> = {
  click: () => tone(880, 0, 0.05, 'square', 0.08),
  confirm: () => { tone(660, 0, 0.07); tone(990, 0.07, 0.1) },
  error: () => { tone(220, 0, 0.12, 'sawtooth', 0.12); tone(180, 0.12, 0.16, 'sawtooth', 0.12) },
  hit: () => { noise(0, 0.12, 0.3); tone(300, 0, 0.12, 'square', 0.15, 90) },
  gem: () => { tone(1320, 0, 0.06, 'triangle', 0.2); tone(1760, 0.06, 0.12, 'triangle', 0.2) },
  heal: () => [523, 659, 784].forEach((f, i) => tone(f, i * 0.08, 0.18, 'triangle', 0.18)),
  hurt: () => tone(400, 0, 0.3, 'sawtooth', 0.15, 80),
  victory: () => [523, 659, 784, 1047, 784, 1047].forEach((f, i) => tone(f, i * 0.11, i === 5 ? 0.5 : 0.12)),
  levelup: () => [392, 523, 659, 784, 1047, 1319].forEach((f, i) => tone(f, i * 0.07, i === 5 ? 0.6 : 0.1, 'square', 0.16)),
}

export function sfx(name: keyof typeof SFX) {
  if (!settings.sfx) return
  try { SFX[name]() } catch { /* audio unavailable */ }
}

// ------------------------------------------------------------------ background music
// A short original chiptune loop: square-wave melody over a triangle bass, A minor, 112 BPM.
const MELODY = [69, 72, 76, 74, 72, 69, 67, 69, 72, 74, 76, 79, 77, 76, 74, 72, 69, 72, 76, 81, 79, 76, 74, 72, 71, 72, 74, 71, 69, 0, 69, 0]
const BASS = [45, 45, 41, 41, 43, 43, 40, 40]
const midi = (n: number) => 440 * 2 ** ((n - 69) / 12)
let timer: number | null = null
let step = 0
let nextAt = 0

function scheduleMusic() {
  const { ctx } = audio()
  const beat = 60 / 112 / 2
  while (nextAt < ctx.currentTime + 0.3) {
    const at = nextAt - ctx.currentTime
    const m = MELODY[step % MELODY.length]
    if (m) tone(midi(m), Math.max(0, at), beat * 0.9, 'square', 0.05)
    if (step % 4 === 0) tone(midi(BASS[(step / 4) % BASS.length]), Math.max(0, at), beat * 3.6, 'triangle', 0.09)
    step++
    nextAt += beat
  }
}

function startMusic() {
  if (timer !== null) return
  const { ctx } = audio()
  nextAt = ctx.currentTime + 0.05
  timer = window.setInterval(scheduleMusic, 100)
}

function stopMusic() {
  if (timer !== null) window.clearInterval(timer)
  timer = null
}

/** Browsers only allow audio after a user gesture: call this from the first click. */
export function resumeMusicIfEnabled() {
  if (settings.music) startMusic()
}
