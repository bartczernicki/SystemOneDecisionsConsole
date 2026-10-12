# Test migration coverage

The original standard suite was executed successfully before retirement: **806 grouped checks**. `LegacyCheckInventory.json` records the increment location and execution count of every original check block, captured by instrumenting a temporary in-memory syntax tree without editing the original runner. Its block counts total 806. The active functionality suite retains **804 grouped checks**; two original checks that measured request delay are excluded. This inventory is historical data; it requires no Python interpreter.

| Original standard coverage | Historical grouped checks | Active functionality groups | Native classes |
| --- | ---: | ---: | --- |
| Basic workflows, charts, configuration, menus, transport, and publishing | 118 | 116 | `BasicExamplesTests`, `ConsoleMenuTests`, `TransportConfigurationTests`, `PublishingTests` |
| Courier, mixed football forecast, total points, shipment evidence, Apple Watch, and energy framing | 313 | 313 | See [scenario matrix inventory](CourierAndAdvancedCoverage.md) |
| Customer calibration and reports | 136 | 136 | `CustomerCalibrationTests` |
| Work queue workflows | 107 | 107 | `WorkQueueTests` |
| Restaurant workflows | 128 | 128 | `RestaurantTests` |
| Three standalone harnesses and the embedded framing harness | 4 | 4 | `BinaryEvaluationHarnessTests`, `WorkQueueHarnessTests`, `RestaurantHarnessTests`, `FrameMetadataHarnessTests` |
| Total | **806** | **804** | Functional workflows and harness subcases |

The four grouped harness checks become **80 independently discoverable native tests**. Two adjacent basic score-rendering checks share one native workflow while retaining both assertions. The 804 functionality groups therefore yield **879 migrated native tests**, with two additional fixture regression tests for surfaced I/O and assertion callback failures bringing the functionality suite to **881 tests**. The old counter and native test count measure different groupings. [Customer, queue, and restaurant matrix inventory](Tests/CustomerQueueRestaurant.Coverage.md) identifies their response, asset, report, status, and failure-position matrices.

Shared infrastructure records exact request paths, case-insensitive headers, JSON payloads, request order, and maximum concurrent requests through a concurrent loopback `TcpListener` on port zero. Callbacks may return arbitrary statuses, raw malformed JSON, or delayed responses; callback failures surface as test failures. Console processes use UTF-8 streams, concurrent draining, controlled fixture settings, isolated full application-output copies, and process-tree cleanup on timeout. Image-bearing diagnostics are redacted. Assembly-level `DoNotParallelize` serializes stateful workflows and publication.

Build/publish tests verify the active configuration's complete application output and compare source, build, `TestApp`, isolated runtime, and published assets: **18 scenarios and 65 files**, with both original PNG SHA-256 values unchanged. Report tests independently compute Brier score, baseline score, skill score, calibration bins, ECE, and entropy; validate CSV, JSON, timestamps, provenance hashes, checkpoint state, report isolation, and failure continuation.

Both Debug and Release configurations verify complete application-output copying, configuration-specific execution, publication, 18 scenarios, 65 assets, and unchanged original PNG bytes. Discovery exposes all 881 named functionality tests without category filtering. The original runner, standalone harness source files, generated Python cache, and root `tests` directory were removed after parity and native validation passed. No native test invokes Python or compiles a temporary harness project.

Latest functionality validation: **881 passed, 0 failed, 0 skipped** in Debug. Debug and Release solution builds complete with zero warnings and errors. Discovery contains no timeout tests.
