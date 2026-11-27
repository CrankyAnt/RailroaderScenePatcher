// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

// The entry point of the fault build: it runs only the fault suites, which the normal core test run cannot reach.
static class FaultTestsProgram {
    static int Main(string[] args) {
        if (args.Length != 0) return 2;
        try {
            FaultInjectionTests.Run();
            Console.WriteLine("PASS: fault build of the core tests");
            return 0;
        } catch (Exception e) {
            Console.Error.WriteLine("FAIL: fault build of the core tests: " + e);
            return 1;
        }
    }
}
