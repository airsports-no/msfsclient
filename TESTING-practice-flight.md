# Testing the MSFS practice flight ("Go flying")

What a first-time pilot should experience: open the client, press **Go flying**, and the client registers them
(70 kt, aircraft "MSFS", club "MSFS pilots", no wind, adaptive start), starts tracking at once and opens the flight
order in the browser as soon as the server has made it.

## What is ready

| Part | Where | State |
|---|---|---|
| Server change | backend branch `msfs-quickstart` (migrations 0193, 0194) | pushed, **not deployed** |
| Client | branch `feature/mobile-app-port` | committed, not pushed |
| Client + panel bundle (as on flightsim.to) | `dist\Airsports-MSFS-2.0.0-33.zip` | built, starts |
| Toolbar panel alone | `msfs-panel\dist\airsports-flight-display-1.0.33.zip` | built with the MSFS 2024 SDK |
| Offline preview | `msfsclient.exe --preview quickstart` | works without any server |

Nothing works against the live site until the server branch is deployed there: the client then falls back to the
website's registration form. Test against your local dev stack first (A), or deploy the branch to a test server (B).

## 1. Prepare the server

### A. Local dev stack

1. In your own backend checkout: `git fetch` and `git checkout msfs-quickstart`.
2. Migrate (in the dev container's terminal): `python manage.py migrate display`.
3. Make sure a **Celery worker** runs. It makes the flight order (`generate_and_maybe_notify_flight_order`).
   Without it the client shows "Your flight order is being made" and gives up after 10 minutes.
4. Create the practice task, or reuse one:
   - a **public contest** with a **navigation task** whose finish time is in the future;
   - **Allow self management** ticked;
   - a route with a **take-off gate**. Without one, the client cannot offer "Move my aircraft to the runway".
5. In Django admin go to **Display → Simulator quickstarts → Add**:
   - **Navigation task**: the task above.
   - **Start location**: what the pilot looks for in the MSFS world map, e.g. `Kjeller (ENKJ), runway 30`.
   - **Description** and **Start instructions**: optional; both are shown on the card.
   - **Air start altitude**: used by "Start in the air".
   - **Air speed**, **Aircraft registration**, **Club name**: the one-press defaults (70, MSFS, MSFS pilots).
   - **Active**: ticked. The newest active quickstart is the one offered.
6. Check: signed in on the local site, `http://localhost:8002/api/v1/userprofile/msfs_quickstart/` answers JSON.

### B. A deployed server

Deploy `msfs-quickstart`, run the migrations, and do steps 3–6 of A there.

## 2. Start the client

1. Unzip `dist\Airsports-MSFS-2.0.0-33.zip`, for example to `C:\AirsportsTest`.
2. Use a **test account without flights**: the card is only offered to pilots with no flight now and none upcoming.
3. To keep this test apart from your normal client (sign-in, settings, "Not now"), give it its own data folder.
   In PowerShell:

   ```powershell
   cd "C:\AirsportsTest\Airsports client"
   $env:MSFSCLIENT_DATA_DIR = "C:\AirsportsTest\data"

   # A. local server: API, website pages, flight orders and positions all go to your machine
   .\msfsclient.exe --base-url http://localhost:8002 --traccar-url http://localhost:5055

   # B. deployed server: no options (production), or --base-url https://<test server>
   .\msfsclient.exe
   ```

   `5055` is Traccar's OsmAnd port. The local `traccar` container publishes 5000–5150.

## 3. Test checklist

| # | Do | Expect |
|---|---|---|
| 1 | Sign in with the test account | Home shows **"New here? Try a practice flight"**: title, *Start at …*, and "When you press Go flying" with 4 steps (defaults, flight order, where to start, tracking starts at once). |
| 2 | Press **Go flying** | The button shows "Registering…". The card then switches to **"You are registered for your practice flight"**: ✓ registered at 70 knots with an adaptive start, then "Your flight order is being made…". The bar at the bottom shows **Tracking is on** without pressing play. The flight strip shows the practice task. |
| 3 | Check the server (admin → Contestants) | A new contestant: air speed 70, wind 0/0, adaptive start, team aircraft MSFS / club MSFS pilots, tracker start time ≤ now. |
| 4 | Wait (usually under a minute) | The **browser opens the flight order** (`/display/maplink/<id>/`) by itself, **once**. The card says ✓ "ready and was opened" and shows a **Flight order** button. With MSFS connected, the sim shows "Air Sports: your flight order is open in your browser". The e-mail arrives too. |
| 5 | Start MSFS and load a free flight anywhere | The card offers **Move my aircraft to the runway** and **Start in the air**. |
| 6 | Press **Move my aircraft to the runway** → Move | The aircraft sits on the runway at the start, pointing along it. The bar shows tracking off for about 3 s, then **Tracking is on** again. |
| 7 | Try **Start in the air** (another time) | Placed 2 NM before the starting point at the set altitude, 100 kt, heading for it. Take the controls at once. |
| 8 | Fly across the starting line and the route | **Live map** shows the track. The adaptive start begins when you cross the line. |
| 9 | Close and reopen the client | The card shows the registered state again; the flight order does **not** open again. |
| 10 | "Choose speed and aircraft" (fresh account) | Opens the website registration form instead. Registering there brings you back to Home, and the flight order also opens once it is ready. |
| 11 | Untick **Active** on the quickstart, then Refresh on Home | The card disappears. |
| 12 | Toolbar panel: **Profile → In the simulator → Toolbar panel → Install**, restart MSFS | An Air Sports icon in the MSFS toolbar opens the flight display: tracking state, flight, gates. Or copy `Community\airsports-flight-display` from the zip to your Community folder yourself. |

To look at the screens without any server: `.\msfsclient.exe --preview quickstart`. In preview, "Go flying" works and
the flight order "appears" after 8 s; its browser page does not exist.

## Things to watch

- **The aircraft jump.** Moving the aircraft while tracking pauses tracking around the move, but the server still
  sees two positions far apart: the last one before and the first one after. It does not filter such jumps. If that
  line crosses the starting line, the adaptive start may trigger at the wrong moment. Unlikely when the flight is loaded
  near the start; worth checking in step 6 that the start did not trigger.
- **Local positions.** If the track does not appear locally, check that the local Traccar knows the device
  (the profile's simulator tracking id), or run the same test against a deployed server.
- **Flight order never opens.** Check the Celery worker and its log for `Generating flight order for …`.
  After 10 minutes the card says the order is taking longer and will be e-mailed.
- **Register error.** The card shows the server's reason in red (e.g. the contest is full, or you are a co-pilot
  there).

## Reset to test again

- Cancel the flight in the client (**My flights → Cancel**), or delete the contestant in admin.
- "Not now" is remembered on this PC: delete `C:\AirsportsTest\data` (it also signs you out).
