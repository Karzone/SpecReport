@sample @outlines
Feature: Scenario Outlines
  Exercises Examples tables, including a deliberate duplicate row so the parser's
  example-row-index tiebreaker in scenario identity (SPEC 6.2) is provable.

  Scenario Outline: Discount bands
    Given a product "<name>" priced <price>
    When I apply the "<band>" discount band
    Then the basket total should be <expected>

    Examples: Standard bands
      | name    | price | band     | expected |
      | Widget  | 100   | none     | 100      |
      | Widget  | 100   | ten      | 90       |
      | Gadget  | 50    | half     | 25       |

    Examples: Duplicate rows on purpose
      | name    | price | band     | expected |
      | Doohick | 20    | ten      | 18       |
      | Doohick | 20    | ten      | 18       |

  Scenario Outline: An outline row that fails
    Given a product "<name>" priced <price>
    When I apply the "<band>" discount band
    Then the basket total should be <expected>

    Examples:
      | name   | price | band | expected |
      | Widget | 100   | ten  | 90       |
      | Widget | 100   | ten  | 12345    |
