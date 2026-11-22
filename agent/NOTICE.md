# NetTactical agent

This folder is a modified copy of the Tactical RMM agent (`github.com/amidaware/rmmagent`, version 2.12.0, commit `2db2aab0b5b354d9e876a17826d8f421d58be5e8`),
Copyright (c) AmidaWare Inc, used under the **Tactical RMM License Version 1.0** (see `LICENSE.md` and https://license.tacticalrmm.com).

What was changed: the program, service, folder, registry key, installer and log names (`nettactical`, `NetTacticalAgent`, `SOFTWARE\NetTactical`) so the agent no
longer carries the "Tactical RMM" name, the Go module path, and the installer script (builds on a GitHub runner, no code signing). The protocol the agent speaks
to the server (`/api/v3` and NATS) is unchanged. The license requires that this source, with the changes, stays available online; it is, in this repository.

"Tactical RMM" is a trademark of AmidaWare Inc. NetTactical is not affiliated with or endorsed by AmidaWare Inc.
