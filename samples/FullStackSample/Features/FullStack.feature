@ui @api @fullstack
Feature: Full stack
  The scenarios this whole sample exists for: a single scenario that talks to the API *and*
  drives the browser, so one timeline carries HTTP exchange panels and screenshots side by
  side. No special casing - the report attaches whatever evidence a step produced.

  Scenario: An item added through the API shows up in the storefront
    Given the basket is empty via the API
    When I add "WIDGET-1" to the basket via the API
    And the storefront is open
    Then the basket count should be 1
    And the basket total should be "9.99"

  Scenario: The storefront and the API agree on the catalogue
    Given the storefront is open
    When I request the product catalogue
    Then the number of products on screen should match the API

  @known-fail
  Scenario: A mismatch between UI and API is visible from both sides
    Given the basket is empty via the API
    When I add "GADGET-2" to the basket via the API
    And I add "GIZMO-4" to the basket via the API
    And the storefront is open
    Then the basket count should be 5
