---
version: 1
id: calc-arithmetic
owner: Platform
risk: low
tags:
  - desktop
  - calculator
  - arithmetic
---

# Capability: Calculator Arithmetic

The Windows Calculator correctly performs basic arithmetic operations and displays results.

Target: Windows Calculator (`calc.exe` / Windows Store `Microsoft.WindowsCalculator`)

## Rules

- Addition of two integers must produce the correct sum in the display.
- The CalculatorResults element must reflect the final value after pressing Equals.
- The display shows values prefixed with "Display is" in its accessibility Name property.

## Acceptance Criteria

### CALC-AC1

Given Calculator is open in Standard mode, when the user enters 2 + 2 = , then the display shows "Display is 4".

### CALC-AC2

Given Calculator is open, when a calculation completes, then the CalculatorResults element name matches the expected display string.
