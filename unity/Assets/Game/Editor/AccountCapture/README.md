# Live account UI capture (#638)

Explicit Editor-only automation, adapted from the existing BarracksCapture flow.
It connects to a real Development prototype account using the UI, confirms a recruitment,
and captures recruitment/odds/result/research screens. It does not inject account state.

After acquiring the shared-Mac lock and preparing wave-1a, run Unity with `-batchmode`,
`--server-url http://127.0.0.1:5082` and
`-executeMethod Game.AccountCapture.AccountCapture.Start`, without `-quit`.
Set `ACCOUNT_CAPTURE_TOKEN_FILE` to an owned private token file and `ACCOUNT_CAPTURE_DIR`
to a new empty owned artifact directory. The token must not be in arguments, logs or commits.
The account must have server-granted development gems sufficient for a recruit.

Optional `ACCOUNT_CAPTURE_RESEARCH_NODE` selects a server node and attempts start only
if the actual UI enables it. Building/proof prerequisites must be obtained through normal
server commands and verified replay settlement; no fixture grant, time acceleration or
client-side `settle-expedition` is allowed. A captured locked tree is not a successful
research-start/complete gate. Inspect the separate server status in the response capture.

Outputs are 390x844 Unity screenshots, UI text excluding InputField content, cyan/shader
records and result.txt. Captures are runtime evidence, not user art acceptance, C05 or
mobile performance evidence. The default test suites do not invoke this network workflow.
