export type Option = { label: string; value: string; isExclusive: boolean }

export type Question = {
  code: string
  prompt: string
  helpText: string | null
  inputType: 'text' | 'number' | 'single_choice' | 'multi_choice' | 'time' | 'time_range' | 'avatar_picker' | 'reward_list'
  unit: string | null
  min: number | null
  max: number | null
  isRequired: boolean
  isSensitive: boolean
  options: Option[]
}

export type Step = { code: string; title: string; narration: string; isSkippable: boolean; questions: Question[] }

export type AvatarInfo = { code: string; name: string; spriteSheetUrl: string; isDefault: boolean; evolutions: { stage: number; minLevel: number }[] }

export type OnboardingState = {
  status: 'NotStarted' | 'InProgress' | 'Completed'
  currentStep: string | null
  steps: Step[]
  answers: Record<string, unknown>
  avatars: AvatarInfo[]
}

export type Stat = { key: string; name: string; value: number; delta: number }
export type Skill = { group: string; name: string; tier: number; maxTier: number; active: boolean; completed: number }

export type Status = {
  character: {
    name: string; level: number; totalXp: number; xpIntoLevel: number; xpForNext: number
    hp: number; maxHp: number; mp: number; maxMp: number; streakDays: number; evolutionStage: number
    title: string | null; avatar: { code: string; name: string; spriteSheetUrl: string } | null; nextEvolutionLevel: number
  }
  gems: number
  titles: { code: string; name: string; minLevel: number }[]
  stats: Stat[]
  skills: Skill[]
  body: { weightKg: number | null; waistCm: number | null; bodyFatPct: number | null; waistChangeCm: number | null }
  targets: { kcal: number; proteinG: number; maintenanceKcal: number; steps: number | null }
  today: { date: string; required: number; done: number; questTime: string }
}

export type Quest = {
  id: string; slot: number; title: string; narration: string; sets: number; targetValue: number; unit: string
  xp: number; gems: number; isRequired: boolean; status: 'Pending' | 'Completed' | 'Partial'; doneValue: number | null
  hasProof: boolean; awardedXp: number; awardedGems: number; kind: string | null; canReroll: boolean
  exercise: { code: string; nameVi: string; group: string; tier: number; formTips: string } | null
}

export type QuestBoard = {
  date: string; locked: boolean; unlockAt: string; proofRequired?: boolean; rerollCost?: number; quests: Quest[]
}

export type Reward = {
  xpGained: number; gemsGained: number; levelBefore: number; levelAfter: number; streakDays: number
  newTitles: string[]; evolvedToStage: number | null; questStatus: string
}
