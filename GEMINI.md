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
- **Services (msfsclient/Services):**
  - `SimConnectService` (`ISimConnectService`): the low-level connection to MSFS.
  - `SimPositionSource`: SimConnect data -> Core `TrackPoint` (PC clock UTC, knots -> m/s, feet -> metres).
  - `AuthService`: Firebase email/password sign-in behind Core `IAuthService`; the refresh token is kept in the Windows
    credential store (`VaultUserRepository`), not in a file.
  - `AppConfigService`: base URLs (`--base-url`, `--test-mode`), version and build number, local folder.
  - `PlatformServices`: browser links, share dialog, GPX save dialog, photo picker, connection state.
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

## Backend
- API `https://airsports.no/api/v1/`, `Authorization: JWT <Firebase ID token>`, one refresh and retry on 401/403.
- The simulator uses `userprofile/get_current_sim_navigation_task/` (the phones use the `app` variant).
- Every authorised request carries `X-App-Platform: msfs`, `X-App-Version` (shared version name) and `X-App-Build`.

## UI Conventions
- Design tokens (colours, fonts, shapes) are in `Themes/Theme.xaml`, for light, dark and high contrast. Barlow for text, Barlow Condensed for headings and numbers.
- The dock (`FlightDeck`) is always dark ink. The page column is at most about 560 px wide, centred.
