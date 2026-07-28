@sample @hooks
Feature: Failing hooks
  The highest-traffic CI failure mode (SPEC 4.3): a scenario whose Before hook throws has
  a failed outcome but ZERO failed steps. Without hook rows in the timeline the report
  would show "everything skipped" and no error anywhere.

  @fail-before-hook
  Scenario: Before hook throws
    Given a product "Widget" priced 9.99
    When I add it to the basket
    Then the basket total should be 9.99

  @fail-after-hook
  Scenario: After hook throws though every step passed
    Given a product "Widget" priced 9.99
    When I add it to the basket
    Then the basket total should be 9.99
