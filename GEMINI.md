# Air Sports MSFS Client

## Project Overview
This is a WinUI 3 application designed to interface with Microsoft Flight Simulator (MSFS) via SimConnect. It tracks flight data and provides a real-time dashboard for pilots.

## Core Mandates
- **SimConnect Lifecycle:** Ensure SimConnect is initialized with the correct Window Handle (`HWND`) using `WinRT.Interop.WindowNative.GetWindowHandle`.
- **UI Thread Safety:** All updates to Observable properties and collections must be dispatched to the UI thread using `DispatcherQueue`.
- **Data Precision:** Latitude/Longitude should be formatted to at least 4 decimal places.

## Technical Architecture
- **Framework:** .NET 8, WinUI 3 (Windows App SDK)
- **Pattern:** MVVM (using CommunityToolkit.Mvvm)
- **Services:**
  - `ISimConnectService`: Manages the low-level connection to MSFS.
  - `IAuthService`: Handles user authentication via Firebase.
  - `IProfileService`: Manages user profile data.

## SimConnect Implementation
- **Polling:** The application uses a `DispatcherTimer` in `MainWindow.xaml.cs` (100ms interval) to call `ReceiveMessage()` on the `SimConnectService`.
- **Data Requests:** Flight data is requested periodically (`SIMCONNECT_PERIOD.SECOND`) to ensure a steady stream of updates without overwhelming the bus.

## UI Conventions
- **Converters:** Use the custom converters in `msfsclient.Helpers.Converters` for data formatting and status visualization.
- **Styling:** Adhere to the `Dark` theme defined in `App.xaml`.
