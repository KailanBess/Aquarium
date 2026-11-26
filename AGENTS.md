# Development instructions

## Product boundaries

- Keep Aquarium and its companion applications independently installable. Shared libraries may ship with each app; a running Aquarium hub or online account must not be required for local functionality.
- Use `docs/overhaul-requirements.json` to retain the full product scope and roadmap. An item marked `needs-audit` may already exist: inspect its implementation before replacing it. Record actual evidence before marking work complete.
- Tailscale and the private networking project are excluded. Supported security networking features remain in scope. Shoal is the separate operating-system project; current Windows behavior does not establish Linux or future-OS support.
- Preserve working functionality. Keep platform-specific operations behind explicit adapters as those adapters are introduced; retain suitable existing technologies.

## Data and capability integrity

- Never display protection, service, compatibility or completion statuses without supporting observations. Distinguish unsupported, unavailable, stale and verified states.
- Preserve user data and unknown settings fields. Refuse unsupported future schemas and corrupt records; do not silently overwrite them with defaults. Explicit migrations must have tests and recoverable publication.
- Keep program files, replaceable caches, application records and user-selected media/saves separate. Repairs and uninstallation must not silently remove personal content.
- Use atomic writes and the appropriate profile/record writer protocol. Validate archives, file boundaries and resource limits. Do not treat lexical path checks as an OS security sandbox.
- Keep integrations optional. Missing companions must not disable unrelated local features. Future sensitive IPC must authenticate requests; discovery alone never grants permissions.
- Keep secrets, personal profiles and signing credentials out of source and diagnostic exports. No signing or independent security certification may be claimed without evidence.

## Delivery and verification

- Develop on a branch; keep main and production releases separate from unverified work. Prepare reviewable changes and describe what changed, why, checks performed and remaining acceptance work.
- Run tests appropriate to the changed behavior, including failure, cancellation, migration or recovery cases where relevant. Keep existing regression coverage. Do not substitute fixture results for physical hardware, live service, accessibility or security acceptance.
- Keep requirements, documentation and release claims consistent with the implemented behavior. Do not mark the complete ecosystem overhaul finished after one implementation batch.
- Additional instructions in a companion repository continue to apply when working there, including its language, shared-schema, platform and handoff requirements.
