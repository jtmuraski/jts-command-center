# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project overview

**JT's Command Center** is a personal command center: a single place to organize both personal and professional life. It is used from a laptop, desktop, and tablet, so it must work well at tablet widths and with touch.

<!-- TODO: Describe the modules/features you want (tasks, notes, calendar, finances, etc.) and priorities. -->

## Tech stack

- **.NET 10** / C# (SDK pinned in `global.json`)
- **Blazor Web App**, Interactive Server render mode, global interactivity (`Routes` and `HeadOutlet` render as `InteractiveServer` in `App.razor`)
- No Bootstrap or other CSS framework: styling is hand-written CSS with design tokens
- Planned hosting: **Azure** (App Service is the likely target; Blazor Server needs WebSockets enabled and, if scaled out, ARR affinity / Azure SignalR Service)

## Repository layout

```
JtsCommandCenter.slnx            Solution (new XML .slnx format)
global.json                      Pins the .NET SDK
src/
  JtsCommandCenter.Web/          Blazor Web App (UI + server)
    Components/
      App.razor                  HTML shell, head, scripts
      Routes.razor               Router
      Layout/                    MainLayout (sidebar + topbar shell), NavMenu, ReconnectModal
      Pages/                     Routable pages (Home = dashboard; Sports/ = Sports Center pages)
      SportsCenter/              Sports Center components (ScoreboardCard, NewsList)
      Dashboard/                 Dashboard cards (Weather, Todo, Projects, Sports, News)
      Shared/                    Reusable components (DashboardCard wrapper, WeatherIcon)
    Services/
      Weather/                   IWeatherService + Open-Meteo implementation, options, WMO code mapping
      Sports/                    ESPN team schedules (dashboard card), weekly scoreboards, merged news feeds
    wwwroot/app.css              Global theme: color tokens + base styles
```

## Common commands

Run from the repository root:

```bash
dotnet build
dotnet watch --project src/JtsCommandCenter.Web
dotnet run --project src/JtsCommandCenter.Web
```

Dev URLs (from `launchSettings.json`): http://localhost:5070, https://localhost:7212

## UI / design conventions

- **Dark theme only.** Dark navy and chalkboard-black surfaces, muted secondary/accent colors. **No bright or neon colors**, and do not reintroduce the default Blazor/Bootstrap styling.
- All colors are CSS custom properties defined on `:root` in `wwwroot/app.css` (prefix `--cc-`). Use the tokens; don't hard-code hex values in components. Add a new token if one is needed.
  - Surfaces: `--cc-bg`, `--cc-surface`, `--cc-surface-raised`, `--cc-sidebar`, `--cc-border`
  - Text: `--cc-text`, `--cc-text-strong`, `--cc-text-muted`, `--cc-text-faint`
  - Accents: `--cc-primary` (steel blue), `--cc-accent` (slate teal), `--cc-accent-2` (worn brass), `--cc-accent-3` (dusty violet)
  - Status: `--cc-success`, `--cc-warning`, `--cc-danger`, `--cc-info`
- Shared utility classes in `app.css`: `.cc-card`, `.cc-btn`, `.cc-btn-ghost`, `.cc-muted`.
- Component-specific styles go in a scoped `Component.razor.css` file next to the component. Use `::deep` when styling child components (e.g. `NavLink`).
- Layout must stay responsive: below 900px the sidebar becomes an off-canvas drawer toggled from the top bar.
- Dashboard (`Pages/Home.razor`) uses CSS grid areas: 1 column on phones (to-do first), 2 columns from 720px, 3 columns from 1200px. New cards wrap their content in `<DashboardCard Title="...">` and get a grid area in `Home.razor.css`.
- Dashboard cards other than Weather and Sports still use hard-coded placeholder data in their `@code` blocks. When wiring real data, move it behind an injected service (Weather is the reference pattern).
- New sections get a link in `Components/Layout/NavMenu.razor` (group under a `nav-section` heading).

## Integrations

- **Weather:** [Open-Meteo](https://open-meteo.com/) forecast API, free and with no API key. Configured under `Weather` in `appsettings.json` (location, coordinates, temperature/wind/precipitation units, `CacheMinutes`, `RefreshMinutes`). The card shows current conditions, feels-like, wind/gusts, humidity, precipitation chance and snowfall (today and per forecast day), and sunrise/sunset. `OpenMeteoWeatherService` is a typed `HttpClient` that caches the report in `IMemoryCache`; `WeatherCard` re-requests on a `PeriodicTimer` while open and keeps showing the last good report if a refresh fails. Times shown come from the API in the location's own time zone, not server time.
- **Sports:** ESPN's public, undocumented site API (`site.api.espn.com`, no API key). Configured under `Sports` in `appsettings.json`: a `Teams` list (ESPN sport/league/team slugs, a `Display` mode, `AlwaysShowTies`), plus `TimeZone`, `CacheMinutes`, `LiveCacheSeconds`, `RefreshMinutes`. Each team's regular-season and postseason schedules are fetched (`teams/{team}/schedule?seasontype=2|3`) and parsed as loose JSON, since shapes vary by sport. `LastAndNext` mode (Vikings, Wild) shows the last final and the next or live game. `Series` mode (Twins) shows every game of the current series plus the next series. Between series, the one that just finished stays current until the next one's first game day. When a team has nothing left to play, the section shows a season recap instead: the record, plus the division place and full division name from `apis/v2/.../standings`. One team failing shows "unavailable" for that section only. While a game is live, the cache drops to `LiveCacheSeconds` and the card re-checks every minute.
- **Sports Center** (`/sports/football`, `/sports/baseball`, `/sports/hockey`; Baseball and Hockey are placeholders): configured under `SportsCenter:Leagues` keyed by ESPN league slug. `EspnScoreboardService` loads a league week (`scoreboard?seasontype=&week=`) and the season calendar used for paging (regular season and postseason only). Completed weeks are cached longer and live weeks shorter. `ScoreboardCard` pins games involving `PinnedTeams` (Vikings, Bengals) at the top, and also leaves them in their normal day group. It shows a pinned team's bye week too. `SportsNewsService` merges ESPN's league news with RSS/Atom `Feeds` (deduped by title, `ExcludeKeywords` filters sportsbook promos). Team news (`FocusTeam`) comes from the team's own feeds, ESPN articles tagged with the team (only when the story is about at most two teams or names the team), and league-feed headlines that contain a `Keywords` entry. ESPN's `news?team=` filter is ignored by the API, so don't rely on it. Feed requests send a User-Agent because some hosts block requests without one.

## Code conventions

- Nullable reference types and implicit usings are enabled.
- Keep pages thin: put business logic in services registered in `Program.cs` and inject them.

## Open decisions / roadmap

<!-- TODO: Fill in as decisions are made. -->
- Authentication (likely Microsoft Entra ID, single user) — required before deploying publicly
- Data storage (e.g. Azure SQL, Cosmos DB, or SQLite + EF Core)
- Azure deployment pipeline (GitHub Actions to App Service)
