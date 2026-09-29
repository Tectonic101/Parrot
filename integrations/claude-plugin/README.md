# Parrot for Claude

Your meetings from [Parrot](https://openparrot.app), the free, local Mac app that records your calls and writes the transcript with real speaker names, available in Claude. Read-only.

Ask things like:

- "What did I promise last week?"
- "When did Sarah mention the budget?"
- "Brief me for my call with Acme."
- "Draft the follow-up for this morning's call."
- "Coach me across my last 10 calls: where do I talk too much?"

## What you need

1. Parrot on your Mac (macOS 14 or later, Apple Silicon): [openparrot.app](https://openparrot.app).
2. In Parrot, open **Claude & AI Apps** and turn on **Allow AI apps to read my meetings**.

## What's inside

- An MCP server: Parrot itself (`Parrot --mcp`), started by `server/launch.sh`, which finds Parrot in Applications or by its app id. No network, no writes.
- Tools: `list_meetings`, `get_meeting`, `search_meetings`, `get_transcript`, `list_commitments`, `export_meeting`, `meeting_stats`, `list_profiles`, `get_profile`. All read-only; `export_meeting` saves a copy of one meeting to Downloads/Parrot Exports.
- Skills: weekly digest, follow-up email, prep for a call, PRD from calls.

## For reviewers

Install Parrot, record or import one short audio file (File > Import Audio), wait for the report, then turn on the switch above. Ask "What did I promise?" or "Summarize my latest meeting".

More: [Use your meetings in Claude](https://openparrot.app/help/claude.html). Privacy: [PRIVACY.md](PRIVACY.md).
