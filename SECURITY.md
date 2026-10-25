# Security Policy

## Supported versions

Only the latest released version of each package on NuGet is supported:

- [HyperCodecs](https://www.nuget.org/packages/HyperCodecs/)
- [HyperCodecs.Platform](https://www.nuget.org/packages/HyperCodecs.Platform/)
- [HyperCodecs.Platform.Linux](https://www.nuget.org/packages/HyperCodecs.Platform.Linux/)

Fixes ship in a new release; no patches are issued for earlier versions. Upgrade to the latest
version before reporting an issue.

## Reporting a vulnerability

Report vulnerabilities through GitHub private vulnerability reporting:

**[Report a vulnerability](https://github.com/glennawatson/HyperCodecs/security/advisories/new)**

Do not open a public issue, pull request, or discussion for a security report.

HyperCodecs reads media files that may come from anyone, so a crafted file that crashes the
process, hangs a decoder, reads or writes outside its buffers, or uses far more memory than the
file justifies is in scope.

Include the package and version you tested, the target framework, the operating system and
processor, steps to reproduce, and the impact you believe it has. The smallest file that triggers
the problem is the most useful thing you can attach.

You will get an acknowledgement on the advisory thread, and updates there as the report is triaged
and fixed. Once a fix is released the advisory is published with credit to the reporter unless you
ask otherwise.
