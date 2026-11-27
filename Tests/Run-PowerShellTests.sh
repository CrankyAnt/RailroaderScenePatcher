#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 CrankyAnt
# SPDX-License-Identifier: MIT
# The free CI job (parts.custom in .github/ci-config.yml): the PowerShell tests that need no build, run from the
# repository root whatever the working directory. Every test runs, also after a failure, and the script fails
# when one of them failed.
set -u
cd "$(dirname "$0")/.." || exit 2
pwsh --version || { echo "::error title=powershell-tests::pwsh is not on PATH"; exit 2; }
status=0
for test in \
    Tests/FixedPlacementJourney/Test-FixturePreparation.ps1 \
    Tests/FixedPlacementJourney/Test-TestHome.ps1 \
    Tests/FixedPlacementJourney/Test-AttemptSummary.ps1 \
    Tests/DocumentationChecks/Test-Documentation.ps1; do
    echo "+ pwsh -NoProfile -File $test"
    if ! pwsh -NoProfile -File "$test"; then
        echo "::error title=powershell-tests::$test failed"
        status=1
    fi
done
exit "$status"
