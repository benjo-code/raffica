# Security policy

## Reporting a vulnerability

Please report it privately through GitHub: open the **Security** tab of this repository and choose **Report a
vulnerability** (private vulnerability reporting). Do not open a public issue and do not post details elsewhere before a
fix is out. Include the version, the operating system, and, if you can, a small file or steps that show the problem.

You will get an answer as soon as the maintainer can read the report; this is a project of one maintainer, so please be
patient, and please give a fix a reasonable time before you publish.

## Supported versions

Only the latest release gets security fixes. Older releases are not patched; update to the latest one.

## In scope

- The update check: the signature of the release information, and the size and SHA-256 checks of the downloaded file.
- Parsing of PDF files (a file that crashes the app, runs code, reads files it should not, or uses unbounded memory or time).
- Redaction: content that is reported as removed but is still in the saved file.
- The installers and packages (the Windows installer and ZIP, the `.deb`, the AppImage): files placed outside their
  folder, wrong permissions, or anything that lets one user affect another.

## Out of scope

- Findings that need a modified build of the app or access to the user's account.
- The unsigned Windows files (SmartScreen shows a notice by design, see the README).
- Vulnerabilities in third-party components that are not reachable from Raffica; please report those upstream.
