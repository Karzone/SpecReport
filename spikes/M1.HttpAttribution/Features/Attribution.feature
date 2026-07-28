Feature: HTTP attribution under parallel execution
  M1 spike. Eight scenarios run concurrently (MSTest method-level parallelism), each making
  four async HTTP calls. Each call's URL states which scenario and step it belongs to, so the
  handler's ambient-state lookups can be scored against ground truth.

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
