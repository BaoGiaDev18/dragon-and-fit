import { useCallback, useEffect, useState } from 'react'
import { api, isLoggedIn, logout } from './api'
import { Game } from './screens/Game'
import { Login } from './screens/Login'
import { Onboarding } from './screens/Onboarding'

type Screen = 'loading' | 'login' | 'onboarding' | 'game'

export default function App() {
  const [screen, setScreen] = useState<Screen>('loading')

  // After login: no living character yet → character creation, otherwise the main menu.
  const route = useCallback(async () => {
    if (!isLoggedIn()) return setScreen('login')
    try {
      const me = await api.get<{ hasCharacter: boolean }>('/api/me')
      setScreen(me.hasCharacter ? 'game' : 'onboarding')
    } catch {
      setScreen('login')
    }
  }, [])

  useEffect(() => { route() }, [route])

  switch (screen) {
    case 'loading': return <div className="scene" />
    case 'login': return <Login onDone={route} />
    case 'onboarding': return <Onboarding onDone={() => setScreen('game')} />
    case 'game': return <Game onLogout={() => { logout(); setScreen('login') }} />
  }
}
