# Feature requests

Track new work in [GitHub issues](https://github.com/glennawatson/HyperCodecs/issues). Search open and closed issues before filing one. Add details to an existing issue when it covers the same work.

Use the [feature form](https://github.com/glennawatson/HyperCodecs/issues/new?template=features.yml). Keep one feature in each issue. Give the title a clear action and object. For example, `[Feature]: Decode AV1 video`.

Fill each field in this order:

| Field | What to write |
| --- | --- |
| Problem | The task and the obstacle. State the need before implementation details. |
| Proposed change | The behavior to add. Include a short before-and-after example where useful. |
| Completion checks | An unchecked list of observable results. Cover the files that must decode and the checksums they must match. |
| Platforms | Windows, Linux or macOS. List every affected platform. |
| Alternatives | Workarounds or other approaches. Write `None known` if there are none. |
| Context | Related issue links, the specification and evidence. Use repo-relative paths for code references. Mark unverified ideas clearly. |

For command-line submissions, use these exact field labels as `###` headings. Keep their order. Write `Not provided` for an optional field you cannot fill. Use `gh issue create --repo glennawatson/HyperCodecs --title '<title>' --body-file <file>`.

Check a completion item only after verifying its result. Put follow-up work in a linked issue. The issue must contain enough detail to work on without any local notes.

Use the [bug form](https://github.com/glennawatson/HyperCodecs/issues/new?template=bugs.yml) for broken behavior. See [bug report guidance](bugs.md).
