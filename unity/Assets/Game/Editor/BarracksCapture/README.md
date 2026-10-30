# Explicit live-server barracks capture

Editor-only, opt-in evidence command; not a test-suite skip and not a fake account.
Needs a Development prototype server with a real account whose barracks construction
has completed normally, enough spendable materials and an empty training queue.
The command submits two real one-person training requests through the existing UI.

After the shared-Mac lock is held, export wave-1a as usual, then launch the repository
Unity version with the project path, `-batchmode`, `-server-url http://127.0.0.1:5082`,
and `-executeMethod Game.BarracksCapture.BarracksCapture.Start` (do not pass `-quit`).
Set `BARRACKS_CAPTURE_TOKEN_FILE` to the user's private token file and
`BARRACKS_CAPTURE_DIR` to an empty, agent-owned artifact folder. Never put the token
itself in arguments or logs. The command uses isolated local-save/preferences paths,
390×844 rendering, synchronous editor shaders and cyan monitoring; it exits with a
failure if live connection, spending, waiting queue or capture geometry fails.

Outputs: connected, confirmation, active, waiting screenshots and visible UI text;
no token or private account JSON. `result.txt` records final pass/failure. A passing
capture command does not replace human visual acceptance of the approved #563 art.
