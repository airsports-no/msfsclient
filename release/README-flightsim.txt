Air Sports Live Tracking for Microsoft Flight Simulator - version {VERSION}
=============================================================================

Fly real air sports competitions in MSFS 2020 and MSFS 2024: air navigation races (ANR), precision flying and more,
with a live map and automatic scoring. The Airsports client sends your aircraft's position to Air Sports Live Tracking
(https://airsports.no) while you fly. You are scored exactly like the pilots who fly the same tasks in real aircraft.

It is free to use. Sign in with Google, Apple or a free Air Sports account, which you can create in the client.

What to expect: this is paper-map navigation, as in a real competition. Nothing is added to the simulator's world: no
gates, rings or route line, no new scenery. You get a flight order with a map and fly the route by map, clock and compass.
The client scores you from your position, and the live map on airsports.no shows the result.


What is in this zip
-------------------
  Airsports client\                       the client: start msfsclient.exe
  Community\airsports-flight-display\     the toolbar panel for MSFS (optional, see step 2)
  README.txt                              this file


Requirements
------------
- Windows 10 (version 1809 or later) or Windows 11, 64-bit
- Microsoft Flight Simulator 2020 or 2024 (Microsoft Store or Steam)
- An internet connection while you fly


1. Install the client
---------------------
1. Unzip this folder anywhere, for example to Documents\Airsports. Nothing else needs to be installed: the client brings
   everything it needs.
2. Open "Airsports client" and start msfsclient.exe. You may want to pin it to the taskbar.
3. The first time, Windows may say "Windows protected your PC". Click "More info", then "Run anyway".

To remove the client, delete the folder. To update, unzip the new version over the old one, or next to it. Your sign-in
and settings are kept.


2. Install the toolbar panel in MSFS (optional, recommended)
------------------------------------------------------------
The panel shows the flight display inside the simulator: tracking on/off, the start timer, how early or late you were at
each gate, and the photos to look for.

Easiest: in the client, open your profile (your picture or initials at the top right) and press "Install the panel"
under "Toolbar panel in MSFS". The client finds the Community folder of MSFS 2020 and MSFS 2024 by itself.

By hand: copy the folder Community\airsports-flight-display from this zip into your MSFS Community folder.

Then restart MSFS. In a flight, open "Airsports" from the toolbar at the top of the screen. The panel needs the client
to be running on the same PC. After a client update, press "Update the panel" in the same place.


3. Set up the client
--------------------
1. Sign in with your airsports.no account, with "Continue with Google" or "Continue with Apple" (you finish in your
   browser, with the same account as on the website and in the Air Sports app), or press "Create an account". With a new
   email account, open the link in the e-mail you get to verify the address, then press "I have verified my email".
2. Complete your profile: your name, country code and phone number. Your name is how you appear on the live map and in
   the results.
3. A short tour shows the main parts of the client. You can see it again from your profile.
4. Open "Pre-flight" on Home. It checks your flight, that you are online, that the simulator is connected and that the
   PC's clock is right. The clock matters: your positions are stamped with the PC's time, and gates are timed to the
   second. If the check says the clock is off, press "Open date and time settings" and turn on "Set time automatically".

Messages in the simulator: the client prints short lines at the top of the MSFS screen (tracking started, positions
received, gate results in training mode, the start minute). You can turn them off in your profile.

Second screen: the same flight display as in the toolbar panel is at http://localhost:47911/ in any browser on this PC
while the client runs. Your profile shows the address.


4. Your first flight: the practice flight
-----------------------------------------
When you have no flight booked, Home offers a practice flight: a short route that shows you how tracking, the start and
the gates work. You can fly it whenever you like.

1. Read the card "New here? Try a practice flight". It says where to start, for example an airport and runway, and lists
   what happens when you press Go flying.
2. Press "Go flying". The client registers you at 70 knots, with no wind and an adaptive start, opens your flight
   order in the browser as soon as it is ready, and starts tracking. You can fly it any time in the next two hours.
   There is nothing to fill in. To choose your own speed and aircraft instead, press "Choose speed and aircraft".
3. Keep the flight order at hand, on paper or on a second screen. It has the map, the turning points and the time to be
   at each gate.
4. Start MSFS and begin a flight at the start location. With the simulator connected, the card can also do it for you:
   "Move my aircraft to the runway" puts you on the runway, ready for take-off, and "Start in the air" puts you 2 NM
   before the starting point, already flying towards it.
5. Use a weather preset, for example clear skies, instead of live weather. The flight is planned with no wind, so your
   times only work out if MSFS has no wind either. Fly in real time: no simulation rate above 1x, and do not pause.
6. Take off and fly the route. The start is adaptive: your time starts at the whole minute when you cross the starting
   line. Then try to pass each gate at its time on the flight order. Training mode tells you after every gate how many
   seconds early or late you were.
7. After landing, press stop at the bottom of the client. The client offers to show your flight on the live map.


5. Regular flights in a contest
-------------------------------
Register:
- On Home, "Register today" shows the tasks that are open for registration, nearest to your aircraft first. Pick a task,
  choose your start time, speed, aircraft and wind, and register. You can also register on airsports.no, or the
  organizer can add you.
- "My flights" lists your upcoming and past flights. You can cancel a flight there; one that has started is ended.
- Download the flight order from the flight on Home ("Flight orders") and plan your flight with it.

On the day:
1. Run "Pre-flight" and fix anything it reports.
2. Start MSFS and set it up the way the organizer asks, usually the aircraft, the start airport and the weather. Use the
   wind you registered with, and fly in real time.
3. Press play at the bottom of the client before your tracking window opens. Home shows the window under "Tracking for".
   The client sends your position every second. Positions are only scored inside your flight's tracking window.
4. Starts: with a fixed take-off time, take off at that time. With an adaptive start, open "Timer" and press START when
   you begin your start procedure. It counts from the nearest whole minute, the way your start is scored.
5. Photo observations: if the task has photos to find, open "Photos" and mark each photo when you pass it. You can also
   mark them in the toolbar panel.
6. Training mode (on Home) shows how early or late you were at each gate, where the organizer allows it. It is on at
   every start; switch it off on Home to fly without the feedback.
7. After landing, press stop. "Live map" shows your flight. "Share live link" lets friends follow you live.


6. Organize your own competition
--------------------------------
Anyone can set up a contest on https://airsports.no: draw the route in the route editor, choose the task type (ANR,
precision flying and more), set the time window and let pilots register themselves. Air Sports Live Tracking scores
every flight automatically and shows everybody on a live map. It works the same for simulator pilots and real aircraft,
so a club night, a league or a cross-over event with real pilots are all possible. Open "Contests and dashboard on the
website" in the client to get started.


7. Fly the real thing
---------------------
Air Sports Live Tracking was built for real pilots in real aircraft; the simulator joins the same platform, so what you
practise in MSFS works the same way in the air.
- Watch real competitions live on https://airsports.no: every pilot's track, timing and score as they fly, and the
  results afterwards.
- Train for a real event: fly its route in MSFS first, with the same flight order and the same scoring.
- Take part yourself: with a licence and an aircraft, the Air Sports app tracks you on your phone (Google Play and the
  App Store, "Air Sports Live Tracking"). Your simulator account works there too.


Troubleshooting
---------------
- The client does not connect to MSFS: start MSFS and load a flight. The client tries again every 10 seconds. Run the
  client the same way as MSFS: if MSFS runs as administrator, run the client as administrator too.
- "We are not receiving your position": check that tracking is on (stop button at the bottom) and that you are online.
  Positions queued while you were offline are sent when you are back online.
- Times are off by a few seconds: check the PC clock in "Pre-flight", and make sure the simulation rate is 1x.
- The panel is not in the MSFS toolbar: restart MSFS after installing it, and check that the folder
  airsports-flight-display is directly in the Community folder (not in a subfolder).
- The panel or second screen says the client is not running: start the client. The display uses
  http://localhost:47911/ on this PC only.


Help and terms
--------------
Air Sports Live Tracking: https://airsports.no (contests, live maps, results, organizing your own)
The Air Sports app: https://play.google.com/store/apps/details?id=no.airsports.android.livetracking (Android)
                    https://apps.apple.com/us/app/air-sports-live-tracking/id1559193686 (iOS)
Source code: https://github.com/airsports-no/msfsclient (open source, AGPL-3.0; every version is on its Releases page)
Terms and privacy: https://airsports.no/terms_and_conditions/
Crash reports: if the client crashes, it sends a technical report (the error, the client and Windows version and your
Air Sports user id; never your name, e-mail or position) to Sentry, so we can fix it.
