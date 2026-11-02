# World API Editor capture (#648)

This harness uses an actual prototype bearer and the public map UI navigation. It never fabricates city, tile, resource stock, combat, or account responses. It is Editor evidence, not a phone, native key-store, or C05 measurement.

With the shared Mac lock, outside the night measurement window, prepare `wave-1a`, then run Unity with `-projectPath unity -executeMethod Game.WorldCapture.WorldCapture.Start --server-url http://127.0.0.1:55443`. Provide `WORLD_CAPTURE_TOKEN_FILE` pointing to a private local bearer file and `WORLD_CAPTURE_DIR` pointing to a new owned artifact directory. Never include the bearer in arguments, logs, repository, or Release. Leave `SOW_ACCOUNT_SERVER` unset for this explicit prototype contract run.

The harness connects through the actual settings input, opens the same public navigation route used by the estate tab, then clicks the map tab and requests the city. It captures map, navigation, selection, and tall viewport. A separate client instance verifies city POST idempotency, chunk GET, invalid-chunk rejection preserving the last confirmed cache, and authentication clearing. `api-proof.txt` records only outcomes. Screenshots and their text labels contain no bearer.

The visible maximum 7×7 cells are a viewport projection of the fetched server chunk; they are not the world or chunk dimensions. Tile geometry derives from the complete validated response. Missing stock remains `재고 정보 미제공`, never an invented grade or quantity. City, terrain, and target illustrations reuse existing approved estate art as display markers; new world artwork and full launch map interactions are outside this bounded integration.

Read `result.txt`, inspect all PNGs, and record the exact client and server SHAs separately. Preserve failures. This harness is additional to the full two-profile Unity record and its supplemental tests.
