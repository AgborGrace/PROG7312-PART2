# Updates made after lecturer feedback on Part 1

| # | Lecturer feedback | What changed | Where |
|---|---|---|---|
| 1 | Landing page must look like a real Smart-X IoT product | New landing page: logo, animated mesh-network hero (nodes, links, packets), feature cards, data-flow strip | `Views/LandingView`, `Controls/MeshHero`, `Controls/SmartXLogo` |
| 2 | Real authentication API, no hardcoded logins | Firebase Authentication (sign-in, sign-up, password reset, silent token refresh). The API verifies every bearer token with Firebase | `Services/FirebaseAuthService`, `Views/AuthView`, `SmartX.Api/Services/FirebaseAuthMiddleware` |
| 3 | After login all three modules visible; tab 2 works; tab 3 greyed; switching must keep data | Sidebar with three modules; module 3 disabled with a tooltip; pages are created once and reused; simulator and session live at application level; all calls are async | `Views/ShellView`, `App.xaml.cs` |
| 4 | File upload must read the file, extract devices, fill the grid and update the system | `.txt` import parsed by the API (`POST /api/devices/import`); every valid line is validated and registered; results shown in a DataGrid and the Command Stream refreshes | `DeviceImportParser`, `DevicesController`, `Views/IngestionView`, `SampleData/sample-devices.txt` |
| 5 | Live telemetry visuals; simulated data must hit the API like a device | Live device cards with sparklines, status colours, KPI tiles, health ring; the simulator posts real HTTP packets to `/api/telemetry/ingest` | `Views/CommandStreamView`, `Controls/LiveChart`, `Services/SimulatorService` |
| 6 | Dedicated API class holding named data structures | `SmartXDataStore` with `TelemetryQueue`, `CriticalAlertQueue`, `CommandUndoStack`, `DeviceRegistry`, `SensorTimeline`, `DisconnectedNodes` | `SmartX.Api/Services/SmartXDataStore.cs` |
| 7 | README must explain how to test queues/dictionaries and simulate a high-priority alert | README sections 5–7 with step-by-step tests A–F | `README.md` |
| 8 | Docker recommended | Dockerfile for the API and `docker-compose.yml` | `SmartX.Api/Dockerfile`, `docker-compose.yml` |
