@sample @basics
Feature: Basics
  Covers the ordinary shapes: passing scenario, failing scenario, skipped-after-failure,
  tags, data tables and doc strings.

  Background:
    Given the catalogue is seeded
    And the session is anonymous

  @smoke
  Scenario: A wholly passing scenario
    Given a product "Widget" priced 9.99
    When I add it to the basket
    Then the basket total should be 9.99

  @smoke @known-fail
  Scenario: A failing assertion, with steps skipped after it
    Given a product "Gadget" priced 19.99
    When I add it to the basket
    Then the basket total should be 999.00
    And the basket should contain 1 item
    And the checkout button should be enabled

  Scenario: A data table step
    Given the following products
      | Name    | Price | Stock |
      | Widget  | 9.99  | 12    |
      | Gadget  | 19.99 | 0     |
      | Doohick | 4.50  | 7     |
    When I add all in-stock products to the basket
    Then the basket total should be 14.49

  Scenario: A doc string step
    Given the following order note
      """
      Please gift-wrap this order.
      Leave with the neighbour at number 12 if we are out.
      """
    When I submit the order note
    Then the order note should be 2 lines long

  @pending-work
  Scenario: A pending step
    Given a product "Prototype" priced 0.01
    When the pricing engine is rewritten
    Then the basket total should be 0.01

  @undefined-step
  Scenario: An undefined step
    Given a product "Mystery" priced 1.00
    When I do something nobody has implemented yet
    Then the basket total should be 1.00
