# Energy-Flow Simulation Core — Build Specification

## 0. What this document is

This is a complete, unambiguous build specification for a single deliverable: a
generic **energy-flow simulation core**. It is written so that an implementer
(human or Claude Code) can build it with **zero design decisions left to make**.
Every behavioral choice is stated. Anything genuinely out of scope is listed
explicitly in Section 9 so it is never silently invented.

If any instruction here seems to require a design decision that is not written
down, **stop and ask**. Do not fill the gap.

**Engine:** Unity (C#). The core must be plain C# with no Unity dependencies in
the simulation layer (see Section 7). Unity is the host/render layer only.

---

## 1. Scope

### In scope (build this)
A deterministic simulation that moves a continuous resource ("flow") from
producers to consumers through a network of nodes, with player-controllable
switches that reroute flow automatically by ratio. The core also includes three
infrastructure subsystems: a typed event bus (Section 7.5), JSON graph
serialization (Section 7.6), and a node-type registry (Section 7.7).

### Out of scope (do NOT build, do NOT stub with invented behavior)
Weapons, enemies, waves, win/lose, the 3D world, the village/research/economy
wrapper, theming, faults/short-circuits, conduit capacity limits, manual aim
mode, and the visual circuit editor UI. These are later layers. See Section 9.

The core knows nothing about mana, wizards, water, or weapons. It only knows
**resource**, **nodes**, and **flow**.

---

## 2. Core model and governing principles

1. **Resource is continuous**, modeled on water (not electricity). There is no
   voltage, no resistance, no current-direction math. Flow is a non-negative
   real-valued rate.
2. **Flow is forward-only.** The network is a **Directed Acyclic Graph (DAG)**.
   Cycles are forbidden and must be rejected at graph-build time with an error.
3. **Flow always moves toward sinks** and is never intelligently held back based
   on downstream "need." Nodes are deliberately dumb.
4. **No waste by default.** Any node holding flow it cannot pass downstream
   banks it internally rather than discarding it.
5. **The core is theme-agnostic and portable.** Same engine must be reusable for
   a tower defense, a water network, a power grid, or an economy sim, by
   swapping only Sources and Sinks.
6. **Engineered to size, not over-engineered.** Prefer the simplest rule that
   produces the required behavior. Behavior should emerge from simple local
   rules, not from special-case code.

---

## 3. Execution model — the tick

1. The simulation runs on a **global fixed-timestep tick**, fully decoupled from
   render frame rate. Default tick rate is a single configurable constant
   `TICKS_PER_SECOND` (see Section 8). The render layer draws the most recent
   tick state; it never advances the simulation.
2. The simulation must be **deterministic**: identical initial graph + identical
   inputs (including the exact tick at which switches toggle) → identical output
   on every machine, every run. Evaluation order must be fixed and stable.
3. **Each tick has two passes, in this order:**

   **Pass 1 — Backward accept pass (compute reachability).**
   Determine, for every node, whether it is currently `accepting` flow, using
   **one general rule** (do not special-case node types in the pass itself):

   > A node is accepting iff its **local accept condition** is true AND
   > (it has no outputs, or at least one of its outputs leads to an accepting
   > node).

   The local accept condition is part of the node-type contract (Section 7.7).
   In this phase, exactly one node type has a non-trivial condition:
   - **Switch:** local condition is `open`. A closed switch is not accepting.
   - **All other node types:** local condition is always true.
   - A **Sink** has no outputs, so it is accepting whenever its local condition
     holds (i.e., always, in this phase).

   This pass propagates "closed" backward from switches up to the nearest
   Splitter (or further), which is what lets a switch "two nodes higher" inform
   an upstream splitter that a path is unavailable.

   **Forward-compatibility rule:** later phases extend the system *only* by
   adding new local accept conditions (e.g., "held amount >= capacity" for
   saturation). The pass itself, its ordering, and Pass 2's behavior are frozen
   and must never need modification for that. If an extension appears to require
   changing the pass, stop and ask.

   **Pass 2 — Forward flow pass (move resource).**
   Evaluate nodes in **topological order, sources first**. Each node reads the
   flow delivered to it this tick, applies its rule (Section 4), and pushes
   output only to outputs that are `accepting` (per Pass 1). Splitters normalize
   over accepting outputs only. A node that cannot push banks (principle 2.4);
   banking is the universal not-accepting response in every phase — the earlier
   "spew/waste" model from design discussion is **retired and must not be
   implemented**.

4. All per-tick flow values are **rates per tick**. A source producing 10 means
   10 units enter the graph this tick.

---

## 4. Node taxonomy

There are seven node types. Each node has zero or more typed input ports and
zero or more typed output ports, connected by **edges** (conduits). Edges are
instant: flow placed on an edge arrives at the destination node the same tick.
Flow lives in **nodes**, never in edges. Edge routing is animation only.

For every node below, "input this tick" means the sum of flow arriving on all
its input edges this tick (see Merger rule 4.3).

**Reserved fields (data model now, behavior later).** To guarantee later phases
never change the data model or serialization format:
- Every node that can hold/bank flow (Source buffer, Splitter bank, Gate, Sink)
  has a `capacity` field. Default: infinite. **In this phase the engine stores
  and serializes it but attaches no behavior to it.** Phase B (Section 10)
  activates it as a local accept condition.
- Every edge (Conduit) has a `throughputCap` field. Default: infinite. Stored
  and serialized, **no behavior in this phase.** Phase C activates it for
  faults.
Reserved fields must appear in the JSON format (7.6) from day one, so graph
files never need migration when later phases land.

### 4.1 Source
- Produces a configurable `productionRate` units of flow per tick.
- Pushes its production to its (single) output.
- If its output is not accepting, it **banks** the unproduced/unpushed flow in an
  internal buffer and pushes it on a later tick when the path reopens
  (consistent with no-waste, principle 2.4). Buffer is uncapped by default
  (capping is a later tuning concern, Section 8).

### 4.2 Conduit (edge)
- A connection between one output port and one input port.
- **Instant and uncapped** in the core. Carries whatever is pushed, immediately,
  with no limit and no delay.
- Has no behavior of its own. Exists for topology and for the animation layer.

### 4.3 Merger
- Multiple input edges → one output.
- **Sums** all incoming flow additively: inputs 3 and 2 → output 5.
- No reconciliation, no loss.

### 4.4 Splitter
- One input → multiple outputs, each output assigned a fixed **weight** (a
  ratio, e.g. 5 : 1 : 1). Weights are constant and never rewritten by the engine.
- Each tick: sum the weights of **accepting** outputs only, then divide the input
  flow among those outputs in proportion to their weights.
  - Example, weights 5,1,1, all accepting: outputs get 5/7, 1/7, 1/7 of input.
  - If the weight-1 third output's path is closed: sum is 6, outputs get
    5/6, 1/6, 0.
  - Reopened: returns to 5/7, 1/7, 1/7 automatically.
- **The splitter stores no percentages and no history.** Redistribution is the
  emergent result of normalizing open weights every tick. There is no special
  "redistribute" code path.
- If **no** outputs are accepting, the splitter is not accepting (Pass 1) and
  banks its input (principle 2.4).

### 4.5 Gate (threshold bucket)
- One input, one output, one configurable `threshold` (e.g. 30).
- Fills continuously from its input. Overfill is allowed: if it holds 30 and
  receives 1 more, it holds 31.
- When its held amount is **>= threshold**, it discharges **exactly `threshold`
  units as one instant packet in a single tick** to its output, reducing its
  held amount by `threshold`. (31 → discharge 30 → holds 1.)
- Smooth/gradual release is **animation only**, never core logic.
- If its output is not accepting, it holds and does not discharge.

### 4.6 Switch
- One input, one output. A pure **boolean**: `open` or `closed`. No capacity, no
  reservoir, no fill behavior in the core.
- When **open**: passes input straight through to output unchanged.
- When **closed**: it is not accepting (Pass 1). It passes nothing. Closing it is
  what triggers upstream splitters to renormalize around it.
- Toggled by the player (the host layer sets the boolean). The optional on-ramp
  (gradual return to full flow over a configurable time after reopening) is an
  **animation/feel flag**, default OFF/instant, and must not affect core logic
  or determinism when off.

### 4.7 Sink
- One input. Consumes flow and is the boundary where continuous flow becomes
  discrete events. Always accepting (Pass 1).
- Holds a configurable per-unit `cost` (e.g. 5). Accumulates incoming flow.
- Each tick, while held amount **>= cost**: emit **one discrete fire event** and
  subtract `cost` from the held amount. (Held 10, cost 5 → two fire events this
  tick, held returns to 0. Held 12, cost 5 → two events, holds 2.)
- **Banks the remainder** toward the next event. No waste.
- The core only emits an abstract "fire event" with the amount consumed. **What a
  fire event does (damage, projectile, beam, weapon archetype, stat allocation)
  is out of scope** and is supplied by the host layer (Section 7). Do not invent
  weapon behavior.

> Note on the Delay/Reservoir component discussed during design: a time-delay
> buffer (hold for N ticks, then release) was raised but is **not required** for
> this first core build and is **not** specified here. If it is wanted later it
> will be specified separately. Do not add it speculatively.

---

## 5. Graph rules

1. The graph is a DAG. Reject any graph containing a cycle at build time.
2. Edges connect exactly one output port to one input port.
3. A Merger is the only node permitted to have multiple input edges. A Splitter
   is the only node permitted to have multiple output edges. (Other nodes: one
   in, one out, except Source = no in / one out, and Sink = one in / no out.)
4. Topological order is computed once when the graph changes, then reused each
   tick. Order must be deterministic (stable tie-breaking, e.g. by node ID).

---

## 6. Determinism requirements

1. Fixed timestep only. No `Time.deltaTime` in simulation logic.
2. Fixed, stable evaluation order every tick.
3. Switch toggles take effect on a defined tick boundary, not mid-tick.
4. Given the same graph, same constants, and the same sequence of (tick, switch
   toggle) inputs, output flow and fire events must be identical across runs and
   machines. Build a test that asserts this.
5. **Conservation invariant (required test).** Resource is never created or
   destroyed except by Sources (creation) and Sink fire events (consumption).
   Every tick, the following must hold within a small epsilon (float tolerance):
   `total produced by all Sources since t=0` =
   `total consumed by all Sink fire events since t=0` +
   `sum of all flow currently held/banked in all nodes`.
   Build an automated test that steps a representative graph (including
   splitters with non-terminating ratios such as 5:1:1, switch toggles, and
   gate discharges) for at least 10,000 ticks and asserts this invariant every
   tick. A violation is a build-blocking bug, not a tuning issue.

---

## 7. Architecture and portability

1. **Two layers, hard boundary.**
   - **Simulation layer:** pure C#, no `UnityEngine` references. Contains all of
     Sections 3–6. This is the portable core.
   - **Host layer:** Unity-side. Owns rendering, input, the editor UI, and the
     concrete meaning of Sources (what produces) and Sinks (what a fire event
     does). Talks to the core through interfaces only.
2. **Two extension interfaces** are the only things a new project must implement:
   - `ISource` — supplies `productionRate` per tick (themed: spring, reactor,
     pump…).
   - `ISink` — receives fire events with consumed amount (themed: weapon,
     building load…).
   Swapping these, keeping the middle, must let the same core run a tower defense
   or a water/economy sim with no core changes.
3. Nodes should be small, independently unit-testable, and free of references to
   the wider graph beyond their immediate input/output ports.
4. No global mutable singletons in the simulation layer. The whole sim state must
   be ownable by a single object that can be instantiated, stepped, inspected,
   serialized, and discarded.

### 7.5 Event bus (locked decision — build this)

The simulation layer emits **typed events**; the host layer subscribes. The sim
never calls into the host directly. This is the only channel through which
rendering, sound, particles, analytics, and game logic observe the sim.

1. Required event types in the core (one struct/class each, carrying the
   emitting node's ID and the current tick):
   - `SourceProduced` — amount produced this tick.
   - `FlowDelivered` — node received flow this tick, with amount. (Drives the
     edge-flow animation layer.)
   - `GateDischarged` — gate ID, amount discharged (= threshold).
   - `SinkFired` — sink ID, amount consumed (= cost). One event per discrete
     fire (Section 4.7), so a sink crossing its cost twice in one tick emits
     two events.
   - `SwitchToggled` — switch ID, new state, tick it takes effect.
   - `NodeBanking` — node ID, banked amount, emitted when a node banks flow it
     could not push downstream.
2. Events are emitted in deterministic order (same order every run for the same
   inputs), so an event log is itself a determinism test artifact.
3. Subscribing or not subscribing must have **zero effect** on simulation
   behavior. Events are observation only.
4. New event types may be added later; the six above are the required core set.
   Do not invent additional event types beyond these without asking.

### 7.6 Graph serialization — graphs as data (locked decision — build this)

The complete graph definition is serializable to and from **JSON**. A circuit is
a file.

1. The format must capture everything needed to reconstruct the graph exactly:
   node IDs, node types, per-node configuration (production rates, thresholds,
   weights, costs, switch initial state), and all edges (from-node/port →
   to-node/port).
2. Round-trip requirement: serialize → deserialize → the resulting graph is
   functionally identical (same topology, same constants, same deterministic
   behavior from the same inputs). Build a test that asserts this.
3. The format should optionally also capture **runtime state** (per-node held/
   banked amounts, current tick) as a separate, clearly-marked section, so a
   mid-session save is possible. Definition-only files (no state section) are
   valid and load at tick 0 with empty buffers.
4. Test fixtures for the test suite (Sections 6.4, 6.5, 7.6.2) should be stored
   as JSON graph files, so every bug reproduction is a replayable file.
5. Versioning: the format carries a single integer `formatVersion` field.
   Version handling beyond writing/reading this field is out of scope for now.
6. Human-readable JSON (indented, stable key order) is preferred; performance
   of serialization is not a concern at this stage.

### 7.7 Node-type registry (locked decision — build this)

Node types are **registered** with the engine, not hardcoded into it.

1. The engine defines a node-type contract (interface or abstract base): a node
   declares its input/output port arity, its per-tick accept behavior (Pass 1)
   and flow behavior (Pass 2), and its configurable fields.
2. The seven core node types (Source, Conduit/edge handling, Merger, Splitter,
   Gate, Switch, Sink) are implemented **against that same contract** and
   registered as the standard library. The engine's tick loop, graph validation,
   serialization, and event bus must operate on the contract, never on concrete
   node classes.
3. JSON serialization (7.6) identifies node types by their registered string ID,
   so a graph file containing a future custom node type loads in any project
   that has registered that type.
4. Registration must preserve determinism: registered types are looked up by
   stable string ID, and the graph's evaluation order rules (Section 5.4) apply
   to all node types uniformly.
5. **Do not build any node types beyond the seven core ones.** The registry
   exists so that future types (delay, capacitor variants, fault-capable
   conduits, transformers, priority splitters) can be added as plugins without
   touching engine code — but those types are out of scope now (Section 9).

---

## 8. Tuning constants (set later; do NOT hardcode as magic numbers)

These are real numbers to be decided during balancing, not design gaps. Expose
each as a named, documented constant/field with a stated default:

- `TICKS_PER_SECOND` — global tick rate. Suggested default 20. (Final value TBD.)
- Per-Source `productionRate`.
- Per-Gate `threshold`.
- Per-Splitter output `weights`.
- Per-Sink `cost`.
- Optional Switch reopen-ramp time (default 0 = instant).
- Optional buffer caps (default uncapped).

---

## 9. Explicitly out of scope (must NOT be invented)

Build none of these now. If the work seems to need them, stop and ask.

- Weapons: archetypes, damage, fire rate, AoE, range, projectiles, beams,
  targeting, the mana-to-stat mapping formula.
- Enemies, the 360° field, movement, wave spawning, scaling, pacing.
- Win/lose conditions, tower or village health.
- The world wrapper: village happiness, research center effects, mine output
  progression, the city-vs-tower economy.
- Faults: short circuits, overloads, and consequences of exceeding conduit
  `throughputCap`. (The field exists and serializes from day one, Section 4;
  behavior is Phase C, Section 10.)
- Saturation backflow. (The accept pass is already general, 3.3; saturation is
  Phase B, Section 10 — a new local accept condition, not new machinery.)
- The 3D world, camera, zoom, wizard view, manual aim mode.
- The visual node/circuit editor UI. (The core must be drivable
  programmatically; the editor is a later host-layer concern.)
- Node types beyond the seven core ones (delay/reservoir, capacitor variants,
  fault-capable conduits, transformers, priority splitters). The registry
  (7.7) exists to host them later; do not build them now.
- Any theming or art.

---

## 10. Phase roadmap (additive-only)

**Governing rule: a phase may only add — new local accept conditions, new
registered node types, new event types, new host-layer content, or activation
of a reserved field. A phase must never modify the tick passes, the seven core
node rules, the banking rule, the contract, or the JSON format's existing
fields. If a phase appears to require modifying any of those, the phase is
mis-specified: stop and ask.**

This supersedes the older "Phase 1 spew / Phase 2 backflow" framing from design
discussion. That framing is retired: spew was replaced by universal banking,
and backward signaling already exists in Phase A via the accept pass.

### Phase A — this specification (the only phase being built now)
The complete core as specified in Sections 1–9: seven node types, two-pass
deterministic tick, banking, event bus, JSON serialization, node registry,
conservation test. Reserved fields (`capacity`, `throughputCap`) are stored and
serialized with infinite defaults and no behavior.

### Phase B — saturation (deferred; do not build)
Activates the `capacity` field as a **local accept condition**: a node whose
held amount >= its capacity reports not-accepting in Pass 1. Everything else
falls out of Phase A machinery unchanged: upstream splitters renormalize around
the saturated path exactly as they do around a closed switch, and full
blockage banks upstream. Adds one event type (`NodeSaturated`). No pass
changes, no node-rule changes, no format changes — graphs from Phase A load
as-is (infinite capacity = never saturates).

### Phase C — faults (deferred; do not build)
Activates the `throughputCap` edge field: flow pushed across an edge above its
cap emits a fault event (e.g. `ConduitOverloaded`). What a fault *does*
(melt, disable, damage, brown-out the village) is host-layer consequence logic
driven by that event — the sim itself only detects and reports. No core
changes beyond the detection check and event.

### Phase D — game content (deferred; do not build)
Weapons, enemies, waves, the world wrapper, manual aim. All implemented as
host-layer `ISource`/`ISink` implementations, event subscribers, and (if
needed) new registered node types. Touches zero engine code by construction.

The visual circuit editor and 3D presentation are host-layer work that can land
alongside any phase; they read the sim through the event bus and probe APIs and
never gate or modify core behavior.

---

## 11. Decision log (traceability)

The behaviors above derive from these locked decisions:

- Deliverable is the generic energy-flow core only; theme and weapons are later.
- Resource is continuous, water-model, forward-only DAG, no voltage/resistance.
- Global fixed-timestep tick; deterministic; render decoupled.
- Merging is additive.
- Conduits are instant and uncapped; flow lives in nodes; edge routing is
  animation only.
- Splitters store fixed ratio weights and normalize over open paths every tick;
  redistribution is emergent, stateless.
- Switches are pure boolean on/off; closing reroutes upstream via the backward
  accept pass.
- Gates are threshold buckets; overfill allowed; discharge is an instant packet.
- Sinks accumulate to a per-unit cost, emit one discrete fire event per cost
  crossed, and bank the remainder (no waste). Weapon meaning is host-supplied.
- No-waste banking is universal for nodes that cannot push downstream.
- Optional switch reopen-ramp is an animation flag, default instant, never
  affects core logic.
- The sim communicates outward only via a typed event bus; observation never
  affects behavior (7.5).
- Graphs are data: full JSON serialization with round-trip fidelity, optional
  runtime-state section, JSON files as test fixtures (7.6).
- Node types are registered against a single contract; the seven core types are
  the standard library; future types are plugins, not engine edits (7.7).
- Conservation invariant is a required automated test: produced = consumed +
  held, every tick, within float epsilon (6.5).
- Phases are additive-only: later phases add accept conditions, node types,
  events, or activate reserved fields; they never modify passes, core node
  rules, banking, the contract, or existing format fields (Section 10).
- The accept pass is one general rule with per-node local conditions; switch
  `open` is the only non-trivial condition in Phase A (3.3).
- `capacity` and `throughputCap` are reserved fields: serialized from day one,
  default infinite, behavior activated in Phases B and C respectively
  (Section 4, Section 10).
- The original "Phase 1 spew / Phase 2 backflow" framing is formally retired;
  banking is universal in all phases.
