---
name: ci-cd
description: >
  CI/CD pipelines for .NET applications. Covers GitHub Actions and Azure DevOps
  YAML pipelines with build, test, publish, and deploy stages.
  Load this skill when setting up continuous integration, automated testing,
  deployment workflows, or when the user mentions "CI/CD", "pipeline",
  "GitHub Actions", "Azure DevOps", "workflow", "deploy", "build pipeline",
  "publish", "NuGet push", "release", or "continuous integration".
---

# CI/CD

## Core Principles

1. **Pipeline as code** — YAML pipelines committed to the repo. No click-ops in the UI.
2. **Fast feedback** — Build and test on every push. Cache NuGet packages. Fail fast.
3. **Build once, deploy many** — Build the artifact once, promote it through environments (dev → staging → production).
4. **Never skip tests** — Tests gate the pipeline. No deployment without passing tests.

## Patterns

### GitHub Actions — Build + Test

```yaml
# .github/workflows/ci.yml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

env:
  DOTNET_VERSION: '10.0.x'
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    services:
      postgres:
        image: postgres:17
        env:
          POSTGRES_DB: testdb
          POSTGRES_USER: postgres
          POSTGRES_PASSWORD: postgres
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Format check
        run: dotnet format --verify-no-changes --no-restore

      - name: Test
        run: dotnet test --no-build --configuration Release --logger trx --results-directory TestResults
        env:
          ConnectionStrings__Default: "Host=localhost;Database=testdb;Username=postgres;Password=postgres"

      - name: Publish test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: TestResults/*.trx
```

### GitHub Actions — Build + Publish Docker Image

```yaml
# .github/workflows/publish.yml
name: Publish

on:
  push:
    tags: ['v*']

jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Login to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract version from tag
        id: version
        run: echo "VERSION=${GITHUB_REF#refs/tags/v}" >> $GITHUB_OUTPUT

      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context: .
          push: true
          tags: |
            ghcr.io/${{ github.repository }}:${{ steps.version.outputs.VERSION }}
            ghcr.io/${{ github.repository }}:latest
```

### Azure DevOps — Build + Test

```yaml
# azure-pipelines.yml
trigger:
  branches:
    include:
      - main
  paths:
    exclude:
      - '*.md'
      - docs/

pool:
  vmImage: 'ubuntu-latest'

variables:
  dotnetVersion: '10.0.x'
  buildConfiguration: 'Release'

stages:
  - stage: Build
    jobs:
      - job: BuildAndTest
        steps:
          - task: UseDotNet@2
            inputs:
              version: $(dotnetVersion)

          - script: dotnet restore
            displayName: Restore

          - script: dotnet build --no-restore -c $(buildConfiguration)
            displayName: Build

          - script: dotnet format --verify-no-changes --no-restore
            displayName: Format check

          - script: dotnet test --no-build -c $(buildConfiguration) --logger trx
            displayName: Test

          - task: PublishTestResults@2
            condition: always()
            inputs:
              testResultsFormat: VSTest
              testResultsFiles: '**/*.trx'
```

### NuGet Package Publishing

```yaml
# Part of GitHub Actions workflow
- name: Pack
  run: dotnet pack src/MyLibrary -c Release -o ./nupkg --no-build

- name: Push to NuGet
  run: dotnet nuget push ./nupkg/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```

## Anti-patterns

### Don't Build Different Artifacts per Environment

```yaml
# BAD — building separately for each environment
- script: dotnet publish -c Debug   # for dev
- script: dotnet publish -c Release # for prod

# GOOD — build once, deploy everywhere
- script: dotnet publish -c Release -o ./publish
# Then deploy the same ./publish artifact to dev, staging, prod
```

### Don't Skip Format Checks in CI

```yaml
# BAD — no format enforcement
steps:
  - run: dotnet build
  - run: dotnet test

# GOOD — format check catches style issues early
steps:
  - run: dotnet build
  - run: dotnet format --verify-no-changes
  - run: dotnet test
```

### Don't Hardcode Secrets in Pipelines

```yaml
# BAD — secret in pipeline YAML
env:
  DB_PASSWORD: "my-secret-password"

# GOOD — use pipeline secrets
env:
  DB_PASSWORD: ${{ secrets.DB_PASSWORD }}
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Open source project | GitHub Actions |
| Enterprise with Azure | Azure DevOps Pipelines |
| Docker deployment | Multi-stage build in CI, push to container registry |
| NuGet library | Build → Test → Pack → Push on tag |
| Database migrations | Run in CI test stage, script for production |
| Environment promotion | Same artifact, different configuration |
