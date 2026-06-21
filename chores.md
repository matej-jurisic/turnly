# Chore Use Cases

Every chore is shaped by four orthogonal axes: **when** it recurs, **how many times** it must be
done per occurrence, **who** does it, and **how the next date is calculated** after completion.
This document walks through every meaningful combination with a household example.

---

## 1. One-time chores

A chore that happens exactly once and disappears after completion. No scheduling preference or
assignment rotation applies.

| Setting | Value |
|---|---|
| Repeat | One-time |
| Assignment | Any single assignee (no rotation) |

**Example — Book the car service**
Schedule it for next Tuesday, assign to Dad. Once marked done it leaves the list permanently.

---

## 2. Fixed-interval recurrence

### 2a. Daily

Advances exactly one calendar day each time.

| Setting | Value |
|---|---|
| Repeat | Daily |
| Times of day | (none — one occurrence per day) |

**Example — Feed the cat**
Due every day. Whoever is assigned marks it done; the next occurrence appears for tomorrow.

---

### 2b. Daily — multiple times a day

Two or more fixed clock times on the same day each count as separate occurrences.
Only available on Daily (and the custom calendar modes).

| Setting | Value |
|---|---|
| Repeat | Daily |
| Times of day | 08:00, 20:00 |

**Example — Feed the dog (morning and evening)**
The chore appears twice a day. Completing the 08:00 slot moves the due date to 20:00 the same
day; completing 20:00 rolls to 08:00 the next morning.

---

### 2c. Weekly

Advances exactly seven days each time.

**Example — Take out the bins**
Due every Monday. Completing it moves the due date to the following Monday.

---

### 2d. Monthly

Advances one calendar month (end-of-month clamped: Jan 31 → Feb 28).

**Example — Pay the electricity bill**
Due on the 1st of each month.

---

### 2e. Yearly

Advances exactly one calendar year.

**Example — Renew the car insurance**
Due once a year on the policy renewal date.

---

## 3. Custom interval recurrence

A fixed number of days / weeks / months / years between occurrences.

### 3a. Every N days

**Example — Water the indoor plants (every 3 days)**
More granular than weekly; advances exactly 3 days each completion.

---

### 3b. Every N weeks

**Example — Deep-clean the bathroom (every 2 weeks)**
Fortnightly chore; advances 14 days.

---

### 3c. Every N months

**Example — Replace the HVAC filter (every 3 months)**
Quarterly; advances 3 calendar months (end-of-month clamped).

---

### 3d. Every N years

**Example — Bleed the radiators (every 2 years)**
Advances 2 calendar years.

---

## 4. Custom DaysOfWeek recurrence

Fires on one or more named weekdays each week. Add an optional week-of-month restriction to
target only specific occurrences within each month.

### 4a. Specific weekdays (every week)

**Example — Take kids to football (every Tuesday and Thursday)**
Due every Tue and Thu. Completing Tuesday's slot moves the due date to Thursday; completing
Thursday's moves it to next Tuesday.

---

### 4b. Specific weekdays — multiple times a day

Combine DaysOfWeek with TimesOfDay for sub-day slots on particular days.

**Example — Walk the dog (Mon / Wed / Fri at 07:00 and 18:00)**
Six occurrences per week. Completing the 07:00 Monday slot advances to 18:00 Monday; completing
18:00 Monday advances to 07:00 Wednesday.

---

### 4c. Nth weekday of the month

Restrict which occurrence of the weekday counts (1st–4th or last).

**Example — Family game night (1st and 3rd Friday)**
Due on the first and third Friday of each month only; other Fridays are skipped.

---

### 4d. Last weekday of the month

**Example — Review the household budget (last Sunday)**
Due on the final Sunday of each month regardless of how many Sundays there are.

---

## 5. Custom DaysOfMonth recurrence

Fires on one or more specific calendar dates within a selected set of months.

### 5a. Single day, selected months

**Example — Pay quarterly tax estimate (15th of Jan, Apr, Jul, Oct)**
Due on the 15th of those four months only; all other months are skipped.

---

### 5b. Multiple days, selected months

**Example — Check pool chemistry (1st and 15th of Jun, Jul, Aug)**
Six occurrences per summer; the nearest upcoming date in the set is always next.

---

### 5c. End-of-month edge case

Days that don't exist in a selected month are skipped silently.

**Example — Archive bank statements (31st of Jan, Mar, May, Jul, Aug, Oct, Dec)**
Only months with 31 days fire; February and the short months are automatically skipped.

---

## 6. Scheduling preferences

Controls how the **next** due date is calculated after a completion. Applies to all recurring
types except fixed-slot calendar modes (DaysOfWeek / DaysOfMonth), where the "next slot" is
always the next calendar match.

### 6a. From scheduled date (rigid grid)

Next due = scheduled date + interval. Completing early or late makes no difference to the grid.

**Example — Pay rent (monthly, from scheduled date)**
Due the 1st of each month. Paying on the 28th still schedules the next payment for the 1st of
next month, not 28 days later.

---

### 6b. From completion date (drifting)

Next due = actual completion time + interval. The schedule drifts with your real-world rhythm.

**Example — Mow the lawn (every 2 weeks, from completion date)**
If you mow on a Thursday instead of the usual Monday, the next due lands on the Thursday two
weeks later, keeping a genuine two-week gap.

---

### 6c. To first next repeat (catch-up)

Next due = the first naturally occurring slot strictly after now. Missed occurrences are
silently skipped; the chore is never shown as multiply overdue.

**Example — Check the smoke alarm batteries (monthly)**
Forgotten for three months — marking it done today jumps straight to next month's slot rather
than showing three overdue occurrences to catch up on.

---

### 6d. Smart scheduling — no grace (default)

Next due = max(from scheduled date, from completion date). Holds the planned cadence when done
on time or early; gives a full interval of rest when done late.

**Example — Clean the oven (weekly)**
Done two days late on Wednesday? Next due is Wednesday + 7 days (not the original Friday + 7,
which would only be 5 more days away). Done two days early on Friday? Next due stays the
following Friday, keeping the weekly rhythm.

---

### 6e. Smart scheduling — with grace window

When completed more than the grace window early, the cadence resets from completion (treating
it as a genuine early action rather than forcing a long gap until the original grid date).

**Example — Descale the coffee machine (every 2 weeks, 2-day grace)**
Descaled 1 day early? Grace absorbs it — next due is still original date + 14 days.
Descaled 6 days early (genuinely ahead of schedule)? Cadence resets from today + 14 days so
you don't wait 20 days for the "next" occurrence.

---

## 7. Multi-completion occurrences

One occurrence requires N completions (or skips) before it closes and the schedule advances.
Only available on non-custom repeat types.

### 7a. N completions, same assignee throughout

The occurrence stays open and the assignee unchanged until the Nth completion.

**Example — Practice piano (weekly, 3 times)**
The chore stays due the same week until logged three times; on the third completion the week
closes and next week's occurrence opens.

---

### 7b. N completions, rotate on each completion

The assignee rotates on every completion, not just when the occurrence closes. Useful for
splitting shared work within an occurrence.

**Example — Cook dinner (weekly, 7 times, round-robin)**
Each day's cook marks it done; the assignee advances to the next person for the next day's
slot. After seven completions the week closes and rotation continues.

---

## 8. Assignment strategies

Determines who becomes the current assignee when a new occurrence opens (or, with
rotate-on-each-completion, after each completion).

### 8a. Keep last assigned (permanent owner)

The same person is always assigned. No rotation ever occurs.

**Example — Take out the recycling — always Mum**
One person owns this chore permanently regardless of who completes it.

---

### 8b. Round robin

Cycles through assignees in a fixed, stable order (creation order).

**Example — Cook dinner (weekly) — Alice → Bob → Carol → Alice …**
Perfectly predictable; everyone knows whose week it is.

---

### 8c. Least assigned

The next assignee is whoever has been assigned this chore the fewest times in total.
Tie-broken by who was assigned least recently.

**Example — Clean the bathroom (weekly)**
Balances the raw number of times each person has been on the hook, catching up anyone who
joined late or was away for a while.

---

### 8d. Least completed

The next assignee is whoever has actually completed this chore the fewest times.
Tie-broken by who completed it least recently.

**Example — Walk the dog (daily, round-the-family)**
Rewards people who actually do the work rather than those who were merely assigned.

---

### 8e. Random

Picks any assignee at random each time, including possibly the same person again.

**Example — Who washes up tonight?**
Adds an element of chance; everyone might get picked twice in a row.

---

### 8f. Random except last assigned

Picks any assignee at random, but guarantees it won't be the same person as last time.

**Example — Who sets the table? (not the same person as yesterday)**
Keeps some variety without strictly rotating.

---

## 9. Everyone independently (track mode)

Each assignee gets their **own** due date, completion count, and quota. One person's progress
never blocks another's. There is no "current assignee" — everyone owns their share.

### 9a. Equal quotas

Everyone must complete the chore the same number of times per occurrence.

**Example — Do the dishes (weekly, everyone once)**
All three family members each need to mark dishes done once per week. Alice finishing on Monday
doesn't affect Bob or Carol's due date.

**Example — Tidy your room (weekly, everyone once)**
Each child has their own weekly due date. A child who falls behind doesn't delay the others.

---

### 9b. Uneven quotas

Different assignees have different per-occurrence completion counts.

**Example — Cook dinner (weekly — Alice 3×, Bob 2×, Carol 2×)**
One chore, one weekly schedule, but the distribution reflects each person's availability.
Alice needs to log three dinners before her week closes; Bob and Carol need two each.

---

## 10. Combining recurrence and assignment

Any recurrence type can be combined with any assignment strategy. A few meaningful combinations:

### 10a. Every-other-week rotation between two people

**Example — Clean the car (every 2 weeks, round-robin, Alice and Bob)**
Alice cleans week 1, Bob cleans week 3, Alice week 5, and so on — each person handles it
roughly once a month.

---

### 10b. Monthly chore, least-completed rotation across the whole family

**Example — Deep-clean the fridge (monthly)**
Goes to whichever family member has cleaned the fridge the fewest times, balancing the load
fairly over months.

---

### 10c. DaysOfWeek + independent tracks (everyone's own schedule)

**Example — Empty the dishwasher (Mon / Wed / Fri, everyone independently)**
Three occurrences per week, each person on their own track. Alice empties it on Monday morning;
that closes her Monday slot and advances her to Wednesday. Bob hasn't touched Monday yet — his
Monday slot is still open.

---

### 10d. Daily multiple times + round-robin

**Example — Feed the baby (every 3 hours: 06:00, 09:00, 12:00, 15:00, 18:00, 21:00 — round-robin)**
Each feed slot rotates to the next parent, distributing six daily feeds evenly.

---

## 11. Skip

An admin can skip the current occurrence of any recurring chore. The schedule advances to the
next due date without awarding points or rotating the assignee.

**Example — Take out the bins — bin collection cancelled this week**
Admin skips the occurrence. Schedule jumps to next Monday as normal but no one gets points and
the assignee stays the same.

**Example — Independent chore, skip one person's track**
In track mode the admin skips a specific assignee's current slot — useful when one family
member is on holiday. Only their track advances; everyone else is unaffected.

---

## 12. Reschedule

Move the current occurrence's due date (and optionally its time) to a specific date without
triggering a completion or skip. In track mode, one person's track is targeted.

**Example — Mow the lawn — postponed because of rain**
Rescheduled from Friday to next Monday. No completion is recorded; the assignee is unchanged.

**Example — Independent chore, reschedule one person**
Bob's weekly dishes slot is moved forward two days because he's travelling. Alice and Carol's
slots are untouched.

---

## 13. Notifications

Each chore can carry multiple notification entries. Every entry is one (type, timing, offset,
recipients) combination and fires independently.

### Notification types
- **Reminder** — advance warning before the chore is due
- **Due** — fires exactly when the chore becomes due
- **Follow-up** — fires after the chore is overdue

### Timing & offsets
- **Before due** — N minutes / hours / days before the due time
- **At due** — exactly at the due time (no offset)
- **After due** — N minutes / hours / days after the due time

### Recipients
- **Current assignee** — only the person currently assigned
- **All assignees** — every person listed on the chore

### 13a. Simple reminder

**Example — Pay the electricity bill — reminder 2 days before**
One notification: Reminder, 2 days before, current assignee.

---

### 13b. Reminder + overdue follow-up

**Example — Replace the HVAC filter — reminder 1 week before, follow-up 1 day after if not done**
Two notifications: Reminder (7 days before) and Follow-up (1 day after), both to current
assignee.

---

### 13c. At-due alert to all assignees

**Example — Family meeting (weekly) — alert everyone at the time**
One notification: Due, at due time, all assignees. Everyone gets pinged simultaneously.

---

### 13d. Independent chore — per-person reminders

In track mode, "current assignee" sends to each person's own track separately, so Alice gets
her reminder at her due time and Bob gets his reminder at his (potentially different) due time.

**Example — Do the dishes (weekly, everyone independently) — reminder 2 hours before**
Each person's push notification fires two hours before their own due date, not a shared one.

---

## Quick-reference matrix

| Repeat | Times of day | Strategy | Scheduling | Multi-completion | Example |
|---|---|---|---|---|---|
| One-time | — | Single assignee | — | — | Book dentist appointment |
| Daily | — | Keep last assigned | From scheduled | — | Make the beds |
| Daily | 08:00, 20:00 | Keep last assigned | From scheduled | — | Feed the dog (twice daily) |
| Weekly | — | Round-robin | From scheduled | — | Take out the bins |
| Weekly | — | Least completed | Smart | — | Cook dinner |
| Weekly | — | Independent (1×) | From scheduled | — | Tidy your room |
| Weekly | — | Independent (uneven) | From scheduled | — | Cook dinner (Alice 3×, Bob 2×) |
| Monthly | — | Least assigned | From scheduled | — | Deep-clean the fridge |
| Monthly | — | Keep last assigned | From completion | — | Pay rent |
| Monthly | — | Round-robin | To first next repeat | — | Check smoke alarm batteries |
| Yearly | — | Keep last assigned | From scheduled | — | Renew car insurance |
| Custom interval (3 days) | — | Keep last assigned | Smart + 1-day grace | — | Water the plants |
| Custom interval (2 weeks) | — | Keep last assigned | From completion | — | Mow the lawn |
| Custom interval (3 months) | — | Keep last assigned | From scheduled | — | Replace HVAC filter |
| Custom DaysOfWeek (Mon/Wed/Fri) | — | Round-robin | From scheduled | — | Walk the dog |
| Custom DaysOfWeek (Mon/Wed/Fri) | 07:00, 18:00 | Round-robin | From scheduled | — | Walk the dog (am + pm) |
| Custom DaysOfWeek (1st + 3rd Fri) | — | Keep last assigned | From scheduled | — | Family game night |
| Custom DaysOfWeek (last Sun) | — | Keep last assigned | From scheduled | — | Review household budget |
| Custom DaysOfMonth (15th, Q months) | — | Keep last assigned | From scheduled | — | Quarterly tax estimate |
| Daily | — | Round-robin | From scheduled | 3× per week | Practice piano |
| Weekly | — | Round-robin | From scheduled | 7× + rotate each | Cook dinner (daily, rotating) |
