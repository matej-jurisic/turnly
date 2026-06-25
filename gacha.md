# Gacha (Cosmetics) — Draft Catalog

Planning doc. Nothing implemented yet. Edit freely — add/remove items, adjust tiers,
rename, set dust values. Once this settles we turn it into a code-defined catalog
(`GachaCosmeticCatalog.cs`, same pattern as `AchievementCatalog.cs`).

## Principles (so we don't drift)

- **Cosmetic-only.** No gameplay power. No skip/reroll tokens in v1.
- **Earned currency only.** Pulls cost chore-earned points. No real money, ever.
- **Published odds + pity.** Rates shown openly; guaranteed Legendary within N pulls.
- **Dupe protection.** Duplicates convert to dust; dust crafts a specific item.
- **Reduced-motion fallback.** Every animated item needs a static version.
- **Parent controls.** Household setting to disable gacha or cap pulls/day.

## Tiers & odds (tweak)

| Tier | Odds | Vibe |
|------|------|------|
| Common | ~60% | flavor, high volume, dust fodder |
| Rare | ~28% | first real "wearable" identity items |
| Epic | ~10% | themes & motion, clearly special |
| Legendary | ~2% | bragging rights, visible to the whole household |

## Equip slots (proposed)

One active item per slot, mix-and-match. Confetti can roll randomly from owned.

- Avatar background
- Avatar frame
- Avatar effect (animated)
- Title (text under name)
- App theme palette
- Confetti style
- Leaderboard nameplate

---

## Common (~60%)

**Avatar backgrounds**
- Sunset gradient
- Ocean gradient
- Forest gradient
- Lavender gradient
- Slate gradient
- Coral gradient
- Mint gradient
- Rosé gradient
- Storm gradient
- Sand gradient
- Solid pastel set (10 soft colors)
- Two-tone "split" background

**Confetti variants**
- Gold coins
- Hearts
- Stars
- Leaves
- Snowflakes
- Bubbles
- Petals
- Sparks

**Profile micro-cosmetics**
- Emoji mood sticker next to name (🌞 🌙 ⚡ 🍀 …)
- Initials font tweak (rounded / serif / mono)
- Flair dot color on avatar

---

## Rare (~28%)

**Avatar frames (rings)**
- Thin gold ring
- Thin silver ring
- Thin bronze ring
- Dashed "achiever" ring
- Double ring
- Beaded ring
- Pastel gradient ring
- Neon outline

**Confetti upgrades**
- Two-color combo bursts (team colors)
- Heart-shape burst pattern
- Star-shape burst pattern
- Streamer ribbon confetti

**Profile / titles**
- Title: "The Reliable"
- Title: "Early Bird"
- Title: "Night Owl"
- Title: "Dish Slayer"
- Title: "Clean Machine"
- Reaction emoji pack (extra feed reactions)
- Custom badge shape (square / shield / hexagon)

**Theme (light-mode swaps)**
- Peach
- Sky
- Sage
- Graphite

---

## Epic (~10%)

**App theme palettes**
- Midnight (deep navy dark)
- Sakura (pink)
- Matrix (green-on-black)
- Sepia / Newsprint
- Solarized
- High-contrast

**Animated avatar effects (subtle loop)**
- Gentle glow / pulse ring
- Floating orbiting particles
- Holographic color-shift background
- Breathing gradient

**Confetti showpieces**
- Fireworks sequence (multi-burst)
- Emoji rain (chosen emoji pours down)
- Side-cannons (dual-origin blast)

**Leaderboard cosmetics**
- Colored name highlight / row tint
- Small rank icon

---

## Legendary (~2%)

**Avatar showcase effects**
- Animated crown / floating laurel
- Particle aura (embers / stardust / petals) others can see
- Fully animated frame (rotating gradient / shimmering metal / liquid gold)
- Seasonal: pumpkin-vine frame (Oct)
- Seasonal: snow-globe avatar (Dec)
- Seasonal: fireworks frame (New Year)

**Whole-app legendary themes**
- Galaxy (animated starfield on cards)
- Aurora (slow-moving gradient header)
- Retro Arcade (pixel font + neon)

**Leaderboard / house trophies**
- Trophy icon pinned to profile + leaderboard
- Hall of Fame animated gradient nameplate
- House banner: "🏆 Current Chore Champion" styling (kept until dethroned)

**Confetti legendary**
- Full-screen sequence (cannons + fireworks + emoji rain), reduced-motion-aware

---

## Open questions (decide before implementation)

- Pull cost (points per single pull / 10-pull discount?)
- Pity counter N (guaranteed Legendary within how many pulls?)
- Dust: dupe → how much dust, and craft cost per tier?
- v1 scope: which subset ships first? (suggestion: cosmetics-only, no seasonal, no leaderboard nameplate)
- Seasonal items: separate "banner" with availability windows, or always in pool?
- Equip vs. random: which slots are equip-one vs. roll-from-owned (confetti)?
