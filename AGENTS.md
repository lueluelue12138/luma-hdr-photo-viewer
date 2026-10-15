# Luma development

- Commit each completed iteration after its appropriate validation passes. Use a descriptive commit message and include its related documentation updates; keep unrelated changes out of the commit. Local commits do not authorize pushing or publishing.
- Maintain `docs/TODO.md`: record new user feedback and outstanding work there, and mark completed items with their actual validation evidence.
- Keep the latest validated local build at `artifacts/Luma/Luma.App.exe` for manual testing. Use separate directories for experiments, and retain the previous usable build when validation fails. Do not replace files while that preview is running.
- Keep measured results distinct from estimates. Do not claim HDR/VSR success without SDK evaluation and the actual FP16 presentation path.
- Do not publish a repository, push changes, upload to Microsoft Store, or make a public release without an explicit user request.

## Commit attribution

- Agent-created commits must use the actual tool identity as `Author`. For Codex, use `OpenAI Codex <codex@openai.com>`; do not attribute another tool's work to Codex. Apply the identity only to the current Git command with `git -c user.name="<tool name>" -c user.email="<tool email>" ...`; do not change the global Git identity or replace the human's identity with the agent's.
- Include the human participants who contributed requirements, discussion, optimization, verification, or acceptance as `Co-authored-by: <name> <GitHub-associated email>`. Prefer their confirmed GitHub noreply address. Do not hardcode a maintainer's identity in repository rules.
- The current participant's confirmed attribution is stored in this checkout's local Git configuration as `luma.coAuthorName` and `luma.coAuthorEmail`. These values were confirmed directly by the human; reuse that confirmation in later iterations. If a different participant joins or the configured identity is absent or ambiguous, obtain their confirmation rather than guessing from an account or another repository.
- Before committing, inspect `git config --show-origin --get user.name`, `git config --show-origin --get user.email`, the local co-author settings, and `git var GIT_AUTHOR_IDENT` using the same temporary tool identity options as the commit. Check `git diff --staged --stat` against the intended change. Leave one blank line before the footer and include each participant exactly once. Human-created commits do not need a duplicate footer for their own author identity.
- After committing, inspect `git show -s --format=fuller HEAD` and its parsed trailers. Correct attribution mistakes before any authorized push. Rewriting history requires an explicit human request, a verified recoverable backup, and verification that commit contents and dates are preserved; do not rewrite already-pushed history without specific authorization.
