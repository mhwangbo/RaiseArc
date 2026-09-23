---
id: concepts
title: Plans, rules, and content
---

An **activity** has a duration, cost or reward, conditions, and effects. Conditions are checks; they should not spend time or random state. An activity executes through the official session host, which applies committed effects and records once. Evaluation records, qualification flags, and period modifiers are part of the game's state and can affect later rules.

A **draft plan** is editable. **Confirm plan** locks its activities. **Run next** executes one official host step; **Run plan** repeats that step with a visual feedback interval and stops at a dialogue, choice, error, unsupported boundary, or completion. Opening or closing a panel does not execute a step. Choosing an event option leaves execution paused until the player resumes.

The **current date** describes progress already made. The **plan target date** describes the next draft's start. In the next-day example, a completed plan can be followed by a new draft for the next date; the old confirmed slots do not change.

The weekly, monthly, and next-day examples share the same host and plan rules. They demonstrate different plan durations and UI templates. Their prior validation reports are historical evidence for the original Assets installation, not proof that the separated package works. Explorer's fixed-plan checks do not enumerate every possible repeated future plan.

UI Toolkit bindings can display stats, money, activities, dialogue, and endings, and invoke supported commands. The optional Composer is a simpler entry. Neither binding system owns a second copy of game rules.
