@api
Feature: Shop API
  Backend scenarios. Every step's HTTP exchange appears in the report attached to the step that
  made it - method, URL, status, latency, headers and bodies, with secrets redacted.

  Scenario: The catalogue lists every product
    When I request the product catalogue
    Then the response status should be 200
    And the catalogue should contain 4 products

  Scenario: A known product can be fetched by SKU
    When I request the product "GADGET-2"
    Then the response status should be 200
    And the product name should be "Gadget"

  Scenario: An unknown SKU is a 404, not a crash
    When I request the product "NOPE-9"
    Then the response status should be 404

  @secrets
  Scenario: Logging in returns tokens that must never reach the report
    # The password deliberately does NOT appear here. SpecReport redacts captured traffic, but
    # it renders step text exactly as written - a credential typed into a feature file will show
    # up in the report, because it is part of the test rather than part of the traffic.
    When I log in as "ada"
    Then the response status should be 200
    And the response should contain an access token

  Scenario: An out-of-stock item cannot be added to the basket
    When I add "DOOHICK-3" to the basket via the API
    Then the response status should be 409

  @restsharp
  Scenario: RestSharp traffic is captured exactly like HttpClient
    When I request the product catalogue using RestSharp
    Then the RestSharp response status should be 200

  @known-fail
  Scenario: A failing assertion shows the exchange that produced it
    When I request the product catalogue
    Then the catalogue should contain 99 products
