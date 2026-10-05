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

**Sending a day**
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Technology stack


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
Set these in `appsettings.json`, as user-secrets locally, or as environment variables when hosted. A colon becomes a double underscore in environment variables (`Jwt:Key` is `Jwt__Key`). Anything from the secrets or environment outranks the files.

| Setting | Default | Meaning |
|---|---|---|
| `Jwt:Key` | none, **required** | Signing key for access tokens, at least 32 bytes. The app refuses to start without it. |
| `Jwt:Issuer`, `Jwt:Audience` | `TimePlanner.Dashboard`, `TimePlanner.Clients` | Must match on every instance that validates the tokens. |
| `Jwt:ExpiryMinutes` | `30` | Lifetime of an access token. |
| `Jwt:RefreshDays` | `30` | Lifetime of a refresh token, renewed each time it is used. |
| `Jwt:RefreshMaxDays` | `90` | The longest a sign-in session can last, however often it is refreshed. |
| `Seed:AdminEmail`, `Seed:AdminPassword` | none | Creates the first administrator at startup if no such account exists. The password must meet the password policy. If unset, no admin is created. |
| `Database:Provider` | `Sqlite` | `Sqlite` or `SqlServer`. |
| `ConnectionStrings:Default` | `Data Source=timeplanner.db` | Used for both the time data and the login data. |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | Host names the app answers to. **Add the real domain when hosting.** |
| `Proxy:TrustForwardedHeaders` | `false` | Set `true` when the app sits behind a proxy that ends TLS (Azure App Service does). Without it, forms fail and every user shares one rate limit. |
| `Api:EnableDocs` | `false` | Serves Swagger UI outside Development. |
| `RateLimiting:LoginPerMinute` | `10` | Sign-in, refresh, logout and password-change attempts per address per minute. |
| `RateLimiting:ImportPerMinute` | `20` | Imports and uploads per signed-in user per minute. |
| `Display:TimeZone` | `Africa/Johannesburg` | Time zone for "today", week boundaries and shown submission times. |
| `Goals:DailyHours` | `8` | Default daily goal when a person has not set their own. |
| `Home:DayStart`, `Home:DayEnd` | `08:00`, `17:00` | Working day used for the Home timeline (`HH:mm`). |
| `Home:LunchStart`, `Home:LunchEnd` | `12:00`, `13:00` | Lunch window, left out of "unlogged gap" warnings. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` turns on Swagger and relaxes two security settings (below). |

Two things change in Development only: the Content-Security-Policy is sent as **report-only**, and the antiforgery cookie follows the request scheme instead of being Secure-only. This is why a problem with either can first appear once the site is hosted.

### Widget settings
### Per-user check-in settings
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
| Team · Users       | `/Users`                                            | Admin    | Account list; create, reset password, deactivate, reactivate.                                                                                                                |
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

### CSV import (website upload and API)
### Validation (both formats)
### Timesheet export (company layout)
### Sample-day CSV
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## REST API reference
### Authentication flow
### Endpoints
### Conventions
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Security
**Authentication and accounts**

**Transport and browser**

**Input and output**

**Rate limiting**

**Widget**

**Build and supply chain**
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Databases and migrations
**When you change the model, add a migration for every provider that context runs on:**

<p align="right">(<a href="#readme-top">back to top</a>)</p>




## Testing
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## CI/CD and releases
**Repository settings the workflows expect**
**Releasing the widget**
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Deployment
### Dashboard on Azure App Service 
### Running it elsewhere
### Distributing the widget





### GitHub Actions
<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Video Demonstration

## References 
