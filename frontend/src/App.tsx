import { useCallback, useEffect, useState } from 'react'
import type { Death, Me } from './types'
import { api, isLoggedIn, logout } from './api'
import { DeathScreen } from './screens/DeathScreen'
import { Game } from './screens/Game'
import { Login } from './screens/Login'
import { Onboarding } from './screens/Onboarding'

type Screen = 'loading' | 'login' | 'onboarding' | 'game' | 'dead'

export default function App() {
  const [screen, setScreen] = useState<Screen>('loading')
  const [death, setDeath] = useState<Death | null>(null)

  // After login: no living character yet → character creation, otherwise the main menu.
  const route = useCallback(async () => {
    if (!isLoggedIn()) return setScreen('login')
    try {
      const me = await api.get<Me>('/api/me')
      setDeath(me.death)
      setScreen(me.hasCharacter ? 'game' : me.death && me.onboarding === 'Completed' ? 'dead' : 'onboarding')
    } catch {
      setScreen('login')
    }
  }, [])

  useEffect(() => { route() }, [route])

  // Any API call answering 410 means the character just died at settlement: show the death screen.
  useEffect(() => {
    const onDead = () => { route() }
    window.addEventListener('df:dead', onDead)
    return () => window.removeEventListener('df:dead', onDead)
  }, [route])

  switch (screen) {
    case 'loading': return <div className="scene" />
    case 'login': return <Login onDone={route} />
    case 'onboarding': return <Onboarding onDone={() => setScreen('game')} />
    case 'game': return <Game onLogout={() => { logout(); setScreen('login') }} />
    case 'dead': return death
      ? <DeathScreen death={death} onReborn={route} onLogout={() => { logout(); setScreen('login') }} />
      : <div className="scene" />
  }
}
