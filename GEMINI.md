# Air Sports MSFS Client

## Project Overview
A WinUI 3 application that tracks a Microsoft Flight Simulator (MSFS) flight via SimConnect for Airsports Live Tracking. It
has the same features and look as the Air Sports mobile app (airsports-mobile, Kotlin Multiplatform), ported to C#; see
`msfs_client_port_plan.md` in the backend repo (`documentation/`) for the plan and what differs from the phone apps.

## Solution layout
- `msfsclient.Core/` (net8.0, no WinUI or SimConnect references): API client and models, the `AppController` state
  machine, tracking (SQLite position queue, Traccar uploader, session), track log and GPX, photo observations, clock check,
  pre-flight rules. Ported from `shared/src/commonMain` of the mobile repo; keep names close to the Kotlin ones.
- `msfsclient.Core.Tests/` (xUnit): the mobile app's tests, ported case by case. They are the specification.
  `dotnet test msfsclient.Core.Tests` runs anywhere, also on Linux (CI).
- `msfsclient/`: the WinUI app: XAML views, `ShellViewModel`, SimConnect, Firebase sign-in, Windows-only services.

## Core Mandates
- **SimConnect Lifecycle:** Ensure SimConnect is initialized with the correct Window Handle (`HWND`) using `WinRT.Interop.WindowNative.GetWindowHandle`.
- **UI Thread Safety:** Core raises `StateChanged` on any thread; `ShellViewModel` dispatches to the UI thread with `DispatcherQueue`. Never touch UI objects from Core callbacks directly.
- **Data Precision:** Numbers sent to servers or written to files use `CultureInfo.InvariantCulture` (a dot, whatever the PC's region).
- **Wording:** Text shown to pilots is plain sentence case, no jargon, no exclamation marks; copy it from the Kotlin screens.

## Technical Architecture
- **Framework:** .NET 8, WinUI 3 (Windows App SDK)
- **Pattern:** one `ShellViewModel` (CommunityToolkit.Mvvm) wraps the Core `AppController` and the `TrackingSession`. Views are
  `ShellPart` user controls bound one way (`x:Bind`) to it; forms keep their own input until submitted.
  x:Bind does not call a function binding when its argument becomes null, so `helpers:Ui.ShowText(...)`, `Ui.OrEmpty(...)` and
  `Ui.Image(...)` on a value that can go back to null need a `FallbackValue` (`Collapsed`, `''`, `{x:Null}`); without it a
  notice or error stays on screen after it is cleared.
- **Services (msfsclient/Services):**
  - `SimConnectService` (`ISimConnectService`): the low-level connection to MSFS.
  - `SimPositionSource`: SimConnect data -> Core `TrackPoint` (PC clock UTC, knots -> m/s, feet -> metres).
  - `AuthService`: Firebase email/password sign-in behind Core `IAuthService`; the refresh token is kept in the Windows
    credential store (`VaultUserRepository`), not in a file.
  - Google / Apple sign-in (Core `DesktopSignIn`, RFC 8252): the client listens on a free 127.0.0.1 port and opens the
    website's `/accounts/desktop_sign_in/` in the browser (port, state, PKCE-style code challenge). After the sign-in there,
    the browser comes back to `http://127.0.0.1:<port>/callback` with a one-time code, which the client trades with its
    secret at `/accounts/desktop_sign_in/token/` for a Firebase custom token. `AuthService.SignInWithCustomTokenAsync`
    signs in with it over REST (FirebaseAuthentication.net has no custom tokens) and restarts its Firebase client from the
    stored user. `--preview login` shows the login screen.
  - `AppConfigService`: base URLs (`--base-url`, `--test-mode`), version and build number, local folder.
  - `PlatformServices`: browser links, share dialog, GPX save dialog, photo picker, connection state.
  - `NotificationService`: Windows notifications for announcements that arrive while the client runs and its window is
    not in front (announcements are checked every 5 minutes; the desktop has no push), and for the flight notifications
    the phones get by push (tracking opens soon, no positions, scoring started, tracking still on, flight scored). Those
    come from `GET userprofile/client_notifications/?after=<id>`, polled every minute (`PollFlightNotificationsAsync`);
    the newest id seen is kept in `AppSettings`, the first check only notes it, and ones older than 15 minutes are not
    shown. They are also printed in the simulator when it is connected and simulator messages are on.
  - `PreviewMode`: `--preview [home|timer|photos|flights|preflight|history|profile|debug|training|tour]` runs on sample data
    with a simulated aircraft, for screenshots and checking screens without an account or MSFS.
- **Local data:** `airsports.db` (SQLite) in the app's local folder: position queue, track log, photo observation queue,
  settings. `MSFSCLIENT_DATA_DIR` overrides the folder (testing).

## SimConnect Implementation
- **Polling:** `ShellViewModel` runs a 100 ms `DispatcherTimer` that calls `ReceiveMessage()`, and reconnects every 10 s while MSFS is not running.
- **Data Requests:** Flight data is requested once a second (`SIMCONNECT_PERIOD.SECOND`).

## Traccar Integration
- **Tracking ID:** The profile's `simulator_tracking_id` is the Traccar device id.
- **Upload:** every position is stored first, then sent in time order to `https://traccar.airsports.no/` (OsmAnd protocol:
  timestamp in seconds, speed in knots, altitude in metres) with backoff 5 s doubling to 5 min. Traccar gets its own
  `HttpClient`: the Firebase token never goes there.

## In the simulator
- **Simulator messages:** `SimMessageTracker` (Core) turns the flight status into one-off lines (tracking on/off, server
  reception lost and regained, scoring, window closed, 4 h, training gate results, one minute to and the start minute, new
  announcements); `SimConnectService.ShowText` prints them with SimConnect text. Switched off in the profile.
  Whether MSFS 2020 and 2024 show SimConnect text has to be checked in the simulator.
- **Flight display server:** `HudServer` (Core) on `http://localhost:47911/` (`--hud-port` to change), loopback only (IPv4 and
  IPv6). Refuses requests whose Origin is a website or whose Host is not a loopback name, so web pages cannot read or command
  it; MSFS panels (`coui://`) and the client's own page are allowed.
  - `GET /` the display page (`Hud/hud.html`, embedded; also the starting point for the MSFS toolbar panel), `GET /fonts/*.ttf`.
  - `GET /state` the `HudSnapshot` as JSON (camelCase).
  - `WS /ws` pushes `{"type":"state","state":{...}}` on every change and takes commands, answering `{"type":"result","ok":..,"message":..}`.
  - `POST /command` the same commands over HTTP.
  - Commands (`HudHost`, same rules as the client's buttons): `startTracking`, `stopTracking` (needs `"confirm":true`),
    `startTimer`, `resetTimer` (needs `"confirm":true`), `mark` (`"photoId"`; needs tracking and a position under 10 s old),
    `setTraining` (`"on"`; only where the server offers training), `dismissAnnouncement` (`"id"`).
  - The snapshot holds only what the client already shows: no route, no gates beyond the server's own training feedback.

- **Toolbar panel:** `msfs-panel/` (see its README). The panel page wraps the same `hud.js`/`hud.css`; `build-panel.ps1`
  assembles the Community package from a committed `prebuilt/InGamePanels/InGamePanel_Airsports.spb` (built once with the
  MSFS SDK, whose package tool starts the simulator). The client bundles the package when it exists and installs it from
  the profile into each Community folder it finds (`CommunityFolders`, `PanelInstaller` in Core).

- **Practice flight (MSFS quickstart):** `GET userprofile/msfs_quickstart/` (backend `SimulatorQuickstart`, admin-edited). Home
  offers it to pilots without a flight (`UiState.QuickstartOffer`) and lists, step by step, what happens. "Go flying"
  (`AppController.RegisterQuickstartAsync`, `POST userprofile/msfs_quickstart_register/`) registers in one press with the
  server's defaults (70 kt, aircraft "MSFS", club "MSFS pilots", no wind, adaptive start open now) and starts tracking at once
  (`StartTrackingRequested`, immediate tracking; moving the aircraft to the start pauses tracking around the jump);
  an older server (404/405) gets the registration page instead ("Choose speed and aircraft" opens it too). The client then
  polls every 5 s (`PollFlightOrderAsync`, up to 10 minutes) until `registered_flight.flight_order_path` appears and opens it
  once in the browser (`FlightOrderReady`). Once registered it can move the aircraft to the runway or into the air before the
  starting point (`ISimConnectService.MoveAircraft`, SimConnect initial position; refused while tracking).
  `--preview quickstart` / `quickstart-registered` show it with sample data (the preview flight order "appears" 8 s after Go flying).

## Build and release
- `.github/workflows/ci.yml`: Core tests on Linux; on Windows the Store package (unsigned) and the portable release.
- `.github/workflows/release.yml` (run by hand, "Release"): the release path. One build number; builds the flightsim.to
  package (on by default) and, when asked, the Store package uploaded as a draft or submitted for certification. It calls
  the two workflows below (`workflow_call`), which can still be run on their own; the run summary lists what was released.
  The build number is optional: empty means the next automatic one (the Release run number + `BUILD_NUMBER_OFFSET`, 36).
  The Store only needs it to rise, so gaps from failed runs do no harm.
  With "Create a draft GitHub release" (on by default) the `github-release` job attaches the flightsim.to zip and the
  panel-only zip to a draft release `v2.0.0-<build>` (the only job with `contents: write`); publish it on the Releases page.
- `.github/workflows/store.yml` (run by hand): tests, sets the package version to `1.0.<build>.0`, builds the Store
  package and creates a submission for Store ID 9N4MZBKPDS5X with the Microsoft Store CLI (a draft unless "submit" is ticked).
  Secrets in the `store` environment: `AZURE_AD_TENANT_ID`, `AZURE_AD_CLIENT_ID`, `AZURE_AD_CLIENT_SECRET`, `SELLER_ID`.
- `.github/workflows/flightsim.yml` (run by hand): the flightsim.to zip (`release/make-flightsim-zip.ps1`: portable client with
  the panel bundled, the panel for the Community folder, README.txt), optionally signed with Azure Trusted Signing. The
  listing text is in `release/flightsim-listing.md`.
- `publish_portable.ps1` copies `msfsclient.pri` and `*.xbf` into the publish folder: an unpackaged publish leaves them out
  and the window then fails to load.
- Store packages need Visual Studio's MSBuild (`msbuild`); `dotnet build` cannot make the .msixupload.

## Backend
- API `https://airsports.no/api/v1/`, `Authorization: JWT <Firebase ID token>`, one refresh and retry on 401/403.
- The simulator uses `userprofile/get_current_sim_navigation_task/` (the phones use the `app` variant).
- Every authorised request carries `X-App-Platform: msfs`, `X-App-Version` (shared version name) and `X-App-Build`.

## UI Conventions
- Design tokens (colours, fonts, shapes) are in `Themes/Theme.xaml`, for light, dark and high contrast. Barlow for text, Barlow Condensed for headings and numbers.
- The dock (`FlightDeck`) is always dark ink. The page column is at most about 560 px wide, centred.
