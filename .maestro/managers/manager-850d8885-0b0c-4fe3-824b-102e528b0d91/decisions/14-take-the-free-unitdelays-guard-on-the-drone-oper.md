# Take the free UnitDelays guard on the drone operator, overriding my own provisional acceptance

_Recorded 2026-08-27T00:37:51.579Z by 17dc66e4_

Worker `8091c81e` found that ordinal sorting of `floorTypes` (`UnitBuilderBotModule.cs:529`) gives `aa < dr < medi`, so when several support types sit below their floor the drone operator is bought **before** the medic. With a lobby platoon start the denominator opens at 11 → `min(2, 11/8) = 1` → the operator becomes the **opening support call-in**, ahead of the first medic, with `aa.*` held off to tick 2000. That is the exact shape the surrounding comment block exists to prevent. The author accepted it rather than guarding; I provisionally accepted too. Reviewer `787bacd8` agreed with the acceptance and then argued for taking the guard anyway. I reversed.

## What changed my mind

Not a disagreement about the risk — the reviewer's own verdict was "I would accept it too". The argument is about **cost**, and it is one neither the author nor I had made:

> Under the shipped `StartingUnitsClass = "none"` the denominator is 0 at t=0, so the floor cannot fire there anyway. A `dr.america: 300` `UnitDelays` entry therefore costs **exactly zero** in the default configuration, while removing the platoon-start exposure entirely.

A guard whose downside is provably nil in the shipped path does not need a risk argument to justify it; it needs one to justify *omitting* it. I had been weighing "is the exposure acceptable" when the question was "is the guard free", and it is.

It also corrected the author's stated reason for accepting, which I had taken at face value. The author argued a platoon start means a squad already exists so there is genuinely something to scout for. True, but not responsive: the surrounding block's objection was never "there is nothing to scout" — it was that **the floor pre-empt outranks the ceiling and every demand gate**, so a cheap floored type is *guaranteed*, not merely permitted, to be the opening buy. The acceptance was answering a weaker objection than the one on record.

The tipping fact: **this defect has shipped twice in this file already.** Given a free guard against a third instance, writing the paragraph explaining why the third is tolerable is the worse trade.

## What I am not doing

Not raising the denominator. The reviewer and author agree the fix shape is a `UnitDelays` entry, not a bigger divisor — changing the divisor would move every support type's floor behaviour, which is a balance change nobody asked for and which would reach `@stable`'s benchmark control.

## Generalisation worth keeping

When a worker accepts a known-bad shape with reasoning, check whether the guard it declined is *symmetric*. A guard with real cost deserves the tolerability argument. A guard that is inert in the shipped configuration should simply be taken, and the reasoning about whether the exposure matters becomes unnecessary — which is also cheaper than the paragraph, and does not decay when the configuration changes underneath it.
