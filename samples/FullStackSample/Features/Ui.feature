@ui
Feature: Shop storefront
  Frontend scenarios driven by Playwright. Screenshots are captured on failure by default, so a
  failing step carries a picture of the page as it was.

  Background:
    Given the storefront is open

  Scenario: The catalogue renders every product
    Then I should see 4 products listed

  Scenario: Searching filters the list
    When I search for "gad"
    Then I should see 1 product listed
    And the first product should be "Gadget"

  Scenario: Out-of-stock items cannot be added
    Then the "DOOHICK-3" add button should be disabled

  Scenario: Adding an item updates the basket
    When I add "Widget" to the basket
    Then the basket count should be 1

  @known-fail
  Scenario: A failing UI assertion captures a screenshot
    When I search for "gizmo"
    Then I should see 3 products listed
