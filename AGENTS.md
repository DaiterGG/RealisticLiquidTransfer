this is a mod for game Casualties unknown to add realistic gaphical liquid transfer
when player tries to transfer luqids by dragging one container into another, instead of slider, a minigame window appear with 2 containers and simulated liquid in them
player then can drag one container to spill into another, transferred liquid get calculated into in game liqid transfer

After making changes commit all task changes on current branch.

## Headless liquid integration tests

- Add scenarios to `tools/LiquidSimulationDiagnostics/Runner.cs` and keep the
  scenario name in the `ValidateSet` in `tools/LiquidSimulationDiagnostics/Run.ps1`.
- Use fake simple container or real container boundary outlines from `GeneratedContainerBoundaryCatalog`
- For simple test, seed one drop and log its parameters throut the simulation
- Trace every frame for membership, accounting owner, position, velocity,
  source-local offset, and removal. Run the scenario for at least 16 simulated
  seconds so settling and later edge cases are visible.
- First run without guessed numeric limits. Record observed maximum frame
  movement, source-relative drift, and velocity from the diagnostic log.
  If test reveal some error, leave the test and report abnormal behaviour
  If test reveal nothing abnormal, set reasonable boundaries based on observed behaviour and complete the test
- Run only the affected scenario while developing:

  ```powershell
  & .\tools\LiquidSimulationDiagnostics\Run.ps1 -Scenario <scenario>
  ```
