@recorded @draft @calculator
Feature: Calculator: 2 + 2 = 4
  Verifies that Calculator correctly computes 2 + 2 = 4 and displays the result.

  Scenario: Calculator: 2 + 2 = 4
    Given the ApplicationFrameHost application is open
    And I invoke clearButton
    And I invoke num2Button
    And I invoke plusButton
    And I invoke num2Button
    And I invoke equalButton
    Then the window title should be "REVIEW: replace with expected window title"
