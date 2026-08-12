# Improvements — data_structure_queue_customer

**Goal:** A cross-platform, real-time customer queue-management app with a web UI, a REST/WebSocket backend, and a live cloud demo — showcasing queue mechanics (enqueue, dequeue, partial-reverse) beyond a Windows-only desktop toy.

## TL;DR — Path to production
- [x] Extract queue domain into a tested .NET 8/9 class library
- [x] Minimal API + SignalR for live queue updates
- [x] EF Core persistence (queue survives restarts)
- [x] Web UI with animated queue visualization
- [x] Docker + GitHub Actions CI/CD
- [ ] Public live demo with wait-time/throughput metrics

## Current state
- .NET 6 WinForms desktop app (Windows-only), single Form1.cs with all logic.
- Implements Queue<Customer> with Add / Dequeue / reverse-first-N (via Stack + List).
- No persistence — state is in-memory static fields, lost on close.
- No tests, no CI, no API; UI and domain logic tightly coupled; README is one line.

## Key improvements
- Extract queue/customer domain into a testable class library (separate from UI).
- Backend: ASP.NET Core Minimal API exposing enqueue/dequeue/reverse/peek + SignalR for live queue updates.
- Frontend: Blazor WASM (or React) SPA replacing WinForms — animated queue visualization of enqueue/dequeue/reverse.
- Persistence: EF Core + SQLite/Postgres so queue survives restarts; add position/wait-time tracking.
- Quality: xUnit unit tests on the queue operations, input validation, FluentValidation for name/age.
- Model as a real domain (ticketing / service desk) with metrics: throughput, avg wait time.

## Latest tech to showcase
- .NET 8/9 Minimal APIs + SignalR real-time hub.
- Blazor WebAssembly (or React + TypeScript) with an animated data-structure visualizer.
- EF Core with SQLite/Postgres; Docker multi-stage build.
- GitHub Actions CI (build, test, publish) + deploy to Azure Container Apps / Fly.io.
- OpenTelemetry + health checks for basic observability.

## Roadmap
1. Refactor: pull domain into a library, add xUnit tests, upgrade to .NET 8/9.
2. Build ASP.NET Core API + SignalR, add EF Core persistence, containerize.
3. Ship Blazor/React UI with live visualization, wire up CI/CD, deploy public demo.
