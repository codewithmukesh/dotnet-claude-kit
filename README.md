# dotnet-claude-kit

> The opinionated Claude Code companion for .NET developers.

Project-ready templates, intelligent agents, workflow automation, and a Roslyn MCP server for token-efficient codebase navigation.

---

**Status: Under Construction**

This project is actively being built. The first usable release (v0.1.0) will include 5 core skills, a web-api template, and knowledge documents targeting .NET 10 and C# 14.

## What This Is

dotnet-claude-kit is a curated set of instructions, skills, agents, and tools that make Claude Code dramatically more effective for .NET development. Instead of generic AI assistance, you get opinionated guidance grounded in modern .NET best practices.

### Key Components

- **Skills** — 17 opinionated skill files covering C# 14, Vertical Slice Architecture, EF Core, testing, and more
- **Agents** — 8 specialist agents (architect, API designer, EF Core specialist, test engineer, security auditor, performance analyst, DevOps engineer, code reviewer)
- **Templates** — 5 drop-in `CLAUDE.md` files for web APIs, modular monoliths, Blazor apps, worker services, and class libraries
- **Knowledge** — Living documents on .NET releases, anti-patterns, package recommendations, and architectural decisions
- **Roslyn MCP Server** — Semantic code analysis via MCP for 5-10x token reduction in codebase navigation
- **Hooks** — Pre-commit formatting and post-scaffold restore automation

## License

[MIT](LICENSE)
