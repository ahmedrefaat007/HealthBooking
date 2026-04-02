# Development Guide

## Branch Strategy

```
master (stable baseline)
  └── 001-distributed-healthcare-system (main feature branch)
       ├── 001-w1-foundation (Week 1: Scaffolding, Auth, API Gateway) [T001–T020]
       ├── 001-w2-core-domain (Week 2: PatientService + ProviderService) [T021–T065]
       ├── 001-w3-scheduling (Week 3: AppointmentService & booking engine) [T066–T092]
       ├── 001-w4-integration (Week 4: NotificationService & RabbitMQ) [T093–T117]
       ├── 001-w5-resilience (Week 5: Polly, YARP, Redis, health checks) [T118–T128]
       └── 001-w6-observability (Week 6: OpenTelemetry, E2E tests, closure) [T129–T141]
```

## Getting Started

### 1. Create GitHub Repository

**Option A: Via GitHub Web UI**
1. Go to https://github.com/new
2. Repository name: `Booking.net` (or your choice)
3. Description: `Distributed Healthcare Appointment System — 6-week .NET sprint`
4. **Do NOT** initialize with README, .gitignore, or license (already have these locally)
5. Click **Create repository**

**Option B: Via GitHub CLI**
```powershell
gh repo create Booking.net --public --source=. --remote=origin --push
```

### 2. Add Remote & Push to GitHub

```powershell
cd "C:\Users\Ahmed Refaat\Downloads\Booking .net"

# Add remote
git remote add origin https://github.com/YOUR_USERNAME/Booking.net.git

# Push main branch
git branch -M master main
git push -u origin main

# Push feature branch
git push -u origin 001-distributed-healthcare-system

# Push all week branches
git push -u origin 001-w1-foundation
git push -u origin 001-w2-core-domain
git push -u origin 001-w3-scheduling
git push -u origin 001-w4-integration
git push -u origin 001-w5-resilience
git push -u origin 001-w6-observability
```

### 3. Verify Branches on GitHub

```bash
git branch -a
# Should show:
# * master
#   001-distributed-healthcare-system
#   001-w1-foundation
#   001-w2-core-domain
#   ...
#   remotes/origin/master
#   remotes/origin/001-distributed-healthcare-system
#   ...
```

---

## Weekly Task Execution

### Week 1: Foundation (T001–T020)

```powershell
# Checkout Week 1 branch
git checkout 001-w1-foundation

# See tasks in specs/001-distributed-healthcare-system/tasks.md
# Execute T001 through T020 in order

# After each task or task group, commit
git add .
git commit -m "T005: add .gitignore and .env.example"

# When Week 1 complete, create PR or merge to 001-distributed-healthcare-system
git push origin 001-w1-foundation
```

### Task Template

When starting a task:

1. **Read** the task description in `tasks.md`
2. **Check** the Agent Reference (R1–R10) if you need code templates or NuGet packages
3. **Verify the prerequisites** are marked `✅ completed`
4. **Execute** the task step-by-step
5. **Run** the verification command (marked with `✅ Verify:` in tasks.md)
6. **Commit** to the week branch: `git commit -m "T{num}: {description}"`
7. **Push** weekly: `git push origin 001-w{week}-{name}`

Example:
```powershell
# Start T006
# >> Read task, copy code from R2 (SharedKernel base types)
# >> dotnet build src/SharedKernel/HealthBooking.SharedKernel/
# ✅ Verify: Build succeeds
git add src/SharedKernel/
git commit -m "T006: implement SharedKernel base types (AggregateRoot, IDomainEvent, OutboxMessage)"
git push origin 001-w1-foundation
```

---

## Parallel Work (Within-Week)

Tasks marked with `[P]` can execute in parallel. Example Week 2:

**Stream A** (Sequential within self):
- T025 (PatientService handlers) → T026 (UpdateProfileCommand) → T027 (GetByIdQuery) → T028 (GetByEmailQuery)

**Stream B** (Can run in parallel with Stream A):
- T047 (ProviderService handlers) → T048 (UpdateProfileCommand) → ...

Git strategy:
```powershell
# Developer 1
git checkout 001-w2-core-domain
git checkout -b 001-w2-patient-handlers
# Execute T025, T026, T027, T028
git push origin 001-w2-patient-handlers

# Developer 2
git checkout 001-w2-core-domain
git checkout -b 001-w2-provider-handlers
# Execute T047, T048, T049, T050
git push origin 001-w2-provider-handlers

# After both complete: rebase & squash
# OR merge both PRs into 001-w2-core-domain sequentially
```

---

## Testing & Verification

### Per-Service Build
```powershell
dotnet build src/Services/PatientService/PatientService.sln
```

### Full Solution Build
```powershell
dotnet build HealthBooking.sln
```

### Run Tests
```powershell
dotnet test tests/PatientService.UnitTests/ --verbosity normal
```

### Coverage Check (Week 6)
```powershell
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:Html
```

---

## Frequently Run Commands

### View current branch & status
```powershell
git branch
git status
```

### Switch to Week 1
```powershell
git checkout 001-w1-foundation
```

### View untracked/unstaged files
```powershell
git diff
git status
```

### Undo last commit (keep changes)
```powershell
git reset --soft HEAD~1
```

### View commit history for current branch
```powershell
git log --oneline -10
```

### Rebase on main (stay updated)
```powershell
git fetch origin
git rebase origin/001-distributed-healthcare-system
```

---

## Pull Request Workflow (End of Week)

When week is complete:

```powershell
# Ensure all commits pushed to week branch
git push origin 001-w1-foundation

# On GitHub: Create PR from 001-w1-foundation → 001-distributed-healthcare-system
# Title: "Week 1: Foundation & Scaffolding (T001–T020)"
# Description: List all completed tasks, DoD checklist, any blockers

# After review & approval, merge into 001-distributed-healthcare-system
# Optionally: squash commits to 1 "Week 1" commit for clean history
```

---

## Troubleshooting

### "I committed to master by mistake"
```powershell
git reset --soft HEAD~1  # Undo the commit (keep changes)
git checkout 001-w1-foundation
git add .
git commit -m "T006: ..."
```

### "Merge conflicts after rebase"
```powershell
# Fix conflicts in files
# Then:
git add .
git rebase --continue
```

### "Docker Compose not starting"
```powershell
docker compose logs -f
# Check individual service:
docker compose logs -f sqlserver-patient
```

### ".NET build fails with missing types"
```powershell
# Clean & rebuild
dotnet clean
dotnet build -v minimal
```

---

## Definition of Done — Per Week

Before closing a week:

- [ ] All tasks T{start}–T{end} marked complete in tasks.md
- [ ] `dotnet build HealthBooking.sln` ✅ succeeds
- [ ] `dotnet test` ✅ all tests pass
- [ ] 80% coverage gate ✅ (Week 2+)
- [ ] All verification commands (`✅ Verify:`) ✅ executed successfully
- [ ] Weekly PR created & reviewed
- [ ] Branch pushed to GitHub
- [ ] `docker compose up` ✅ all services reach `healthy` state
- [ ] DoD checklist from tasks.md ✅ green

---

**Good luck!** 🚀
