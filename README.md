<div align="center">


<h3 align="center">Time Planner</h3>
  <p align="center">
    XBCAD Repo for Time Planner, a check-in based time tracker for developers.
    <br />
    <br />
  </p>
</div>

## Team Members
<table>
  <tr>
    <td align="center"><img src="https://github.com/ST10449392.png" width="80"/></td>
    <td>
      <b>Mihir Jagdaw</b><br/>
      Student Number: 10449392<br/>
      <a href="https://github.com/ST10449392">@ST10449392</a>
    </td>
  </tr>
  <tr>
    <td align="center"><img src="https://github.com/AndreFourie12.png" width="80"/></td>
    <td>
      <b>Andre Fourie</b><br/>
      Student Number: 10438312<br/>
      <a href="https://github.com/AndreFourie12">@AndreFourie12</a>
    </td>
  </tr>
  <tr>
    <td align="center"><img src="https://github.com/Doingle.png" width="80"/></td>
    <td>
      <b>Dylan Jurgens</b><br/>
      Student Number: 10434135<br/>
      <a href="https://github.com/Doingle">@Doingle</a>
    </td>
  </tr>
</table>



## Table of Contents

<ol>
  <li>
    <a href="#project-overview">Project Overview</a>
    <ul>
      <li><a href="#solution-components">Solution Components</a></li>
      <li><a href="#users-and-roles">Users and Roles</a></li>
    </ul>
  </li>
  <li>
    <a href="#features">Features</a>
    <ul>
      <li><a href="#desktop-widget">Desktop Widget</a></li>
      <li><a href="#web-dashboard-user">Web Dashboard (User)</a></li>
      <li><a href="#web-dashboard-admin">Web Dashboard (Admin)</a></li>
      <li><a href="#rest-api">REST API</a></li>
    </ul>
  </li>
  <li>
    <a href="#core-purpose-and-scope">Core Purpose and Scope</a>
    <ul>
      <li><a href="#the-problem">The Problem</a></li>
      <li><a href="#purpose">Purpose</a></li>
      <li><a href="#in-scope">In Scope</a></li>
      <li><a href="#out-of-scope-this-version">Out of Scope (This Version)</a></li>
      <li><a href="#assumptions-and-constraints">Assumptions and Constraints</a></li>
    </ul>
  </li>
  <li>
    <a href="#design-considerations-and-architectural-choices">Design Considerations and Architectural Choices</a>
    <ul>
      <li><a href="#architecture-overview">Architecture Overview</a></li>
      <li><a href="#key-design-decisions">Key Design Decisions</a></li>
      <li><a href="#design-patterns-used">Design Patterns Used</a></li>
      <li><a href="#sending-a-day-end-to-end-flow">Sending a Day: End-to-End Flow</a></li>
    </ul>
  </li>
  <li>
    <a href="#comprehensive-summary">Comprehensive Summary</a>
    <ul>
      <li><a href="#implementation-of-version-control">Implementation of Version Control</a></li>
      <li><a href="#github-actions">GitHub Actions</a></li>
    </ul>
  </li>
  <li><a href="#video-demonstration">Video Demonstration</a></li>
  <li><a href="#technology-stack">Technology Stack</a></li>
  <li><a href="#repository-layout">Repository Layout</a></li>
  <li>
    <a href="#getting-started">Getting Started</a>
    <ul>
      <li><a href="#prerequisites">Prerequisites</a></li>
      <li><a href="#step-1-clone-and-build">Step 1: Clone and Build</a></li>
      <li><a href="#step-2-configure-the-dashboards-secrets">Step 2: Configure the Dashboard's Secrets</a></li>
      <li><a href="#step-3-run-the-dashboard">Step 3: Run the Dashboard</a></li>
      <li><a href="#step-4-create-people">Step 4: Create People</a></li>
      <li><a href="#step-5-run-the-widget-windows">Step 5: Run the Widget (Windows)</a></li>
      <li><a href="#step-6-try-a-full-round-trip-in-two-minutes">Step 6: Try a Full Round Trip in Two Minutes</a></li>
    </ul>
  </li>
  <li>
    <a href="#configuration-reference">Configuration Reference</a>
    <ul>
      <li><a href="#dashboard-settings">Dashboard Settings</a></li>
      <li><a href="#widget-settings">Widget Settings</a></li>
      <li><a href="#widget-data-files">Widget Data Files</a></li>
      <li><a href="#per-user-check-in-settings">Per-User Check-In Settings</a></li>
    </ul>
  </li>
  <li>
    <a href="#using-the-widget">Using the Widget</a>
    <ul>
      <li><a href="#the-day-screen-by-screen">The Day, Screen by Screen</a></li>
      <li><a href="#clients-projects-and-activities">Clients, Projects and Activities</a></li>
      <li><a href="#check-in-rules">Check-In Rules</a></li>
    </ul>
  </li>
  <li>
    <a href="#using-the-dashboard">Using the Dashboard</a>
    <ul>
      <li><a href="#pages">Pages</a></li>
      <li><a href="#roles-and-submission-expectations">Roles and Submission Expectations</a></li>
      <li><a href="#days-and-time-zones">Days and Time Zones</a></li>
    </ul>
  </li>
  <li>
    <a href="#domain-model-and-business-rules">Domain Model and Business Rules</a>
    <ul>
      <li><a href="#app-data-model-appdbcontext">App Data Model (AppDbContext)</a></li>
      <li><a href="#seeded-top-level-categories">Seeded Top-Level Categories</a></li>
      <li><a href="#business-rules">Business Rules</a></li>
      <li><a href="#identity-data-model-authdbcontext">Identity Data Model (AuthDbContext)</a></li>
    </ul>
  </li>
  <li>
    <a href="#import-and-export-formats">Import and Export Formats</a>
    <ul>
      <li><a href="#json-import-api">JSON Import (API)</a></li>
      <li><a href="#csv-import-website-and-api">CSV Import (Website and API)</a></li>
      <li><a href="#import-validation-rules">Import Validation Rules</a></li>
      <li><a href="#timesheet-export-company-layout">Timesheet Export (Company Layout)</a></li>
      <li><a href="#sample-day-csv">Sample-Day CSV</a></li>  
    </ul>
  </li>
  <li>
    <a href="#rest-api-reference">REST API Reference</a>
    <ul>
      <li><a href="#conventions-at-a-glance">Conventions at a Glance</a></li>
      <li><a href="#authentication-flow">Authentication Flow</a></li>
      <li><a href="#endpoints">Endpoints</a></li>
      <li><a href="#access-rules-and-errors">Access Rules and Errors</a></li>
    </ul>
  </li>
  <li>
    <a href="#security">Security</a>
    <ul>
      <li><a href="#authentication-and-accounts">Authentication and Accounts</a></li>
      <li><a href="#transport-and-browser">Transport and Browser</a></li>
      <li><a href="#input-and-output">Input and Output</a></li>
      <li><a href="#rate-limiting">Rate Limiting</a></li>
      <li><a href="#audit-trail">Audit Trail</a></li>
      <li><a href="#widget-security">Widget Security</a></li>
      <li><a href="#build-and-supply-chain">Build and Supply Chain</a></li>
    </ul>
  </li>
  <li>
    <a href="#databases-and-migrations">Databases and Migrations</a>
    <ul>
      <li><a href="#contexts-and-providers">Contexts and Providers</a></li>
      <li><a href="#adding-a-migration">Adding a Migration</a></li>
      <li><a href="#how-migrations-stay-in-sync">How Migrations Stay in Sync</a></li>
    </ul>
  </li>
  <li>
    <a href="#testing">Testing</a>
    <ul>
      <li><a href="#running-the-tests">Running the Tests</a></li>
      <li><a href="#what-the-tests-cover">What the Tests Cover</a></li>
    </ul>
  </li>
  <li>
    <a href="#deployment">Deployment</a>
    <ul>
      <li><a href="#dashboard-on-azure-app-service">Dashboard on Azure App Service</a></li>
      <li><a href="#running-the-dashboard-elsewhere">Running the Dashboard Elsewhere</a></li>
      <li><a href="#distributing-the-widget">Distributing the Widget</a></li>
    </ul>
  </li>
  <li>
    <a href="#troubleshooting">Troubleshooting</a>
    <ul>
      <li><a href="#dashboard-and-api">Dashboard and API</a></li>
      <li><a href="#widget">Widget</a></li>
    </ul>
  </li>
</ol>

## Project Overview
Time Planner is a check-in based time tracker made for software development teams. Its a Windows desktop widget that asks developers what the have been working on, and a web dashboard where they can review their time and administrators can oversee the team.

Time Planner was built for Leading Edge. The widget sits in the corner of the screen and checks in on them at a set interval, at check-in, it asks the user what they have been doing, where the user can then enter the client, a project, a activity, and any extra notes they wish to add. At the end of the day the user reviews the timesheet, fixes anything that is wrong and send the day to the dashboard. At the dashboard the user can see their won hours, and reports, and administrators can see who submitted, where the teams time went, and can export timesheets in the companies existing spreadsheet layout.

| Part                      | What it is                                                                                                   | Runs on                                          |
| ------------------------- | ------------------------------------------------------------------------------------------------------------ | ------------------------------------------------ |
| **TimePlanner.Widget**    | WPF tray app that tracks the working day, prompts check-ins, keeps a local timesheet and sends finished days | Windows 10 / 11 (x64)                            |
| **TimePlanner.Dashboard** | ASP.NET Core MVC website + versioned REST API (`/api/v1`) with cookie and JWT authentication                 | Any .NET 10 host (deployed to Azure App Service) |
| **TimePlanner.Core**      | Shared class library: domain model, EF Core data layer, check-in engine, business rules, sync client         | Used by both of the above                        |

Current version: **0.1.1** (set in [`Directory.Build.props`](Directory.Build.props); widget releases take their version from the git tag).

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Features

### Desktop Widget

- **Check-in prompts** - At a chosen interval (15 min to 3 h; 90 min by default). Only active working time counts towards the interval: breaks are taken out, and no prompt fires during the lunch window.

- **Snooze:** A check-in (10 minutes, up to 3 times per check-in by default).

- **Log now:**  Can be done at any time, without waiting for a prompt.

- **Pause / resume:** Used for breaks. Break time is never logged, and a period that spans a break is split into separate entries.

- **Breadcrumb pickers:** For *Client › Project* and *Activity › Sub-activity › …* (up to 3 levels), with recent choices and the last-used project and activity pre-filled. New clients, projects and sub-activities can be typed in on the spot, and unused ones can be removed.

- **End of day flow:** Log the remaining time or end without logging, then see a day summary (time logged, number of entries).

- **Timesheet review:** Step through past days, see entries, untracked gaps and breaks, fill gaps and add missing time, edit or delete entries (with a 10-second undo), and export the day to CSV.

- **Send day:** Send to the dashboard once the day has ended. The widget signs in once, never stores the password, and stays signed in for up to 30 days (a sign-in session is capped at 90 days).

- **Offline first:** Everything is saved to a local SQLite database, so the widget works without a network connection; only *sending* a day needs the dashboard.

- **Idle appearance:**  A pill (time left), ring (fills as time runs out) or dot. The widget can be always visible, faded until you point at it, or hidden in the tray until a check-in. A sound on check-in is optional.
<p align="right">(<a href="#readme-top">back to top</a>)</p>

### Web Dashboard (User)
- **Home:** Today's timeline ribbon, hours logged against the daily goal, unlogged gaps, the last submitted day, and a breakdown by category and project for today or this week.

- **My Timesheet:** A week or month laid out day by day, with each working day marked *Submitted*, *Pending*, *Missing* or *Not due* (read-only; corrections are made in the widget and sent again).

- **My Reports:** Totals, billable vs non-billable split, and breakdowns by category and project for a week, month or custom range. Downloads to Excel or CSV in the company's timesheet layout, with quick downloads for recent periods.

- **Upload CSV:** Import a timesheet file (for example one exported from the widget).

- **Settings:** Change your display name and password. **Accessibility:** text size (100–150%), bold and italic, remembered in the browser.
<p align="right">(<a href="#readme-top">back to top</a>)</p>


### Web Dashboard (Admin)
- **Overview:** Team hours, hours by category, project and person, and the submission rate for today and for the period.

- **Submissions:** A grid of every developer against every working day of the week or month, plus a detailed hours report for any range, grouped by project, company, person, day or activity.

- **Exports:** One person's timesheet as Excel or CSV, or a `.zip` with one file per person.

- **Users:** Create accounts (a temporary password is shown once), reset passwords, deactivate and reactivate accounts, and see lockouts and last sign-in.

- **Audit log:** Log of security-relevant events (through the API).


## Rest API
A versioned JSON API (`/api/v1`) with JWT bearer tokens and rotating refresh tokens, Swagger UI in development, row-level access rules, rate limiting and RFC 7807 problem-details errors. The widget uses it to sign in and send days, and any other client can use it as well.
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## How it works
**Key design points**
1. **Local first.** The widget keeps its own SQLite database under `%LOCALAPPDATA%\TimePlanner`. It identifies the local user by Windows account name, creates them on first launch with default settings, and adds the internal company _Leading Edge (Internal)_ with its project Internal.
2. **Two separate databases.** The widget's local database and the dashboard's database are separate. They share the same schema (both use TimePlanner.Core), but nothing is replicated row by row. A day is sent as a list of entries described by names (company, project, activity path) rather than database ids, so a row means the same thing on any machine.
3. **The dashboard owns identity.** The dashboard keeps ASP.NET Core Identity accounts (ApplicationUser) in a separate AuthDbContext. Each account links to a time-tracking profile (AppUser) through AppUserId, which travels as the uid claim in both the cookie and the JWT. An imported entry always belongs to the caller identified by the token, never to a user named in the request.
4. **Send replaces the day.** The widget sends a finished day with `replaceDays: true`. The server checks every row first, then, in one transaction, deletes that user's entries for the day and stores the new ones. Sending the same day again after a correction is safe and leaves the server matching the widget.
5. **A submission is a record, not an approval.** Every accepted import records a DaySubmission (user + date + time). The admin grid is built from these records; there is no approve or reject workflow.

**Sending a day**
If the server rejects the day, the widget lists the row errors and nothing is stored. If the dashboard can't be reached, the day stays saved locally and can be sent later.
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Technology stack
| Area      | Technology                                                                                                                                                 |
| --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Runtime   | .NET 10 (`net10.0`, widget `net10.0-windows`), C# with nullable reference types and implicit usings                                                        |
| Desktop   | WPF, [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) (tray icon), Microsoft.Extensions.Hosting / DependencyInjection               |
| Web       | ASP.NET Core MVC (Razor views), ASP.NET Core Identity, JWT bearer authentication, built-in rate limiter                                                    |
| Data      | Entity Framework Core 10 with **SQLite** (widget, local dev, tests) and **SQL Server** (hosted dashboard)                                                  |
| Files     | [CsvHelper](https://joshclose.github.io/CsvHelper/) (CSV), [ClosedXML](https://github.com/ClosedXML/ClosedXML) (Excel `.xlsx`)                             |
| API docs  | Swashbuckle (OpenAPI + Swagger UI)                                                                                                                         |
| Other     | Windows DPAPI (`System.Security.Cryptography.ProtectedData`) for the widget's token. HtmlSanitizer is referenced by the dashboard but not used in code yet |
| Tests     | xUnit, `Microsoft.AspNetCore.Mvc.Testing` (in-memory test server), coverlet                                                                                |
| CI/CD     | GitHub Actions, Dependabot, CodeQL, Azure Web Apps deploy                                                                                                  |
| Front end | Plain CSS with design tokens and plain JavaScript (no framework, no CDN). Self-hosted Inter and Lora fonts                                                 |


<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Repository layout

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Getting started
### Prerequisites
| Tool                                                 | Needed for                      | Notes                                                                                                                    |
| ---------------------------------------------------- | ------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | everything                      | CI uses `10.0.x`                                                                                                         |
| Windows 10 or 11                                     | building and running the widget | The WPF project targets `net10.0-windows` and can't be built on Linux or macOS. Core, Dashboard and tests build anywhere |
| An IDE                                               | development                     | A Visual Studio version that supports .NET 10 and `.slnx` solutions, JetBrains Rider, or VS Code with C# Dev Kit         |
| `dotnet-ef`                                          | only to add migrations          | `dotnet tool install --global dotnet-ef`                                                                                 |
| SQL Server / Azure SQL                               | optional                        | Only if you want to run the dashboard on SQL Server locally. SQLite is the default                                       |

<p align="right">(<a href="#readme-top">back to top</a>)</p>

### 1. Clone and build

```bash
git clone <https://github.com/Doingle/TimePlanner_LeadingEdge_INSY7315_XBCAD.git>
cd TimePlanner_LeadingEdge_INSY7315_XBCAD

# Windows: build the whole solution
dotnet build TimePlanner.slnx

# Linux/macOS: build everything except the WPF widget
dotnet build TimePlanner.Dashboard
dotnet build TimePlanner.Core.Tests
dotnet build TimePlanner.Api.Tests
```

Restore runs a NuGet vulnerability audit (`NuGetAuditMode=all`). A **high or critical** advisory on any direct or transitive package **fails the build** (`NU1903`, `NU1904`). Update the package rather than suppressing the warning

### 2. Configure the dashboard's secrets
No credential is committed to the repository. The dashboard needs at least a **JWT signing key**, and you'll want a **first administrator**. For local development, store them with user secrets (the project already has a `UserSecretsId`

```bash
cd TimePlanner.Dashboard

# a random key of at least 32 bytes (both commands print one)
#   bash:        openssl rand -base64 48
#   PowerShell:  [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:Key" "<paste the generated key>"

# the first admin, created at startup if no account with this email exists yet
dotnet user-secrets set "Seed:AdminEmail" "admin@example.com"
dotnet user-secrets set "Seed:AdminPassword" "<a strong password>"
```

The admin password must satisfy the Identity policy: **at least 12 characters, with an upper-case letter, a lower-case letter, a digit and a symbol**. If it doesn't, startup stops with Seeding the admin failed: ....

### 3. Run the dashboard

```bash
dotnet dev-certs https --trust          # once per machine
dotnet run --project TimePlanner.Dashboard --launch-profile https
```

|URL|What|
|---|---|
|`https://localhost:7043`|the website (sign in with the seeded admin)|
|`https://localhost:7043/swagger`|Swagger UI for the API (Development only, unless `Api:EnableDocs=true`)|
|`https://localhost:7043/health`|anonymous health check, `{"status":"ok"}`|
Use the https profile. The sign-in cookie is marked Secure, so the plain-http profile (`http://localhost:5075`) isn't a reliable way to sign in.

On first start the dashboard creates and migrates its database automatically (`timeplanner.db`, a SQLite file in the working directory, by default). It also creates the Developer and Admin roles and seeds the admin account. There is no SQL script to run.

### 4. Create people
Sign in as the admin, open Team → Users, and create an account for each developer (name, email, role). The temporary password is shown **once**. Hand it over securely. The person must choose their own password at first sign-in before they can use anything else, the widget included.

### 5. Run the widget (Windows)
```powershell
# against the hosted dashboard (the default address)
dotnet run --project TimePlanner.Widget

# against your local dashboard
$env:TIMEPLANNER_DASHBOARD_URL = "https://localhost:7043/"
dotnet run --project TimePlanner.Widget
```

The widget's launch profiles (in Visual Studio's run menu) cover the common cases:

|Profile|What it does|
|---|---|
|`TimePlanner.Widget`|normal start|
|`Widget (sample day)`|starts with `--sample-day` (see below)|
|`Widget (sample day, local dashboard)`|`--sample-day` and `TIMEPLANNER_DASHBOARD_URL=https://localhost:7043/`|

The widget opens on Setup (_Welcome to Time Planner_): pick a check-in interval and press Start tracking.


### 6. Try a full round trip
1. Start the dashboard locally (step 3) and create a developer account (step 4). Sign in once on the website as that developer and set a permanent password.
2. Start the widget with the Widget (sample day, local dashboard) profile. --sample-day loads SampleData/sample-day.csv into the most recent weekday before today, provided that day has no entries yet. It creates the entries and a finished day session, and turns gaps of 30 minutes or more into breaks.
3. Right-click the tray icon → Timesheet, step back to that day, review it and press Send day. Sign in as the developer when asked.
4. On the website, that day now shows as Submitted on My Timesheet (step back a week if it was last Friday), and in Team → Submissions for the admin.
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Configuration reference
### Dashboard settings
Settings come from the standard ASP.NET Core sources: `appsettings.json` → `appsettings.{Environment}.json` → user secrets (Development) → environment variables → command line. In environment variables, replace `:` with `__` (for example `Jwt__Key`, `ConnectionStrings__Default`).

| Key                                     | Default                      | Description                                                                                                                                                                                                                                                                                     |
| --------------------------------------- | ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ConnectionStrings:Default`             | `Data Source=timeplanner.db` | **Required.** Connection string for both the app data and the identity data (same database, separate migration histories).                                                                                                                                                                      |
| `Database:Provider`                     | `Sqlite`                     | `Sqlite` or `SqlServer`. Each provider has its own context types and migrations.                                                                                                                                                                                                                |
| `Jwt:Key`                               | _(none)_                     | **Required.** HMAC-SHA256 signing key, at least 32 bytes (UTF-8). API authentication throws `Jwt:Key must be configured with at least 32 bytes` without it.                                                                                                                                     |
| `Jwt:Issuer`                            | `TimePlanner.Dashboard`      | Token issuer (validated).                                                                                                                                                                                                                                                                       |
| `Jwt:Audience`                          | `TimePlanner.Clients`        | Token audience (validated).                                                                                                                                                                                                                                                                     |
| `Jwt:ExpiryMinutes`                     | `30`                         | Lifetime of an access token.                                                                                                                                                                                                                                                                    |
| `Jwt:RefreshDays`                       | `30`                         | Lifetime of each refresh token.                                                                                                                                                                                                                                                                 |
| `Jwt:RefreshMaxDays`                    | `90`                         | Absolute limit for one sign-in. After this the client must sign in with the password again, however often it refreshed.                                                                                                                                                                         |
| `Seed:AdminEmail`, `Seed:AdminPassword` | _(none)_                     | When both are set and no account with that email exists, an Admin account and its profile are created at startup. Changing them later does **not** change an existing account.                                                                                                                  |
| `AllowedHosts`                          | `localhost;127.0.0.1;[::1]`  | Host filtering. **Must be set to your public host name when deployed**, or every request is rejected with `400`.                                                                                                                                                                                |
| `Proxy:TrustForwardedHeaders`           | `false`                      | Honour `X-Forwarded-For` / `X-Forwarded-Proto`. Turn it on behind a TLS-terminating reverse proxy (such as Azure App Service), and only when the app can be reached through the proxy alone. Without it, rate limits count all users as one address and secure-only cookies and forms can fail. |
| `Api:EnableDocs`                        | `false`                      | Serve Swagger UI and the OpenAPI document outside Development.                                                                                                                                                                                                                                  |
| `RateLimiting:LoginPerMinute`           | `10`                         | Sign-in, refresh, sign-out and password-change requests per **IP address** per minute.                                                                                                                                                                                                          |
| `RateLimiting:ImportPerMinute`          | `20`                         | Timesheet imports and uploads per **signed-in user** per minute.                                                                                                                                                                                                                                |
| `Display:TimeZone`                      | `Africa/Johannesburg`        | The company time zone used for "today" (Windows and IANA ids are both tried; falls back to UTC).                                                                                                                                                                                                |
| `Goals:DailyHours`                      | `8`                          | Daily goal on Home when the person has no saved goal.                                                                                                                                                                                                                                           |
| `Home:DayStart`, `Home:DayEnd`          | `08:00`, `17:00`             | The window of Home's timeline (`HH:mm`), widened to fit earlier or later entries.                                                                                                                                                                                                               |
| `Home:LunchStart`, `Home:LunchEnd`      | `12:00`, `13:00`             | Lunch is not reported as an unlogged gap on Home.                                                                                                                                                                                                                                               |
| `Logging:*`                             | Information                  | Standard ASP.NET Core logging configuration.                                                                                                                                                                                                                                                    |
Other built-in behaviour (not configurable): sign-in cookie lifetime 8 hours (sliding); account lockout after 5 failed attempts for 15 minutes; request body limit 2 MB; HSTS for 365 days outside Development.

### Widget settings
| Setting                     | Where                                                        | Description                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| --------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `TIMEPLANNER_DASHBOARD_URL` | environment variable                                         | Dashboard address. Must be **https**, except a loopback address (`http://localhost:...` is allowed). Defaults to the hosted Azure dashboard (`SyncServiceCollectionExtensions.DefaultDashboardUrl`).                                                                                                                                                                                                                                        |
| `ConnectionStrings:Default` | optional `appsettings.json` next to `TimePlanner.Widget.exe` | Overrides the local database location. Defaults to `%LOCALAPPDATA%\TimePlanner\timeplanner.db`.                                                                                                                                                                                                                                                                                                                                             |
| `--sample-day`              | command-line switch                                          | Loads the sample day. Available in release builds too.                                                                                                                                                                                                                                                                                                                                                                                      |
| `--screen <id>`             | command-line switch, **Debug builds only**                   | Opens a single screen on throwaway in-memory sample data, for design review. Ids: `setup`, `settings`, `advanced-hidden`, `idle-pill`, `idle-ring`, `idle-dot`, `timer`, `timer-paused`, `log-entry`, `log-now`, `menu-open`, `menu-coding`, `menu-adding`, `no-activity`, `entry-saved`, `end-day`, `end-day-logged`, `day-ended`, `day-ended-empty`, `timesheet`, `check-in`. Debug builds also add **Preview screens** to the tray menu. |

**Files the widget keeps** in `%LOCALAPPDATA%\TimePlanner\`:

|File|Contents|
|---|---|
|`timeplanner.db`|The local SQLite database: entries, days and breaks, projects, activities, settings. Migrated automatically on start.|
|`widget.json`|Appearance preferences: idle visibility, idle shape, sound on check-in.|
|`sent-days.json`|Which days were sent and when (last 366), and whether a day changed after it was sent.|
|`dashboard-token.bin`|Dashboard access and refresh token, encrypted with Windows DPAPI for the current Windows user. The password is never stored.|

Deleting the folder resets the widget completely. **Unsent time is lost** if you do.

### Per-user check-in settings
Stored per user in UserSettings. Ranges are enforced both by SettingsService and by database CHECK constraints.

| Setting                   | Default       | Allowed                                    | Meaning                                                                                          |
| ------------------------- | ------------- | ------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| `CheckInIntervalMinutes`  | 90            | 5–480                                      | Active working minutes between check-ins. The widget offers 15, 20, 30, 45, 60, 90, 120 and 180. |
| `SnoozeMinutes`           | 10            | 1–60                                       | How long a snooze delays the prompt.                                                             |
| `MaxSnoozes`              | 3             | 0–10                                       | Snoozes allowed per check-in.                                                                    |
| `MaxSkipsPerDay`          | 3             | 0–10                                       | Skipped check-ins allowed per day.                                                               |
| `DailyGoalHours`          | 8             | 0.5–24                                     | Target hours per day (shown on the dashboard's Home).                                            |
| `IgnoredCheckInMinutes`   | 5             | 1–60                                       | How long an unanswered prompt waits before the ignored rule applies.                             |
| `IgnoredCheckInAction`    | `KeepAsking`  | `KeepAsking`, `LogAsUntracked`, `AutoSkip` | What happens to an unanswered prompt.                                                            |
| `LunchStart` / `LunchEnd` | 12:00 / 13:00 | start < end                                | No prompts in this window. A prompt that would fall inside it waits until it ends.               |
The widget's Setup and Settings screens currently change **only the interval** (plus the appearance preferences above). The other values use their defaults 
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Using the widget
### The day, screen by screen
1. **Setup** (_Welcome to Time Planner_): choose how often to check in. Advanced options set what the widget looks like when idle (Visible, Faded or Hidden; Pill, Ring or Dot) and whether a sound plays. Start tracking opens the day.
2. **Idle:** the widget rests in the corner (bottom right at first). Drag it and it re-anchors to the nearest corner. Click it to open the **Timer** with the countdown, Pause/Resume, Settings, Log now and End day.
3. **Check-in:** when the interval of active time has passed, Time to check in appears: Log what you did since 3:05 PM. Choose Log time or Snooze.
4. **Log entry:** choose Client › Project and an Activity (top-level category, then optional sub-activities), and add an optional note (up to 500 characters). The period runs from the end of your last entry (or the start of the day) until now. Breaks inside it are cut out, so it may be saved as more than one entry. Pieces under a minute are dropped.
5. **End day:** if time is still unlogged, you're offered Log the last 1h 12m or End without logging. Day ended shows the total logged and the number of entries, with Export CSV, Open timesheet and Resume day (to carry on as if you hadn't ended).
6. **Timesheet:** step through days (not into the future). Rows are entries, Untracked gaps (5 minutes or more) and Breaks. Click an entry to edit it, press Fill on a gap to log it, use + Add time for other missing work, or delete an entry with a 10-second Undo. The status line shows Not sent, Sent 5:12 PM or Changed since sent · send again. Send day is enabled once the day is finished and has entries.

Picking things in the widget:

- New clients and projects can be typed into the project picker (New company…, New project in …). A closed project comes back when typed again.
- Sub-activities can be added under any activity, up to three levels deep (for example Coding › Feature work › Frontend). The six top-level categories are fixed.
- Removing an item deletes it if nothing was logged to it. If time was logged, it's hidden instead and the logged time is kept. The internal company and the top-level categories can't be removed.
<p align="right">(<a href="#readme-top">back to top</a>)</p>



## Using the dashboard
Every page needs a sign-in, except Login, Privacy, the error page, /health and (when enabled) the Swagger page.

| Page               | Path                                                | Who      | Contents                                                                                                                                                                     |
| ------------------ | --------------------------------------------------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Login              | `/Account/Login`                                    | anyone   | Email + password. The same message is shown for every failure.                                                                                                               |
| Home               | `/` (`?period=week`)                                | everyone | Today's timeline, minutes vs goal, gaps over 15 min (lunch excluded), last submitted day in full, breakdown for today or the week.                                           |
| My Timesheet       | `/Timesheet?view=week\|month&date=yyyy-MM-dd`       | everyone | Day-by-day status (Submitted, Pending = today not sent yet, Missing = past working day not sent, Not due, Non-working day). The month view is a keyboard-navigable calendar. |
| My Reports         | `/Report?view=week\|month&date=…` or `?from=…&to=…` | everyone | Totals, billable split, by category and project, submitted-day count, Excel/CSV download, quick downloads for the last 8 weeks or 6 months.                                  |
| Upload CSV         | `/CsvUpload`                                        | everyone | Import a CSV timesheet. The template is at /CsvUpload/Template.                                                                                                              |
| Settings           | `/Settings`                                         | everyone | Display name, password change (needs the current password; signs out other sessions).                                                                                        |
| Accessibility      | `/Settings/Accessibility`                           | everyone | Text size Default / Large (112%) / Larger (125%) / Largest (150%), bold, italic. Kept in the tp_text cookie for a year.                                                      |
| Team · Overview    | `/Admin`                                            | Admin    | Team totals, breakdowns, submission rate today and for the period.                                                                                                           |
| Team · Submissions | `/Admin/Submissions`                                | Admin    | People × working days grid, plus the detailed report (any range, grouping, person) with download.                                                                            |
| Team · Exports     | `/Admin/Exports`                                    | Admin    | One person's file, or everyone as a .zip, as Excel or CSV.                                                                                                                   |
| Team · Users       | `/Users`                                            | Admin    | Account list; create, reset password, deactivate, reactivate.  


**Roles.**
There are two: **Developer** (sees and submits their own time) and **Admin** (also manages accounts and sees everyone's data). The submissions grid expects every _active Developer_ to submit each working day (Monday to Friday). Anyone else who submitted in the period is shown too.

**Days and time zones.**
Entries are stored as local wall-clock times without a time zone, exactly as the person worked them. "Today" on the dashboard is worked out in Display:TimeZone, so a server running on UTC doesn't roll the day over at 02:00 South African time. Submission times are stored in UTC and shown in the company time zone.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Domain model and business rules
### App data (`AppDbContext`, in `TimePlanner.Core`)
### Rules worth knowing
### Identity data (`AuthDbContext`, in `TimePlanner.Dashboard`)

This context uses its own history table (`__AuthMigrationHistory`), separate from the time data.

| Table | Purpose | Key columns |
|---|---|---|
| ASP.NET Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, …) | Logins and roles (**Admin**, **Developer**) | `AspNetUsers` adds `AppUserId` (the time-tracking profile), `IsActive` (default true) and `MustChangePassword` |
| `AuditEvents` | Security audit trail, add-only | `TimestampUtc`, `UserId`, `Email`, `Action`, `Detail`, `IpAddress` |
| `DaySubmissions` | That a person's day reached the dashboard | `AppUserId`, `Date`, `SubmittedAtUtc`; unique on (`AppUserId`, `Date`) |
| `RefreshTokens` | Long-lived API sessions | `TokenHash` (unique, SHA-256), `FamilyId`, `FamilyStartedUtc`, `CreatedUtc`, `ExpiresUtc`, `UsedUtc`, `RevokedUtc`, `Stamp` |

`AppUserId` points at a profile in the other database. There is no foreign key between the two, because they are separate contexts.

---

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Import and export formats

### JSON import (API, used by the widget)

`POST /api/v1/timesheets/import` with a bearer token.

```json
{
  "replaceDays": true,
  "entries": [
    {
      "company": "Acme Ltd",
      "project": "Website Redesign",
      "activity": "Coding > Frontend",
      "start": "2026-09-28T09:00:00",
      "end": "2026-09-28T10:30:00",
      "note": "Built the login page",
      "method": "Manual"
    }
  ]
}
```

- `start` and `end` are **local times with no offset** (no `Z`, no `+02:00`), exactly as the person worked them.
- `activity` is a path separated by `>`. The first part must be one of **Meeting, Coding, Email, Admin, Design, Learning**. Up to three levels are allowed, and new sub-activities are created on the fly.
- `note` and `method` are optional. `method` is `Manual` (default), `AutoPrompted` or `AutoTracked`.
- There is **no user field**: every entry belongs to the person the token was issued to.
- `replaceDays` (default `false`) replaces that person's stored entries for each day in the request. The widget sends `true`.

Success returns `200`:

```json
{ "created": 12, "skipped": 0, "errors": [] }
```

`skipped` counts entries that were already stored (same task, start and end), so sending the same data twice is harmless. If any row fails, the response is `400`, **nothing is stored**, and `errors` lists each problem as `{ "row": 3, "message": "..." }` (`row` 0 means the whole file).

### CSV import (website upload and API)

The website's **Upload CSV** page and `POST /api/v1/timesheets/import/csv` (multipart form field `file`) use the same rules as the JSON import.

```csv
Company,Project,Activity,Start,End,Note,Method
Acme Ltd,Website Redesign,Coding > Frontend,2026-09-28 09:00,2026-09-28 10:30,Built the login page,Manual
```

- The header row is required. Column order and capitalisation do not matter. `Company`, `Project`, `Activity`, `Start` and `End` are required, `Note` and `Method` are optional, and **any other column is rejected** so a misspelt heading cannot silently drop data.
- Times are written `yyyy-MM-dd HH:mm` (seconds and a `T` separator are also accepted).
- A template can be downloaded from `/CsvUpload/Template`.

### Validation (both formats)

| Rule | Limit |
|---|---|
| Rows per import | 5,000 |
| Upload size | 1 MB for a CSV file, 2 MB for any request body |
| Company and project names | 1 to 100 characters |
| Activity names (each level) | 1 to 60 characters, up to 3 levels |
| Names starting with `=`, `+`, `-` or `@`, or containing control characters | refused (stops spreadsheet formula injection) |
| Entry length | 1 minute to 24 hours, end after start |
| Dates | after 1 January 2020, and not in the future (one day of allowance for time zones) |
| Overlaps | two entries for the same person cannot cover the same minute |
| Note | up to 2,000 characters |
| Project status | entries for a closed project are refused |

The whole import is **all-or-nothing**: every row is checked first, then everything is saved in one transaction.

### Timesheet export (company layout)

Exports use the company's timesheet layout, one row per entry:

| Column | Example |
|---|---|
| Date | `2026-09-28` |
| Activity/Task | the note, or the activity path if there is no note |
| Client / Project | `Acme Ltd / Website Redesign` (just the company name for internal work) |
| Start Time, End Time | `09:00`, `10:30` |
| Duration (hours) | `1.50` (always a point, whatever the server's language) |
| Notes | the activity path |
| Billable | `Yes`, `No` or `Internal` |

### Sample-day CSV

The Excel file has one worksheet per month, each titled `<Name> Time Log`, with the same columns. Downloads are available as Excel (`.xlsx`) or CSV from the website (My Reports, and Team → Exports for administrators, who can also download a `.zip` with one file per person). The API offers CSV at `GET /api/v1/reports/timesheet.csv` and, for administrators, `GET /api/v1/administration/exports/timesheets`. Both formats are safe to open: CSV is written with formula escaping, and the Excel file stores text such as `=SUM(A1)` as plain text, not a formula (there is a test for each).

---
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## REST API reference

Interactive documentation is at `/swagger` in Development, or anywhere when `Api:EnableDocs` is `true`. Paste a token into the **Authorize** box to try calls.

### Authentication flow

```text
POST /api/v1/auth/login     { "email": "...", "password": "..." }
  -> 200 { accessToken, tokenType: "Bearer", expiresAtUtc, refreshToken, refreshExpiresAtUtc }

(send)  Authorization: Bearer <accessToken>    on every other call

POST /api/v1/auth/refresh   { "refreshToken": "..." }
  -> 200 the same shape as login, with a NEW refreshToken. The old one is now dead.

POST /api/v1/auth/logout    { "refreshToken": "..." }
  -> 204 always, and the session ends
```

- The **access token** lasts 30 minutes. The **refresh token** lasts 30 days, renewed on each use, and the whole sign-in session ends after 90 days.
- A refresh token works **once**. If an already-used one is presented again, it is treated as stolen and the whole session ends (the next refresh gets `401`).
- Changing a password, deactivating the account, or an account still on a temporary password all end every refresh token.
- Every failure of login, refresh and logout looks the same (`401`, or `204` for logout), so nobody can tell an unknown email from a wrong password, or a stolen token from an expired one.
- An account with a temporary password gets `403` from login until it has chosen a new password on the website.
- Clients must store the new `refreshToken` after every refresh, and should refresh from one place only.

### Endpoints

All paths start with `/api/v1`. Every endpoint needs a bearer token unless marked **anyone**. **Admin** means the Admin role. Where a *developer* sees only their own data, an admin may pass `userId` to look at one person.

| Method and path | Who | What it does |
|---|---|---|
| `POST /auth/login` | anyone | Exchange an email and password for tokens. |
| `POST /auth/refresh` | anyone | Swap a refresh token for a new pair. |
| `POST /auth/logout` | anyone | End the session a refresh token belongs to. |
| `GET /auth/me` | signed in | Who the token belongs to (email, profile id, roles). |
| `GET /profile` | signed in | Your account details. |
| `PUT /profile/name` | signed in | Change your display name. |
| `POST /profile/password` | signed in | Change your password (needs the current one). |
| `GET /overview?period=today\|week` | signed in | Your home screen: timeline, hours against goal, last submission, breakdown. |
| `GET /timesheets?view=week\|month&date=` | signed in | A week or month day by day with each day's status. |
| `POST /timesheets/import` | signed in | Import entries as JSON (see above). |
| `POST /timesheets/import/csv` | signed in | Import a CSV file (multipart field `file`). |
| `GET /time-entries?from=&to=` | signed in | Entries between two dates (at most 92 days). |
| `GET /reports/summary` | signed in | Totals, billable split, breakdown by category, project and person. Give `from` and `to`, or `view` and `date`. |
| `GET /reports/hours?from=&to=&groupBy=` | signed in | Hours grouped by `Project` (default), `Company`, `User`, `Day` or `Activity`. Optional `userId`, `companyId`. |
| `GET /reports/timesheet.csv?from=&to=` | signed in | A timesheet as CSV in the company layout. |
| `GET /companies`, `GET /categories`, `GET /projects`, `GET /projects/{id}`, `GET /tasks` | signed in | Reference data. `projects` takes `companyId` and `activeOnly`. `tasks` returns only your own. |
| `GET /administration/overview?view=&date=` | Admin | Team hours and submission rate. |
| `GET /administration/submissions?view=&date=` | Admin | People against working days. |
| `GET /administration/exports/timesheets?view=&date=&userId=&format=` | Admin | One person's file, or a `.zip` of everyone's without `userId`. `format` is `csv` (default) or `xlsx`. |
| `GET /users`, `POST /users` | Admin | List accounts, create one (the response holds the temporary password, shown once). |
| `POST /users/{id}/reset-password`, `/deactivate`, `/reactivate` | Admin | Account actions. |
| `GET /audit?action=&take=` | Admin | Newest audit events first (`take` 1 to 500, default 100). |
| `GET /health` (no `/api/v1`) | anyone | `{"status":"ok"}`, or `503` if the database cannot be reached. |

### Conventions

- **Format.** JSON, camelCase. Dates in a query string are `yyyy-MM-dd`. Times in a body are local, with no offset.
- **Errors.** Failures are JSON problem details (`application/problem+json`) with a `title`. A validation failure is `400`, no token or an invalid one is `401`, a role or ownership problem is `403`, a missing item is `404`, and too many requests is `429` with `Retry-After: 60`. An unexpected failure is `500` with a generic message and a `traceId`, never a stack trace.
- **Ownership.** The owner of any data is decided from the token, never from the request. Changing an id in a URL cannot expose someone else's records.
- **Limits.** Date ranges are at most 92 days. Request bodies are at most 2 MB. Login, refresh, logout and password change share a limit of 10 per minute per address, and imports are limited to 20 per minute per user.
- **Caching.** Responses carry `Cache-Control: no-store`.

---
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Security
**Authentication and accounts**

**Authentication and accounts**

- Passwords are hashed by ASP.NET Core Identity. They must be at least 12 characters with an upper-case letter, a lower-case letter, a digit and a symbol.
- **Lockout:** five wrong passwords lock the account for 15 minutes. Every sign-in failure shows the same message, and an unknown email still costs a password hash, so response time does not reveal which emails have accounts.
- **Temporary passwords.** An administrator creating or resetting an account gets a 16-character random password, shown once. The person must choose their own before doing anything else, on the website or the API.
- **Sessions.** The website cookie is `Secure`, `HttpOnly` and `SameSite=Lax`, and lasts 8 hours with sliding expiry. It is re-checked against the account on **every request**, so deactivating someone or changing their password ends their open sessions at once.
- **Tokens.** Access tokens are signed JWTs (HS256) with issuer and audience checked and a 30-minute life. Each carries a security stamp that is checked on every request. Refresh tokens are random, **stored only as a SHA-256 hash**, single-use, rotated on every use, and a replay ends the whole session. A session is capped at 90 days.
- **Roles.** Two roles, **Developer** and **Admin**. A login is required everywhere by default. Developers see only their own data, and admin-only pages and endpoints check the role.
- **Deactivation** is a flag, not a delete, so history stays in reports. You cannot deactivate yourself or the last active admin.

**Transport and browser**

- HTTPS redirection in every environment, and HSTS (one year) outside Development.
- **Content-Security-Policy** with no exceptions for other sites: `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'` (report-only in Development, and not applied to the Swagger page). The Inter font is self-hosted for this reason.
- Also sent on every response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Permissions-Policy` (camera, microphone, geolocation, payment and USB off), `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Resource-Policy: same-origin`. The `Server` header is removed.
- **CSRF.** Every form post needs an antiforgery token. The antiforgery cookie is `Secure` and `SameSite=Strict`. The bearer-token API is not exposed to CSRF, since browsers do not send an `Authorization` header on their own.
- Personal pages and downloads are sent `no-store`, so the back button on a shared computer does not show someone else's time.
- Behind a proxy that ends TLS, set `Proxy:TrustForwardedHeaders=true` so the real client address and `https` scheme are used. Only turn it on when the app is reachable through the proxy alone.
  
**Input and output**

- Every import is validated before anything is stored, with the limits in the table above, and stored in one transaction.
- Razor encodes every value written into a page, so names and notes containing HTML are shown as text. Tests check this on the admin and user pages.
- Names beginning with `=`, `+`, `-` or `@` are refused on import, and CSV exports escape formulas, so a spreadsheet opened from the dashboard cannot run injected formulas.
- Request bodies are limited to 2 MB, and a CSV file to 1 MB.
- Database access is through Entity Framework with parameters, so there is no string-built SQL.
- Error responses never include stack traces or file content.

**Rate limiting**

Sign-in, token refresh, logout and password change are limited to 10 per minute per address (`RateLimiting:LoginPerMinute`), on top of the per-account lockout. Imports and uploads are limited to 20 per minute **per signed-in user** (`RateLimiting:ImportPerMinute`), so one person cannot starve the others. A limited request gets `429` and `Retry-After: 60`.

**Audit trail**

Security-relevant events are written to an add-only `AuditEvents` table: `LoginSucceeded`, `LoginFailed`, `LoginLockedOut`, `LoginBlocked`, `Logout`, `RefreshFailed`, `RefreshTokenReuse`, `UserCreated`, `PasswordReset`, `UserDeactivated`, `UserReactivated`, `PasswordChanged`, `ProfileUpdated`, `TimesheetImported`, `TimesheetImportRejected` and `TimesheetExported`. Each row holds the time, the person, the address and a short detail. **Passwords and tokens are never written.** Text that came from a request is shortened and stripped of control characters, so a crafted value cannot forge or hide log lines. Administrators read it at `GET /api/v1/audit`.

**Widget**

- The widget signs in once and **never stores the password**. It keeps the access and refresh tokens in a file encrypted with **Windows DPAPI** for the current Windows user (`dashboard-token.bin`), so another Windows account, or a copy of the file, cannot read it.
- It only sends a day when the person chooses **Send day**.
- Signing in with a temporary password is refused until the person has chosen their own on the website.
- The widget zip is not code-signed, so Windows may show an "unknown publisher" warning the first time. Each release publishes a SHA-256 checksum so the download can be verified.

**Build and supply chain**

- **Security analysers.** `Directory.Build.props` turns on every .NET security rule, and about 70 of them (weak cryptography, injection, unsafe XML, missing antiforgery, predictable random numbers) **fail the build**.
- **Vulnerable packages.** Every restore audits direct and indirect packages against the public advisory list. A **high or critical** advisory fails the build.
- **Dependabot** opens weekly update pull requests for NuGet packages and GitHub Actions (minor and patch updates grouped).
- **CodeQL** is set up but stays off until GitHub code scanning is enabled (see CI/CD).
- **No secrets in the repository.** The signing key, admin credentials and connection strings come from user-secrets or environment variables. The app refuses to start without a strong signing key.
- **Mutation checks.** Security controls were tested by deliberately breaking them (removing a check, turning off replay detection) and confirming a test fails.

---


## Databases and migrations
### Contexts and providers

| Context | Holds | SQLite migrations in | SQL Server migrations in |
|---|---|---|---|
| `AppDbContext` | people, companies, projects, activities, time entries, settings | `TimePlanner.Core/Migrations` | `TimePlanner.Dashboard/Data/SqlServer/Migrations/App` |
| `AuthDbContext` | logins, audit, submissions, refresh tokens (history table `__AuthMigrationHistory`) | `TimePlanner.Dashboard/Data/Migrations` | `TimePlanner.Dashboard/Data/SqlServer/Migrations/Auth` |

`Database:Provider` chooses SQLite (default) or SQL Server (`SqlServer`). The dashboard applies any missing migrations to **both** contexts when it starts, so there is no SQL script to run. The widget uses `AppDbContext` on SQLite only.

### Adding a migration

**When you change the model, add a migration for every provider that context runs on.** Install the tool once with `dotnet tool install --global dotnet-ef`, then from the repository root:

```bash
# time data, SQLite
dotnet ef migrations add <Name> --context AppDbContext --project TimePlanner.Core

# time data, SQL Server
dotnet ef migrations add <Name>SqlServer --context SqlServerAppDbContext --project TimePlanner.Dashboard --output-dir Data/SqlServer/Migrations/App

# logins, SQLite  (see the warning below)
dotnet ef migrations add <Name> --context AuthDbContext --project TimePlanner.Dashboard --output-dir Data/Migrations

# logins, SQL Server
dotnet ef migrations add <Name>SqlServer --context SqlServerAuthDbContext --project TimePlanner.Dashboard --output-dir Data/SqlServer/Migrations/Auth
```

> **Warning, check the output.** `dotnet ef` finds design-time factories by type, and the SQL Server login factory can be picked up when you ask for `AuthDbContext`. If the new SQLite migration contains SQL Server types (`nvarchar`, `bit`, `datetime2`) instead of `TEXT`/`INTEGER`, it came from the wrong factory. Delete it and temporarily move `Data/SqlServer/SqlServerDesignTimeFactories.cs` aside while generating the SQLite one, then put the file back.

### How migrations stay in sync

Each model change needs both a SQLite and a SQL Server migration. Two tests in `SqlServerModelTests` fail when a SQL Server migration is missing or stale (`HasPendingModelChanges`), with no server needed, so CI catches a forgotten one. Provider-specific details also matter: for example SQLite cannot translate `ToLowerInvariant()` in a query, so use `ToLower()` inside queries.

---

<p align="right">(<a href="#readme-top">back to top</a>)</p>




## Testing
### Running the tests

```bash
dotnet test TimePlanner.Core.Tests
dotnet test TimePlanner.Api.Tests
```

At the time of writing: **128 Core tests and 380 API tests, all passing.** The same two commands run in CI on every pull request and again before every deployment, and a release is not built unless the Core tests pass.

### What the tests cover

- **Core tests** (`TimePlanner.Core.Tests`): the domain model and business rules, repositories, the check-in engine and services, and the widget's dashboard client and sync logic.
- **API tests** (`TimePlanner.Api.Tests`): the real dashboard running in memory against a throwaway SQLite database, with test-only secrets, so they exercise the actual pipeline. They cover:
  - sign-in on the website (cookie) and the API (JWT), lockout, and rate limits
  - refresh tokens: rotation, replay detection, expiry, the 90-day cap, theft, password change, deactivation, sign-out
  - roles, row-level access (one person cannot read another's data) and the admin-only endpoints
  - the import: every validation rule, all-or-nothing, repeat-safe sends and day replacement
  - reports, overview, submissions and the Excel and CSV exports, including formula safety
  - user administration, account settings, temporary passwords and the audit log
  - security headers, the content-security policy, and a guard that **no page loads anything from another site**
  - page output encoding, accessibility settings and the SQL Server model
- **Fixed clock.** Date-dependent tests run with a fixed clock, so they never depend on the day they run.
- **Mutation checks.** For every security control, I broke the code on purpose and confirmed at least one test failed. Where none did, the test was strengthened.

---
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## CI/CD and releases

| Workflow | Runs on | What it does |
|---|---|---|
| **Build** (`build.yml`) | every push and pull request to `main` and `development` | Backend job (Windows): restore, build and test Core, Dashboard and both test projects. Widget job: build the WPF widget. Security analysers and the package audit run as part of restore and build. |
| **Deploy Dashboard** (`deploy-dashboard.yml`) | push to `main`, or by hand | Runs both test suites, publishes the dashboard, deploys to Azure App Service and polls `/health` for up to five minutes. |
| **Release Widget** (`release-widget.yml`) | a tag starting with `v`, or by hand | Tests Core, publishes the widget as a self-contained single file, zips it, writes a SHA-256 file, and attaches both to a GitHub Release. |
| **CodeQL** (`codeql.yml`) | pushes, pull requests, and weekly | Static security analysis of the C# code. Only runs when the repository variable `ENABLE_CODEQL` is `true`. |
| **Dependabot** (`dependabot.yml`) | weekly | Update pull requests for NuGet and GitHub Actions, aimed at `development`, with major version bumps ignored. |

**Repository settings the workflows expect**

- **Secret** `AZURE_WEBAPP_PUBLISH_PROFILE` (the App Service publish profile) and **variables** `AZURE_WEBAPP_NAME` and `AZURE_WEBAPP_URL`, used by the deploy workflow.
- **Settings → Actions → General → Workflow permissions:** read and write, so a tagged run can create a Release.
- **Branch protection** on `main` and `development`: require a pull request, require the `Backend (Core, Dashboard, tests)` and `Widget (WPF)` checks to pass, block force pushes, and do not allow bypassing. (This needs a plan that supports it on private repositories.)
- **Dependabot security updates:** switch on in Settings → Code security, so vulnerability fixes arrive straight away rather than weekly.
- **CodeQL:** enable code scanning in Settings → Code security (needs GitHub Advanced Security on a private repository), then set the repository variable `ENABLE_CODEQL` to `true`.
- **Labels** `area:widget`, `area:dashboard`, `area:core`, `area:devops` and `area:testing` on pull requests, which group the generated release notes (`.github/release.yml`).

**Releasing the widget**

1. Make sure `development` has been merged into `main` and the **Deploy Dashboard** run is green (the widget talks to the live dashboard).
2. Tag the commit on `main` and push the tag:

   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

3. The **Release Widget** workflow tests, builds and publishes `TimePlanner-Widget-1.0.0-win-x64.zip` and `.sha256` to the GitHub Release. The tag becomes the version inside the app (`v1.0.0` becomes `1.0.0`). A tag that is not a version like `1.2.3` is refused.
4. To try the build without releasing, run the workflow by hand from the Actions tab: it keeps the zip as a build artifact with the version `0.0.0-dev.<run number>`.

---
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Deployment
### Dashboard on Azure App Service 
### Running it elsewhere
### Distributing the widget





### GitHub Actions
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Video Demonstration

## References 
