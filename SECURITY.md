# Security Policy

## Supported Status

This Windows companion app is under active development and is not yet an official release-ready openHAB distribution.

Security support, release signing, and package ownership must be finalized by openHAB maintainers before this repository can be treated as an official release channel.

## Reporting A Vulnerability

If this repository is transferred into the official openHAB organization or published as an official openHAB app, follow the current openHAB security reporting process.

Until ownership is finalized, report suspected vulnerabilities privately to the repository maintainers. Do not open a public issue containing secrets, exploit details, private endpoint data, or full diagnostic logs.

## Sensitive Data

The app can process or store sensitive data, including:

- openHAB endpoint URLs.
- API tokens.
- Basic-auth credentials.
- Item names and states.
- Sitemap content.
- Notification payloads.
- Local diagnostics.

Do not paste full logs or settings files into public issues. Redact tokens, credentials, private hostnames, public URLs with query strings, item names that reveal private information, and notification payloads.

## Local Files

Runtime files are stored under:

```text
%LocalAppData%\OpenHab.WinApp
```

Review files before sharing them. Useful files such as `diagnostics.log`, `settings.json`, `notifications.json`, and `task-crash.log` can contain private data.

## Signing Keys

Do not commit signing keys, `.pfx` files, `.user` project metadata, package output, or private release credentials.
