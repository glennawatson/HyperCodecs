# Bug reports

Search [open and closed issues](https://github.com/glennawatson/HyperCodecs/issues) before filing a report. Use the [bug form](https://github.com/glennawatson/HyperCodecs/issues/new?template=bugs.yml). Keep one fault in each issue.

Name the trigger and the fault in the title. For example, `[Bug]: Opus decoder throws on a 2.5 ms CELT frame`.

Fill each field in this order:

| Field | What to write |
| --- | --- |
| Package version | The HyperCodecs version or commit. Write `Unknown` when it is unavailable. |
| Environment | Operating system, version, architecture and .NET runtime. |
| Steps to reproduce | Numbered steps. Name the container, codec and the calls made. State how often the fault occurs. |
| Expected behavior | What should happen after those steps. |
| Actual behavior | What happens instead. Include the exact exception text. |
| Completion checks | Observable results that prove the fault is fixed. Leave each checkbox unchecked. |
| Context | Related issues, logs or a sample file. Separate observations from suspected causes. |

For command-line submissions, use these exact field labels as `###` headings. Keep their order. Write `Not provided` for an optional field you cannot fill. Use `gh issue create --repo glennawatson/HyperCodecs --title '<title>' --body-file <file>`.

Give the smallest example that shows the fault, such as a short file and the packet number where decoding goes wrong. Record missing details as unknown. Use repo-relative paths for code references. Link an existing report when the fault is already tracked.
