# Changelog

All notable changes to TimePlanner. Versions follow [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-05

First release: the Task 2 MVP for the Leading Edge pilot.

### Widget (Windows)
- Start, pause, resume and end a tracked working day from a small always-available widget and tray icon.
- Check-ins at a chosen interval with snooze and skip limits, quiet over lunch.
- Log time against a client › project and an activity tree (Meeting, Coding, Design, Email, Admin, Learning), adding new ones as you type.
- Timesheet for any day: edit, delete, add time, fill gaps, undo, export CSV.
- Send a finished day to the Dashboard. Sending again replaces the day. Stays signed in for up to 30 days with refresh tokens; the password is never stored.
- Sample day for testers (`--sample-day`).

### Dashboard and API (Azure)
- Sign-in with lockout, roles (Admin, Developer, Billing) and forced password change for new accounts.
- Personal home and timesheet views; reports by day, project, activity and person; CSV export in the company timesheet layout.
- Admin: user management, team overview and daily submission tracking, audit log.
- Versioned REST API with JWT and refresh tokens, rate limiting and Swagger.
- Billing rule: everything is billable except internal work and learning.

### Platform
- Azure App Service and Azure SQL, deployed from `main` with a health check.
- CI builds and tests every pull request. Security analysers, vulnerable package checks and Dependabot.
- Database check constraints and indexes on both SQLite and SQL Server.

[0.1.0]: https://github.com/Doingle/TimePlanner_LeadingEdge_INSY7315_XBCAD/releases/tag/v0.1.0
