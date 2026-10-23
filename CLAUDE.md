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
Directory.Build.props            Shared settings for every project (net10.0, nullable, implicit usings)
src/
  JtsCommandCenter.Core/         Domain models, service interfaces, options classes, pure logic (no I/O, no packages)
    Weather/                     IWeatherService, WeatherReport, WeatherOptions, WeatherCondition
    Sports/                      ISportsService, IScoreboardService, ISportsNewsService, models, options, ScheduleMath
    Projects/                    IProjectService, Project/Subtask/Note/Link entities, inputs, ProjectMath
  JtsCommandCenter.Infrastructure/  Implementations that call external services, plus DI registration
    ServiceCollectionExtensions.cs  AddWeather / AddSports / AddProjects, MigrateDatabaseAsync
    Data/                        CommandCenterDbContext (EF Core), design-time factory, Migrations/
    Weather/                     OpenMeteoWeatherService, WMO code mapping
    Sports/                      ESPN schedule/scoreboard services, RSS/Atom news service, EspnJson helpers
    Projects/                    ProjectService (EF Core)
  JtsCommandCenter.Web/          Blazor Web App (UI + startup only)
    Components/
      App.razor                  HTML shell, head, scripts
      Routes.razor               Router
      Layout/                    MainLayout (sidebar + topbar shell), NavMenu, ReconnectModal
      Pages/                     Routable pages (Home = dashboard; Sports/ = Sports Center pages)
      SportsCenter/              Sports Center components (ScoreboardCard, NewsList, GameDetailView, WinProbabilityChart)
      Dashboard/                 Dashboard cards (Weather, Todo, Projects, Sports, News)
      Shared/                    Reusable components (DashboardCard wrapper, WeatherIcon)
    wwwroot/app.css              Global theme: color tokens + base styles
tests/
  JtsCommandCenter.Tests/        xUnit tests for Core logic and Infrastructure services
    Support/                     FakeHttpHandler (canned HTTP responses), TestDatabase (in-memory SQLite), ESPN JSON fixtures
```

Dependencies point inward: Web → Infrastructure → Core, and Web → Core. Core references nothing.

## Common commands

Run from the repository root:

```bash
dotnet build
dotnet test
dotnet watch --project src/JtsCommandCenter.Web
dotnet run --project src/JtsCommandCenter.Web

# EF Core (dotnet-ef is a local tool: run `dotnet tool restore` once)
dotnet ef migrations add <Name> --project src/JtsCommandCenter.Infrastructure --output-dir Data/Migrations
dotnet ef database update --project src/JtsCommandCenter.Infrastructure
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
- Dashboard cards other than Weather and Sports still use hard-coded placeholder data in their `@code` blocks. When wiring real data, add the interface and models to Core, the implementation to Infrastructure, and inject the interface (Weather is the reference pattern).
- New sections get a link in `Components/Layout/NavMenu.razor` (group under a `nav-section` heading). Groups that hold several pages (like Sports Center) use the collapsible `nav-group-toggle` pattern: open/closed is remembered in `localStorage`, and the group opens itself when you arrive at one of its pages from elsewhere.

## Integrations

- **Weather:** [Open-Meteo](https://open-meteo.com/) forecast API, free and with no API key. Configured under `Weather` in `appsettings.json` (location, coordinates, temperature/wind/precipitation units, `CacheMinutes`, `RefreshMinutes`). The card shows current conditions, feels-like, wind/gusts, humidity, precipitation chance and snowfall (today and per forecast day), and sunrise/sunset. `OpenMeteoWeatherService` is a typed `HttpClient` that caches the report in `IMemoryCache`; `WeatherCard` re-requests on a `PeriodicTimer` while open and keeps showing the last good report if a refresh fails. Times shown come from the API in the location's own time zone, not server time.
- **Sports:** ESPN's public, undocumented site API (`site.api.espn.com`, no API key). Configured under `Sports` in `appsettings.json`: a `Teams` list (ESPN sport/league/team slugs, a `Display` mode, `AlwaysShowTies`), plus `TimeZone`, `CacheMinutes`, `LiveCacheSeconds`, `RefreshMinutes`. Each team's regular-season and postseason schedules are fetched (`teams/{team}/schedule?seasontype=2|3`) and parsed as loose JSON, since shapes vary by sport. `LastAndNext` mode (Vikings, Wild) shows the last final and the next or live game. `Series` mode (Twins) shows every game of the current series plus the next series. Between series, the one that just finished stays current until the next one's first game day. When a team has nothing left to play, the section shows a season recap instead: the record, plus the division place and full division name from `apis/v2/.../standings`. One team failing shows "unavailable" for that section only. While a game is live, the cache drops to `LiveCacheSeconds` and the card re-checks every minute.
- **Sports Center** (`/sports/football`, `/sports/baseball`, `/sports/hockey`; Baseball and Hockey are placeholders): configured under `SportsCenter:Leagues` keyed by ESPN league slug. `EspnScoreboardService` loads a league week (`scoreboard?seasontype=&week=`) and the season calendar used for paging (regular season and postseason only). Completed weeks are cached longer and live weeks shorter. `ScoreboardCard` pins games involving `PinnedTeams` (Vikings, Bengals) at the top, and also leaves them in their normal day group. It shows a pinned team's bye week too. `SportsNewsService` merges ESPN's league news with RSS/Atom `Feeds` (deduped by title, `ExcludeKeywords` filters sportsbook promos). Team news (`FocusTeam`) comes from the team's own feeds, ESPN articles tagged with the team (only when the story is about at most two teams or names the team), and league-feed headlines that contain a `Keywords` entry. ESPN's `news?team=` filter is ignored by the API, so don't rely on it. Feed requests send a User-Agent because some hosts block requests without one.
- **Game pages** (`/sports/football/game/{eventId}`): scoreboard tiles link here. The scoreboard keeps its week in the query string (`?type=2&week=5`), so a game page's back link returns to the same week. `EspnGameDetailService` reads ESPN's `summary?event=` endpoint and returns:
  - the scorebug and line score, game info (venue, attendance, TV, weather, officials), and the injury report
  - team stats with comparison bars (`LowerIsBetter` for turnovers and penalties; "count-yards" stats such as penalties compare by count)
  - box score tables per category, scoring plays, and play-by-play grouped by drive
  - win probability per play (drawn by `WinProbabilityChart`), and leaders (game leaders once play starts, season leaders before)
  - before kickoff only: the matchup predictor, betting line, and last five games (`Pregame`). ESPN's team stats are season averages at that point.

  Timeouts and period breaks carry a down-and-distance in ESPN's data but aren't snaps, so they get no down or yardage. A turnover's `statYardage` is the return, so it isn't shown as a gain. Final games are cached for `CompletedWeekCacheMinutes`, and live games refresh every minute.

## Database and projects

- **Storage:** EF Core on SQL Server. Development uses LocalDB (`ConnectionStrings:CommandCenter` in `appsettings.Development.json`), and the app applies pending migrations on startup in Development only. Production is Azure SQL: the connection string comes from App Service configuration (Managed Identity, no password), and migrations are applied by the deployment pipeline. `EnableRetryOnFailure` covers transient errors, including a serverless database waking up.
- **DbContext usage:** `CommandCenterDbContext` is `internal`. Services take `IDbContextFactory<CommandCenterDbContext>` and create one short-lived context per operation, because a Blazor circuit is long-lived and a context isn't thread-safe. Reads use `AsNoTracking`.
- **Model:** a `Project` (Work/Personal `Area`) has `Subtask`s (two levels only), plus `Note`s and `Link`s. Notes and links always carry `ProjectId`, and also `SubtaskId` when they belong to a subtask, so the project's work log includes its subtasks' notes. Enums are stored as strings, and `DateTime`s are UTC.
- **Boards:**
  - Columns are fixed: `ProjectStatus` (Planned / Active / On Hold / Done) and `SubtaskStatus` (To Do / In Progress / Blocked / Done).
  - `SortOrder` is a card's position within its column. Moves renumber the target column.
  - A status change logs an Activity note, sets or clears `CompletedAt`, and keeps `BlockedReason` only while On Hold (projects) or Blocked (subtasks).
- **Concurrency:** `Version` is a `Guid` concurrency token, replaced on every edit. Edits pass the version they started from, and a stale one throws `ConcurrencyConflictException`. Moves don't change the version.
- **Deletes:**
  - Projects are archived first, and only archived projects can be deleted.
  - SQL Server allows only one cascade path to Notes/Links. Subtask → Note/Link is therefore `ClientCascade`, and the service loads those rows before removing a subtask or project.
- **"Today"** for due dates comes from `Projects:TimeZone`, not server time.
- **Changing the model:** add a migration (see Common commands). The test `Migrations_MatchTheModel` fails if you forget.

## Code conventions

- Nullable reference types and implicit usings are enabled (set once in `Directory.Build.props`).
- Keep pages thin: components inject Core interfaces (`IWeatherService`, `ISportsService`, ...), never Infrastructure classes.
- Where new code goes:
  - Models, interfaces, options classes and pure logic go in **Core**. Core takes no package or framework dependencies.
  - Anything that does I/O (HTTP APIs, feeds, the database/EF Core) goes in **Infrastructure**. It is registered through an `Add<Feature>(IConfiguration)` extension in `ServiceCollectionExtensions.cs`, which `Program.cs` calls.
  - UI goes in **Web**.
- Infrastructure classes that are only used internally can be `internal`; `InternalsVisibleTo` exposes them to the test project.
- Tests: every Core logic class and Infrastructure service gets tests. Services are tested against canned responses via `FakeHttpHandler`, so tests never hit the network. Use `MemoryCache`, `Options.Create`, and `NullLogger<T>.Instance` for the other dependencies. Database services are tested against `TestDatabase` (in-memory SQLite built from the real model) with a `FakeTimeProvider`.

## Open decisions / roadmap

<!-- TODO: Fill in as decisions are made. -->
- Authentication (likely Microsoft Entra ID, single user) — required before deploying publicly
- Project management UI: Phase 1 (data layer) is done. Still to build: the projects board (`/projects`), the project page with its subtask board and work log, the subtask side panel, image attachments in Markdown descriptions (Azure Blob Storage behind an `IAttachmentStore`, private container served through the app), and wiring the dashboard Projects and Due Today cards to `IProjectService`
- Azure deployment pipeline (GitHub Actions to App Service)
