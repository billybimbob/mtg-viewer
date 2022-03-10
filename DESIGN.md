# Design Rationale

## Provisioning

The Treasury can be overprovisioned, which with current implementations can lead to undefined behavior.

### Causes

* Treasury can have a non-static capacity
* More cards are added than space is available
* Boxes have capacities lowered or removed

### Current Behavior

* Excess cards are tacked on to a "random" box
  * The selected box is random since the location is not deterministic across different treasury transactions
  * Results in
    * Any possible box to become overprovisioned
    * Excess cards are not centralized -> can be **artificially fragmented**

### Proposed Solution

Upon Treasury modifications, add extra behavior to eagerly "refresh" to prevent artificial fragmentation.

#### Possible Treasury Modifications

* Mods that result in overprovision
  * Cards are added
  * Boxes are removed
  * Box capacity value decreases
* Mods that result in defragmenting
  * Cards are removed
  * Boxes are added
  * Box capacity value increases

#### Options to "listen" for Treasury Modifications

* Use ef core Triggers for Box modifications
  * Does not handle Card modifications - extra edge case
* Use service with di, and insert service calls in currently existing paths
  * Easiest to impl
* Create an event that is invoked
  * Event invokes will still have to be inserted
  * Might be overkill since there would only be at most one event listener

## Defragmentation

When Treasury modifications create defragmentation possibilities as listed above, try to transfer cards from overpartitioned boxes.

### Considerations

The actual method of transferring has multiple options, and tradeoffs must be considered for each one.

#### Box Transfer Order

* Box order does not matter
  * Default to boxes sorted by box id -> essentially random
  * Easiest to impl
* Larger overpartition first
  * Leads to overprovisioned boxes to more uniformly divided
* Smaller overpartition first
  * Lead to overprovisioned boxes to be more centralized to specific boxes

#### Card Transfer Amount Order

* Amount order does not matter
  * Defaults to amounts sorted by card name -> essentially random order
  * Easiest to impl
* Larger number of copies first
  * Generally lower amount of box to box transfers
  * Tendency to shard large blocks into smaller amounts -> possible increased **internal fragmentation**
  * Checkouts may have to pull from multiple boxes -> more difficult over time
  * Possibly counteracted with checkouts prioritizing smaller amounts
    * Checkouts are still more difficult
* Smaller number of copies first
  * Makes average checkout more difficult

### Takeaways

Overpartitioning across each box leads to multiple edge cases that cannot be fully addressed without individual drawbacks. Fundamentally, invariants about the underlying storage system should be modified. A distinction should be made on whether a box can be overpartitioned or not. Only boxes defined as excess should be able to be overpartitioned.

With the listed options above, the best approach would be to have the **box** defragment order prioritize **smaller overpartition**, and **card amounts** to also prioritize **smaller number of copies**. Both orderings have the tendencies to centralize the overprovisions to fewer boxes.

## Find Origin Expression Visitor

### Purpose

Visit the seeking query to determine the shape of the find id query. The given seeking query has extra parameters that should not be present for the id query.

A find id query does need to preserve majority of the seek query parameters with the exception of non-keyset filters.

### Problem

The find id query result is used as a keyset filter. Because of the relational query interface, the exact references required for filtering may not be present based on the current id query builder.

### Solution

The missing references can be specified and filled by reading the specified seek query order properties. However, the specified ordering properties are not a direct translation. The main goal is to determine a set of missing property references.

The ordering properties are a class property chain, which may or may not contain missing references. Each ordering property chain can be interpreted as a direct acyclic graph, where each node is an object instance, and each vertex is a property reference.

The missing references are specified as graph chain of one or more nodes. One guaranteed property is that a missing reference will always be a subset of an ordering property graph.

Therefore, missing references can be defined as a **nonempty subset of an ordering property** specified by the given seek query.

### Implementation

1. Use the visitor pattern on the given seek query to find specified ordering properties.
2. For each ordering property, determine if any part of the chain contains references that would by default be missing.
3. If no references are found, continue on to the next ordering property.
4. If found, determine the largest graph subset that are missing references
    * A lowest common ancestor algorithm can be used to determine the largest graph.
5. Keep track of found missing references in a set.

Special considerations for the missing reference set must be accounted for, specifically, on what is determined as overlapping missing references. Since each missing reference is itself a direct acyclic graph, that definition can be also reframed as a set of points, where each graph node is a point.

Using this view, overlaps can then be determined checking which missing references are a superset of each other. Therefore, the missing reference set is structured as a **set of sets**, where each inner is disjoint from all other inner sets.
