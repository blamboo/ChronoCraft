#!/usr/bin/env bash
# Compiles the plain-C# ChronoCraft simulation against the headless UnityEngine shim and
# runs the verification harness. Requires Mono's mcs (the editor is not needed).
set -euo pipefail
cd "$(dirname "$0")/../.."   # repo root

SIM=Assets/Scripts/Simulation
WORLD=Assets/Scripts/World
OUT=/tmp/chronocraft_harness.exe

mcs -langversion:latest -nowarn:0169,0414,0649 -out:"$OUT" \
  Tools/SimHarness/UnityShim.cs \
  Tools/SimHarness/Program.cs \
  "$SIM/WorldEvent.cs" \
  "$SIM/TrueLog.cs" \
  "$SIM/SimulationClock.cs" \
  "$SIM/Civ.cs" \
  "$SIM/ResourceNode.cs" \
  "$SIM/StorageNode.cs" \
  "$SIM/StructureNode.cs" \
  "$SIM/Simulation.cs" \
  "$SIM/NeedsSystem.cs" \
  "$SIM/AgentBehavior.cs" \
  "$SIM/LineageSystem.cs" \
  "$SIM/CombatSystem.cs" \
  "$SIM/TerritoryGrowth.cs" \
  "$SIM/ResourceRespawn.cs" \
  "$SIM/SeparationSystem.cs" \
  "$WORLD/Agent.cs" \
  "$WORLD/GridData.cs" \
  "$WORLD/Pathfinder.cs"

echo "Build OK -> $OUT"
echo
mono "$OUT"
