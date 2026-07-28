@sample @evidence
Feature: Attachments and evidence
  Produces the attachment envelopes the report's evidence renderers dispatch on (SPEC 3.2):
  an image, a SpecReport HTTP-exchange JSON payload, a plain JSON file with no discriminator
  (must degrade to a plain JSON view), text output, and the run-meta payload.

  @ui
  Scenario: A step that attaches a screenshot
    Given a product "Widget" priced 9.99
    When I add it to the basket
    Then I attach a screenshot
    And the basket total should be 9.99

  @ui @known-fail
  Scenario: A failing step that attaches a screenshot
    Given a product "Widget" priced 9.99
    When I add it to the basket
    Then I attach a screenshot
    And the basket total should be 404.00

  @api
  Scenario: A step that records an HTTP exchange
    Given a product "Widget" priced 9.99
    And I add it to the basket
    When I record a successful HTTP exchange
    Then the basket total should be 9.99

  @api @known-fail
  Scenario: A step with a failing HTTP exchange
    Given a product "Widget" priced 9.99
    When I record a failing HTTP exchange
    Then the basket total should be 500.00

  @api
  Scenario: A step with several HTTP exchanges
    Given a product "Widget" priced 9.99
    And I add it to the basket
    When I record 3 HTTP exchanges
    Then the basket total should be 9.99

  Scenario: A step that writes text output and an undiscriminated JSON file
    Given a product "Widget" priced 9.99
    And I add it to the basket
    When I write some text output
    And I attach a plain JSON file
    Then the basket total should be 9.99
