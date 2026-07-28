Feature: HTTP capture attributes to the right step
  Eight scenarios run concurrently, each making four async HTTP calls plus one from a
  BeforeScenario hook. Every URL states which step it belongs to, so the recorded NDJSON can be
  checked against ground truth: SR-H1 (right step) and SR-H2 (out-of-step traffic bucketed,
  not misattributed).

  Scenario Outline: Concurrent scenario <slot>
    Given step <slot>-a makes a call
    When the scenario pauses briefly
    And step <slot>-b makes a call
    Then step <slot>-c makes a call
    And step <slot>-d makes a call

    Examples:
      | slot |
      | s1   |
      | s2   |
      | s3   |
      | s4   |
      | s5   |
      | s6   |
      | s7   |
      | s8   |

  @secrets
  Scenario: Secrets are redacted before they reach the NDJSON
    Given step redact-a makes a call carrying secrets
    Then the call should have completed
