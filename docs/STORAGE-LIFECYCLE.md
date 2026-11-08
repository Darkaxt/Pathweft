# Development output retention

The user's 2026-11-08 cleanup instruction replaces the earlier blanket retention
of every local release and every private capture. It does not change installed
application data, source history, upstream provenance or release availability.

- `dist` holds the current qualified release only. Its corresponding dependency
  sources remain available to the next packaging workflow. Once a newer release
  is delivered and verified, retire the previous local delivery after checking
  all of its assets against the published release hashes and sizes. GitHub keeps
  the published release history. A uniquely required recovery artifact needs an
  explicit recovery purpose and retirement condition; do not keep all releases
  as an implicit backup.
- Private evidence retains compact acceptance results, unresolved diagnostic
  evidence and reusable regression inputs/contracts. Completed stage reports
  belong in `docs/verification`. Retire superseded UI captures, generated test
  profiles and expanded cleanup manifest copies after the corresponding outcome
  is recorded. Do not reproduce the cleanup helper's complete manifest in another
  JSON file; retain its transaction, reviewed hash, counts, bytes and outcome.
- Use exact disposable scratch roots for generated build, extraction and native
  test output. Clean after the last consumer exits, including failed runs. Keep
  persistent active dependencies separate from that scratch.
- Perform removal through the installed transactional-cleanup workflow with
  reviewed exact roots. Preserve active consumers, newly created members, user
  inputs, source, live profiles and unresolved diagnostic records. Location,
  filename and age alone are not deletion authority.

The current source correction remains source-only at the user's request:
no additional compilation, packaging, version bump or release for this cleanup.
