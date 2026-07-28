@sample @rules
Feature: Rules and grouping
  Gherkin `Rule:` blocks. The report must group Feature -> Rule -> Scenario (SPEC 4.2);
  without a Rule slot these four scenarios would merge into one flat list.

  Rule: In-stock products can be bought

    Background:
      Given the catalogue is seeded

    Example: Buying an in-stock product succeeds
      Given a product "Widget" priced 9.99
      When I add it to the basket
      Then the basket total should be 9.99

    Example: Buying two in-stock products sums the total
      Given a product "Widget" priced 9.99
      And a product "Doohick" priced 4.50
      When I add all in-stock products to the basket
      Then the basket total should be 14.49

  Rule: Out-of-stock products cannot be bought

    Example: Adding an out-of-stock product is rejected
      Given a product "Gadget" priced 19.99 with no stock
      When I add it to the basket
      Then the basket should contain 0 items

    Example: An out-of-stock rejection that fails on purpose
      Given a product "Gadget" priced 19.99 with no stock
      When I add it to the basket
      Then the basket should contain 1 item
