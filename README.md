# Dragon&Fit

A fitness quest game for the web: real training and eating become daily quests, XP, Gems and
levels for a 2D pixel character, in a dark-fantasy JRPG menu style.

Done so far:

- **Phase 0–1 (quest loop):** register → create a character (7 narrated steps) → daily quests →
  report with a photo → XP, Gems, levels, titles, avatar evolution.
- **Stats (B1):** STR, VIT, END, DIS, WIS on a 1–100 scale from real data, one snapshot per week.
- **Phase 2 (health and food):** meal log with 51 common Vietnamese dishes, daily weigh-in,
  HP settlement at 4:00 (damage for eating off target or missing quests, healing on perfect days,
  30 % daily cap), rest mode, death at 0 HP with a 30-day purge, rebirth.

## Stack

| Layer | Choice |
| --- | --- |
| Front end | React 19 + TypeScript + Vite, PixiJS 8 for sprites |
| Fonts | VT323 (pixel, has Vietnamese glyphs) + Be Vietnam Pro, self-hosted via Fontsource (OFL) |
| Back end | ASP.NET Core minimal APIs on .NET 10 (LTS) |
| Data | EF Core 10 + PostgreSQL (local Postgres in dev, Neon in production) |
| Auth | ASP.NET Core Identity bearer tokens (`/api/auth/register`, `/login`, `/refresh`) |
| Proof photos | Local disk in dev (`IProofStorage`); Cloudflare R2 in production (not wired yet) |

## Run locally

Requirements: .NET 10 SDK, Node 22+, PostgreSQL 16 (or Docker).

```bash
# 1. Database (skip if you already run Postgres with the same user/password)
docker compose up -d db

# 2. API on http://localhost:5008 — applies migrations and seed data on start
cd backend/DragonFit.Api
dotnet run --launch-profile http

# 3. Front end on http://localhost:5173 (proxies /api to the API)
cd frontend
npm install
npm run dev
```

Tests: `dotnet test` from the repository root (64 rule tests: levels, nutrition, quest generation,
answer validation, character names, stat formulas, HP settlement).

**Testing several days locally:** in Development the API exposes `POST /dev/clock` with
`{"hours": 24}` to move the game clock forward (and `{"reset": true}` to go back). Sign in again
after a jump, because bearer tokens expire with the moved clock.

## Project layout

```
backend/
  DragonFit.Api/
    Domain/Entities.cs          all tables (plan B5, B9)
    Data/AppDbContext.cs        keys, indexes, relations
    Data/SeedData.cs            titles, avatars, exercises, quest templates, character-creation questions
    Data/Migrations/            EF Core migrations
    Game/Progression.cs         level curve 40·L, streak multiplier, evolution stages
    Game/Nutrition.cs           kcal and protein targets with the B3 guardrails
    Game/QuestGenerator.cs      rule-based daily quests (the AI Dungeon Master arrives in Phase 4)
    Game/QuestService.cs        today's board, reporting, rewards, reroll
    Game/StatusService.cs       everything the Status screen shows
    Onboarding/                 answer validation + character creation
    Storage/ProofStorage.cs     private proof-photo storage
    Endpoints/GameEndpoints.cs  HTTP API
  DragonFit.Tests/              xUnit tests for the game rules
  Dockerfile                    production image (Render web service)
frontend/
  src/screens/                  Login, Onboarding, Status, QuestBoard, Game shell
  src/components/               Window frame, bars, icons, PixiJS AvatarSprite
  public/avatars/               placeholder sprite sheets (256×384, 6 stages × 4 frames)
tools/make_avatars.py           regenerates the placeholder sprite sheets
```

## API (Phase 1)

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh` | Identity endpoints |
| GET | `/api/me` | Has the player finished character creation? |
| GET | `/api/onboarding` | Steps, questions, saved answers, avatars |
| POST | `/api/onboarding/steps/{code}` | Save one step (`{ answers, skip }`) |
| POST | `/api/onboarding/complete` | Create the character (`{ timeZone }`) |
| GET | `/api/status` | Status screen data |
| GET | `/api/quests/today` | Today's quests (locked until the player's chosen time; open on day one) |
| POST | `/api/quests/{id}/complete` | multipart: `doneValue`, `photo` |
| POST | `/api/quests/{id}/reroll` | Swap the exercise, costs 20 Gems |
| GET | `/api/quests/{id}/proof` | Owner-only proof photo |
| GET | `/api/foods?q=` | Food list (estimates per serving) |
| GET | `/api/meals/day?date=` | Meal log of an open day with totals and targets |
| POST | `/api/meals` · DELETE `/api/meals/{id}` | Log or remove a dish (open until 4:00 next morning) |
| POST | `/api/body-metrics` | Today's weight, waist, sleep, energy |
| POST | `/api/character/rest-mode` | Rest mode for 1–7 days (`{ days }`, 0 ends it) |
| POST | `/api/character/report/seen` | Mark the morning HP report as read |
| POST | `/api/character/rebirth` | New character after death (`{ name, avatar }`) |
| POST | `/jobs/settle`, `/jobs/cleanup` | Cron jobs, need the `X-Job-Key` header |

## Configuration

| Key | Meaning |
| --- | --- |
| `ConnectionStrings__Default` | PostgreSQL connection string (Neon in production) |
| `Cors__Origins__0` | Front-end origin allowed to call the API |
| `Game__RequireProofPhoto` | `true` by default (plan B2) |
| `Database__MigrateOnStartup` | Apply migrations and seed on start (default `true`) |
| `Jobs__Key` | Secret for `/jobs/*`; jobs are refused while it is empty |
| `VITE_API_BASE` | Front end: API URL in production |

## Not in this phase

The menu gacha (B3), weekly and monthly bosses, AI quests and narration, shop, leaderboard,
Google sign-in, R2 storage, the cron-job.org schedule, and the real avatar art.
The placeholder knights in `public/avatars` are original, generated by `tools/make_avatars.py`.
